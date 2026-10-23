import sqlite3
import os
from collections import Counter, defaultdict

db = os.path.join(os.environ["APPDATA"], "TradeWithBeast", "trade-history.db")
con = sqlite3.connect(db)
con.row_factory = sqlite3.Row
cur = con.cursor()
cur.execute(
    """
    SELECT t.id,t.underlying,t.pnl,t.exit_reason,t.opened_at,t.closed_at,
           l.instrument_type,l.side,l.entry_price,l.exit_price,t.stop_loss,t.target
    FROM paper_trades t
    JOIN paper_trade_legs l ON l.trade_id=t.id
    WHERE t.strategy='TEN_AM_HIGH_FADE_DAY_LOW' AND t.status='CLOSED'
    """
)
rows = [dict(r) for r in cur.fetchall()]
losses = [r for r in rows if float(r["pnl"] or 0) < 0]

print("losses", len(losses))
print("exit reasons", Counter(r["exit_reason"] for r in losses))

# stop hit: exit above stop for short?
def as_f(x):
    return float(x) if x is not None else 0.0


hard = [r for r in losses if r["exit_reason"] == "hard stop"]
stop_breach = 0
for r in hard:
    entry, exit_p, stop = as_f(r["entry_price"]), as_f(r["exit_price"]), as_f(r["stop_loss"])
    if r["side"] == "SELL" and exit_p >= stop * 0.998:
        stop_breach += 1
print("hard stop losses where exit >= stop (short)", stop_breach, "of", len(hard))

# risk reward at entry (underlying scale)
tight_stop = 0
for r in losses:
    entry, stop, tgt = as_f(r["entry_price"]), as_f(r["stop_loss"]), as_f(r["target"])
    if entry > 0 and (stop - entry) / entry < 0.003:
        tight_stop += 1
print("losses with stop within 0.3% of entry", tight_stop)

# multi same underlying same IST day
by_day = defaultdict(list)
for r in losses:
    day = (r["opened_at"] or "")[:10]
    by_day[(r["underlying"], day)].append(r)
multi = sum(1 for k, v in by_day.items() if len(v) > 1)
print("underlying-day with 2+ losing trades", multi)

# tiny reward to day low at entry
small_reward = 0
for r in losses:
    entry, tgt = as_f(r["entry_price"]), as_f(r["target"])
    if entry > 0 and 0 < (entry - tgt) / entry < 0.005:
        small_reward += 1
print("losses with target <0.5% below entry", small_reward)
