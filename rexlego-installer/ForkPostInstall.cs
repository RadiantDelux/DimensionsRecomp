namespace RecompSetup;

/// <summary>Fork-specific settings applied after the shared upstream InstallJob.</summary>
public static class ForkPostInstall
{
    public const string UpdateRepo = "RadiantDelux/DimensionsRecomp";

    public static void Apply(string installDir)
    {
        // Keep both sources the updater understands in sync: TOML is primary,
        // install.json is the fallback when the config is missing.
        string toml = Path.Combine(installDir, TomlConfig.FileName);
        if (File.Exists(toml))
        {
            string text = File.ReadAllText(toml);
            text = ReplaceTomlValue(text, "updates_repo", TomlConfig.Literal(UpdateRepo));
            File.WriteAllText(toml, text);
        }

        var manifest = InstallManifest.Load(installDir);
        if (manifest is not null)
        {
            manifest.UpdateRepo = UpdateRepo;
            manifest.TomlWritten["updates_repo"] = TomlConfig.Literal(UpdateRepo);
            manifest.Save(installDir);
        }
    }

    static string ReplaceTomlValue(string text, string key, string value)
    {
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        bool found = false;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            int eq = line.IndexOf('=');
            if (eq <= 0 || !line[..eq].Trim().Equals(key, StringComparison.Ordinal)) continue;
            lines[i] = key + " = " + value;
            found = true;
            break;
        }
        if (!found)
        {
            if (lines.Count > 0 && lines[^1].Length != 0) lines.Add("");
            lines.Add("# Fork update source");
            lines.Add(key + " = " + value);
        }
        return string.Join(newline, lines);
    }
}
