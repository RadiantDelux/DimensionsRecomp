using System.Diagnostics;

namespace RecompSetup;

/// <summary>
/// The recommended setup surface. It keeps the legal/source-file decisions in
/// front of the user, but handles extraction, validation, components and config
/// without turning installation into a long wizard.
/// </summary>
public sealed class EasyInstallForm : Form
{
    static readonly Color Page = Color.FromArgb(245, 247, 251);
    static readonly Color Ink = Color.FromArgb(25, 32, 48);
    static readonly Color Muted = Color.FromArgb(102, 112, 133);
    static readonly Color Line = Color.FromArgb(222, 227, 236);
    static readonly Color Accent = Color.FromArgb(103, 80, 214);
    static readonly Color AccentSoft = Color.FromArgb(241, 238, 255);
    static readonly Color Success = Color.FromArgb(24, 128, 85);
    static readonly Color Danger = Color.FromArgb(180, 35, 24);
    static readonly Color Community = Color.FromArgb(176, 96, 15);

    readonly PayloadSource payload;
    readonly TextBox game = PathBox("Xbox 360 ISO or extracted game folder");
    readonly TextBox update = PathBox("TU23 package or extracted update folder");
    readonly TextBox dlc = PathBox("Optional folder containing DLC packages");
    readonly TextBox install = PathBox("Installation folder");
    readonly ComboBox language = new();
    readonly Label languageAvailability = new();
    readonly Label languageDetail = new();
    readonly Label status = new();
    readonly ProgressBar progress = new() { Maximum = 1000, Visible = false };
    readonly Button detect = SecondaryButton("Find files automatically");
    readonly Button installButton = PrimaryButton("Install");
    readonly Button openFolder = SecondaryButton("Open folder");
    readonly LinkLabel advanced = new();
    readonly CheckBox shortcut = OptionBox("Create a desktop shortcut", true);
    readonly CheckBox updates = OptionBox("Check for updates automatically", true);
    readonly List<Button> browseButtons = new();
    CancellationTokenSource? cts;
    bool installing;
    string? completedDir;

