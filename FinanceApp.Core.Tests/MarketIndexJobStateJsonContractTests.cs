using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceApp.API.Infrastructure;
using FinanceApp.API.Models;
using Xunit;

namespace FinanceApp.Core.Tests;

public class MarketIndexJobStateJsonContractTests
{
    [Fact]
    public void BatchQuoteJobResponse_QueuedState_SerializesAsString()
    {
        var payload = new IndexConstituentsBatchQuoteRefreshJobResponse
        {
            JobId = "job-1",
            MarketIndexId = 1,
            State = IndexConstituentsBatchQuoteRefreshJobState.Queued,
            CreatedAtUtc = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(payload, CreateMvcJsonOptions());

        Assert.Contains("\"state\":\"Queued\"", json);
        Assert.DoesNotContain("\"state\":0", json);
    }

    [Theory]
    [InlineData(IndexConstituentsBatchQuoteRefreshJobState.Running, "Running")]
    [InlineData(IndexConstituentsBatchQuoteRefreshJobState.Succeeded, "Succeeded")]
    [InlineData(IndexConstituentsBatchQuoteRefreshJobState.RateLimited, "RateLimited")]
    [InlineData(IndexConstituentsBatchQuoteRefreshJobState.Failed, "Failed")]
    [InlineData(IndexConstituentsBatchQuoteRefreshJobState.Interrupted, "Interrupted")]
    public void BatchQuoteJobResponse_StateNames_SerializeExactly(
        IndexConstituentsBatchQuoteRefreshJobState state,
        string expected)
    {
        var payload = new IndexConstituentsBatchQuoteRefreshJobResponse
        {
            JobId = "job-1",
            MarketIndexId = 1,
            State = state,
            CreatedAtUtc = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(payload, CreateMvcJsonOptions());
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, document.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public void HistoryRefreshJobResponse_State_SerializesAsString()
    {
        var payload = new IndexConstituentHistoryRefreshJobResponse
        {
            JobId = "h-job-1",
            MarketIndexId = 1,
            StockId = 10,
            State = IndexConstituentHistoryRefreshJobState.Queued,
            CreatedAtUtc = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(payload, CreateMvcJsonOptions());
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Queued", document.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.String, document.RootElement.GetProperty("state").ValueKind);
    }

    [Fact]
    public void UnrelatedEnumContract_RemainsNumeric()
    {
        var payload = new IndexConstituentsBatchQuoteRefreshJobEnqueueResult
        {
            Status = IndexConstituentsBatchQuoteRefreshJobEnqueueStatus.Enqueued
        };

        var json = JsonSerializer.Serialize(payload, CreateMvcJsonOptions());
        using var document = JsonDocument.Parse(json);

        var statusProperty = document.RootElement.GetProperty("status");
        Assert.Equal(JsonValueKind.Number, statusProperty.ValueKind);
        Assert.Equal(0, statusProperty.GetInt32());
    }

    private static JsonSerializerOptions CreateMvcJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            ReferenceHandler = ReferenceHandler.IgnoreCycles
        };
        options.Converters.Add(new UtcDateTimeJsonConverter());
        options.Converters.Add(new UtcNullableDateTimeJsonConverter());
        return options;
    }
}
