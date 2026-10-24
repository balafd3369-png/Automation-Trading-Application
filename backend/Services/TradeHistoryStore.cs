using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using TradeWithBeast.Broker;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

public sealed class TradeHistoryStore
{
    public const string LocalUserId = "local-user";
    private static readonly AsyncLocal<string?> ScopedUserId = new();
    private readonly object _gate = new();
    private readonly string _connectionString;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly SystemIdentityOptions _systemIdentity;
    private readonly ILogger<TradeHistoryStore> _log;

    public string ActiveUserId
    {
        get
        {
            var scoped = ScopedUserId.Value ?? _currentUser.UserId;
            if (!string.IsNullOrWhiteSpace(scoped))
            {
                return scoped;
            }

            if (_systemIdentity.AllowAnonymousLocalUser)
            {
                return LocalUserId;
            }

            throw new UnauthorizedAccessException("Authentication required.");
        }
    }

    public string StrategyEngineUserId => _systemIdentity.StrategyEngineUserId;

    public IDisposable RunAsUser(string userId) => new UserScope(ScopedUserId, userId);

    public TradeHistoryStore(IConfiguration configuration, ILogger<TradeHistoryStore> log)
        : this(configuration, new CurrentUserAccessor(), new SystemIdentityOptions(), log)
    {
    }

    public TradeHistoryStore(
        IConfiguration configuration,
        ICurrentUserAccessor currentUser,
        SystemIdentityOptions systemIdentity,
        ILogger<TradeHistoryStore> log)
    {
        _currentUser = currentUser;
        _systemIdentity = systemIdentity;
        var configured = configuration["TradeHistory:DatabasePath"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TradeWithBeast",
                "trade-history.db")
            : configured;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString();
        _log = log;
        Initialize();
    }

    public sealed record RecoveredLeg(
        string Id,
        string Symbol,
        string Side,
        int Quantity,
        decimal EntryPrice,
        decimal CurrentPrice,
        decimal? Strike,
        string? OptionRight,
        DateTimeOffset? Expiry);

    public sealed record RecoveredTrade(
        string Id,
        string Strategy,
        string Underlying,
        string Direction,
        decimal StopLoss,
        decimal Target,
        decimal MaxRisk,
        decimal MaxReward,
        decimal CapitalRequired,
        DateTimeOffset OpenedAt,
        IReadOnlyList<RecoveredLeg> Legs);

    public sealed record StoredLiveTrade(
        string Id,
        PaperTradeProposal Proposal,
        string Product,
        string Status,
        string? FailureReason,
        DateTimeOffset CreatedAt,
        IReadOnlyList<LiveOrderSnapshot> Orders);

