# When the market trends up (breadth + zone playbook)

Use this with the **Trade with Beast** board after broker connect. It matches `NiftyZoneFormula` and `ZoneBoardPattern` in code — not generic TV advice.

## How your app labels a stock (important)

Zones each session (from yesterday / day-before OHLC + **today’s open**):

| Line | Meaning |
|------|--------|
| **B2** | Upper buy target |
| **B1** | Buy zone line — **break above = bullish board mark** |
| **S** | Pivot (stop reference on index zone trades) |
| **S1** | Sell zone line |
| **S2** | Lower sell / deep support |

**Board mark (Buying / Selling / Range):**

- **BUYING** → last price **≥ B1** (not “above S1” alone).
- **SELLING** → last **≤ S1**.
- **RANGE BOUND** → between S1 and B1, or **tight range** (B1–S1 too narrow).

So a 3-day rally usually looks like:

1. Many names **close or trade above B1** → board **Buying** count rises.
2. Index (NIFTY/BANKNIFTY) same — holds **above B1**, often steps toward **B2**.
3. Names still **above S1 and S2** is normal in an uptrend (price is above support), but **your app’s “buying zone” flag is B1**, not S1.

That breadth (many BUYING marks) is **why the index feels strong** — it is participation, not one magic line.

## Why three green days happen (framework, not news)

Typical chain in **your** rules:

1. **Open holds above pivot S** and reclaims / holds **B1**.
2. **5m structure** keeps making higher lows above **S1** (sellers fail at S1).
3. **Board skew**: Buying count &gt; Selling count on verified quotes → tailwind for index longs / CE strategies.
4. **No tight-range day** — if `tightRange` is true, the app **skips** auto zone entries.

We do not predict FII/DII or news here; we **trade the same conditions you already codified**.

## Patterns already in the product (grab these trades)

| Pattern ID | When | Direction | Target | Stop (index) |
|------------|------|-----------|--------|--------------|
| `ZONE_B1_BREAK_B2` | 5m **close crosses above B1** (was at/below) | Long / **CE** on index | **B2** | **S** |
| `ZONE_S1_BREAK_S2` | 5m **close crosses below S1** | Short / **PE** | **S2** | **S** |
| `ZONE_S2_BOUNCE_B1` | Reclaim **S2** after touch | Long / **CE** | **B1** | below S2 |

Live automation: **Paper agent** + `ZoneBoardIntradayStrategy` (indices, zone crosses, option legs).

Backtest proof: `export-zone-backtest-csv.ps1` → `exports/zone-backtest-*.csv` (needs broker on 45211).

## Auto paper execution (implemented)

When **Auto-admit trades** is ON (default in `appsettings.json`):

1. Paper agent scans every **5 seconds** (`ZoneBoardIntradayStrategy`).
2. **B1 break → B2** fires only if:
   - Index **5m cross above B1** (session tracker), and
   - **Board breadth OK**: Buying ≥ 5, Buying/Selling ≥ **1.25**, ≥ 8 verified symbols.
3. Rollout **paper_auto** indices (NIFTY, BANKNIFTY, FINNIFTY, SENSEX for B1 with breadth gate) → trade opens without manual confirm.
4. Messages appear in paper chat: *Auto paper trade — B1 break…* with breadth line in evidence.

S1 / S2 setups unchanged (no breadth gate).

## Tomorrow morning checklist (manual + agent)

**Before 9:20**

1. Connect **m.Stock**; wait for **verified** quotes on board.
2. Note board counts: **Buying vs Selling vs Range** (top of board).
3. Check **NIFTY** (or your index): mark = BUYING? Last **above B1**? Chart 5m — are we already extended into **B2** (late chase)?

**Trend day filter (take B1-break style trades)**

- Index mark = **BUYING**.
- Buying count **clearly above** selling (e.g. buying ≥ 1.5× selling — tune to your taste).
- **Not** tight range on index.
- Prefer entry on **first 5m close above B1** after open or shallow dip to **S** / top of range — not after full move to B2 unless scaling.

**Stock ideas**

- Open **Buying** rail in the app (`buying()` list) — these are **≥ B1** on verified symbols.
- For each candidate: confirm **above B1**, not just above S1; read tooltip / `lastVs` (“above B1” vs “between S1–B1”).
- Align with index: avoid heavy longs in large caps still **below S1** unless you have a separate reversal plan (`S2 bounce`).

**When to stand down**

- Tight range flag on index.
- Buying count collapses while index still up (narrow rally — few names).
- You **locked chart view** studying history — use **Go live** before entries.

## “Perfect without fail” — realistic bar

No rule wins every day. To improve consistency:

1. **One primary playbook per day**: e.g. only `ZONE_B1_BREAK_B2` when breadth confirms.
2. **Run 90d backtest** for your index; only trade patterns with **≥ 55% win rate** and **≥ 8 samples** (`ZoneBoardPattern.Qualifies`).
3. **Paper first** until agent journal matches backtest behaviour.
4. **Log session**: Buying/Selling counts at 9:30, 11:00, 14:00 — spot when breadth diverges from price.

## Where this lives in code

- Zones: `backend/Formulas/NiftyZoneFormula.cs`
- Board marks: `NiftyZoneFormula.Mark`
- Patterns + journal: `backend/Services/ZoneBoardPattern.cs`
- Live index option entries: `backend/Services/ZoneBoardIntradayStrategy.cs`
- Playbook JSON (API): `ZoneBoardTradePlaybook.Summary`

## Related

- `exports/README-zone-backtest-csv.md` — regenerate historical zone + option premium stats.
- `CHART-SESSION-LIVE.md` — chart must show full 09:15 session to judge 5m crosses correctly.
