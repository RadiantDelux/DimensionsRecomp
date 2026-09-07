namespace RecompSetup;

public sealed record DiscoveryResult(
    string? GamePath,
    string? UpdatePath,
    string? DlcPath,
    IReadOnlyList<string> SearchedRoots);

/// <summary>
/// Conservative auto-discovery for a user's own LEGO Dimensions files. It only
/// walks common user/game locations plus an optional folder explicitly chosen by
/// the user, and it always runs the existing hash/title validation before a
/// candidate is accepted.
/// </summary>
public static class AutoDiscovery
{
    const int MaxDirectories = 15000;
    const int MaxDepth = 5;

    public static DiscoveryResult Discover(string? explicitRoot = null, CancellationToken ct = default)
    {
        var roots = CandidateRoots(explicitRoot).ToList();
        string? game = null;
        string? update = null;
        string? dlc = null;
        string? likelyIso = null;
        int seen = 0;

        foreach (string root in roots)
        {
            ct.ThrowIfCancellationRequested();
            foreach ((string dir, int depth) in Walk(root, MaxDepth, ct))
            {
                if (++seen > MaxDirectories) break;
                ct.ThrowIfCancellationRequested();

                if (game is null && LooksLikeGameFolder(dir)
                    && Validation.CheckGameDir(dir, out _) is null)
                    game = dir;

                if (update is null && File.Exists(Path.Combine(dir, "Default.xexp"))
                    && Validation.CheckUpdate(dir, out _) is null)
                    update = dir;

                if (dlc is null && LooksLikeDlcContainer(dir))
                {
                    try
                    {
                        if (Validation.ScanDlc(dir).Count > 0) dlc = dir;
                    }
                    catch { /* unreadable folder: keep searching */ }
                }

                foreach (string file in SafeFiles(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    string name = Path.GetFileName(file);
                    string ext = Path.GetExtension(file);

                    if (game is null && likelyIso is null
                        && ext.Equals(".iso", StringComparison.OrdinalIgnoreCase)
                        && (name.Contains("dimension", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("lego", StringComparison.OrdinalIgnoreCase)))
                    {
                        likelyIso = file;
                    }

                    if (update is null && IsLikelyTitleUpdate(file))
                    {
                        try
                        {
                            if (Validation.CheckUpdate(file, out _) is null) update = file;
                        }
                        catch { /* not our TU */ }
                    }
                }

                if (game is not null && update is not null && dlc is not null) break;
            }
            if (seen > MaxDirectories || (game is not null && update is not null && dlc is not null)) break;
        }

        return new DiscoveryResult(game ?? likelyIso, update, dlc, roots);
    }

    static bool LooksLikeGameFolder(string dir) =>
        File.Exists(Path.Combine(dir, "Default.xex"))
        && File.Exists(Path.Combine(dir, "GAME.DAT"))
        && File.Exists(Path.Combine(dir, "GAME.HDR"));

    static bool LooksLikeDlcContainer(string dir)
    {
        string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        if (name.Equals("00000002", StringComparison.OrdinalIgnoreCase)
            || name.Equals("5752084B", StringComparison.OrdinalIgnoreCase)
            || name.Contains("dlc", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            return Directory.EnumerateDirectories(dir)
                .Any(d => Path.GetFileName(d).Equals("00000002", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    static bool IsLikelyTitleUpdate(string file)
    {
        string name = Path.GetFileName(file);
        if (name.StartsWith("tu", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            long size = new FileInfo(file).Length;
            return size == KnownGame.Tu23PackageSize || size == KnownGame.Tu24PackageSize;
        }
        catch { return false; }
    }

    static IEnumerable<string> CandidateRoots(string? explicitRoot)
    {
        var result = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try
            {
                p = Path.GetFullPath(p);
                if (Directory.Exists(p) && !result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
            }
            catch { }
        }

        Add(explicitRoot);
        Add(AppContext.BaseDirectory);
        Add(Environment.CurrentDirectory);

        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add(Path.Combine(profile, "Downloads"));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                foreach (string name in new[] { "Games", "Xbox", "Xbox 360", "Emulation", "Roms", "ROMs", "LEGO", "LEGO Dimensions", "Downloads" })
                    Add(Path.Combine(drive.RootDirectory.FullName, name));

                // Also inspect obviously relevant top-level folders without
                // recursively walking an entire drive.
                foreach (string top in SafeDirectories(drive.RootDirectory.FullName))
                {
                    string n = Path.GetFileName(top);
                    if (n.Contains("dimension", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("lego", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("xbox", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("game", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("rom", StringComparison.OrdinalIgnoreCase))
                        Add(top);
                }
            }
            catch { }
        }
        return result;
    }

    static IEnumerable<(string Path, int Depth)> Walk(string root, int maxDepth, CancellationToken ct)
    {
        var q = new Queue<(string Path, int Depth)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        q.Enqueue((root, 0));
        while (q.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var item = q.Dequeue();
            string full;
            try { full = Path.GetFullPath(item.Path); }
            catch { continue; }
            if (!visited.Add(full)) continue;
            yield return (full, item.Depth);
            if (item.Depth >= maxDepth) continue;
            foreach (string child in SafeDirectories(full)) q.Enqueue((child, item.Depth + 1));
        }
    }

    static IEnumerable<string> SafeDirectories(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.GetFiles(dir); }
        catch { return Array.Empty<string>(); }
    }
}
