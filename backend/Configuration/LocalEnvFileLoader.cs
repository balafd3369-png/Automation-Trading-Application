namespace TradeWithBeast.Configuration;

/// <summary>
/// Loads key=value pairs from a .env file into environment variables (Development convenience only).
/// </summary>
public static class LocalEnvFileLoader
{
    public static void LoadIfPresent(string contentRoot)
    {
        var path = Path.Combine(contentRoot, ".env");
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (value.Length >= 2
                && ((value.StartsWith('"') && value.EndsWith('"'))
                    || (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
