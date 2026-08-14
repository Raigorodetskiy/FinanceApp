using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

public enum YahooSessionFailureCategory
{
    None = 0,
    Unauthorized,
    Forbidden,
    NotFound,
    RateLimited,
    Provider5xx,
    Timeout,
    ConsentFailure,
    InvalidSessionPayload,
    NetworkError
}

public sealed record YahooSession(string CookieHeader, string Crumb, DateTimeOffset ExpiresAtUtc);

public sealed record YahooSessionAcquisitionResult(
    YahooSession? Session,
    YahooSessionFailureCategory FailureCategory,
    int StatusCode,
    string ErrorMessage)
{
    public bool IsSuccess => Session is not null;

    public static YahooSessionAcquisitionResult Success(YahooSession session) =>
        new(session, YahooSessionFailureCategory.None, StatusCodes.Status200OK, string.Empty);

    public static YahooSessionAcquisitionResult Failure(
        YahooSessionFailureCategory failureCategory,
        int statusCode,
        string errorMessage) =>
        new(null, failureCategory, statusCode, errorMessage);
}

public interface IYahooSessionService
{
    Task<YahooSessionAcquisitionResult> GetSessionAsync(CancellationToken cancellationToken = default);
    void InvalidateSession();
}

public sealed class YahooSessionService : IYahooSessionService
{
    private const string SessionClientName = "YahooSession";
    private const int MaxRedirects = 6;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly YahooFinanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<YahooSessionService> _logger;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private YahooSession? _cachedSession;

