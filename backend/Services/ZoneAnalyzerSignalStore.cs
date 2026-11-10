using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradeWithBeast.Configuration;
using TradeWithBeast.Models;

namespace TradeWithBeast.Services;

/// <summary>
/// Persists analyzer signals for adaptive learning (outcome tracking over time).
/// </summary>
public sealed class ZoneAnalyzerSignalStore
{
    private static readonly TimeZoneInfo Ist = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
    private readonly object _gate = new();
    private readonly string _path;
    private readonly ILogger<ZoneAnalyzerSignalStore> _log;
    private AnalyzerSignalFile _file = new();

    public ZoneAnalyzerSignalStore(
        IConfiguration configuration,
        DatabaseOptions database,
        ILogger<ZoneAnalyzerSignalStore> log)
    {
        var dbPath = DatabaseConnectionResolver.ResolveSqliteDatabasePath(configuration, database);
        var dir = Path.GetDirectoryName(dbPath) ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TradeWithBeast");
        _path = Path.Combine(dir, "zone-analyzer-signals.json");
        _log = log;
        Load();
    }

    public void RecordScan(ZoneStockAnalyzerResponse response)
    {
        var today = TodayIst();
        var keysToday = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var row in response.Buy.Concat(response.Sell))
            {
                if (row.QualityScore < 70)
                {
                    continue;
                }

                var key = $"{today:yyyy-MM-dd}|{row.Name}|{row.PatternId}";
                if (!keysToday.Add(key))
                {
                    continue;
                }

                if (_file.Signals.Any(s => s.DedupeKey == key))
                {
                    continue;
                }

                _file.Signals.Add(new AnalyzerSignalEntry(
                    key,
                    row.Name,
                    row.PatternId,
                    row.Action,
                    row.Last,
                    row.QualityScore,
                    response.AnalyzedAt));
            }

            PruneOld();
            Save();
        }
    }

    public AnalyzerAdaptiveStats StatsForPattern(string patternId, int lastDays = 30)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-lastDays);
        lock (_gate)
        {
            var relevant = _file.Signals
                .Where(s => s.PatternId.Equals(patternId, StringComparison.OrdinalIgnoreCase)
                    && s.GeneratedAt >= cutoff)
                .ToList();
            var wins = relevant.Count(s => s.Outcome == "win");
            var losses = relevant.Count(s => s.Outcome == "loss");
            var closed = wins + losses;
            var wr = closed > 0 ? wins * 100m / closed : (decimal?)null;
            return new AnalyzerAdaptiveStats(relevant.Count, wins, losses, wr);
        }
    }

    private void PruneOld()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-120);
        _file.Signals.RemoveAll(s => s.GeneratedAt < cutoff);
    }

    private static DateOnly TodayIst()
    {
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        return DateOnly.FromDateTime(now.Date);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path);
            _file = JsonSerializer.Deserialize<AnalyzerSignalFile>(json) ?? new AnalyzerSignalFile();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not load zone analyzer signals from {Path}", _path);
            _file = new AnalyzerSignalFile();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not save zone analyzer signals to {Path}", _path);
        }
    }

    private sealed class AnalyzerSignalFile
    {
        public List<AnalyzerSignalEntry> Signals { get; set; } = [];
    }

    public sealed record AnalyzerSignalEntry(
        string DedupeKey,
        string Symbol,
        string PatternId,
        string Action,
        decimal EntryPrice,
        int QualityScore,
        DateTimeOffset GeneratedAt,
        string? Outcome = null);

    public sealed record AnalyzerAdaptiveStats(int SignalsLogged, int Wins, int Losses, decimal? WinRatePercent);
}
