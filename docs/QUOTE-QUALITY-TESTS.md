# Quote source quality — production checks

Each board symbol includes:

```json
{
  "last": 22517,
  "quoteQuality": "VerifiedClose",
  "quoteTimestamp": "2026-10-23T10:00:00Z"
}
```

- **`quoteQuality`** — how `last` was chosen.
- **`quoteTimestamp`** — when that price was valid (live tick UTC for `VerifiedLive`; last session close reference for close-based qualities).

UI (IST):

- **LIVE ✓** + `14:35:22`
- **PREV CLOSE** + `23 Oct 2026`

## Values

| `quoteQuality`   | UI label      | `quoteTimestamp` meaning |
|------------------|---------------|---------------------------|
| `VerifiedLive`   | LIVE ✓        | Broker `dataTimestamp` (tick time) |
| `VerifiedClose`  | PREV CLOSE    | Last session close (daily candle / 15:30 IST) |
| `FallbackClose`  | CLOSE         | Same as close reference when history partial |
| `SeedData`       | SEED          | Seed / last known session |
| `Placeholder`    | PLACEHOLDER   | Omitted when no price |
| `Rejected`       | REJECTED      | Close reference (bad tick discarded) |

Automated: `QuoteSourceResolverTests.cs`, frontend `quote-quality.util.spec.ts`.

## Manual verification (after restart)

```powershell
.\stop-app.ps1
.\start-app.ps1
```

Hard refresh (Ctrl+F5).

### Test 1 — Market open (NIFTY)

**Expected:** **LIVE ✓** and a **time** (HH:mm:ss IST), not a calendar date.

### Test 2 — Weekend (NIFTY)

**Expected:** **PREV CLOSE** and **date** (e.g. `23 Oct 2026`); not **LIVE**.

### Test 3 — Bad tick (MARUTI)

**Expected:** **REJECTED** + close date; price = previous close, not garbage LTP.

### Test 4 — Broker disconnected

**Expected:** Stocks never show **100.00**; **SEED** or **CLOSE** with date where applicable.

### Test 5 — New symbol (no quote yet)

**Expected:** `quoteQuality` **FallbackClose** or **VerifiedClose** after history; never **100.00** with **LIVE**.