    public EasyInstallForm(PayloadSource payload)
    {
        this.payload = payload;
        Text = WizardForm.AppName + " Setup";
        ClientSize = new Size(920, 700);
        MinimumSize = MaximumSize = new Size(936, 739);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Page;
        Font = new Font("Segoe UI", 9.5f);
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        BuildHeader();
        BuildSourceCard();
        BuildOptionsCard();
        BuildFooter();

        install.Text = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\", "Games", WizardForm.AppName);
        PopulateLanguages();

        detect.Click += async (_, _) => await DetectAsync();
        advanced.LinkClicked += (_, _) => OpenAdvanced();
        installButton.Click += async (_, _) =>
        {
            if (installing)
            {
                cts?.Cancel();
                return;
            }
            if (completedDir is not null)
            {
                LaunchGame();
                return;
            }
            await InstallAsync();
        };
        openFolder.Click += (_, _) => OpenInstallFolder();
        FormClosing += (_, e) =>
        {
            if (!installing) return;
            e.Cancel = true;
            if (MessageBox.Show(this, "Cancel the installation in progress?", "Dimensions Recompiled",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                cts?.Cancel();
        };

        AcceptButton = installButton;
        var sys = SystemCheck.Run();
        SetStatus(sys.Supported ? sys.Summary : string.Join("  ", sys.Warnings),
                  sys.Supported ? Success : Danger);
        Shown += async (_, _) => { if (sys.Supported) await DetectAsync(silent: true); };
    }

    void BuildHeader()
    {
        var header = new Panel
        {
            Bounds = new Rectangle(0, 0, ClientSize.Width, 132),
            BackColor = Color.FromArgb(18, 24, 38),
        };
        Controls.Add(header);

        Image? logo = LoadLogo();
        if (logo is not null)
        {
            header.Controls.Add(new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Bounds = new Rectangle(28, 18, 248, 96),
                BackColor = Color.Transparent,
            });
        }

        header.Controls.Add(new Label
        {
            Text = "Set up Dimensions Recompiled",
            Font = new Font("Segoe UI Semibold", 20f),
            ForeColor = Color.White,
            Bounds = new Rectangle(310, 30, 560, 38),
        });
        header.Controls.Add(new Label
        {
            Text = "Bring your game files once. Setup handles the rest.",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = Color.FromArgb(196, 202, 215),
            Bounds = new Rectangle(312, 72, 520, 26),
        });

        string version = typeof(EasyInstallForm).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        header.Controls.Add(new Label
        {
            Text = "PRE-RELEASE  ·  " + version,
            Font = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = Color.FromArgb(190, 179, 255),
            TextAlign = ContentAlignment.MiddleRight,
            Bounds = new Rectangle(710, 100, 170, 20),
        });
    }

    void BuildSourceCard()
    {
        var card = Card(new Rectangle(24, 154, 564, 444));
        Controls.Add(card);
        card.Controls.Add(SectionTitle("Game files", 22));
        card.Controls.Add(new Label
        {
            Text = "Use your own disc dump and Title Update 23. Files are validated before anything is installed.",
            ForeColor = Muted,
            Bounds = new Rectangle(22, 52, 516, 38),
        });

        AddPathRow(card, "Disc image or extracted folder", game, 92, ShowGameMenu);
        AddPathRow(card, "Title Update 23", update, 167, ShowUpdateMenu);
        AddPathRow(card, "DLC", dlc, 242, b => PickFolder(dlc, "Select the folder containing your DLC packages", false));
        AddPathRow(card, "Install location", install, 317, b => PickFolder(install, "Choose where Dimensions Recompiled will be installed", true));

        detect.Bounds = new Rectangle(22, 394, 184, 32);
        card.Controls.Add(detect);
        card.Controls.Add(new Label
        {
            Text = "No game data is downloaded.",
            ForeColor = Muted,
            TextAlign = ContentAlignment.MiddleRight,
            Bounds = new Rectangle(290, 397, 250, 26),
        });
    }

    void BuildOptionsCard()
    {
        var card = Card(new Rectangle(606, 154, 290, 444));
        Controls.Add(card);
        card.Controls.Add(SectionTitle("Preferences", 22));
        card.Controls.Add(new Label
        {
            Text = "Game language",
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Ink,
            Bounds = new Rectangle(22, 66, 240, 22),
        });

        language.DropDownStyle = ComboBoxStyle.DropDownList;
        language.FlatStyle = FlatStyle.Flat;
        language.Font = new Font("Segoe UI", 10f);
        language.BackColor = Color.White;
        language.ForeColor = Ink;
        language.Bounds = new Rectangle(22, 91, 246, 32);
        language.SelectedIndexChanged += (_, _) => RefreshLanguageDescription();
        card.Controls.Add(language);

        languageAvailability.Font = new Font("Segoe UI Semibold", 8.75f);
        languageAvailability.Bounds = new Rectangle(22, 132, 246, 22);
        card.Controls.Add(languageAvailability);
        languageDetail.ForeColor = Muted;
        languageDetail.Bounds = new Rectangle(22, 156, 246, 72);
        card.Controls.Add(languageDetail);

        card.Controls.Add(new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Bounds = new Rectangle(22, 238, 246, 2),
        });
        card.Controls.Add(new Label
        {
            Text = "Install options",
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Ink,
            Bounds = new Rectangle(22, 258, 240, 22),
        });

        updates.Location = new Point(22, 289);
        shortcut.Location = new Point(22, 321);
        card.Controls.AddRange(new Control[] { updates, shortcut });

        card.Controls.Add(new Label
        {
            Text = "Toy Pad support and bundled mods are installed automatically when present in this build.",
            ForeColor = Muted,
            Bounds = new Rectangle(22, 357, 246, 52),
        });

        advanced.Text = "Advanced setup";
        advanced.LinkColor = Accent;
        advanced.ActiveLinkColor = Color.FromArgb(79, 57, 184);
        advanced.VisitedLinkColor = Accent;
        advanced.Font = new Font("Segoe UI Semibold", 9f);
        advanced.AutoSize = true;
        advanced.Location = new Point(22, 410);
        card.Controls.Add(advanced);
    }

    void BuildFooter()
    {
        status.ForeColor = Muted;
        status.Font = new Font("Segoe UI", 9.25f);
        status.Bounds = new Rectangle(24, 617, 590, 24);
        Controls.Add(status);

        progress.Bounds = new Rectangle(24, 647, 590, 8);
        progress.Style = ProgressBarStyle.Continuous;
        Controls.Add(progress);

        openFolder.Bounds = new Rectangle(624, 624, 122, 42);
        openFolder.Visible = false;
        Controls.Add(openFolder);

        installButton.Bounds = new Rectangle(758, 624, 138, 42);
        Controls.Add(installButton);

        Controls.Add(new Label
        {
            Text = "You can change most settings later with F4 in game.",
            ForeColor = Muted,
            Font = new Font("Segoe UI", 8.5f),
            Bounds = new Rectangle(24, 668, 480, 20),
        });
    }

    void PopulateLanguages()
    {
        var available = GameLanguages.AvailableFor(payload);
        foreach (var item in available) language.Items.Add(item);
        var preferred = GameLanguages.DetectWindowsLanguage(payload);
        int selected = available.ToList().FindIndex(l => l.Key == preferred.Key);
        language.SelectedIndex = selected >= 0 ? selected : 0;
        RefreshLanguageDescription();
    }

