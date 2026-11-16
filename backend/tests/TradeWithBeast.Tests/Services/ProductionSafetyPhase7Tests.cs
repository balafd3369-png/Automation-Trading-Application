using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradeWithBeast.Broker;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;
using TradeWithBeast.Services;

namespace TradeWithBeast.Tests.Services;

public sealed class ProductionSafetyPhase7Tests
{
    [Fact]
    public async Task Sqlite_backup_creates_verified_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-main-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var options = new ProductionSafetyOptions
        {
            Enabled = true,
            DatabaseBackupEnabled = true,
            DatabaseBackupDirectory = Path.Combine(Path.GetTempPath(), $"twb-bk-{Guid.NewGuid():N}"),
        };
        var store = new ProductionSafetyStore(configuration, database, NullLogger<ProductionSafetyStore>.Instance);
        new TradeHistoryStore(configuration, NullLogger<TradeHistoryStore>.Instance);
        var service = new SqliteDatabaseBackupService(
            options,
            database,
            configuration,
            store,
            NullLogger<SqliteDatabaseBackupService>.Instance);
        var backup = await service.CreateBackupAsync("test", CancellationToken.None);
        Assert.True(backup.Status is DatabaseBackupResultStatus.Verified or DatabaseBackupResultStatus.Succeeded);
        Assert.True(File.Exists(backup.BackupFilePath));
        Assert.True(service.VerifyBackupFile(backup.BackupFilePath));
    }

    [Fact]
    public async Task Sqlite_backup_can_be_restored_to_temporary_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-main-{Guid.NewGuid():N}.db");
        var restorePath = Path.Combine(Path.GetTempPath(), $"twb-restore-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var options = new ProductionSafetyOptions
        {
            Enabled = true,
            DatabaseBackupEnabled = true,
            DatabaseBackupVerifyAfterCreate = true,
            DatabaseBackupDirectory = Path.Combine(Path.GetTempPath(), $"twb-bk-{Guid.NewGuid():N}"),
        };
        var store = new ProductionSafetyStore(configuration, database, NullLogger<ProductionSafetyStore>.Instance);
        new TradeHistoryStore(configuration, NullLogger<TradeHistoryStore>.Instance);
        var service = new SqliteDatabaseBackupService(
            options,
            database,
            configuration,
            store,
            NullLogger<SqliteDatabaseBackupService>.Instance);
        var backup = await service.CreateBackupAsync("restore-test", CancellationToken.None);
        Assert.True(File.Exists(backup.BackupFilePath));
        File.Copy(backup.BackupFilePath, restorePath, overwrite: true);
        Assert.True(File.Exists(restorePath));
        Assert.True(service.VerifyBackupFile(restorePath));
    }

    [Fact]
    public void Reconciliation_mismatch_raises_production_incident_candidate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"twb-ps-{Guid.NewGuid():N}.db");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Database:SqlitePath"] = path })
            .Build();
        var database = new DatabaseOptions { SqlitePath = path };
        var options = new ProductionSafetyOptions { Enabled = true, ReconciliationIncidentsEnabled = true };
        var store = new ProductionSafetyStore(configuration, database, NullLogger<ProductionSafetyStore>.Instance);
        var history = new TradeHistoryStore(configuration, NullLogger<TradeHistoryStore>.Instance);
        var ops = new OperationsStore(
            configuration,
            database,
            new CurrentUserAccessor(),
            new SystemIdentityOptions(),
            NullLogger<OperationsStore>.Instance);
        var heartbeats = new WorkerHeartbeatRegistry(new SystemMonitoringOptions());
        var brokers = new BrokerConnectionManager(new StubMarketClient(), new StubOrderClient());
        var recon = new BrokerReconciliationService(
            brokers,
            history,
            ops,
            heartbeats,
            NullLogger<BrokerReconciliationService>.Instance);
        var snapshot = new BrokerReconciliationSnapshot(
            BrokerReconciliationHealth.IssuesFound,
            "Issues Found",
            true,
            0,
            1,
            0,
            1,
            1,
            DateTimeOffset.UtcNow,
            null,
            [new BrokerReconciliationMismatch("ORDER", "RELIANCE", "SUBMITTED", "COMPLETE", "status mismatch", "Investigate")],
            []);
        var field = typeof(BrokerReconciliationService).GetField("_snapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(recon, snapshot);
        var evaluator = new ProductionSafetyIncidentEvaluator(
            options,
            store,
            recon,
            new SystemIncidentDeduplicationService());
        var candidates = evaluator.Evaluate();
        Assert.Contains(
            candidates,
            c => !c.IsHealthy && c.Category == SystemIncidentCategory.OrderReconciliationMismatch);
    }

    private sealed class StubMarketClient : IBrokerMarketClient
    {
        public string ProviderId => BrokerIds.MStock;
        public string DisplayName => "test";
        public bool HasSession => false;
        public void ClearSession() { }
        public Task<IReadOnlyDictionary<string, QuoteSnap>> GetOhlcAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, QuoteSnap>>(new Dictionary<string, QuoteSnap>());
        public Task<IReadOnlyDictionary<string, decimal>> GetLtpAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>());
        public Task<IReadOnlyDictionary<string, decimal>> GetLtpForBoardNamesAsync(IReadOnlyList<string> boardNames, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, decimal>>(new Dictionary<string, decimal>());
        public Task<QuoteSnap?> GetBoardQuoteAsync(string boardName, CancellationToken ct) => Task.FromResult<QuoteSnap?>(null);
        public Task<OptionQuoteSnap?> GetOptionQuoteAsync(string underlying, DateTime expiry, decimal strike, string side, CancellationToken ct) =>
            Task.FromResult<OptionQuoteSnap?>(null);
        public Task<IReadOnlyList<OptionMarketQuote>> GetOptionQuotesAsync(IReadOnlyList<OptionQuoteRequest> contracts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OptionMarketQuote>>(Array.Empty<OptionMarketQuote>());
        public Task<FundSummary?> GetFundSummaryAsync(CancellationToken ct) => Task.FromResult<FundSummary?>(null);
        public Task<IReadOnlyList<DayCandle>> GetDayCandlesAsync(string exchange, int instrumentToken, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
        public Task<IReadOnlyList<DayCandle>> GetIntradayCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
        public Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
        public Task<IReadOnlyList<DayCandle>> GetCandlesAsync(string exchange, int instrumentToken, string interval, DateTimeOffset? fromIst, DateTimeOffset? toIst, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DayCandle>>(Array.Empty<DayCandle>());
    }

    private sealed class StubOrderClient : IBrokerOrderClient
    {
        public string ProviderId => BrokerIds.MStock;
        public Task<BrokerApiResult<string>> PlaceOrderAsync(BrokerOrderRequest order, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<string>> ModifyOrderAsync(string orderId, BrokerOrderRequest order, int remainingQuantity, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<string>> CancelOrderAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<IReadOnlyList<BrokerOrder>>> GetOrdersAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerOrder>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerTrade>>> GetTradesAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerTrade>>(true, [], ""));
        public Task<BrokerApiResult<IReadOnlyList<BrokerPosition>>> GetPositionsAsync(CancellationToken ct) =>
            Task.FromResult(new BrokerApiResult<IReadOnlyList<BrokerPosition>>(true, [], ""));
        public Task<BrokerApiResult<BrokerMargin>> CalculateMarginAsync(BrokerOrderRequest order, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerApiResult<bool>> ConvertPositionAsync(string tradingSymbol, string exchange, string transactionType, int quantity, string oldProduct, string newProduct, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}