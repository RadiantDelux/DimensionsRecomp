using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;

namespace RecompSetup;

/// <summary>
/// Turns either an already-extracted disc folder or a user-provided Xbox 360 ISO
/// into a validated game folder. ISO extraction uses XboxDev/extract-xiso, fetched
/// from its official GitHub release only when needed.
/// </summary>
public sealed class PreparedGameSource : IDisposable
{
    public string GameDir { get; }
    readonly string? tempDir;

    PreparedGameSource(string gameDir, string? tempDir)
    {
        GameDir = gameDir;
        this.tempDir = tempDir;
    }

    public static bool IsIso(string path) =>
        File.Exists(path) && Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase);

    public static string? CheckInput(string path, out Xex2Info? info)
    {
        info = null;
        if (Directory.Exists(path)) return Validation.CheckGameDir(path, out info);
        if (IsIso(path)) return null; // Full validation happens after extraction.
        if (File.Exists(path)) return "Select an Xbox 360 .iso file or the extracted game folder.";
        return "Select your Xbox 360 ISO or extracted game folder.";
    }

    public static async Task<PreparedGameSource> PrepareAsync(
        string path, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        if (Directory.Exists(path))
        {
            string? err = Validation.CheckGameDir(path, out _);
            if (err is not null) throw new InvalidOperationException(err);
            return new PreparedGameSource(Path.GetFullPath(path), null);
        }

        if (!IsIso(path))
            throw new InvalidOperationException("The game source is neither a valid extracted folder nor an ISO file.");

        string work = Path.Combine(Path.GetTempPath(), "DimensionsRecompiled", Guid.NewGuid().ToString("N"));
        string toolDir = Path.Combine(work, "tool");
        string gameDir = Path.Combine(work, "game");
        Directory.CreateDirectory(toolDir);
        Directory.CreateDirectory(gameDir);

        try
        {
            progress?.Report(new InstallProgress(0, "Downloading Xbox ISO extractor..."));
            string tool = await EnsureExtractXisoAsync(toolDir, ct);

            progress?.Report(new InstallProgress(0, "Extracting Xbox 360 disc image..."));
            await RunExtractorAsync(tool, Path.GetFullPath(path), gameDir, ct);

            string? err = Validation.CheckGameDir(gameDir, out _);
            if (err is not null)
                throw new InvalidOperationException("The selected ISO is not the required LEGO Dimensions Xbox 360 disc: " + err);

            return new PreparedGameSource(gameDir, work);
        }
        catch
        {
            TryDelete(work);
            throw;
        }
    }

    static async Task<string> EnsureExtractXisoAsync(string toolDir, CancellationToken ct)
    {
        const string url = "https://github.com/XboxDev/extract-xiso/releases/download/build-202505152050/extract-xiso-Win64_Release.zip";
        string zip = Path.Combine(toolDir, "extract-xiso.zip");
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DimensionsRecompiled-Setup/1.0");
        using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var src = await response.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
            await src.CopyToAsync(dst, ct);
        }
        ZipFile.ExtractToDirectory(zip, toolDir, true);
        string? exe = Directory.EnumerateFiles(toolDir, "extract-xiso*.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (exe is null) throw new InvalidOperationException("extract-xiso download did not contain the Windows executable.");
        return exe;
    }

    static async Task RunExtractorAsync(string exe, string iso, string destination, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(iso);
        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(destination);
        psi.ArgumentList.Add("-q");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start extract-xiso.");

        // Drain both redirected pipes concurrently. Reading one to completion
        // before the other can deadlock if the child fills the other pipe.
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Could not extract the Xbox 360 ISO. "
                    + (stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim()));
        }
        catch (OperationCanceledException)
        {
            // Cancellation must not leave an extractor holding files in the temp
            // directory, otherwise cleanup fails and a hidden process keeps running.
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { }
    }

    public void Dispose()
    {
        if (tempDir is not null) TryDelete(tempDir);
    }
}