    public YahooSessionService(
        IHttpClientFactory httpClientFactory,
        IOptions<YahooFinanceOptions> options,
        TimeProvider timeProvider,
        ILogger<YahooSessionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<YahooSessionAcquisitionResult> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCachedSession(out var cachedSession))
        {
            return YahooSessionAcquisitionResult.Success(cachedSession);
        }

        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCachedSession(out cachedSession))
            {
                return YahooSessionAcquisitionResult.Success(cachedSession);
            }

            var sessionResult = await CreateSessionAsync(cancellationToken);
            if (sessionResult.IsSuccess && sessionResult.Session is not null)
            {
                _cachedSession = sessionResult.Session;
                _logger.LogInformation("Yahoo session initialized successfully; ttlMs={TtlMs}.", (int)_options.SessionTtl.TotalMilliseconds);
            }
            else
            {
                _cachedSession = null;
            }

            return sessionResult;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public void InvalidateSession()
    {
        _cachedSession = null;
        _logger.LogInformation("Yahoo session invalidated.");
    }

    private bool TryGetCachedSession(out YahooSession session)
    {
        var cached = _cachedSession;
        if (cached is not null && cached.ExpiresAtUtc > _timeProvider.GetUtcNow())
        {
            session = cached;
            return true;
        }

        session = default!;
        return false;
    }

    private async Task<YahooSessionAcquisitionResult> CreateSessionAsync(CancellationToken cancellationToken)
    {
        var timeout = _options.SessionInitializationTimeout > TimeSpan.Zero
            ? _options.SessionInitializationTimeout
            : TimeSpan.FromSeconds(15);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var client = _httpClientFactory.CreateClient(SessionClientName);
        var cookieJar = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var sessionBootstrap = await SendWithRedirectsAsync(client, new Uri("https://fc.yahoo.com"), cookieJar, timeoutCts.Token);
            if (IsConsentFailure(sessionBootstrap))
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.ConsentFailure,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider consent flow failed.");
            }

            var crumbResponse = await SendWithRedirectsAsync(
                client,
                new Uri("https://query1.finance.yahoo.com/v1/test/getcrumb"),
                cookieJar,
                timeoutCts.Token);

            if (IsConsentFailure(crumbResponse))
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.ConsentFailure,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider consent flow failed.");
            }

            if (crumbResponse.StatusCode == HttpStatusCode.Unauthorized)
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.Unauthorized,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider authorization failed.");
            }

            if (crumbResponse.StatusCode == HttpStatusCode.Forbidden)
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.Forbidden,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider access is forbidden.");
            }

            if (crumbResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.NotFound,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider endpoint not found.");
            }

            if (crumbResponse.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.RateLimited,
                    StatusCodes.Status429TooManyRequests,
                    "Fundamentals provider rate limit exceeded.");
            }

            if ((int)crumbResponse.StatusCode >= 500)
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.Provider5xx,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider request failed.");
            }

            if (!crumbResponse.IsSuccessStatusCode)
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.NetworkError,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider request failed.");
            }

            var cookieHeader = BuildCookieHeader(cookieJar);
            if (string.IsNullOrWhiteSpace(cookieHeader))
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.InvalidSessionPayload,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider returned an invalid session.");
            }

            var crumb = (crumbResponse.Content ?? string.Empty).Trim();
            if (!IsValidCrumb(crumb))
            {
                return YahooSessionAcquisitionResult.Failure(
                    YahooSessionFailureCategory.InvalidSessionPayload,
                    StatusCodes.Status502BadGateway,
                    "Fundamentals provider returned an invalid session.");
            }

            return YahooSessionAcquisitionResult.Success(
                new YahooSession(
                    cookieHeader,
                    crumb,
                    _timeProvider.GetUtcNow().Add(_options.SessionTtl > TimeSpan.Zero ? _options.SessionTtl : TimeSpan.FromMinutes(20))));
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return YahooSessionAcquisitionResult.Failure(
                YahooSessionFailureCategory.Timeout,
                StatusCodes.Status504GatewayTimeout,
                "Fundamentals provider session initialization timed out.");
        }
        catch (HttpRequestException)
        {
            return YahooSessionAcquisitionResult.Failure(
                YahooSessionFailureCategory.NetworkError,
                StatusCodes.Status502BadGateway,
                "Fundamentals provider session initialization failed.");
        }
    }

    private static bool IsConsentFailure(SessionHttpResponse response) =>
        (response.FinalUri.Host.Contains("consent", StringComparison.OrdinalIgnoreCase) ||
         response.FinalUri.Host.Contains("guce", StringComparison.OrdinalIgnoreCase) ||
         (response.Content?.IndexOf("consent", StringComparison.OrdinalIgnoreCase) >= 0 &&
          response.Content.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0));

    private static bool IsValidCrumb(string crumb) =>
        !string.IsNullOrWhiteSpace(crumb) &&
        crumb.Length <= 256 &&
        crumb.IndexOf('<') < 0 &&
        crumb.IndexOf('>') < 0 &&
        crumb.IndexOf('{') < 0 &&
        crumb.IndexOf('}') < 0;

    private static string BuildCookieHeader(IEnumerable<KeyValuePair<string, string>> cookieJar) =>
        string.Join("; ", cookieJar.Select(static x => $"{x.Key}={x.Value}"));

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static void CaptureSetCookies(HttpResponseMessage response, IDictionary<string, string> cookieJar)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            var pair = value.Split(';', 2, StringSplitOptions.TrimEntries)[0];
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0 || separatorIndex >= pair.Length - 1)
            {
                continue;
            }

            var name = pair[..separatorIndex].Trim();
            var cookieValue = pair[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cookieValue))
            {
                continue;
            }

            cookieJar[name] = cookieValue;
        }
    }

    private static async Task<SessionHttpResponse> SendWithRedirectsAsync(
        HttpClient client,
        Uri initialUri,
        IDictionary<string, string> cookieJar,
        CancellationToken cancellationToken)
    {
        var currentUri = initialUri;

        for (var i = 0; i <= MaxRedirects; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            request.Headers.TryAddWithoutValidation("Accept", "application/json,text/plain,*/*");

            if (cookieJar.Count > 0)
            {
                request.Headers.TryAddWithoutValidation("Cookie", BuildCookieHeader(cookieJar));
            }

            using var response = await client.SendAsync(request, cancellationToken);
            CaptureSetCookies(response, cookieJar);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
                continue;
            }

            var content = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);

            return new SessionHttpResponse(response.StatusCode, content, currentUri);
        }

        return new SessionHttpResponse(HttpStatusCode.BadGateway, null, currentUri);
    }

    private sealed record SessionHttpResponse(HttpStatusCode StatusCode, string? Content, Uri FinalUri)
    {
        public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
    }
}
