using System.Globalization;
using System.Text.RegularExpressions;

namespace TradeWithBeast.Services;

public enum PaperAgentIntent
{
    Unknown,
    Greeting,
    Help,
    Status,
    ConfigureAndStart,
    Stop,
    Confirm,
    Reject,
}

public static partial class PaperAgentCommandParser
{
    public sealed record ParsedCommand(
        decimal? AllocatedCapital,
        decimal? ProfitTarget,
        decimal? MaxDailyLoss,
        PaperAgentIntent Intent,
        bool StopRequested,
        bool ConfirmRequested,
        bool RejectRequested);

    public static ParsedCommand Parse(string message)
    {
        var text = message.Trim().ToLowerInvariant();
        var stop = Regex.IsMatch(
            text,
            @"^(?:please\s+)?(?:stop|pause|disable|halt)(?:\s+(?:the\s+)?(?:paper\s+)?(?:trading|agent|automation))?\s*[.!]?$");
        var confirm = Regex.IsMatch(
            text,
            @"^(?:yes|confirm|confirmed|proceed|approve|approved|accept|accepted|go ahead)\s*[.!]?$");
        var reject = Regex.IsMatch(
            text,
            @"^(?:no|reject|rejected|cancel|decline|skip|do not proceed|don't proceed)\s*[.!]?$");
        var use = ReadAmount(text, @"(?:use|deploy|trade\s+with|allocate)\s+(?:only\s+)?");
        var capital = ReadAmount(text, @"(?:capital|funds?|balance)\s+(?:is|of|=|:)?\s*")
            ?? ReadAmount(text, @"(?:i\s+have|with)\s+");
        var target = ReadAmount(text, @"(?:target|profit)\s+(?:is|of|=|:)?\s*");
        var loss = ReadAmount(text, @"(?:max(?:imum)?\s+loss|stop[\s-]*loss|loss\s+limit|daily\s+loss)\s+(?:is|of|=|:)?\s*");
        var allocated = use ?? capital;
        var intent = stop
            ? PaperAgentIntent.Stop
            : confirm
                ? PaperAgentIntent.Confirm
                : reject
                    ? PaperAgentIntent.Reject
                    : Regex.IsMatch(text, @"^(?:hi|hello|hey|good\s+(?:morning|afternoon|evening))[\s!.]*$")
                        ? PaperAgentIntent.Greeting
                        : Regex.IsMatch(text, @"^(?:help|what can you do|how does (?:the )?agent work)[\s?.!]*$")
                            ? PaperAgentIntent.Help
                            : Regex.IsMatch(text, @"^(?:status|show status|what is (?:the )?(?:agent )?status|are you running)[\s?.!]*$")
                                ? PaperAgentIntent.Status
                                : allocated is not null
                                    || target is not null
                                    || loss is not null
                                    || Regex.IsMatch(text, @"\b(?:start|run|begin|enable)\b.*\b(?:paper|agent|trading|automation)\b")
                                        ? PaperAgentIntent.ConfigureAndStart
                                        : PaperAgentIntent.Unknown;
        return new ParsedCommand(allocated, target, loss, intent, stop, confirm, reject);
    }

    private static decimal? ReadAmount(string text, string prefix)
    {
        var match = Regex.Match(
            text,
            prefix + @"₹?\s*(\d+(?:\.\d+)?)\s*(crore|cr|lakhs?|lacs?|k|thousand)?",
            RegexOptions.IgnoreCase);
        if (!match.Success
            || !decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }
        var suffix = match.Groups[2].Value;
        var multiplier = suffix switch
        {
            "crore" or "cr" => 10_000_000m,
            "lakh" or "lakhs" or "lac" or "lacs" => 100_000m,
            "k" or "thousand" => 1_000m,
            _ => 1m,
        };
        return value * multiplier;
    }
}
