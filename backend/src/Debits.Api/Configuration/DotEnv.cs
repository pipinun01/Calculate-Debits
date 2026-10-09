using Microsoft.Extensions.Configuration.Memory;

namespace Debits.Api.Configuration;

/// <summary>
/// Loads the repo-root <c>.env</c> (KEY=VALUE, the keys from <c>.env.example</c>) right after the appsettings json files,
/// so its values override the empty placeholders there while real environment variables and user secrets still win.
/// </summary>
public static class DotEnv
{
    private static readonly Dictionary<string, string> KeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BOT_TOKEN"] = "Bot:Token",
        ["BOT_USERNAME"] = "Bot:Username",
        ["BOT_WEBHOOK_SECRET"] = "Bot:WebhookSecret",
        ["BOT_MODE"] = "Bot:Mode",
        ["PUBLIC_URL"] = "Bot:PublicUrl",
        ["MINIAPP_URL"] = "MiniApp:Url",
        ["GROQ_API_KEY"] = "Groq:ApiKey",
        ["JWT_SECRET"] = "Jwt:Secret",
        ["DATABASE_URL"] = "ConnectionStrings:Default",
    };

    public static void AddDotEnv(this IConfigurationBuilder configuration, string startDirectory)
    {
        var path = Find(startDirectory);
        if (path is null)
        {
            return;
        }

        var values = new Dictionary<string, string?>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..];
            var comment = value.IndexOf(" #", StringComparison.Ordinal);
            if (comment >= 0)
            {
                value = value[..comment];
            }

            value = value.Trim().Trim('"', '\'');
            if (value.Length == 0)
            {
                continue;
            }

            if (KeyMap.TryGetValue(key, out var mapped))
            {
                values[mapped] = value;
            }
        }

        var afterJson = 0;
        for (var i = 0; i < configuration.Sources.Count; i++)
        {
            if (configuration.Sources[i] is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource)
            {
                afterJson = i + 1;
            }
        }

        configuration.Sources.Insert(afterJson, new MemoryConfigurationSource { InitialData = values });
    }

    private static string? Find(string startDirectory)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