    public void SaveDecision(RankedDecisionSnapshot decision)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT OR REPLACE INTO paper_decisions
                    (id,user_id,strategy,underlying,direction,evidence_score,accepted,max_risk,max_reward,reward_risk,evaluated_at)
                VALUES ($id,$user,$strategy,$underlying,$direction,$score,$accepted,$risk,$reward,$rr,$at);
                """;
            Add(command, "$id", decision.Id);
            Add(command, "$user", ActiveUserId);
            Add(command, "$strategy", decision.Strategy);
            Add(command, "$underlying", decision.Underlying);
            Add(command, "$direction", decision.Direction);
            Add(command, "$score", DecimalText(decision.EvidenceScore));
            Add(command, "$accepted", decision.Accepted ? 1 : 0);
            Add(command, "$risk", DecimalText(decision.MaxRisk));
            Add(command, "$reward", DecimalText(decision.MaxReward));
            Add(command, "$rr", DecimalText(decision.RewardRisk));
            Add(command, "$at", UtcText(decision.EvaluatedAt));
            command.ExecuteNonQuery();
            var ordinal = 0;
            foreach (var item in decision.Evidence.Select(x => ("EVIDENCE", x))
                         .Concat(decision.Rejections.Select(x => ("REJECTION", x))))
            {
                using var evidence = connection.CreateCommand();
                evidence.Transaction = transaction;
                evidence.CommandText =
                    "INSERT INTO paper_decision_evidence(decision_id,kind,ordinal,text) VALUES($id,$kind,$ordinal,$text);";
                Add(evidence, "$id", decision.Id);
                Add(evidence, "$kind", item.Item1);
                Add(evidence, "$ordinal", ordinal++);
                Add(evidence, "$text", item.Item2);
                evidence.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public void SaveMultiProposal(PaperTradeProposal proposal)
    {
        SaveProposal(proposal);
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var trade = connection.CreateCommand();
            trade.Transaction = transaction;
            trade.CommandText =
                """
                INSERT OR REPLACE INTO paper_trades
                    (id,user_id,status,strategy,underlying,direction,stop_loss,target,max_risk,max_reward,capital_required,created_at,updated_at)
                VALUES($id,$user,'PROPOSED',$strategy,$underlying,$direction,$stop,$target,$risk,$reward,$capital,$at,$at);
                """;
            Add(trade, "$id", proposal.Id);
            Add(trade, "$user", ActiveUserId);
            Add(trade, "$strategy", proposal.Strategy);
            Add(trade, "$underlying", string.IsNullOrWhiteSpace(proposal.Underlying) ? proposal.Symbol : proposal.Underlying);
            Add(trade, "$direction", proposal.Direction);
            Add(trade, "$stop", DecimalText(proposal.StopLoss));
            Add(trade, "$target", DecimalText(proposal.Target));
            Add(trade, "$risk", DecimalText(proposal.MaxRisk));
            Add(trade, "$reward", DecimalText(proposal.MaxReward));
            Add(trade, "$capital", DecimalText(proposal.CapitalRequired));
            Add(trade, "$at", UtcText(DateTimeOffset.UtcNow));
            trade.ExecuteNonQuery();
            foreach (var leg in proposal.Legs ?? [])
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT OR REPLACE INTO paper_trade_legs
                        (id,trade_id,symbol,underlying,instrument_type,side,ratio,quantity,proposed_price,strike,option_right,expiry)
                    VALUES($id,$trade,$symbol,$underlying,$instrument,$side,$ratio,$quantity,$price,$strike,$right,$expiry);
                    """;
                Add(command, "$id", leg.Id);
                Add(command, "$trade", proposal.Id);
                Add(command, "$symbol", leg.Symbol);
                Add(command, "$underlying", leg.Underlying);
                Add(command, "$instrument", leg.InstrumentType);
                Add(command, "$side", leg.Side);
                Add(command, "$ratio", leg.Ratio);
                Add(command, "$quantity", leg.Quantity);
                Add(command, "$price", DecimalText(leg.IndicativePrice));
                Add(command, "$strike", leg.Strike is null ? DBNull.Value : DecimalText(leg.Strike.Value));
                Add(command, "$right", leg.OptionRight is null ? DBNull.Value : leg.OptionRight);
                Add(command, "$expiry", leg.Expiry is null ? DBNull.Value : UtcText(leg.Expiry.Value));
                command.ExecuteNonQuery();
            }
            InsertEvent(connection, transaction, proposal.Id, "PROPOSED", proposal.Reason, null, DateTimeOffset.UtcNow);
            transaction.Commit();
        }
    }

    public void OpenMultiLegAtomic(
        string tradeId,
        IReadOnlyList<PaperPositionLegSnapshot> legs,
        DateTimeOffset openedAt,
        decimal? maxRisk = null,
        decimal? maxReward = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            foreach (var leg in legs)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE paper_trade_legs
                    SET quantity=$quantity, entry_price=$entry, current_price=$entry
                    WHERE id=$id AND trade_id=$trade;
                    """;
                Add(command, "$quantity", leg.Quantity);
                Add(command, "$entry", DecimalText(leg.EntryPrice));
                Add(command, "$id", leg.Id);
                Add(command, "$trade", tradeId);
                command.ExecuteNonQuery();
            }
            using var trade = connection.CreateCommand();
            trade.Transaction = transaction;
            var capital = legs.Sum(leg => leg.EntryPrice * leg.Quantity);
            trade.CommandText =
                """
                UPDATE paper_trades
                SET status='OPEN',opened_at=$opened,updated_at=$opened,
                    max_risk=COALESCE($risk,max_risk),max_reward=COALESCE($reward,max_reward),
                    capital_required=$capital
                WHERE id=$id AND user_id=$user;
                """;
            Add(trade, "$opened", UtcText(openedAt));
            Add(trade, "$risk", maxRisk is null ? DBNull.Value : DecimalText(maxRisk.Value));
            Add(trade, "$reward", maxReward is null ? DBNull.Value : DecimalText(maxReward.Value));
            Add(trade, "$capital", DecimalText(capital));
            Add(trade, "$id", tradeId);
            Add(trade, "$user", ActiveUserId);
            trade.ExecuteNonQuery();
            InsertEvent(connection, transaction, tradeId, "OPENED", "atomic simulated paper fill", null, openedAt);
            transaction.Commit();
        }
    }

    public void UpdateLegMarks(string tradeId, IReadOnlyList<PaperPositionLegSnapshot> legs)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            foreach (var leg in legs)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE paper_trade_legs SET current_price=$price WHERE id=$id AND trade_id=$trade;";
                Add(command, "$price", DecimalText(leg.CurrentPrice));
                Add(command, "$id", leg.Id);
                Add(command, "$trade", tradeId);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }

    public void CloseMultiLegAtomic(
        string tradeId,
        IReadOnlyList<PaperPositionLegSnapshot> legs,
        string reason,
        decimal pnl,
        DateTimeOffset at)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            foreach (var leg in legs)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "UPDATE paper_trade_legs SET exit_price=$price WHERE id=$id AND trade_id=$trade;";
                Add(command, "$price", DecimalText(leg.CurrentPrice));
                Add(command, "$id", leg.Id);
                Add(command, "$trade", tradeId);
                command.ExecuteNonQuery();
            }
            using var trade = connection.CreateCommand();
            trade.Transaction = transaction;
            trade.CommandText =
                """
                UPDATE paper_trades
                SET status='CLOSED',exit_reason=$reason,pnl=$pnl,closed_at=$at,updated_at=$at
                WHERE id=$id AND user_id=$user;
                """;
            Add(trade, "$reason", reason);
            Add(trade, "$pnl", DecimalText(pnl));
            Add(trade, "$at", UtcText(at));
            Add(trade, "$id", tradeId);
            Add(trade, "$user", ActiveUserId);
            trade.ExecuteNonQuery();
            InsertEvent(connection, transaction, tradeId, "CLOSED", reason, pnl, at);
            transaction.Commit();
        }
    }

    public void MarkMultiFinal(string tradeId, string status, string reason, DateTimeOffset at)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE paper_trades SET status=$status,exit_reason=$reason,closed_at=$at,updated_at=$at
                WHERE id=$id AND user_id=$user AND status='PROPOSED';
                """;
            Add(command, "$status", status);
            Add(command, "$reason", reason);
            Add(command, "$at", UtcText(at));
            Add(command, "$id", tradeId);
            Add(command, "$user", ActiveUserId);
            command.ExecuteNonQuery();
            InsertEvent(connection, transaction, tradeId, status, reason, null, at);
            transaction.Commit();
        }
    }

    public IReadOnlyList<RecoveredTrade> RecoverOpenTrades()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,strategy,underlying,direction,stop_loss,target,max_risk,max_reward,capital_required,opened_at
                FROM paper_trades
                WHERE user_id=$user AND status='OPEN' AND COALESCE(trade_source,'STRATEGY') <> 'MANUAL'
                ORDER BY opened_at;
                """;
            Add(command, "$user", ActiveUserId);
            var rows = new List<(string Id, string Strategy, string Underlying, string Direction,
                decimal Stop, decimal Target, decimal Risk, decimal Reward, decimal Capital, DateTimeOffset Opened)>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                    decimal.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                    decimal.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                    decimal.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                    DecimalValue(reader, 8) ?? 0,
                    DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture)));
            }
            reader.Close();
            return rows.Select(row => new RecoveredTrade(
                row.Id, row.Strategy, row.Underlying, row.Direction, row.Stop, row.Target, row.Risk, row.Reward,
                row.Capital, row.Opened, ReadLegs(connection, row.Id))).ToList();
        }
    }

    public IReadOnlyList<PaperTradeEventSnapshot> RecentEvents(int limit = 50)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,trade_id,event_type,reason,pnl,created_at
                FROM paper_trade_events ORDER BY created_at DESC LIMIT $limit;
                """;
            Add(command, "$limit", Math.Clamp(limit, 1, 200));
            using var reader = command.ExecuteReader();
            var result = new List<PaperTradeEventSnapshot>();
            while (reader.Read())
            {
                result.Add(new(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    DecimalValue(reader, 4), DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)));
            }
            return result;
        }
    }

    public IReadOnlyDictionary<string, int> CountTradesByStrategyThisMonth(DateTimeOffset now)
    {
        var report = QueryStrategyTrackRecord(now);
        return report.ByStrategy.ToDictionary(
            row => StrategyRecommendationEngine.StrategyCodeFromCatalog(row.StrategyId),
            row => row.Trades,
            StringComparer.OrdinalIgnoreCase);
    }

    public StrategyTrackRecordResponse QueryStrategyTrackRecord(DateTimeOffset now)
    {
        var from = StartOfIstMonth(now);
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = TimeZoneInfo.ConvertTime(now, ist);
        var monthLabel = local.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
        lock (_gate)
        {
            using var connection = Open();
            var trades = ReadGlobalStrategyPaperTradesBetween(connection, from, now);
            var catalog = StrategyCatalog.All();
            var byStrategy = catalog.Select(entry =>
            {
                var matching = trades
                    .Where(t => StrategyRecommendationEngine.MapStrategyCode(t.Strategy) == entry.Id)
                    .ToList();
                return BuildTrackRow(entry.Id, entry.Name, matching);
            }).ToList();

            var catalogIds = catalog.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var other = trades
                .Where(t => !catalogIds.Contains(StrategyRecommendationEngine.MapStrategyCode(t.Strategy)))
                .ToList();
            if (other.Count > 0)
            {
                byStrategy.Add(BuildTrackRow("other", "Other / legacy strategies", other));
            }

            return new(
                monthLabel,
                from,
                now,
                BuildTrackRow("all", "All strategies", trades),
                byStrategy,
                trades);
        }
    }

    private static StrategyTrackRecordRow BuildTrackRow(
        string strategyId,
        string strategyName,
        IReadOnlyList<PaperTradeHistorySnapshot> trades)
    {
        var closed = trades.Where(t => t.Status == "CLOSED").ToList();
        var wins = closed.Count(t => t.Pnl > 0);
        var losses = closed.Count(t => t.Pnl < 0);
        var capitals = trades.Select(t => t.CapitalRequired).Where(c => c > 0).ToList();
        var peakCapital = capitals.Count > 0 ? capitals.Max() : 0;
        var avgCapital = capitals.Count > 0 ? capitals.Average() : 0;
        var winRate = closed.Count > 0 ? wins * 100m / closed.Count : 0;
        return new(
            strategyId,
            strategyName,
            trades.Count,
            closed.Count,
            trades.Count(t => t.Status == "OPEN"),
            wins,
            losses,
            winRate,
            closed.Sum(t => t.Pnl ?? 0),
            trades.Sum(t => t.MaxRisk),
            trades.Sum(t => t.MaxReward),
            peakCapital,
            avgCapital);
    }

    private List<PaperTradeHistorySnapshot> ReadPaperTradesBetween(
        SqliteConnection connection,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,status,strategy,underlying,direction,stop_loss,target,max_risk,max_reward,capital_required,pnl,exit_reason,
                   (SELECT reason FROM paper_trade_events e WHERE e.trade_id=paper_trades.id AND e.event_type='PROPOSED' ORDER BY e.created_at ASC LIMIT 1),
                   created_at,opened_at,closed_at,COALESCE(trade_source,'STRATEGY')
            FROM paper_trades
            WHERE user_id=$user
              AND (
                (created_at >= $from AND created_at <= $to)
                OR (closed_at IS NOT NULL AND closed_at >= $from AND closed_at <= $to)
                OR (status = 'OPEN' AND created_at <= $to AND (closed_at IS NULL OR closed_at > $from))
              )
            ORDER BY created_at DESC;
            """;
        Add(command, "$user", ActiveUserId);
        Add(command, "$from", UtcText(from));
        Add(command, "$to", UtcText(to));
        var rows = new List<(string Id, string Status, string Strategy, string Underlying, string Direction,
            decimal Stop, decimal Target, decimal Risk, decimal Reward, decimal Capital, decimal? Pnl, string? Reason,
            string? EntryReason, DateTimeOffset Created, DateTimeOffset? Opened, DateTimeOffset? Closed, string TradeSource)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), DecimalValue(reader, 5) ?? 0, DecimalValue(reader, 6) ?? 0,
                    DecimalValue(reader, 7) ?? 0, DecimalValue(reader, 8) ?? 0,
                    DecimalValue(reader, 9) ?? 0, DecimalValue(reader, 10), reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture),
                    DateValue(reader, 14), DateValue(reader, 15),
                    reader.IsDBNull(16) ? TradeSourceWire.Strategy : reader.GetString(16)));
            }
        }

        return rows.Select(row =>
        {
            var legs = ReadHistoryLegs(connection, row.Id);
            var snapshot = new PaperTradeHistorySnapshot(
                row.Id, row.Status, row.Strategy, row.Underlying, row.Direction, row.Stop, row.Target, row.Risk, row.Reward,
                row.Capital, row.Pnl, row.Reason, row.EntryReason, row.Created, row.Opened, row.Closed,
                0, 0, null, legs, row.TradeSource);
            return snapshot;
        }).ToList();
    }

    private List<PaperTradeHistorySnapshot> ReadGlobalStrategyPaperTradesBetween(
        SqliteConnection connection,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,status,strategy,underlying,direction,stop_loss,target,max_risk,max_reward,capital_required,pnl,exit_reason,
                   (SELECT reason FROM paper_trade_events e WHERE e.trade_id=paper_trades.id AND e.event_type='PROPOSED' ORDER BY e.created_at ASC LIMIT 1),
                   created_at,opened_at,closed_at,COALESCE(trade_source,'STRATEGY')
            FROM paper_trades
            WHERE COALESCE(trade_source,'STRATEGY')=$source
              AND (
                (created_at >= $from AND created_at <= $to)
                OR (closed_at IS NOT NULL AND closed_at >= $from AND closed_at <= $to)
                OR (status = 'OPEN' AND created_at <= $to AND (closed_at IS NULL OR closed_at > $from))
              )
            ORDER BY created_at DESC;
            """;
        Add(command, "$source", TradeSourceWire.Strategy);
        Add(command, "$from", UtcText(from));
        Add(command, "$to", UtcText(to));
        var rows = new List<(string Id, string Status, string Strategy, string Underlying, string Direction,
            decimal Stop, decimal Target, decimal Risk, decimal Reward, decimal Capital, decimal? Pnl, string? Reason,
            string? EntryReason, DateTimeOffset Created, DateTimeOffset? Opened, DateTimeOffset? Closed, string TradeSource)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                rows.Add((
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), DecimalValue(reader, 5) ?? 0, DecimalValue(reader, 6) ?? 0,
                    DecimalValue(reader, 7) ?? 0, DecimalValue(reader, 8) ?? 0,
                    DecimalValue(reader, 9) ?? 0, DecimalValue(reader, 10), reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12),
                    DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture),
                    DateValue(reader, 14), DateValue(reader, 15),
                    reader.IsDBNull(16) ? TradeSourceWire.Strategy : reader.GetString(16)));
            }
        }

        return rows.Select(row =>
        {
            var legs = ReadHistoryLegs(connection, row.Id);
            return new PaperTradeHistorySnapshot(
                row.Id, row.Status, row.Strategy, row.Underlying, row.Direction, row.Stop, row.Target, row.Risk, row.Reward,
                row.Capital, row.Pnl, row.Reason, row.EntryReason, row.Created, row.Opened, row.Closed,
                0, 0, null, legs, row.TradeSource);
        }).ToList();
    }

    public int CountManualPaperTradesOnDate(DateTimeOffset day)
    {
        var start = StartOfIstDay(day);
        var end = start.AddDays(1);
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*) FROM paper_trades
                WHERE trade_source=$source
                  AND created_at >= $from AND created_at < $to;
                """;
            Add(command, "$source", TradeSourceWire.Manual);
            Add(command, "$from", UtcText(start));
            Add(command, "$to", UtcText(end));
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public int CountOpenManualPaperPositionsGlobal()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(*) FROM paper_trades
                WHERE status='OPEN' AND trade_source=$source;
                """;
            Add(command, "$source", TradeSourceWire.Manual);
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public List<PaperTradeHistorySnapshot> NormalizePaperTrades(
        IReadOnlyList<PaperTradeHistorySnapshot> trades,
        decimal capitalPerTrade) =>
        trades.Select(trade => PaperTradeEconomics.Normalize(trade, capitalPerTrade, _log)).ToList();

    private static DateTimeOffset StartOfIstMonth(DateTimeOffset now)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = TimeZoneInfo.ConvertTime(now, ist);
        var start = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(start, ist.GetUtcOffset(start));
    }

    public PaperTradeHistoryResponse QueryPaperTrades(string range, DateTimeOffset now, decimal capitalPerTrade)
    {
        var normalized = range.Trim().ToLowerInvariant();
        var from = normalized switch
        {
            "today" => StartOfIstDay(now),
            "1w" => StartOfIstDay(now).AddDays(-6),
            "1m" => now.AddMonths(-1),
            "3m" => now.AddMonths(-3),
            _ => now.AddMonths(-12),
        };
        var label = normalized is "today" or "1w" or "1m" or "3m" ? normalized : "all";
        return QueryPaperTradesBetween(from, now, label, capitalPerTrade);
    }

    public PaperTradeHistoryResponse QueryPaperTradesForIstDates(
        string fromYmd,
        string? toYmd,
        DateTimeOffset now,
        decimal capitalPerTrade)
    {
        var from = StartOfIstCalendarDay(fromYmd);
        var to = EndOfIstCalendarDay(string.IsNullOrWhiteSpace(toYmd) ? fromYmd : toYmd!);
        if (to < from)
        {
            to = EndOfIstCalendarDay(fromYmd);
        }

        var label = $"custom:{fromYmd}:{toYmd ?? fromYmd}";
        return QueryPaperTradesBetween(from, to > now ? now : to, label, capitalPerTrade);
    }

    public PaperTradeHistoryResponse QueryPaperTradesBetween(
        DateTimeOffset from,
        DateTimeOffset to,
        string label,
        decimal capitalPerTrade)
    {
        lock (_gate)
        {
            using var connection = Open();
            var trades = NormalizePaperTrades(ReadPaperTradesBetween(connection, from, to), capitalPerTrade);
            var closed = trades.Where(trade => trade.Status == "CLOSED").ToList();
            var exitStats = SummarizeExitOutcomes(closed.Select(trade => (trade.Pnl, trade.ExitReason)));
            return new(
                ActiveUserId,
                label,
                new(
                    closed.Count,
                    closed.Count(trade => trade.Pnl > 0),
                    closed.Count(trade => trade.Pnl < 0),
                    trades.Count(trade => trade.Status == "OPEN"),
                    closed.Sum(trade => trade.Pnl ?? 0),
                    exitStats.TargetHits,
                    exitStats.StopLossHits,
                    exitStats.TrailingStopHits,
                    exitStats.BreakevenTrades),
                trades);
        }
    }

    public void SaveProposal(PaperTradeProposal proposal)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO trade_history (
                    id, user_id, status, instrument_type, symbol, direction, quantity,
                    trigger_reason, proposed_price, stop_loss, target, created_at)
                VALUES (
                    $id, $user, 'PROPOSED', $instrument, $symbol, $direction, $quantity,
                    $trigger, $price, $stop, $target, $created)
                ON CONFLICT(id) DO NOTHING;
                """;
            Add(command, "$id", proposal.Id);
            Add(command, "$user", ActiveUserId);
            Add(command, "$instrument", proposal.InstrumentType);
            Add(command, "$symbol", proposal.Symbol);
            Add(command, "$direction", proposal.Direction);
            Add(command, "$quantity", proposal.Quantity);
            Add(command, "$trigger", proposal.Reason);
            Add(command, "$price", DecimalText(proposal.IndicativePrice));
            Add(command, "$stop", DecimalText(proposal.StopLoss));
            Add(command, "$target", DecimalText(proposal.Target));
            Add(command, "$created", UtcText(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
            DeleteExpiredUnsafe(connection);
        }
    }

    public void MarkOpened(
        string id,
        int quantity,
        decimal entryPrice,
        DateTimeOffset openedAt)
    {
        Update(
            id,
            "OPEN",
            command =>
            {
                command.CommandText +=
                    ", quantity = $quantity, entry_price = $entry, opened_at = $opened";
                Add(command, "$quantity", quantity);
                Add(command, "$entry", DecimalText(entryPrice));
                Add(command, "$opened", UtcText(openedAt));
            });
    }

    public void MarkFinal(
        string id,
        string status,
        string reason,
        decimal? exitPrice = null,
        decimal? pnl = null,
        DateTimeOffset? closedAt = null)
    {
        Update(
            id,
            status,
            command =>
            {
                command.CommandText +=
                    ", exit_reason = $reason, exit_price = $exit, pnl = $pnl, closed_at = $closed";
                Add(command, "$reason", reason);
                Add(command, "$exit", exitPrice is null ? DBNull.Value : DecimalText(exitPrice.Value));
                Add(command, "$pnl", pnl is null ? DBNull.Value : DecimalText(pnl.Value));
                Add(command, "$closed", UtcText(closedAt ?? DateTimeOffset.UtcNow));
            });
    }

    public TradeHistoryResponse Query(string range, DateTimeOffset now)
    {
        var normalized = range.Trim().ToLowerInvariant();
        var from = normalized switch
        {
            "today" => StartOfIstDay(now),
            "1w" => StartOfIstDay(now).AddDays(-6),
            "1m" => now.AddMonths(-1),
            "3m" => now.AddMonths(-3),
            _ => now.AddMonths(-12),
        };
        var label = normalized is "today" or "1w" or "1m" or "3m" ? normalized : "all";
        List<TradeHistoryItem> trades;
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, user_id, status, instrument_type, symbol, direction, quantity,
                       trigger_reason, proposed_price, entry_price, exit_price, stop_loss,
                       target, pnl, exit_reason, created_at, opened_at, closed_at
                FROM trade_history
                WHERE user_id = $user AND created_at >= $from AND created_at <= $to
                ORDER BY created_at DESC;
                """;
            Add(command, "$user", ActiveUserId);
            Add(command, "$from", UtcText(from));
            Add(command, "$to", UtcText(now));
            using var reader = command.ExecuteReader();
            trades = [];
            while (reader.Read())
            {
                trades.Add(Read(reader));
            }
        }
        var closed = trades.Where(trade => trade.Status == "CLOSED").ToList();
        var exitStats = SummarizeExitOutcomes(closed.Select(trade => (trade.Pnl, trade.ExitReason)));
        return new TradeHistoryResponse(
            ActiveUserId,
            label,
            from,
            now,
            new TradeHistorySummary(
                closed.Count,
                closed.Count(trade => trade.Pnl > 0),
                closed.Count(trade => trade.Pnl < 0),
                trades.Count(trade => trade.Status == "OPEN"),
                closed.Sum(trade => trade.Pnl ?? 0m),
                exitStats.TargetHits,
                exitStats.StopLossHits,
                exitStats.TrailingStopHits,
                exitStats.BreakevenTrades),
            trades);
    }

    private static (int TargetHits, int StopLossHits, int TrailingStopHits, int BreakevenTrades) SummarizeExitOutcomes(
        IEnumerable<(decimal? Pnl, string? ExitReason)> rows)
    {
        var list = rows.ToList();
        return (
            list.Count(row => PaperExitAnalytics.IsTargetHit(row.ExitReason)),
            list.Count(row => PaperExitAnalytics.IsStopLossHit(row.ExitReason)),
            list.Count(row => PaperExitAnalytics.IsTrailingStopHit(row.ExitReason)),
            list.Count(row => row.Pnl == 0));
    }

    public void SaveLiveTrade(PaperTradeProposal proposal, string product)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO live_trades(id,user_id,strategy,underlying,product,status,proposal_json,created_at,updated_at)
                VALUES($id,$user,$strategy,$underlying,$product,'CONFIRMED',$json,$at,$at)
                ON CONFLICT(id) DO NOTHING;
                """;
            Add(command, "$id", proposal.Id);
            Add(command, "$user", ActiveUserId);
            Add(command, "$strategy", proposal.Strategy);
            Add(command, "$underlying", proposal.Underlying);
            Add(command, "$product", product);
            Add(command, "$json", JsonSerializer.Serialize(proposal));
            Add(command, "$at", UtcText(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
            InsertLiveEvent(connection, transaction, proposal.Id, "CONFIRMED", $"Live execution approved with {product}.", DateTimeOffset.UtcNow);
            transaction.Commit();
        }
    }

    public void SetLiveTradeStatus(string tradeId, string status, string? reason = null)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                UPDATE live_trades SET status=$status,failure_reason=$reason,updated_at=$at
                WHERE id=$id AND user_id=$user;
                """;
            Add(command, "$status", status);
            Add(command, "$reason", reason is null ? DBNull.Value : reason);
            Add(command, "$at", UtcText(DateTimeOffset.UtcNow));
            Add(command, "$id", tradeId);
            Add(command, "$user", ActiveUserId);
            command.ExecuteNonQuery();
            InsertLiveEvent(connection, transaction, tradeId, status, reason ?? status, DateTimeOffset.UtcNow);
            transaction.Commit();
        }
    }

    public void UpsertLiveOrder(LiveOrderSnapshot order)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO live_orders(
                    id,trade_id,leg_id,broker_order_id,exchange,symbol,transaction_type,product,purpose,status,
                    quantity,filled_quantity,average_price,broker_message,tag,updated_at)
                VALUES($id,$trade,$leg,$broker,$exchange,$symbol,$side,$product,$purpose,$status,
                    $quantity,$filled,$average,$message,$tag,$at)
                ON CONFLICT(id) DO UPDATE SET
                    broker_order_id=excluded.broker_order_id,status=excluded.status,
                    filled_quantity=excluded.filled_quantity,average_price=excluded.average_price,
                    broker_message=excluded.broker_message,updated_at=excluded.updated_at;
                """;
            Add(command, "$id", order.Id);
            Add(command, "$trade", order.TradeId);
            Add(command, "$leg", order.LegId);
            Add(command, "$broker", order.BrokerOrderId);
            Add(command, "$exchange", order.Exchange);
            Add(command, "$symbol", order.Symbol);
            Add(command, "$side", order.TransactionType);
            Add(command, "$product", order.Product);
            Add(command, "$purpose", order.Purpose);
            Add(command, "$status", order.Status);
            Add(command, "$quantity", order.Quantity);
            Add(command, "$filled", order.FilledQuantity);
            Add(command, "$average", DecimalText(order.AveragePrice));
            Add(command, "$message", order.BrokerMessage);
            Add(command, "$tag", order.Tag);
            Add(command, "$at", UtcText(order.UpdatedAt));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<StoredLiveTrade> ReadLiveTrades(bool activeOnly = false)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,proposal_json,product,status,failure_reason,created_at
                FROM live_trades
                WHERE user_id=$user
                  AND ($active=0 OR status NOT IN ('CLOSED','FAILED','REJECTED','CANCELLED'))
                ORDER BY created_at DESC LIMIT 100;
                """;
            Add(command, "$user", ActiveUserId);
            Add(command, "$active", activeOnly ? 1 : 0);
            var rows = new List<(string Id, PaperTradeProposal Proposal, string Product, string Status, string? Reason, DateTimeOffset Created)>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var proposal = JsonSerializer.Deserialize<PaperTradeProposal>(reader.GetString(1));
                    if (proposal is null) continue;
                    rows.Add((
                        reader.GetString(0), proposal, reader.GetString(2), reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetString(4),
                        DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture)));
                }
            }
            return rows.Select(row => new StoredLiveTrade(
                row.Id, row.Proposal, row.Product, row.Status, row.Reason, row.Created,
                ReadLiveOrders(connection, row.Id))).ToList();
        }
    }

    public IReadOnlyList<LiveExecutionEvent> RecentLiveEvents(int limit = 100)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT id,trade_id,event_type,message,created_at FROM live_events ORDER BY created_at DESC LIMIT $limit;";
            Add(command, "$limit", Math.Clamp(limit, 1, 500));
            using var reader = command.ExecuteReader();
            var events = new List<LiveExecutionEvent>();
            while (reader.Read())
            {
                events.Add(new(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture)));
            }
            return events;
        }
    }

    public const string ManualStrategyId = "MANUAL_TRADE";

    public void SaveManualPaperTradeOpen(PaperTradeProposal proposal, IReadOnlyList<PaperPositionLegSnapshot> legs, DateTimeOffset openedAt)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var trade = connection.CreateCommand();
            trade.Transaction = transaction;
            var capital = legs.Sum(leg => leg.EntryPrice * leg.Quantity);
            trade.CommandText =
                """
                INSERT OR REPLACE INTO paper_trades
                    (id,user_id,status,strategy,underlying,direction,stop_loss,target,max_risk,max_reward,capital_required,
                     trade_source,created_at,updated_at,opened_at)
                VALUES($id,$user,'OPEN',$strategy,$underlying,$direction,$stop,$target,$risk,$reward,$capital,
                    $source,$at,$at,$opened);
                """;
            Add(trade, "$id", proposal.Id);
            Add(trade, "$user", ActiveUserId);
            Add(trade, "$strategy", ManualStrategyId);
            Add(trade, "$underlying", string.IsNullOrWhiteSpace(proposal.Underlying) ? proposal.Symbol : proposal.Underlying);
            Add(trade, "$direction", proposal.Direction);
            Add(trade, "$stop", DecimalText(proposal.StopLoss));
            Add(trade, "$target", DecimalText(proposal.Target));
            Add(trade, "$risk", DecimalText(proposal.MaxRisk));
            Add(trade, "$reward", DecimalText(proposal.MaxReward));
            Add(trade, "$capital", DecimalText(capital));
            Add(trade, "$source", TradeSourceWire.Manual);
            Add(trade, "$at", UtcText(openedAt));
            Add(trade, "$opened", UtcText(openedAt));
            trade.ExecuteNonQuery();
            foreach (var leg in legs)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT OR REPLACE INTO paper_trade_legs
                        (id,trade_id,symbol,underlying,instrument_type,side,ratio,quantity,proposed_price,entry_price,current_price)
                    VALUES($id,$trade,$symbol,$underlying,$instrument,$side,1,$quantity,$price,$entry,$entry);
                    """;
                Add(command, "$id", leg.Id);
                Add(command, "$trade", proposal.Id);
                Add(command, "$symbol", leg.Symbol);
                Add(command, "$underlying", proposal.Underlying);
                Add(command, "$instrument", proposal.InstrumentType);
                Add(command, "$side", leg.Side);
                Add(command, "$quantity", leg.Quantity);
                Add(command, "$price", DecimalText(leg.EntryPrice));
                Add(command, "$entry", DecimalText(leg.EntryPrice));
                command.ExecuteNonQuery();
            }
            InsertEvent(connection, transaction, proposal.Id, "PROPOSED", proposal.Reason, null, openedAt);
            InsertEvent(connection, transaction, proposal.Id, "OPENED", "manual paper fill", null, openedAt);
            transaction.Commit();
        }
    }

    public PaperTradeHistorySnapshot? GetManualPaperTrade(string tradeId)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id FROM paper_trades
                WHERE id=$id AND user_id=$user AND trade_source=$source LIMIT 1;
                """;
            Add(command, "$id", tradeId);
            Add(command, "$user", ActiveUserId);
            Add(command, "$source", TradeSourceWire.Manual);
            if (command.ExecuteScalar() is null)
            {
                return null;
            }

            var from = DateTimeOffset.UtcNow.AddYears(-5);
            var to = DateTimeOffset.UtcNow.AddDays(1);
            return ReadPaperTradesBetween(connection, from, to).FirstOrDefault(t => t.Id == tradeId);
        }
    }

    public decimal SumOpenManualDeployedCapital()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                  (SELECT COALESCE(SUM(CAST(capital_required AS REAL)),0)
                   FROM paper_trades
                   WHERE user_id=$user AND status='OPEN' AND trade_source=$source)
                  +
                  (SELECT COALESCE(SUM(CAST(limit_price AS REAL) * quantity),0)
                   FROM manual_orders
                   WHERE user_id=$user AND execution_mode='PAPER' AND status='PENDING' AND limit_price IS NOT NULL);
                """;
            Add(command, "$user", ActiveUserId);
            Add(command, "$source", TradeSourceWire.Manual);
            return Convert.ToDecimal(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public IReadOnlyList<PaperTradeHistorySnapshot> QueryManualPaperTrades(DateTimeOffset from, DateTimeOffset to, decimal capitalPerTrade)
    {
        lock (_gate)
        {
            using var connection = Open();
            var trades = ReadPaperTradesBetween(connection, from, to)
                .Where(t => string.Equals(t.TradeSource, TradeSourceWire.Manual, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return NormalizePaperTrades(trades, capitalPerTrade);
        }
    }

    public void UpsertManualOrder(ManualOrderSnapshot order)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO manual_orders(
                    id,user_id,execution_mode,trade_source,status,symbol,underlying,instrument_type,transaction_type,
                    order_type,product,exchange,quantity,limit_price,filled_quantity,average_price,broker_order_id,trade_id,
                    strike,option_right,expiry,message,created_at,updated_at)
                VALUES(
                    $id,$user,$mode,$source,$status,$symbol,$underlying,$instrument,$side,$orderType,$product,$exchange,
                    $quantity,$limit,$filled,$average,$broker,$trade,$strike,$right,$expiry,$message,$created,$updated)
                ON CONFLICT(id) DO UPDATE SET
                    status=excluded.status,
                    filled_quantity=excluded.filled_quantity,
                    average_price=excluded.average_price,
                    broker_order_id=excluded.broker_order_id,
                    trade_id=excluded.trade_id,
                    message=excluded.message,
                    updated_at=excluded.updated_at;
                """;
            Add(command, "$id", order.Id);
            Add(command, "$user", ActiveUserId);
            Add(command, "$mode", order.ExecutionMode);
            Add(command, "$source", order.TradeSource);
            Add(command, "$status", order.Status);
            Add(command, "$symbol", order.Symbol);
            Add(command, "$underlying", order.Underlying);
            Add(command, "$instrument", order.InstrumentType);
            Add(command, "$side", order.TransactionType);
            Add(command, "$orderType", order.OrderType);
            Add(command, "$product", order.Product);
            Add(command, "$exchange", order.Exchange);
            Add(command, "$quantity", order.Quantity);
            Add(command, "$limit", order.LimitPrice is null ? DBNull.Value : DecimalText(order.LimitPrice.Value));
            Add(command, "$filled", order.FilledQuantity);
            Add(command, "$average", DecimalText(order.AveragePrice));
            Add(command, "$broker", order.BrokerOrderId is null ? DBNull.Value : order.BrokerOrderId);
            Add(command, "$trade", order.TradeId is null ? DBNull.Value : order.TradeId);
            Add(command, "$strike", DBNull.Value);
            Add(command, "$right", DBNull.Value);
            Add(command, "$expiry", DBNull.Value);
            Add(command, "$message", order.Message);
            Add(command, "$created", UtcText(order.CreatedAt));
            Add(command, "$updated", UtcText(order.UpdatedAt));
            command.ExecuteNonQuery();
        }
    }

    public ManualOrderSnapshot? GetManualOrder(string orderId)
    {
        lock (_gate)
        {
            using var connection = Open();
            return ReadManualOrders(connection, includeTerminal: true)
                .FirstOrDefault(o => o.Id.Equals(orderId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<ManualOrderSnapshot> ListManualOrders(string? executionMode, bool includeTerminal)
    {
        lock (_gate)
        {
            using var connection = Open();
            return ReadManualOrders(connection, includeTerminal, executionMode);
        }
    }

    private List<ManualOrderSnapshot> ReadManualOrders(
        SqliteConnection connection,
        bool includeTerminal,
        string? executionMode = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,execution_mode,trade_source,status,symbol,underlying,instrument_type,transaction_type,order_type,
                   product,exchange,quantity,limit_price,filled_quantity,average_price,broker_order_id,trade_id,message,
                   created_at,updated_at
            FROM manual_orders
            WHERE user_id=$user
              AND ($mode='' OR execution_mode=$mode)
              AND ($terminal=1 OR status IN ('PENDING','SUBMITTED','PARTIAL'))
            ORDER BY updated_at DESC
            LIMIT 200;
            """;
        Add(command, "$user", ActiveUserId);
        Add(command, "$mode", executionMode ?? "");
        Add(command, "$terminal", includeTerminal ? 1 : 0);
        var list = new List<ManualOrderSnapshot>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ManualOrderSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetInt32(11),
                DecimalValue(reader, 12),
                reader.GetInt32(13),
                DecimalValue(reader, 14) ?? 0,
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.IsDBNull(17) ? "" : reader.GetString(17),
                DateTimeOffset.Parse(reader.GetString(18), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(19), CultureInfo.InvariantCulture)));
        }
        return list;
    }

    public void SaveManualLiveTrade(PaperTradeProposal proposal, string product)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO live_trades(id,user_id,strategy,underlying,product,status,proposal_json,trade_source,created_at,updated_at)
                VALUES($id,$user,$strategy,$underlying,$product,'CONFIRMED',$json,$source,$at,$at)
                ON CONFLICT(id) DO NOTHING;
                """;
            Add(command, "$id", proposal.Id);
            Add(command, "$user", ActiveUserId);
            Add(command, "$strategy", ManualStrategyId);
            Add(command, "$underlying", proposal.Underlying);
            Add(command, "$product", product);
            Add(command, "$json", JsonSerializer.Serialize(proposal));
            Add(command, "$source", TradeSourceWire.Manual);
            Add(command, "$at", UtcText(DateTimeOffset.UtcNow));
            command.ExecuteNonQuery();
            InsertLiveEvent(connection, transaction, proposal.Id, "MANUAL", "Manual live order submitted.", DateTimeOffset.UtcNow);
            transaction.Commit();
        }
    }

    private void Initialize()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS trade_history (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    status TEXT NOT NULL,
                    instrument_type TEXT NOT NULL,
                    symbol TEXT NOT NULL,
                    direction TEXT NOT NULL,
                    quantity INTEGER NOT NULL,
                    trigger_reason TEXT NOT NULL,
                    proposed_price TEXT NOT NULL,
                    entry_price TEXT NULL,
                    exit_price TEXT NULL,
                    stop_loss TEXT NOT NULL,
                    target TEXT NOT NULL,
                    pnl TEXT NULL,
                    exit_reason TEXT NULL,
                    created_at TEXT NOT NULL,
                    opened_at TEXT NULL,
                    closed_at TEXT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_trade_history_user_created
                    ON trade_history(user_id, created_at DESC);
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER PRIMARY KEY,
                    applied_at TEXT NOT NULL
                );
                INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(1,datetime('now'));
                CREATE TABLE IF NOT EXISTS paper_decisions (
                    id TEXT PRIMARY KEY,user_id TEXT NOT NULL,strategy TEXT NOT NULL,underlying TEXT NOT NULL,
                    direction TEXT NOT NULL,evidence_score TEXT NOT NULL,accepted INTEGER NOT NULL,
                    max_risk TEXT NOT NULL,max_reward TEXT NOT NULL,reward_risk TEXT NOT NULL,evaluated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS paper_decision_evidence (
                    decision_id TEXT NOT NULL,kind TEXT NOT NULL,ordinal INTEGER NOT NULL,text TEXT NOT NULL,
                    PRIMARY KEY(decision_id,kind,ordinal),
                    FOREIGN KEY(decision_id) REFERENCES paper_decisions(id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS paper_trades (
                    id TEXT PRIMARY KEY,user_id TEXT NOT NULL,status TEXT NOT NULL,strategy TEXT NOT NULL,
                    underlying TEXT NOT NULL,direction TEXT NOT NULL,stop_loss TEXT NOT NULL,target TEXT NOT NULL,
                    max_risk TEXT NOT NULL,max_reward TEXT NOT NULL,created_at TEXT NOT NULL,updated_at TEXT NOT NULL,
                    opened_at TEXT NULL,closed_at TEXT NULL,exit_reason TEXT NULL,pnl TEXT NULL
                );
                CREATE TABLE IF NOT EXISTS paper_trade_legs (
                    id TEXT PRIMARY KEY,trade_id TEXT NOT NULL,symbol TEXT NOT NULL,underlying TEXT NOT NULL,
                    instrument_type TEXT NOT NULL,side TEXT NOT NULL,ratio INTEGER NOT NULL,quantity INTEGER NOT NULL,
                    proposed_price TEXT NOT NULL,entry_price TEXT NULL,current_price TEXT NULL,exit_price TEXT NULL,
                    strike TEXT NULL,option_right TEXT NULL,expiry TEXT NULL,
                    FOREIGN KEY(trade_id) REFERENCES paper_trades(id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS paper_trade_events (
                    id TEXT PRIMARY KEY,trade_id TEXT NOT NULL,event_type TEXT NOT NULL,reason TEXT NOT NULL,
                    pnl TEXT NULL,created_at TEXT NOT NULL,
                    FOREIGN KEY(trade_id) REFERENCES paper_trades(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ix_paper_trades_user_updated ON paper_trades(user_id,updated_at DESC);
                CREATE INDEX IF NOT EXISTS ix_paper_events_trade_created ON paper_trade_events(trade_id,created_at);
                INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(2,datetime('now'));
                CREATE TABLE IF NOT EXISTS live_trades (
                    id TEXT PRIMARY KEY,user_id TEXT NOT NULL,strategy TEXT NOT NULL,underlying TEXT NOT NULL,
                    product TEXT NOT NULL,status TEXT NOT NULL,proposal_json TEXT NOT NULL,failure_reason TEXT NULL,
                    created_at TEXT NOT NULL,updated_at TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS live_orders (
                    id TEXT PRIMARY KEY,trade_id TEXT NOT NULL,leg_id TEXT NOT NULL,broker_order_id TEXT NOT NULL,
                    exchange TEXT NOT NULL,symbol TEXT NOT NULL,transaction_type TEXT NOT NULL,product TEXT NOT NULL,
                    purpose TEXT NOT NULL,status TEXT NOT NULL,quantity INTEGER NOT NULL,filled_quantity INTEGER NOT NULL,
                    average_price TEXT NOT NULL,broker_message TEXT NOT NULL,tag TEXT NOT NULL,updated_at TEXT NOT NULL,
                    FOREIGN KEY(trade_id) REFERENCES live_trades(id) ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ux_live_orders_broker ON live_orders(broker_order_id)
                    WHERE broker_order_id <> '';
                CREATE INDEX IF NOT EXISTS ix_live_orders_trade ON live_orders(trade_id,updated_at);
                CREATE TABLE IF NOT EXISTS live_events (
                    id TEXT PRIMARY KEY,trade_id TEXT NOT NULL,event_type TEXT NOT NULL,message TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    FOREIGN KEY(trade_id) REFERENCES live_trades(id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ix_live_events_trade_created ON live_events(trade_id,created_at);
                INSERT OR IGNORE INTO schema_migrations(version,applied_at) VALUES(3,datetime('now'));
                """;
            command.ExecuteNonQuery();
            ApplySchemaMigration(connection, 4, "ALTER TABLE paper_trades ADD COLUMN capital_required TEXT NOT NULL DEFAULT '0';");
            ApplySchemaMigration(connection, 5, "ALTER TABLE paper_trades ADD COLUMN trade_source TEXT NOT NULL DEFAULT 'STRATEGY';");
            ApplySchemaMigration(connection, 6, "ALTER TABLE live_trades ADD COLUMN trade_source TEXT NOT NULL DEFAULT 'STRATEGY';");
            ApplySchemaMigration(
                connection,
                7,
                """
                CREATE TABLE IF NOT EXISTS manual_orders (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    execution_mode TEXT NOT NULL,
                    trade_source TEXT NOT NULL DEFAULT 'MANUAL',
                    status TEXT NOT NULL,
                    symbol TEXT NOT NULL,
                    underlying TEXT NOT NULL,
                    instrument_type TEXT NOT NULL,
                    transaction_type TEXT NOT NULL,
                    order_type TEXT NOT NULL,
                    product TEXT NOT NULL,
                    exchange TEXT NOT NULL,
                    quantity INTEGER NOT NULL,
                    limit_price TEXT NULL,
                    filled_quantity INTEGER NOT NULL DEFAULT 0,
                    average_price TEXT NOT NULL DEFAULT '0',
                    broker_order_id TEXT NULL,
                    trade_id TEXT NULL,
                    strike TEXT NULL,
                    option_right TEXT NULL,
                    expiry TEXT NULL,
                    message TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_manual_orders_user_updated ON manual_orders(user_id,updated_at DESC);
                """);
            using var recovery = connection.CreateCommand();
            recovery.CommandText =
                """
                UPDATE trade_history
                SET status = 'INTERRUPTED',
                    exit_reason = 'legacy open trade has no recoverable legs',
                    closed_at = $now
                WHERE status = 'OPEN' AND id NOT IN (SELECT id FROM paper_trades);
                UPDATE trade_history
                SET status = 'EXPIRED',
                    exit_reason = 'backend restarted before confirmation',
                    closed_at = $now
                WHERE status = 'PROPOSED';
                UPDATE paper_trades
                SET status = 'EXPIRED',
                    exit_reason = 'backend restarted before confirmation',
                    closed_at = $now,
                    updated_at = $now
                WHERE status = 'PROPOSED';
                """;
            Add(recovery, "$now", UtcText(DateTimeOffset.UtcNow));
            recovery.ExecuteNonQuery();
            DeleteExpiredUnsafe(connection);
        }
    }

    private void Update(string id, string status, Action<SqliteCommand> appendFields)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE trade_history SET status = $status";
            Add(command, "$status", status);
            appendFields(command);
            command.CommandText += " WHERE id = $id AND user_id = $user;";
            Add(command, "$id", id);
            Add(command, "$user", ActiveUserId);
            command.ExecuteNonQuery();
        }
    }

    private static void ApplySchemaMigration(SqliteConnection connection, int version, string sql)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT 1 FROM schema_migrations WHERE version=$version LIMIT 1;";
        Add(check, "$version", version);
        if (check.ExecuteScalar() is not null)
        {
            return;
        }

        using var migrate = connection.CreateCommand();
        migrate.CommandText = sql;
        try
        {
            migrate.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
        {
            // already applied manually
        }

        using var mark = connection.CreateCommand();
        mark.CommandText = "INSERT INTO schema_migrations(version,applied_at) VALUES($version,datetime('now'));";
        Add(mark, "$version", version);
        mark.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void DeleteExpiredUnsafe(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM trade_history WHERE created_at < $cutoff;
            DELETE FROM paper_trade_events WHERE created_at < $cutoff;
            DELETE FROM paper_decision_evidence WHERE decision_id IN
                (SELECT id FROM paper_decisions WHERE evaluated_at < $cutoff);
            DELETE FROM paper_decisions WHERE evaluated_at < $cutoff;
            DELETE FROM paper_trade_legs WHERE trade_id IN
                (SELECT id FROM paper_trades WHERE created_at < $cutoff);
            DELETE FROM paper_trades WHERE created_at < $cutoff;
            DELETE FROM live_events WHERE created_at < $cutoff;
            DELETE FROM live_orders WHERE trade_id IN (SELECT id FROM live_trades WHERE created_at < $cutoff);
            DELETE FROM live_trades WHERE created_at < $cutoff;
            """;
        Add(command, "$cutoff", UtcText(DateTimeOffset.UtcNow.AddMonths(-12)));
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<RecoveredLeg> ReadLegs(SqliteConnection connection, string tradeId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,symbol,side,quantity,entry_price,current_price,strike,option_right,expiry
            FROM paper_trade_legs WHERE trade_id=$trade ORDER BY rowid;
            """;
        Add(command, "$trade", tradeId);
        using var reader = command.ExecuteReader();
        var legs = new List<RecoveredLeg>();
        while (reader.Read())
        {
            legs.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                DecimalValue(reader, 4) ?? 0, DecimalValue(reader, 5) ?? DecimalValue(reader, 4) ?? 0,
                DecimalValue(reader, 6), reader.IsDBNull(7) ? null : reader.GetString(7), DateValue(reader, 8)));
        }
        return legs;
    }

    private static IReadOnlyList<PaperTradeHistoryLegSnapshot> ReadHistoryLegs(
        SqliteConnection connection,
        string tradeId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,symbol,instrument_type,side,quantity,proposed_price,entry_price,current_price,
                   exit_price,strike,option_right,expiry
            FROM paper_trade_legs WHERE trade_id=$trade ORDER BY rowid;
            """;
        Add(command, "$trade", tradeId);
        using var reader = command.ExecuteReader();
        var legs = new List<PaperTradeHistoryLegSnapshot>();
        while (reader.Read())
        {
            var entry = DecimalValue(reader, 6);
            var exitPrice = DecimalValue(reader, 8);
            var currentPrice = DecimalValue(reader, 7);
            var mark = exitPrice is > 0
                ? exitPrice
                : currentPrice is > 0
                    ? currentPrice
                    : entry;
            decimal? pnl = null;
            if (entry is > 0 && mark is > 0)
            {
                var movement = reader.GetString(3) == "BUY"
                    ? mark.Value - entry.Value
                    : entry.Value - mark.Value;
                pnl = movement * reader.GetInt32(4);
            }
            var instrumentType = InstrumentTypeNormalizer.Parse(reader.IsDBNull(2) ? null : reader.GetString(2));
            legs.Add(new(
                reader.GetString(0), reader.GetString(1), InstrumentTypeNormalizer.ToApiValue(instrumentType), reader.GetString(3),
                reader.GetInt32(4), DecimalValue(reader, 5) ?? 0, entry, currentPrice,
                exitPrice, pnl, DecimalValue(reader, 9),
                reader.IsDBNull(10) ? null : reader.GetString(10), DateValue(reader, 11)));
        }
        return legs;
    }

    private static void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tradeId,
        string type,
        string reason,
        decimal? pnl,
        DateTimeOffset at)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO paper_trade_events(id,trade_id,event_type,reason,pnl,created_at)
            VALUES($id,$trade,$type,$reason,$pnl,$at);
            """;
        Add(command, "$id", Guid.NewGuid().ToString("N"));
        Add(command, "$trade", tradeId);
        Add(command, "$type", type);
        Add(command, "$reason", reason);
        Add(command, "$pnl", pnl is null ? DBNull.Value : DecimalText(pnl.Value));
        Add(command, "$at", UtcText(at));
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<LiveOrderSnapshot> ReadLiveOrders(SqliteConnection connection, string tradeId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,trade_id,leg_id,broker_order_id,exchange,symbol,transaction_type,product,purpose,status,
                   quantity,filled_quantity,average_price,broker_message,tag,updated_at
            FROM live_orders WHERE trade_id=$trade ORDER BY rowid;
            """;
        Add(command, "$trade", tradeId);
        using var reader = command.ExecuteReader();
        var orders = new List<LiveOrderSnapshot>();
        while (reader.Read())
        {
            orders.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetString(9), reader.GetInt32(10), reader.GetInt32(11),
                DecimalValue(reader, 12) ?? 0, reader.GetString(13), reader.GetString(14),
                DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture)));
        }
        return orders;
    }

    private static void InsertLiveEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tradeId,
        string type,
        string message,
        DateTimeOffset at)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO live_events(id,trade_id,event_type,message,created_at) VALUES($id,$trade,$type,$message,$at);";
        Add(command, "$id", Guid.NewGuid().ToString("N"));
        Add(command, "$trade", tradeId);
        Add(command, "$type", type);
        Add(command, "$message", message);
        Add(command, "$at", UtcText(at));
        command.ExecuteNonQuery();
    }

    private static TradeHistoryItem Read(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetString(7),
            DecimalValue(reader, 8) ?? 0m,
            DecimalValue(reader, 9),
            DecimalValue(reader, 10),
            DecimalValue(reader, 11) ?? 0m,
            DecimalValue(reader, 12) ?? 0m,
            DecimalValue(reader, 13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture),
            DateValue(reader, 16),
            DateValue(reader, 17));

    private static decimal? DecimalValue(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : decimal.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset? DateValue(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    private static DateTimeOffset StartOfIstDay(DateTimeOffset now)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = TimeZoneInfo.ConvertTime(now, ist);
        return new DateTimeOffset(local.Date, ist.GetUtcOffset(local.Date));
    }

    private static DateTimeOffset StartOfIstCalendarDay(string ymd)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = DateTime.ParseExact(ymd.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new DateTimeOffset(local.Date, ist.GetUtcOffset(local.Date));
    }

    private static DateTimeOffset EndOfIstCalendarDay(string ymd)
    {
        var ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        var local = DateTime.ParseExact(ymd.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = local.Date.AddDays(1).AddTicks(-1);
        return new DateTimeOffset(end, ist.GetUtcOffset(end));
    }

    private static string UtcText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static string DecimalText(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static void Add(SqliteCommand command, string name, object value) =>
        command.Parameters.AddWithValue(name, value);

    private sealed class UserScope : IDisposable
    {
        private readonly AsyncLocal<string?> _local;
        private readonly string? _previous;

        public UserScope(AsyncLocal<string?> local, string userId)
        {
            _local = local;
            _previous = local.Value;
            local.Value = userId;
        }

        public void Dispose() => _local.Value = _previous;
    }
}
