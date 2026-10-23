import sqlite3
import os
import json

db = os.path.join(os.environ["APPDATA"], "TradeWithBeast", "trade-history.db")
con = sqlite3.connect(db)
con.row_factory = sqlite3.Row
cur = con.cursor()

# One clear LOSS (hard stop, realistic exit, not bogus 100)
cur.execute(
    """
    SELECT t.id,t.underlying,t.direction,t.pnl,t.exit_reason,t.stop_loss,t.target,
           t.opened_at,t.closed_at,t.max_risk,t.max_reward,
           l.symbol,l.side,l.instrument_type,l.entry_price,l.exit_price,l.quantity
    FROM paper_trades t
    JOIN paper_trade_legs l ON l.trade_id=t.id
    WHERE t.strategy='TEN_AM_HIGH_FADE_DAY_LOW' AND t.status='CLOSED'
      AND t.exit_reason='hard stop'
      AND CAST(l.entry_price AS REAL) > 500
      AND CAST(l.exit_price AS REAL) > 500
      AND CAST(t.pnl AS REAL) < 0
    ORDER BY t.closed_at DESC
    LIMIT 1
    """
)
loss = cur.fetchone()

# One clear WIN (target reached)
cur.execute(
    """
    SELECT t.id,t.underlying,t.direction,t.pnl,t.exit_reason,t.stop_loss,t.target,
           t.opened_at,t.closed_at,t.max_risk,t.max_reward,
           l.symbol,l.side,l.instrument_type,l.entry_price,l.exit_price,l.quantity
    FROM paper_trades t
    JOIN paper_trade_legs l ON l.trade_id=t.id
    WHERE t.strategy='TEN_AM_HIGH_FADE_DAY_LOW' AND t.status='CLOSED'
      AND t.exit_reason='target reached'
      AND CAST(t.pnl AS REAL) > 0
    ORDER BY t.closed_at DESC
    LIMIT 1
    """
)
win = cur.fetchone()


def proposal(trade_id):
    cur.execute(
        "SELECT reason FROM paper_trade_events WHERE trade_id=? AND event_type='PROPOSED' ORDER BY created_at ASC LIMIT 1",
        (trade_id,),
    )
    row = cur.fetchone()
    return row[0] if row else None


def row_dict(r):
    return {k: r[k] for k in r.keys()} if r else None


out = {
    "loss_example": row_dict(loss),
    "win_example": row_dict(win),
}
if loss:
    out["loss_proposal"] = proposal(loss["id"])
if win:
    out["win_proposal"] = proposal(win["id"])

print(json.dumps(out, indent=2, default=str))
