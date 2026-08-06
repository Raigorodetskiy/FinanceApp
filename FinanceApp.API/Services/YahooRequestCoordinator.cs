using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

public sealed class YahooFinanceOptions
{
    public TimeSpan MinRequestInterval { get; init; } = TimeSpan.FromSeconds(1.5);
    public TimeSpan CooldownDuration { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan QuoteCacheDuration { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed record YahooRequestExecutionOptions(
    int MaxAttempts,
    TimeSpan RetryBaseDelay,
    TimeSpan RetryMaxDelay,
    TimeSpan? CacheDuration = null);

public sealed record YahooHttpResponse(
    HttpStatusCode StatusCode,
    string? Content,
    RetryConditionHeaderValue? RetryAfter = null,
    bool IsFromCache = false,
    bool IsCooldownHit = false,
    DateTimeOffset? CooldownUntilUtc = null)
{
    public bool IsSuccessStatusCode => (int)StatusCode is >= 200 and < 300;
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    public static YahooHttpResponse Cooldown(DateTimeOffset cooldownUntilUtc, TimeSpan remaining) =>
        new(
            HttpStatusCode.TooManyRequests,
            "Too Many Requests",
            new RetryConditionHeaderValue(remaining),
            IsFromCache: false,
            IsCooldownHit: true,
            CooldownUntilUtc: cooldownUntilUtc);
}

public interface IYahooRequestCoordinator
{
    Task<YahooHttpResponse> GetAsync(
        string url,
        string requestLabel,
        YahooRequestExecutionOptions executionOptions,
        CancellationToken cancellationToken = default);
}

public sealed class YahooRequestCoordinator : IYahooRequestCoordinator
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<YahooRequestCoordinator> _logger;
    private readonly IOptions<YahooFinanceOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly ConcurrentDictionary<string, Lazy<Task<YahooHttpResponse>>> _inflight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedYahooResponse> _cache = new(StringComparer.Ordinal);
    private readonly object _stateLock = new();

    private DateTimeOffset _nextRequestStartUtc = DateTimeOffset.MinValue;
    private DateTimeOffset? _cooldownUntilUtc;

    public YahooRequestCoordinator(
        IHttpClientFactory httpClientFactory,
        ILogger<YahooRequestCoordinator> logger,
        IOptions<YahooFinanceOptions> options,
        TimeProvider? timeProvider = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _delayAsync = delayAsync ?? ((delay, cancellationToken) => Task.Delay(delay, cancellationToken));
    }

    public Task<YahooHttpResponse> GetAsync(
        string url,
        string requestLabel,
        YahooRequestExecutionOptions executionOptions,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestLabel);

        var normalizedExecutionOptions = Normalize(executionOptions);

        if (TryGetActiveCooldown(requestLabel, out var cooldownResponse))
        {
            return Task.FromResult(cooldownResponse);
        }

        if (TryGetCachedResponse(url, requestLabel, normalizedExecutionOptions.CacheDuration, out var cachedResponse))
        {
            return Task.FromResult(cachedResponse);
        }

        var created = false;
        var lazy = new Lazy<Task<YahooHttpResponse>>(
            () => ExecuteAsync(url, requestLabel, normalizedExecutionOptions),
            LazyThreadSafetyMode.ExecutionAndPublication);

        var inflight = _inflight.GetOrAdd(url, _ =>
        {
            created = true;
            return lazy;
        });

        if (!created)
        {
            _logger.LogInformation("Yahoo request coalesced for {RequestLabel}.", requestLabel);
        }

        return AwaitAndCleanupAsync(url, inflight, cancellationToken);
    }

    private async Task<YahooHttpResponse> AwaitAndCleanupAsync(
        string url,
        Lazy<Task<YahooHttpResponse>> inflight,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WaitAsync(inflight.Value, cancellationToken);
        }
        finally
        {
            if (inflight.IsValueCreated && inflight.Value.IsCompleted)
            {
                _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<YahooHttpResponse>>>(url, inflight));
            }
        }
    }

    private async Task<YahooHttpResponse> ExecuteAsync(
        string url,
        string requestLabel,
        YahooRequestExecutionOptions executionOptions)
    {
        for (var attempt = 1; attempt <= executionOptions.MaxAttempts; attempt++)
        {
            try
            {
                var response = await SendOnceAsync(url, requestLabel);
                if (response.IsRateLimited)
                {
                    return response;
                }

                if (IsTransient(response.StatusCode) && attempt < executionOptions.MaxAttempts)
                {
                    var delay = GetRetryDelay(attempt, executionOptions);
                    _logger.LogWarning(
                        "Yahoo request transient failure for {RequestLabel} status={StatusCode}; retry {Attempt}/{MaxAttempts} in {DelayMs}ms",
                        requestLabel,
                        (int)response.StatusCode,
                        attempt,
                        executionOptions.MaxAttempts,
                        (int)delay.TotalMilliseconds);
                    await _delayAsync(delay, CancellationToken.None);
                    continue;
                }

                if (response.IsSuccessStatusCode && executionOptions.CacheDuration is { } cacheDuration && cacheDuration > TimeSpan.Zero)
                {
                    _cache[url] = new CachedYahooResponse(response, _timeProvider.GetUtcNow().Add(cacheDuration));
                }

                return response;
            }
            catch (TaskCanceledException ex) when (attempt < executionOptions.MaxAttempts)
            {
                var delay = GetRetryDelay(attempt, executionOptions);
                _logger.LogWarning(
                    ex,
                    "Yahoo request timed out for {RequestLabel}; retry {Attempt}/{MaxAttempts} in {DelayMs}ms",
                    requestLabel,
                    attempt,
                    executionOptions.MaxAttempts,
                    (int)delay.TotalMilliseconds);
                await _delayAsync(delay, CancellationToken.None);
            }
            catch (HttpRequestException ex) when (attempt < executionOptions.MaxAttempts)
            {
                var delay = GetRetryDelay(attempt, executionOptions);
                _logger.LogWarning(
                    ex,
                    "Yahoo request network error for {RequestLabel}; retry {Attempt}/{MaxAttempts} in {DelayMs}ms",
                    requestLabel,
                    attempt,
                    executionOptions.MaxAttempts,
                    (int)delay.TotalMilliseconds);
                await _delayAsync(delay, CancellationToken.None);
            }
        }

        throw new InvalidOperationException("Yahoo request coordinator exhausted retries unexpectedly.");
    }

    private async Task<YahooHttpResponse> SendOnceAsync(string url, string requestLabel)
    {
        await _requestGate.WaitAsync(CancellationToken.None);
        try
        {
            if (TryGetActiveCooldown(requestLabel, out var cooldownResponse))
            {
                return cooldownResponse;
            }

            var minRequestInterval = NormalizePositive(_options.Value.MinRequestInterval);
            var now = _timeProvider.GetUtcNow();
            if (_nextRequestStartUtc > now)
            {
                var wait = _nextRequestStartUtc - now;
                _logger.LogInformation(
                    "Yahoo throttle waiting {DelayMs}ms before starting {RequestLabel}.",
                    (int)wait.TotalMilliseconds,
                    requestLabel);
                await _delayAsync(wait, CancellationToken.None);
                now = _timeProvider.GetUtcNow();
            }

            _nextRequestStartUtc = now.Add(minRequestInterval);

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            client.Timeout = NormalizePositive(_options.Value.RequestTimeout, TimeSpan.FromSeconds(10));

            using var response = await client.GetAsync(url, CancellationToken.None);
            var content = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(CancellationToken.None);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                ActivateCooldown(response.Headers.RetryAfter, requestLabel);
            }

            return new YahooHttpResponse(response.StatusCode, content, response.Headers.RetryAfter);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private bool TryGetActiveCooldown(string requestLabel, out YahooHttpResponse response)
    {
        lock (_stateLock)
        {
            var now = _timeProvider.GetUtcNow();
            if (_cooldownUntilUtc is null)
            {
                response = default!;
                return false;
            }

            if (_cooldownUntilUtc <= now)
            {
                _logger.LogInformation("Yahoo cooldown expired at {CooldownUntilUtc}.", _cooldownUntilUtc.Value);
                _cooldownUntilUtc = null;
                response = default!;
                return false;
            }

            var remaining = _cooldownUntilUtc.Value - now;
            _logger.LogInformation(
                "Yahoo cooldown active for {RequestLabel}; failing fast for another {DelayMs}ms until {CooldownUntilUtc}.",
                requestLabel,
                (int)remaining.TotalMilliseconds,
                _cooldownUntilUtc.Value);
            response = YahooHttpResponse.Cooldown(_cooldownUntilUtc.Value, remaining);
            return true;
        }
    }

    private void ActivateCooldown(RetryConditionHeaderValue? retryAfter, string requestLabel)
    {
        var now = _timeProvider.GetUtcNow();
        var retryAfterDelay = GetRetryAfterDelay(retryAfter, now);
        var cooldownDelay = retryAfterDelay ?? NormalizePositive(_options.Value.CooldownDuration, TimeSpan.FromMinutes(30));
        var cooldownUntilUtc = now.Add(cooldownDelay);

        lock (_stateLock)
        {
            if (_cooldownUntilUtc is null || cooldownUntilUtc > _cooldownUntilUtc.Value)
            {
                _cooldownUntilUtc = cooldownUntilUtc;
            }
        }

        _logger.LogWarning(
            "Yahoo cooldown activated by {RequestLabel}; retryAfterMs={RetryAfterMs} cooldownMs={CooldownMs} cooldownUntilUtc={CooldownUntilUtc}.",
            requestLabel,
            retryAfterDelay.HasValue ? (int)retryAfterDelay.Value.TotalMilliseconds : 0,
            (int)cooldownDelay.TotalMilliseconds,
            cooldownUntilUtc);
    }

    private bool TryGetCachedResponse(
        string url,
        string requestLabel,
        TimeSpan? cacheDuration,
        out YahooHttpResponse response)
    {
        if (cacheDuration is not { } configuredCacheDuration || configuredCacheDuration <= TimeSpan.Zero)
        {
            response = default!;
            return false;
        }

        if (!_cache.TryGetValue(url, out var cached))
        {
            response = default!;
            return false;
        }

        if (cached.ExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            _cache.TryRemove(url, out _);
            response = default!;
            return false;
        }

        _logger.LogInformation("Yahoo request served from short-term cache for {RequestLabel}.", requestLabel);
        response = cached.Response with { IsFromCache = true };
        return true;
    }

    private TimeSpan GetRetryDelay(int attempt, YahooRequestExecutionOptions executionOptions)
    {
        var exponentialMs = Math.Min(
            executionOptions.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1),
            executionOptions.RetryMaxDelay.TotalMilliseconds);
        var jitterMs = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(exponentialMs + jitterMs);
    }

    private static TimeSpan? GetRetryAfterDelay(RetryConditionHeaderValue? retryAfter, DateTimeOffset now)
    {
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - now;
            if (delay > TimeSpan.Zero)
            {
                return delay;
            }
        }

        return null;
    }

    private static bool IsTransient(HttpStatusCode statusCode) => (int)statusCode >= 500;

    private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled || task.IsCompleted)
        {
            return await task;
        }

        var cancellationTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancellationTask.TrySetCanceled(cancellationToken));
        var completed = await Task.WhenAny(task, cancellationTask.Task);
        return completed == task
            ? await task
            : throw new OperationCanceledException(cancellationToken);
    }

    private static YahooRequestExecutionOptions Normalize(YahooRequestExecutionOptions options) =>
        options with
        {
            MaxAttempts = Math.Max(1, options.MaxAttempts),
            RetryBaseDelay = NormalizePositive(options.RetryBaseDelay, TimeSpan.FromMilliseconds(500)),
            RetryMaxDelay = NormalizePositive(options.RetryMaxDelay, TimeSpan.FromSeconds(20))
        };

    private static TimeSpan NormalizePositive(TimeSpan value, TimeSpan? fallback = null) =>
        value > TimeSpan.Zero
            ? value
            : fallback ?? TimeSpan.Zero;

    private sealed record CachedYahooResponse(YahooHttpResponse Response, DateTimeOffset ExpiresAtUtc);
}
