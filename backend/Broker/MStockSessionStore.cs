using System.Text.Json;

namespace TradeWithBeast.Broker;

public sealed class MStockSessionStore
{
    private readonly string _path;

    public MStockSessionStore(string? sessionFilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(sessionFilePath))
        {
            _path = sessionFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            return;
        }

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TradeWithBeast");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "mstock-session.json");
    }

    public string? ReadToken()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (doc.RootElement.TryGetProperty("accessToken", out var token))
            {
                var value = token.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    public void SaveToken(string token)
    {
        var json = JsonSerializer.Serialize(new { accessToken = token, savedAt = DateTimeOffset.Now });
        File.WriteAllText(_path, json);
    }

    public void ClearToken()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
