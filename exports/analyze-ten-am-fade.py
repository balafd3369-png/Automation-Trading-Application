import sqlite3
import os
from collections import Counter
from datetime import datetime, timezone, timedelta

db = os.path.join(os.environ["APPDATA"], "TradeWithBeast", "trade-history.db")
con = sqlite3.connect(db)
con.row_factory = sqlite3.Row
cur = con.cursor()
cur.execute(
    "SELECT id,status,underlying,direction,stop_loss,target,pnl,exit_reason,created_at,closed_at "
    "FROM paper_trades WHERE strategy='TEN_AM_HIGH_FADE_DAY_LOW'"
)
rows = cur.fetchall()


def pnl(r):
    v = r["pnl"]
    if v is None:
        return 0.0
    return float(v)


def parse_dt(s):
    if not s:
        return None
    return datetime.fromisoformat(s.replace("Z", "+00:00"))


closed = [r for r in rows if r["status"] == "CLOSED"]
print("=== ALL TIME (paper DB) ===")
print(
    "closed",
    len(closed),
    "wins",
    sum(1 for r in closed if pnl(r) > 0),
    "losses",
    sum(1 for r in closed if pnl(r) < 0),
    "flat",
    sum(1 for r in closed if pnl(r) == 0),
)
print("sum pnl", sum(pnl(r) for r in closed))

now = datetime.now(timezone.utc)
for label, days in [("1w", 7), ("1m", 31), ("3m", 93)]:
    cutoff = now - timedelta(days=days)
    subset = [
        r
        for r in closed
        if parse_dt(r["closed_at"]) and parse_dt(r["closed_at"]) >= cutoff
    ]
    print(
        f"=== last {label} ===",
        len(subset),
        "wins",
        sum(1 for r in subset if pnl(r) > 0),
        "losses",
        sum(1 for r in subset if pnl(r) < 0),
        "pnl",
        round(sum(pnl(r) for r in subset), 2),
    )

exits = Counter(r["exit_reason"] or "null" for r in closed)
print("exit reasons", dict(exits))

leg_types = Counter()
wins_by_type = Counter()
losses_by_type = Counter()
pnl_sum_by_type = Counter()
for r in closed:
    cur2 = con.cursor()
    cur2.execute(
        "SELECT instrument_type,side,entry_price,exit_price,symbol FROM paper_trade_legs WHERE trade_id=?",
        (r["id"],),
    )
    leg = cur2.fetchone()
    if not leg:
        continue
    t = leg["instrument_type"]
    leg_types[t] += 1
    p = pnl(r)
    pnl_sum_by_type[t] += p
    if p > 0:
        wins_by_type[t] += 1
    elif p < 0:
        losses_by_type[t] += 1

print("by instrument_type:")
for t in leg_types:
    print(
        t,
        "n",
        leg_types[t],
        "wins",
        wins_by_type[t],
        "losses",
        losses_by_type[t],
        "sum_pnl",
        round(pnl_sum_by_type[t], 2),
    )

under = Counter(r["underlying"] for r in closed)
print("top underlyings", under.most_common(12))

# win vs loss exit reason
for outcome, pred in [("WIN", lambda p: p > 0), ("LOSS", lambda p: p < 0)]:
    sub = [r for r in closed if pred(pnl(r))]
    print(outcome, "exits", Counter(r["exit_reason"] or "null" for r in sub).most_common(8))

# entry/exit sanity for STOCK shorts
stock_closed = []
for r in closed:
    cur2 = con.cursor()
    cur2.execute(
        "SELECT instrument_type,side,entry_price,exit_price FROM paper_trade_legs WHERE trade_id=?",
        (r["id"],),
    )
    leg = cur2.fetchone()
    if leg and leg["instrument_type"] == "STOCK" and leg["side"] == "SELL":
        stock_closed.append((r, leg))

suspicious = [
    x
    for x in stock_closed
    if float(x[1]["exit_price"] or 0) <= 150
    and float(x[1]["entry_price"] or 0) > 500
]
print("STOCK SHORT count", len(stock_closed))
print("suspicious low exit (<=150, entry>500)", len(suspicious))
if suspicious:
    r, leg = suspicious[0]
    print(
        "example suspicious",
        r["underlying"],
        "pnl",
        pnl(r),
        "exit",
        r["exit_reason"],
        "entry",
        leg["entry_price"],
        "exit",
        leg["exit_price"],
        "target",
        r["target"],
        "stop",
        r["stop_loss"],
    )

# typical win vs loss stock short
wins_s = [x for x in stock_closed if pnl(x[0]) > 0]
loss_s = [x for x in stock_closed if pnl(x[0]) < 0]
if wins_s:
    avg_e = sum(float(x[1]["entry_price"]) for x in wins_s) / len(wins_s)
    avg_x = sum(float(x[1]["exit_price"] or 0) for x in wins_s) / len(wins_s)
    print("WIN stock short avg entry", round(avg_e, 2), "avg exit", round(avg_x, 2))
if loss_s:
    avg_e = sum(float(x[1]["entry_price"]) for x in loss_s) / len(loss_s)
    avg_x = sum(float(x[1]["exit_price"] or 0) for x in loss_s) / len(loss_s)
    print("LOSS stock short avg entry", round(avg_e, 2), "avg exit", round(avg_x, 2))

# proposed evidence sample
cur.execute(
    "SELECT reason FROM paper_trade_events e "
    "JOIN paper_trades t ON t.id=e.trade_id "
    "WHERE t.strategy='TEN_AM_HIGH_FADE_DAY_LOW' AND e.event_type='PROPOSED' LIMIT 3"
)
for row in cur.fetchall():
    print("PROPOSED snippet:", (row[0] or "")[:200])


def bogus_leg(leg):
    e = float(leg["entry_price"] or 0)
    x = float(leg["exit_price"] or 0)
    return leg["instrument_type"] == "STOCK" and e > 500 and x <= 150


bogus_w = sum(1 for r in closed if pnl(r) > 0 and bogus_leg(
    con.execute(
        "SELECT instrument_type,entry_price,exit_price FROM paper_trade_legs WHERE trade_id=?",
        (r["id"],),
    ).fetchone()
))
print("wins with bogus ₹100-style exit", bogus_w, "of", sum(1 for r in closed if pnl(r) > 0))
real_wins = [
    r
    for r in closed
    if pnl(r) > 0
    and not bogus_leg(
        con.execute(
            "SELECT instrument_type,entry_price,exit_price FROM paper_trade_legs WHERE trade_id=?",
            (r["id"],),
        ).fetchone()
    )
]
print("realistic wins", len(real_wins), "exit mix", Counter(r["exit_reason"] for r in real_wins))
tr = [r for r in closed if r["exit_reason"] == "target reached"]
print(
    "target reached trades",
    len(tr),
    "avg pnl",
    round(sum(pnl(r) for r in tr) / max(1, len(tr)), 2),
)
