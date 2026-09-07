using System.Diagnostics;

namespace RecompSetup;

/// <summary>
/// Recommended one-screen installer. It auto-detects the user's files, accepts
/// an Xbox 360 ISO directly, chooses sane components and runs the same InstallJob
/// as the original advanced wizard.
/// </summary>
public sealed class EasyInstallForm : Form
{
    readonly PayloadSource payload;
    readonly TextBox game = new();
    readonly TextBox update = new();
    readonly TextBox dlc = new();
    readonly TextBox install = new();
    readonly Label status = new();
    readonly ProgressBar progress = new();
    readonly Button detect = new() { Text = "Auto-detect" };
    readonly Button installButton = new() { Text = "Install" };
    readonly Button advanced = new() { Text = "Advanced..." };
    readonly CheckBox shortcut = new() { Text = "Create desktop shortcut", Checked = true, AutoSize = true };
    readonly CheckBox updates = new() { Text = "Automatic update checks", Checked = true, AutoSize = true };
    CancellationTokenSource? cts;
    bool installing;
    string? readme;

    public EasyInstallForm(PayloadSource payload)
    {
        this.payload = payload;
        Text = WizardForm.AppName + " - Easy Setup";
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(680, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        var heading = new Label
        {
            Text = "Dimensions Recompiled - Recommended setup",
            Font = new Font("Segoe UI Semibold", 16f),
            Bounds = new Rectangle(24, 20, 700, 34),
            AutoSize = false,
        };
        var intro = new Label
        {
            Text = "Point the installer at your own Xbox 360 game files and Title Update 23. " +
                   "It can use an extracted disc folder or extract your ISO automatically. " +
                   "DLC is optional. No copyrighted game data is downloaded.",
            Bounds = new Rectangle(24, 62, 710, 58), AutoSize = false,
        };
        Controls.AddRange(new Control[] { heading, intro });

        int y = 130;
        AddPathRow("Game disc / ISO", game, y,
            () => PickGame(game));
        y += 78;
        AddPathRow("Title Update 23", update, y,
            () => PickUpdate(update));
        y += 78;
        AddPathRow("DLC folder (optional)", dlc, y,
            () => PickFolder(dlc, "Select the folder containing your DLC packages", false));
        y += 78;
        AddPathRow("Install location", install, y,
            () => PickFolder(install, "Choose where Dimensions Recompiled will be installed", true));

        install.Text = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Games", WizardForm.AppName);

        shortcut.Location = new Point(24, 446);
        updates.Location = new Point(220, 446);
        detect.Bounds = new Rectangle(24, 480, 118, 34);
        advanced.Bounds = new Rectangle(150, 480, 118, 34);
        installButton.Bounds = new Rectangle(620, 480, 116, 34);
        progress.Bounds = new Rectangle(284, 486, 320, 20);
        status.Bounds = new Rectangle(24, 518, 712, 34);
        status.ForeColor = Color.DimGray;

        detect.Click += async (_, _) => await DetectAsync();
        advanced.Click += (_, _) => OpenAdvanced();
        installButton.Click += async (_, _) => await InstallAsync();
        FormClosing += (_, e) =>
        {
            if (!installing) return;
            e.Cancel = true;
            if (MessageBox.Show(this, "Cancel the installation?", "Cancel", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
                cts?.Cancel();
        };

        Controls.AddRange(new Control[] { shortcut, updates, detect, advanced, installButton, progress, status });

        var sys = SystemCheck.Run();
        status.Text = sys.Supported ? sys.Summary : string.Join("  ", sys.Warnings);
        status.ForeColor = sys.Supported ? Color.ForestGreen : Color.Firebrick;
        Shown += async (_, _) => { if (sys.Supported) await DetectAsync(silent: true); };
    }

    void AddPathRow(string label, TextBox box, int y, Action browse)
    {
        var l = new Label { Text = label, Bounds = new Rectangle(24, y, 710, 22) };
        box.Bounds = new Rectangle(24, y + 24, 590, 27);
        var b = new Button { Text = "Browse...", Bounds = new Rectangle(624, y + 22, 112, 30) };
        b.Click += (_, _) => browse();
        Controls.AddRange(new Control[] { l, box, b });
    }

    async Task DetectAsync(bool silent = false)
    {
        if (installing) return;
        SetBusy(true, "Looking for LEGO Dimensions files...");
        try
        {
            var result = await Task.Run(() => AutoDiscovery.Discover());
            if (string.IsNullOrWhiteSpace(game.Text) && result.GamePath is not null) game.Text = result.GamePath;
            if (string.IsNullOrWhiteSpace(update.Text) && result.UpdatePath is not null) update.Text = result.UpdatePath;
            if (string.IsNullOrWhiteSpace(dlc.Text) && result.DlcPath is not null) dlc.Text = result.DlcPath;

            int found = (result.GamePath is null ? 0 : 1) + (result.UpdatePath is null ? 0 : 1) + (result.DlcPath is null ? 0 : 1);
            status.Text = found == 0
                ? "Nothing was auto-detected. Use Browse to select your files."
                : $"Auto-detection found {found} source{(found == 1 ? "" : "s")}. Please verify the paths, then click Install.";
            status.ForeColor = found == 0 ? Color.DimGray : Color.ForestGreen;
            if (!silent && found == 0)
                MessageBox.Show(this, "No matching files were found automatically. Select your game ISO/folder and TU23 manually.",
                    "Auto-detect", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            status.Text = "Auto-detection failed: " + e.Message;
            status.ForeColor = Color.Firebrick;
        }
        finally { SetBusy(false); }
    }

    async Task InstallAsync()
    {
        if (installing) return;
        var sys = SystemCheck.Run();
        if (!sys.Supported)
        {
            MessageBox.Show(this, string.Join(Environment.NewLine, sys.Warnings), "Unsupported PC",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        string gamePath = game.Text.Trim().Trim('"');
        string updatePath = update.Text.Trim().Trim('"');
        string dlcPath = dlc.Text.Trim().Trim('"');
        string installPath = install.Text.Trim().Trim('"');

        string? err = PreparedGameSource.CheckInput(gamePath, out _);
        if (err is not null) { Fail(err); return; }
        err = Validation.CheckUpdate(updatePath, out _);
        if (err is not null) { Fail(err); return; }
        if (dlcPath.Length > 0 && !Directory.Exists(dlcPath)) { Fail("The DLC folder does not exist."); return; }
        err = CheckInstallPath(installPath, gamePath);
        if (err is not null) { Fail(err); return; }

        cts = new CancellationTokenSource();
        SetBusy(true, PreparedGameSource.IsIso(gamePath) ? "Preparing your Xbox 360 ISO..." : "Preparing installation...");
        progress.Value = 0;
        PreparedGameSource? prepared = null;
        try
        {
            var prepProgress = new Progress<InstallProgress>(p => status.Text = p.Status);
            prepared = await PreparedGameSource.PrepareAsync(gamePath, prepProgress, cts.Token);

            var foundDlc = Validation.ScanDlc(dlcPath.Length == 0 ? null : dlcPath);
            foreach (var b in Validation.ScanDlc(Path.Combine(prepared.GameDir, "5752084B", "00000002")))
                if (!foundDlc.Any(d => d.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))) foundDlc.Add(b);

            var options = new InstallOptions
            {
                GameDir = prepared.GameDir,
                UpdatePath = updatePath,
                DlcPath = dlcPath.Length == 0 ? null : dlcPath,
                InstallDir = Path.GetFullPath(installPath),
                IncludeToypad = payload.HasToypad,
                IncludeMods = payload.HasMods,
                IncludeRussian = false,
                IncludeSaveConverter = false,
                IncludeUpdater = updates.Checked,
                DesktopShortcut = shortcut.Checked,
            };

            long need = InstallJob.EstimateBytes(options, payload, foundDlc);
            try
            {
                long free = new DriveInfo(Path.GetPathRoot(options.InstallDir)!).AvailableFreeSpace;
                if (free < need * 1.05)
                    throw new InvalidOperationException($"Not enough free space. About {need / 1024.0 / 1024 / 1024:0.0} GB is needed.");
            }
            catch (ArgumentException) { }

            string last = "";
            var reporter = new Progress<InstallProgress>(p =>
            {
                progress.Value = Math.Clamp((int)(p.Fraction * 1000), 0, 1000);
                if (p.Status != last) { last = p.Status; status.Text = p.Status; }
            });
            progress.Maximum = 1000;
            var job = new InstallJob(options, payload, reporter, cts.Token);
            await job.RunAsync(foundDlc);
            readme = job.ReadmePath;
            progress.Value = 1000;
            status.ForeColor = Color.ForestGreen;
            status.Text = "Installation complete. Dimensions Recompiled is ready to launch.";
            installButton.Text = "Open folder";
            installButton.Click -= async (_, _) => await InstallAsync();
            MessageBox.Show(this, "Installation completed successfully.", WizardForm.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            try { Process.Start(new ProcessStartInfo(options.InstallDir) { UseShellExecute = true }); } catch { }
        }
        catch (OperationCanceledException)
        {
            status.Text = "Installation cancelled.";
            status.ForeColor = Color.DimGray;
        }
        catch (Exception e)
        {
            Fail("Installation failed: " + e.Message);
        }
        finally
        {
            prepared?.Dispose();
            SetBusy(false);
        }
    }

    string? CheckInstallPath(string path, string gamePath)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Choose an install location.";
        try { path = Path.GetFullPath(path); }
        catch { return "The install location is not valid."; }

        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (path.StartsWith(pf, StringComparison.OrdinalIgnoreCase) || path.StartsWith(pf86, StringComparison.OrdinalIgnoreCase))
            return "Choose a folder outside Program Files, for example C:\\Games\\Dimensions Recompiled.";

        if (Directory.Exists(gamePath))
        {
            string g = Path.GetFullPath(gamePath).TrimEnd('\\');
            string i = path.TrimEnd('\\');
            if (g.Equals(i, StringComparison.OrdinalIgnoreCase)
                || g.StartsWith(i + "\\", StringComparison.OrdinalIgnoreCase)
                || i.StartsWith(g + "\\", StringComparison.OrdinalIgnoreCase))
                return "The install location must be separate from the source game folder.";
        }
        return null;
    }

    void OpenAdvanced()
    {
        if (installing) return;
        Hide();
        using var form = new WizardForm(payload);
        form.ShowDialog(this);
        Show();
    }

    void PickGame(TextBox box)
    {
        using var f = new OpenFileDialog { Title = "Select your Xbox 360 LEGO Dimensions ISO", Filter = "Xbox 360 ISO (*.iso)|*.iso|All files|*.*" };
        if (f.ShowDialog(this) == DialogResult.OK) { box.Text = f.FileName; return; }
        PickFolder(box, "Or select the already-extracted LEGO Dimensions disc folder", false);
    }

    void PickUpdate(TextBox box)
    {
        using var f = new OpenFileDialog { Title = "Select the Title Update 23 package", Filter = "All files|*.*" };
        if (f.ShowDialog(this) == DialogResult.OK) { box.Text = f.FileName; return; }
        PickFolder(box, "Or select the extracted Title Update 23 folder", false);
    }

    void PickFolder(TextBox box, string description, bool create)
    {
        using var d = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = create,
        };
        if (Directory.Exists(box.Text)) d.InitialDirectory = box.Text;
        if (d.ShowDialog(this) == DialogResult.OK) box.Text = d.SelectedPath;
    }

    void Fail(string message)
    {
        status.Text = message;
        status.ForeColor = Color.Firebrick;
        MessageBox.Show(this, message, "Cannot continue", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void SetBusy(bool busy, string? message = null)
    {
        installing = busy;
        game.Enabled = update.Enabled = dlc.Enabled = install.Enabled = !busy;
        detect.Enabled = advanced.Enabled = installButton.Enabled = !busy;
        shortcut.Enabled = updates.Enabled = !busy;
        if (message is not null) { status.Text = message; status.ForeColor = Color.DimGray; }
    }
}
