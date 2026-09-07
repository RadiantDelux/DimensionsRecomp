namespace RecompSetup;

/// <summary>Fork-specific settings applied after the shared upstream InstallJob.</summary>
public static class ForkPostInstall
{
    public const string UpdateRepo = "RadiantDelux/DimensionsRecomp";

    public static void Apply(string installDir, GameLanguageOption? language = null)
    {
        // Keep both sources the updater understands in sync: TOML is primary,
        // install.json is the fallback when the config is missing.
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["updates_repo"] = TomlConfig.Literal(UpdateRepo),
        };
        if (language is not null)
        {
            values["user_language"] = language.LanguageId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            values["user_country"] = language.CountryId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        string toml = Path.Combine(installDir, TomlConfig.FileName);
        if (File.Exists(toml))
        {
            string text = File.ReadAllText(toml);
            File.WriteAllText(toml, SetTomlValues(text, values));
        }

        var manifest = InstallManifest.Load(installDir);
        if (manifest is not null)
        {
            manifest.UpdateRepo = UpdateRepo;
            foreach (var pair in values) manifest.TomlWritten[pair.Key] = pair.Value;
            manifest.Save(installDir);
        }
    }

    static string SetTomlValues(string text, IReadOnlyDictionary<string, string> values)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var remaining = new Dictionary<string, string>(values, StringComparer.Ordinal);

        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            if (!remaining.TryGetValue(key, out string? value)) continue;
            lines[i] = key + " = " + value;
            remaining.Remove(key);
        }

        if (remaining.Count > 0)
        {
            if (lines.Count > 0 && lines[^1].Length != 0) lines.Add("");
            lines.Add("# Setup choices");
            foreach (var pair in remaining) lines.Add(pair.Key + " = " + pair.Value);
        }
        return string.Join(newline, lines);
    }
}
