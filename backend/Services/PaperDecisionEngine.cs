using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed record StrategyLeg(
    string Symbol,
    string Underlying,
    string InstrumentType,
    string Side,
    int Ratio,
    decimal Price,
    decimal? Strike = null,
    string? OptionRight = null,
    DateTimeOffset? Expiry = null,
    QuoteMetadata? Quote = null);

public sealed record StrategyCandidate(
    string Strategy,
    string Underlying,
    string Direction,
    decimal EvidenceScore,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<StrategyLeg> Legs,
    decimal StopLoss,
    decimal Target,
    decimal MaxRiskPerUnit,
    decimal MaxRewardPerUnit,
    DateTimeOffset DataTimestamp);

public interface IPaperStrategy
{
    IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous);
}

public sealed class StockZoneLongStrategy : IPaperStrategy
{
    public IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous)
    {
        foreach (var item in context.Where(x => x.InstrumentType == "STOCK"))
        {
            var b1Fact = item.Evidence.Facts.FirstOrDefault(x => x.StartsWith("B1=", StringComparison.Ordinal));
            var stopFact = item.Evidence.Facts.FirstOrDefault(x => x.StartsWith("S=", StringComparison.Ordinal));
            var targetFact = item.Evidence.Facts.FirstOrDefault(x => x.StartsWith("B2=", StringComparison.Ordinal));
            if (!TryFact(b1Fact, out var b1) || !TryFact(stopFact, out var stop)
                || !TryFact(targetFact, out var target) || !previous.TryGetValue(item.Symbol, out var prior)
                || PaperTradeRules.DetectZoneCross(prior, item.Last, b1, decimal.MinValue) != PaperEntrySignal.Long)
            {
                continue;
            }
            var risk = item.Last - stop;
            var reward = target - item.Last;
            if (risk <= 0 || reward <= 0)
            {
                continue;
            }
            var evidence = new List<string> { $"price crossed B1 {b1:0.##}", $"zone={item.Evidence.Zone}" };
            if (item.Evidence.Trend == "UP") evidence.Add("fast EMA above slow EMA");
            if (item.Evidence.MomentumPercent > 0) evidence.Add("positive momentum");
            yield return new StrategyCandidate(
                "STOCK_ZONE_LONG", item.Symbol, "BULLISH", 55m + 10m * (evidence.Count - 2),
                evidence, [new(item.Symbol, item.Symbol, "STOCK", "BUY", 1, item.Last, Quote: item.Quote)],
                stop, target, risk, reward, item.Quote.DataTimestamp);
        }
    }

    private static bool TryFact(string? fact, out decimal value)
    {
        value = 0;
        return fact is not null
            && decimal.TryParse(fact[(fact.IndexOf('=') + 1)..], System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}

public sealed class IndexOptionStrategy : IPaperStrategy
{
    public IEnumerable<StrategyCandidate> Generate(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous)
    {
        foreach (var index in context.Where(x => x.InstrumentType == "INDEX"))
        {
            var options = context.Where(x => x.InstrumentType == "OPTION" && x.Symbol.StartsWith(index.Symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            if (options.Count == 0 || index.Evidence.Trend == "UNKNOWN" || index.Evidence.Regime == "UNKNOWN")
            {
                continue;
            }
            var right = index.Evidence.Trend == "UP" ? "CE" : "PE";
            var direction = right == "CE" ? "BULLISH" : "BEARISH";
            var ordered = options.Where(x => x.Symbol.EndsWith(right, StringComparison.OrdinalIgnoreCase) && x.Last > 0)
                .OrderBy(x => Math.Abs(ReadStrike(x.Symbol) - index.Last)).ToList();
            if (ordered.Count == 0)
            {
                continue;
            }
            var longOption = ordered[0];
            var longStrike = ReadStrike(longOption.Symbol);
            var expiry = ReadExpiry(longOption.Evidence.Facts);
            var longPrice = BuyPrice(longOption);
            var evidence = new List<string> { $"underlying trend={index.Evidence.Trend}", $"regime={index.Evidence.Regime}" };
            var stop = decimal.Round(longPrice * 0.7m, 2);
            var target = decimal.Round(longPrice * 2.2m, 2);
            if (index.Evidence.Regime == "TRENDING")
            {
                yield return new StrategyCandidate(
                    "INDEX_OPTION_LONG", index.Symbol, direction, 65m, evidence,
                    [new(longOption.Symbol, index.Symbol, "OPTION", "BUY", 1, longPrice, longStrike, right, expiry, longOption.Quote)],
                    stop, target, longPrice, target - longPrice, longOption.Quote.DataTimestamp);
            }

            if (index.Evidence.Regime is "RANGE" or "QUIET")
            {
                var shortRight = index.Evidence.Trend == "UP" ? "PE" : "CE";
                var shortDirection = shortRight == "PE" ? "BULLISH" : "BEARISH";
                var shortOptionCandidate = options
                    .Where(option => option.Symbol.EndsWith(shortRight, StringComparison.OrdinalIgnoreCase)
                        && option.Last > 0
                        && (shortRight == "PE"
                            ? ReadStrike(option.Symbol) < index.Last
                            : ReadStrike(option.Symbol) > index.Last))
                    .OrderBy(option => Math.Abs(ReadStrike(option.Symbol) - index.Last))
                    .FirstOrDefault();
                if (shortOptionCandidate is not null)
                {
                    var shortPrice = SellPrice(shortOptionCandidate);
                    var nakedStrike = ReadStrike(shortOptionCandidate.Symbol);
                    var shortExpiry = ReadExpiry(shortOptionCandidate.Evidence.Facts);
                    yield return new StrategyCandidate(
                        "NAKED_INDEX_OPTION_SHORT",
                        index.Symbol,
                        shortDirection,
                        index.Evidence.Regime == "QUIET" ? 72m : 68m,
                        [
                            $"underlying trend={index.Evidence.Trend}",
                            $"regime={index.Evidence.Regime}",
                            "single short option; loss can exceed stop during a price gap",
                        ],
                        [
                            new(
                                shortOptionCandidate.Symbol,
                                index.Symbol,
                                "OPTION",
                                "SELL",
                                1,
                                shortPrice,
                                nakedStrike,
                                shortRight,
                                shortExpiry,
                                shortOptionCandidate.Quote),
                        ],
                        decimal.Round(shortPrice * 1.5m, 2),
                        decimal.Round(shortPrice * 0.5m, 2),
                        shortPrice * 0.5m,
                        shortPrice * 0.5m,
                        shortOptionCandidate.Quote.DataTimestamp);
                }
            }

            var hedge = ordered
                .Where(x => right == "CE" ? ReadStrike(x.Symbol) > longStrike : ReadStrike(x.Symbol) < longStrike)
                .OrderBy(x => Math.Abs(ReadStrike(x.Symbol) - longStrike))
                .FirstOrDefault();
            if (hedge is null)
            {
                continue;
            }
            var hedgeStrike = ReadStrike(hedge.Symbol);
            var width = Math.Abs(hedgeStrike - longStrike);
            var hedgeSellPrice = SellPrice(hedge);
            var debit = longPrice - hedgeSellPrice;
            if (width > 0 && debit > 0 && debit < width)
            {
                yield return new StrategyCandidate(
                    "VERTICAL_DEBIT_SPREAD", index.Symbol, direction,
                    index.Evidence.Regime == "TRENDING" ? 80m : 62m,
                    [.. evidence, $"defined risk debit={debit:0.##}", $"width={width:0.##}"],
                    [
                        new(longOption.Symbol, index.Symbol, "OPTION", "BUY", 1, longPrice, longStrike, right, expiry, longOption.Quote),
                        new(hedge.Symbol, index.Symbol, "OPTION", "SELL", 1, hedgeSellPrice, hedgeStrike, right, expiry, hedge.Quote),
                    ],
                    decimal.Round(debit * 0.55m, 2), decimal.Round(debit + (width - debit) * 0.65m, 2),
                    debit, width - debit, MinTime(longOption.Quote.DataTimestamp, hedge.Quote.DataTimestamp));
            }

            var creditRight = direction == "BULLISH" ? "PE" : "CE";
            var creditOptions = options
                .Where(x => x.Symbol.EndsWith(creditRight, StringComparison.OrdinalIgnoreCase) && x.Last > 0)
                .OrderBy(x => Math.Abs(ReadStrike(x.Symbol) - index.Last))
                .ToList();
            var shortOption = creditOptions.FirstOrDefault();
            var shortStrike = shortOption is null ? 0 : ReadStrike(shortOption.Symbol);
            var protective = creditOptions
                .Where(x => creditRight == "PE" ? ReadStrike(x.Symbol) < shortStrike : ReadStrike(x.Symbol) > shortStrike)
                .OrderBy(x => Math.Abs(ReadStrike(x.Symbol) - shortStrike))
                .FirstOrDefault();
            if (shortOption is null || protective is null)
            {
                continue;
            }
            var shortSellPrice = SellPrice(shortOption);
            var protectiveBuyPrice = BuyPrice(protective);
            var credit = shortSellPrice - protectiveBuyPrice;
            var creditWidth = Math.Abs(ReadStrike(shortOption.Symbol) - ReadStrike(protective.Symbol));
            if (credit > 0
                && creditWidth > credit
                && index.Evidence.Regime is "VOLATILE" or "RANGE")
            {
                yield return new StrategyCandidate(
                    "HEDGED_VERTICAL_CREDIT_SPREAD", index.Symbol, direction,
                    index.Evidence.Regime == "VOLATILE" ? 82m : 70m,
                    ["protective option included", $"defined risk credit={credit:0.##}", $"width={creditWidth:0.##}"],
                    [
                        new(shortOption.Symbol, index.Symbol, "OPTION", "SELL", 1, shortSellPrice, ReadStrike(shortOption.Symbol), creditRight, expiry, shortOption.Quote),
                        new(protective.Symbol, index.Symbol, "OPTION", "BUY", 1, protectiveBuyPrice, ReadStrike(protective.Symbol), creditRight, expiry, protective.Quote),
                    ],
                    decimal.Round(credit * 1.8m, 2), decimal.Round(credit * 0.35m, 2),
                    creditWidth - credit, credit, MinTime(shortOption.Quote.DataTimestamp, protective.Quote.DataTimestamp));
            }
        }
    }

    private static decimal ReadStrike(string symbol)
    {
        var parts = symbol.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Reverse().Skip(1).Select(x => decimal.TryParse(x, out var value) ? value : 0m).FirstOrDefault(x => x > 0);
    }

    private static DateTimeOffset? ReadExpiry(IReadOnlyList<string> facts)
    {
        var value = facts.FirstOrDefault(x => x.StartsWith("expiry=", StringComparison.Ordinal))?[7..];
        return DateTimeOffset.TryParse(value, out var expiry) ? expiry : null;
    }

    private static DateTimeOffset MinTime(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;

    private static decimal BuyPrice(MarketContextSnapshot option) =>
        option.Quote.Ask is > 0 ? option.Quote.Ask.Value : option.Last;

    private static decimal SellPrice(MarketContextSnapshot option) =>
        option.Quote.Bid is > 0 ? option.Quote.Bid.Value : option.Last;
}

public sealed class PaperDecisionEngine(IEnumerable<IPaperStrategy> strategies)
{
    public IReadOnlyList<StrategyCandidate> Rank(
        IReadOnlyList<MarketContextSnapshot> context,
        IReadOnlyDictionary<string, decimal> previous) =>
        strategies.SelectMany(x => x.Generate(context, previous))
            .OrderByDescending(x => x.EvidenceScore)
            .ThenByDescending(x => x.MaxRewardPerUnit / Math.Max(0.01m, x.MaxRiskPerUnit))
            .ThenBy(x => x.Strategy, StringComparer.Ordinal)
            .ThenBy(x => x.Underlying, StringComparer.Ordinal)
            .ToList();
}
