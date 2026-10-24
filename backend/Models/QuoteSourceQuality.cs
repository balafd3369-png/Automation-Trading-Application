using System.Text.Json.Serialization;

namespace TradeWithBeast.Models;

/// <summary>How the board <c>last</c> price was chosen (for UI labels and production debugging).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QuoteSourceQuality
{
    VerifiedLive,
    VerifiedClose,
    FallbackClose,
    SeedData,
    Placeholder,
    Rejected,
}
