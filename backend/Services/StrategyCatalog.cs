using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public static class StrategyCatalog
{
    public const decimal BreakoutCandleStopBufferPoints = 10m;

    public const string TenAmHighFadeId = "bala-10am-high-fade";
    /** @deprecated Use TenAmHighFadeId */
    public const string Bala10AmHighFadeId = TenAmHighFadeId;

    public const string TenAmLowBreakToDayOpenId = "ten-am-low-break-to-day-open";
    public const string StockB1RejectionId = "stock-b1-rejection-to-s2";
    public const string ZoneB1BreakId = "zone-b1-break-b2";
    public const string ZoneS1BreakId = "zone-s1-break-s2";
    public const string ZoneS2BounceId = "zone-s2-bounce-b1";

    public static IReadOnlyList<StrategyCatalogEntry> All() =>
    [
        ZoneB1BreakToB2(),
        ZoneS1BreakToS2(),
        ZoneS2BounceToB1(),
        StockB1RejectionToS2(),
        TenAmHighFadeToDayLow(),
        TenAmLowBreakToDayOpen(),
    ];

    public static StrategyCatalogEntry? Find(string id) =>
        All().FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static StrategyCatalogEntry TenAmHighFadeToDayLow() => new(
        Id: TenAmHighFadeId,
        Name: "10 AM High Fade to Day Low",
        Author: "Bala",
        Summary:
            "Morning push builds H10. After 11:00, a 5m close below H10 fades to day low. Initial stop: rejection "
            + $"candle high + {BreakoutCandleStopBufferPoints} pts; then breakeven and H10 protection.",
        Timeframe: "15 minute candles",
        Session: "NSE cash · 9:15–15:30 IST",
        Status: "paper-ready",
        Tags: ["intraday", "index", "stocks", "morning-structure", "rejection", "breakeven"],
        Playbook: new StrategyPlaybook(
            Intent:
                "SHORT fade when the 10 AM high fails after 11:00. Target session day low. Stop tied to the break "
                + "candle; protect at breakeven then H10 once favorable.",
            Phases:
            [
                new StrategyPhase(
                    "Opening push",
                    "9:15–10:00 IST",
                    "Price often rises into the first hour. Lock H10 = highest high from 9:15 through 10:00 on 5m bars."),
                new StrategyPhase(
                    "Fade entry",
                    "After 11:00 IST — any time",
                    "Had trade at/above H10; then a 5m candle closes below H10 → SHORT. Target: session day low only."),
                new StrategyPhase(
                    "Initial stop",
                    "On entry candle",
                    $"Stop = that rejection candle’s high + {BreakoutCandleStopBufferPoints} index points (exact buffer "
                    + "you specified)."),
                new StrategyPhase(
                    "Protect profits",
                    "Once favorable (down)",
                    "Move stop to breakeven (zero profit), then to H10 so a rally back through H10 does not put the user "
                    + "in a loss; continued drop = profit toward day low."),
                new StrategyPhase(
                    "Re-entry",
                    "Same day",
                    "Up to 2 fade entries if stopped before protection is applied."),
            ],
            PrimarySetup: new StrategyTradePlan(
                Name: "10 AM high rejection fade",
                Direction: "SHORT (sell / bearish)",
                ValidFromIst: "After 11:00 IST — any time until session end",
                Trigger:
                    "Price traded at or above H10; 5m candle closes below H10 at or after 11:00 IST.",
                Target: "Session day low only.",
                StopLoss:
                    $"Initial: high of the break candle (first close below H10) + {BreakoutCandleStopBufferPoints} points. "
                    + "Then breakeven → stop at H10 once trade is favorable.",
                Reentry: "Max 2 entries per symbol per day on this setup."),
            AlternateSetup: null,
            Risk: new StrategyRiskRules(
                MaxAttemptsPerDay: "2 fade entries per symbol per day.",
                Notes:
                    "Stop buffer is fixed at 10 points above the rejection candle high. Paper automation planned.")));

    private static StrategyCatalogEntry TenAmLowBreakToDayOpen() => new(
        Id: TenAmLowBreakToDayOpenId,
        Name: "10 AM Low Break to Day Open",
        Author: "Bala",
        Summary:
            "Negative morning: L10 is the 10 AM low anchor. After a dip, a 5m close back above L10 targets today’s open. "
            + $"Stop: break candle low − {BreakoutCandleStopBufferPoints} pts; breakeven then L10 protection.",
        Timeframe: "15 minute candles",
        Session: "NSE cash · 9:15–15:30 IST",
        Status: "paper-ready",
        Tags: ["intraday", "index", "stocks", "morning-structure", "recovery", "breakeven"],
        Playbook: new StrategyPlaybook(
            Intent:
                "LONG recovery when a weak open makes the 10 AM window the morning low zone, price dips slightly, then "
                + "reclaims L10 and runs toward the day open (e.g. Nifty 29 Sep 2026: open ~22,732, low ~22,570, "
                + "recovery toward open after reclaiming morning structure).",
            Phases:
            [
                new StrategyPhase(
                    "Negative start",
                    "9:15–10:00 IST",
                    "Session opens weak vs day open (selling pressure). Lock L10 = lowest low from 9:15 through 10:00 IST "
                    + "on 5m bars — the morning ‘max negative’ anchor."),
                new StrategyPhase(
                    "Shakeout",
                    "After 10:00",
                    "Price may probe slightly below L10 before buyers return (optional dip under the 10 AM low)."),
                new StrategyPhase(
                    "Break entry",
                    "After 10:00 IST — any time",
                    "5m candle closes above L10 (reclaim / break of the 10 AM low level) while morning context was "
                    + "negative → LONG toward day open."),
                new StrategyPhase(
                    "Initial stop",
                    "On entry candle",
                    $"Stop = that break candle’s low − {BreakoutCandleStopBufferPoints} index points (10 points below "
                    + "the candle that broke L10)."),
                new StrategyPhase(
                    "Protect profits",
                    "Once favorable (up)",
                    "Move stop to breakeven (zero profit), then to L10 so a dip back through L10 does not put the user "
                    + "in a loss; continued rise = profit toward day open."),
                new StrategyPhase(
                    "Re-entry",
                    "Same day",
                    "Up to 2 long entries if stopped before protection is applied."),
            ],
            PrimarySetup: new StrategyTradePlan(
                Name: "10 AM low reclaim to day open",
                Direction: "LONG (buy / bullish)",
                ValidFromIst: "After 10:00 IST — any time until session end",
                Trigger:
                    "Negative morning (below day open into the 10 AM window); price reclaims L10 with a 5m close above L10.",
                Target: "Today’s open price (day open).",
                StopLoss:
                    $"Initial: low of the break candle (first close above L10) − {BreakoutCandleStopBufferPoints} points. "
                    + "Then breakeven → stop at L10 once trade is favorable.",
                Reentry: "Max 2 entries per symbol per day on this setup."),
            AlternateSetup: null,
            Risk: new StrategyRiskRules(
                MaxAttemptsPerDay: "2 long entries per symbol per day.",
                Notes:
                    "Mirrors the fade strategy with reversed bias. Example session: Nifty 29-Sep-2026 opened ~22,732, "
                    + "printed intraday low ~22,570, then recovered toward the open after morning lows — same pattern "
                    + "this rule describes. Paper automation planned.")));

    private static StrategyCatalogEntry ZoneB1BreakToB2() => new(
        Id: ZoneB1BreakId,
        Name: "B1 break → B2 (buy zone)",
        Author: "Trade with Beast",
        Summary:
            "From reference charts: green B1 is the first buy zone. A 5m close above B1 after price was at/below B1 "
            + "→ LONG. Target B2 (upper buy zone). Stop at pivot S. No trade when B1–S1 is tight.",
        Timeframe: "5 minute candles (board zones)",
        Session: "NSE cash & indices · 9:15–15:30 IST",
        Status: "paper-ready",
        Tags: ["zones", "long", "index", "stocks"],
        Playbook: ZonePlaybook(
            "BUY when price reclaims / breaks B1 with a candle close above B1.",
            "BULLISH",
            "5m close above B1 (prior close at or below B1).",
            "B2 zone line.",
            "Pivot S (session average of yesterday H/L and today open).",
            "15:20 IST square-off if neither stop nor target hit."));

    private static StrategyCatalogEntry ZoneS1BreakToS2() => new(
        Id: ZoneS1BreakId,
        Name: "S1 break → S2 (sell zone)",
        Author: "Trade with Beast",
        Summary:
            "Red S1 is the first sell zone. A 5m close below S1 after price was at/above S1 → SHORT. Target S2 (lower sell zone). "
            + "Stop at pivot S.",
        Timeframe: "5 minute candles (board zones)",
        Session: "NSE cash & indices · 9:15–15:30 IST",
        Status: "paper-ready",
        Tags: ["zones", "short", "index", "stocks"],
        Playbook: ZonePlaybook(
            "SELL when price loses S1 with a candle close below S1.",
            "BEARISH",
            "5m close below S1 (prior close at or above S1).",
            "S2 zone line.",
            "Pivot S.",
            "15:20 IST square-off."));

    private static StrategyCatalogEntry ZoneS2BounceToB1() => new(
        Id: ZoneS2BounceId,
        Name: "S2 bounce → B1 (support reclaim)",
        Author: "Trade with Beast",
        Summary:
            "Price dips into S2 (deepest sell zone), then reclaims S2 with a close above → LONG toward B1. Stop just below S2 / session low.",
        Timeframe: "5 minute candles (board zones)",
        Session: "NSE cash & indices · 9:15–15:30 IST",
        Status: "paper-ready",
        Tags: ["zones", "long", "reversal"],
        Playbook: ZonePlaybook(
            "BUY the S2 support reclaim after an intraday touch of S2.",
            "BULLISH",
            "Session low touched S2; 5m close back above S2.",
            "B1 zone line.",
            "Below S2 (reclaim failure).",
            "15:20 IST square-off."));

    private static StrategyPlaybook ZonePlaybook(
        string intent,
        string direction,
        string trigger,
        string target,
        string stop,
        string exitNotes) =>
        new(
            Intent: intent,
            Phases:
            [
                new StrategyPhase("Zones", "Pre-market", "B2/B1/S/S1/S2 from NiftyZoneFormula (yesterday + 2-day OHLC, today open)."),
                new StrategyPhase("Entry", "Session", trigger),
                new StrategyPhase("Exit", "Intraday", $"Target: {target}. Stop: {stop}. {exitNotes}"),
            ],
            PrimarySetup: new StrategyTradePlan(
                Name: intent,
                Direction: direction,
                ValidFromIst: "9:15–15:20 IST",
                Trigger: trigger,
                Target: target,
                StopLoss: stop,
                Reentry: "Up to 2 attempts per setup per symbol per day."),
            AlternateSetup: null,
            Risk: new StrategyRiskRules(
                MaxAttemptsPerDay: "2 per setup per symbol.",
                Notes: "Skip when |B1−S1| is tight (indices/stocks rules). RANGE between S1 and B1 — wait for a zone break or S2 reclaim."));

    private static StrategyCatalogEntry StockB1RejectionToS2() => new(
        Id: StockB1RejectionId,
        Name: "Stock B1 rejection → S2",
        Author: "Trade with Beast",
        Summary:
            "At B1 resistance: session touches B1, then a 5m candle fails (close back below B1) → SHORT toward S2. "
            + "Stop above rejection high / B1. Paper agent only recommends when backtest shows ≥90% win with ≥8 samples.",
        Timeframe: "5 minute candles (per-day zones in backtest)",
        Session: "NSE F&O stocks",
        Status: "paper-ready",
        Tags: ["stocks", "zones", "backtest-gated"],
        Playbook: new StrategyPlaybook(
            Intent: "Defined zone rejection on individual names with historical qualification.",
            Phases:
            [
                new StrategyPhase(
                    "Zones",
                    "Session",
                    "B1 (resistance) and S2 (target support) come from the board snapshot."),
                new StrategyPhase(
                    "Trigger",
                    "Touch + reject",
                    "Session high touches B1; price fails and closes back below B1."),
                new StrategyPhase(
                    "Gate",
                    "Backtest",
                    "Strategy STOCK_B1_REJECTION_TO_S2 runs only if backtest qualifies."),
            ],
            PrimarySetup: new StrategyTradePlan(
                Name: "B1 fade to S2",
                Direction: "SHORT stock / bearish",
                ValidFromIst: "Regular session",
                Trigger: "Qualified B1 rejection signal from zone engine.",
                Target: "S2 zone.",
                StopLoss: "Above rejection / B1 per strategy rules.",
                Reentry: "Per agent risk limits."),
            AlternateSetup: null,
            Risk: new StrategyRiskRules(
                MaxAttemptsPerDay: "Per portfolio risk manager.",
                Notes: "See Paper tab backtest card for live qualification stats.")));
}
