using System.Text.Json;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public sealed record OfficialZReadingEnrichmentPayload(
    string Schema,
    OfficialXReadingLifecycleSummary Lifecycle,
    OfficialXReadingAdjustedSummary AdjustedSummary,
    IReadOnlyList<OfficialXReadingVatBucketSummary> VatBuckets)
{
    public const string SchemaV1 = "SALRYN_OFFICIAL_Z_READING_ENRICHMENT_V1";
}

public static class OfficialZReadingEnrichmentSerializer
{
    public static string Serialize(OfficialZReadingEnrichmentPayload payload)
    {
        return JsonSerializer.Serialize(payload);
    }

    public static Result<OfficialZReadingEnrichmentPayload?> TryDeserialize(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return Result<OfficialZReadingEnrichmentPayload?>.Success(null);
        }

        try
        {
            OfficialZReadingEnrichmentPayload? payload =
                JsonSerializer.Deserialize<OfficialZReadingEnrichmentPayload>(metadataJson);

            if (payload is null ||
                !string.Equals(payload.Schema, OfficialZReadingEnrichmentPayload.SchemaV1, StringComparison.Ordinal))
            {
                return Result<OfficialZReadingEnrichmentPayload?>.Success(null);
            }

            return Result<OfficialZReadingEnrichmentPayload?>.Success(payload);
        }
        catch (JsonException ex)
        {
            return Result<OfficialZReadingEnrichmentPayload?>.Fail(Error.Failure(
                "OfficialZReading.InvalidEnrichmentPayload",
                ex.Message));
        }
    }
}