    void RefreshLanguageDescription()
    {
        if (language.SelectedItem is not GameLanguageOption selected) return;
        languageAvailability.Text = selected.Availability;
        languageAvailability.ForeColor = selected.RequiresRussianMod ? Community : Accent;
        languageDetail.Text = selected.Detail;
    }

    void AddPathRow(Panel card, string label, TextBox box, int y, Action<Button> browse)
    {
        card.Controls.Add(new Label
        {
            Text = label,
            Font = new Font("Segoe UI Semibold", 9.25f),
            ForeColor = Ink,
            Bounds = new Rectangle(22, y, 500, 21),
        });
        box.Bounds = new Rectangle(22, y + 24, 398, 29);
        var button = SecondaryButton("Browse");
        button.Bounds = new Rectangle(430, y + 22, 110, 32);
        button.Click += (_, _) => browse(button);
        browseButtons.Add(button);
        card.Controls.AddRange(new Control[] { box, button });
    }

    void ShowGameMenu(Button anchor)
    {
        using var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add("Disc image (.iso)", null, (_, _) => PickGameIso());
        menu.Items.Add("Extracted game folder", null, (_, _) => PickFolder(game, "Select the extracted LEGO Dimensions game folder", false));
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    void ShowUpdateMenu(Button anchor)
    {
        using var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add("Original TU23 package", null, (_, _) => PickUpdatePackage());
        menu.Items.Add("Extracted TU23 folder", null, (_, _) => PickFolder(update, "Select the extracted Title Update 23 folder", false));
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    async Task DetectAsync(bool silent = false)
    {
        if (installing) return;
        SetBusy(true, "Looking for matching game files on this PC...");
        try
        {
            var result = await Task.Run(() => AutoDiscovery.Discover());
            if (string.IsNullOrWhiteSpace(game.Text) && result.GamePath is not null) game.Text = result.GamePath;
            if (string.IsNullOrWhiteSpace(update.Text) && result.UpdatePath is not null) update.Text = result.UpdatePath;
            if (string.IsNullOrWhiteSpace(dlc.Text) && result.DlcPath is not null) dlc.Text = result.DlcPath;

            int found = (result.GamePath is null ? 0 : 1) + (result.UpdatePath is null ? 0 : 1) + (result.DlcPath is null ? 0 : 1);
            if (found == 0)
            {
                SetStatus("Nothing matched automatically. Choose the game and TU23 with Browse.", Muted);
                if (!silent)
                    MessageBox.Show(this, "No matching files were found automatically. Choose your game ISO or folder and Title Update 23 manually.",
                        "Find files", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                SetStatus(found == 1 ? "Found one matching source. Check the paths before installing."
                                     : $"Found {found} matching sources. Check the paths before installing.", Success);
            }
        }
        catch (Exception e)
        {
            SetStatus("Automatic search could not finish: " + e.Message, Danger);
        }
        finally { SetBusy(false); }
    }

    async Task InstallAsync()
    {
        if (installing) return;
        var sys = SystemCheck.Run();
        if (!sys.Supported)
        {
            Fail(string.Join(Environment.NewLine, sys.Warnings));
            return;
        }
        if (language.SelectedItem is not GameLanguageOption selectedLanguage)
        {
            Fail("Choose a game language.");
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
        SetBusy(true, PreparedGameSource.IsIso(gamePath) ? "Extracting and validating the Xbox 360 disc image..." : "Preparing the installation...");
        progress.Value = 0;
        PreparedGameSource? prepared = null;
        try
        {
            var prepProgress = new Progress<InstallProgress>(p => SetStatus(p.Status, Muted));
            prepared = await PreparedGameSource.PrepareAsync(gamePath, prepProgress, cts.Token);

            var foundDlc = Validation.ScanDlc(dlcPath.Length == 0 ? null : dlcPath);
            foreach (var bundled in Validation.ScanDlc(Path.Combine(prepared.GameDir, "5752084B", "00000002")))
                if (!foundDlc.Any(d => d.Name.Equals(bundled.Name, StringComparison.OrdinalIgnoreCase))) foundDlc.Add(bundled);

            var options = new InstallOptions
            {
                GameDir = prepared.GameDir,
                UpdatePath = updatePath,
                DlcPath = dlcPath.Length == 0 ? null : dlcPath,
                InstallDir = Path.GetFullPath(installPath),
                IncludeToypad = payload.HasToypad,
                IncludeMods = payload.HasMods,
                IncludeRussian = selectedLanguage.RequiresRussianMod,
                IncludeSaveConverter = false,
                IncludeUpdater = updates.Checked,
                DesktopShortcut = shortcut.Checked,
            };

            long need = InstallJob.EstimateBytes(options, payload, foundDlc);
            try
            {
                long free = new DriveInfo(Path.GetPathRoot(options.InstallDir)!).AvailableFreeSpace;
                if (free < need * 1.05)
                    throw new InvalidOperationException($"About {need / 1024.0 / 1024 / 1024:0.0} GB is required, but this drive does not have enough free space.");
            }
            catch (ArgumentException) { }

            string last = "";
            var reporter = new Progress<InstallProgress>(p =>
            {
                progress.Value = Math.Clamp((int)(p.Fraction * 1000), 0, 1000);
                if (p.Status != last)
                {
                    last = p.Status;
                    SetStatus(p.Status, Muted);
                }
            });
            var job = new InstallJob(options, payload, reporter, cts.Token);
            await job.RunAsync(foundDlc);
            ForkPostInstall.Apply(options.InstallDir, selectedLanguage);

            completedDir = options.InstallDir;
            progress.Value = 1000;
            openFolder.Visible = true;
            SetStatus($"Ready · {selectedLanguage.DisplayName} · installed to {options.InstallDir}", Success);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Installation cancelled. Existing source files were not changed.", Muted);
            progress.Visible = false;
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

    static string? CheckInstallPath(string path, string gamePath)
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
            string source = Path.GetFullPath(gamePath).TrimEnd('\\');
            string destination = path.TrimEnd('\\');
            if (source.Equals(destination, StringComparison.OrdinalIgnoreCase)
                || source.StartsWith(destination + "\\", StringComparison.OrdinalIgnoreCase)
                || destination.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
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

    void PickGameIso()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose your LEGO Dimensions Xbox 360 disc image",
            Filter = "Xbox 360 disc image (*.iso)|*.iso|All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) game.Text = dialog.FileName;
    }

    void PickUpdatePackage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the Title Update 23 package",
            Filter = "All files|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) update.Text = dialog.FileName;
    }

    void PickFolder(TextBox box, string description, bool create)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = create,
        };
        if (Directory.Exists(box.Text)) dialog.InitialDirectory = box.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.SelectedPath;
    }

    void LaunchGame()
    {
        if (completedDir is null) return;
        string exe = Path.Combine(completedDir, "legodimensions.exe");
        if (!File.Exists(exe))
        {
            OpenInstallFolder();
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = completedDir,
            });
        }
        catch (Exception e) { Fail("Could not launch the game: " + e.Message); }
    }

    void OpenInstallFolder()
    {
        if (completedDir is null) return;
        try { Process.Start(new ProcessStartInfo(completedDir) { UseShellExecute = true }); }
        catch (Exception e) { Fail("Could not open the install folder: " + e.Message); }
    }

    void Fail(string message)
    {
        SetStatus(message, Danger);
        MessageBox.Show(this, message, "Dimensions Recompiled", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    void SetBusy(bool busy, string? message = null)
    {
        installing = busy;
        game.Enabled = update.Enabled = dlc.Enabled = install.Enabled = !busy;
        language.Enabled = detect.Enabled = advanced.Enabled = !busy;
        foreach (var button in browseButtons) button.Enabled = !busy;
        shortcut.Enabled = updates.Enabled = !busy;
        openFolder.Enabled = !busy;
        progress.Visible = busy || completedDir is not null;
        installButton.Text = busy ? "Cancel" : completedDir is null ? "Install" : "Launch game";
        if (message is not null) SetStatus(message, Muted);
    }

    void SetStatus(string text, Color color)
    {
        status.Text = text;
        status.ForeColor = color;
    }

    static Panel Card(Rectangle bounds) => new()
    {
        Bounds = bounds,
        BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle,
    };

    static Label SectionTitle(string text, int y) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 14f),
        ForeColor = Ink,
        Bounds = new Rectangle(22, y, 500, 30),
    };

    static TextBox PathBox(string placeholder) => new()
    {
        PlaceholderText = placeholder,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5f),
        ForeColor = Ink,
        BackColor = Color.White,
    };

    static CheckBox OptionBox(string text, bool isChecked) => new()
    {
        Text = text,
        Checked = isChecked,
        AutoSize = true,
        Font = new Font("Segoe UI", 9.25f),
        ForeColor = Ink,
    };

    static Button PrimaryButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = Accent,
        ForeColor = Color.White,
        Font = new Font("Segoe UI Semibold", 10f),
        Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false,
        FlatAppearance = { BorderSize = 0 },
    };

    static Button SecondaryButton(string text) => new()
    {
        Text = text,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.White,
        ForeColor = Ink,
        Font = new Font("Segoe UI Semibold", 9f),
        Cursor = Cursors.Hand,
        UseVisualStyleBackColor = false,
        FlatAppearance = { BorderColor = Line, BorderSize = 1 },
    };

    static Image? LoadLogo()
    {
        try
        {
            using Stream? stream = typeof(EasyInstallForm).Assembly.GetManifestResourceStream("DimensionsLogo.png");
            if (stream is null) return null;
            using Image source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch { return null; }
    }
}
