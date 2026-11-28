using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GardenDevLauncher;

public sealed class MainForm : Form
{
    private static readonly Color Backdrop = ColorTranslator.FromHtml("#10160F");
    private static readonly Color Surface = ColorTranslator.FromHtml("#19221A");
    private static readonly Color Leaf = ColorTranslator.FromHtml("#7CB342");
    private static readonly Color Gold = ColorTranslator.FromHtml("#E8B84B");
    private static readonly Color GodotBlue = ColorTranslator.FromHtml("#478CBF");
    private static readonly Color Ink = ColorTranslator.FromHtml("#E6EDE4");
    private static readonly Color Muted = ColorTranslator.FromHtml("#94A48F");
    private static readonly Color Danger = ColorTranslator.FromHtml("#E06C5A");
    private static readonly Color ButtonInk = ColorTranslator.FromHtml("#0E140E");

    private readonly TextBox _repo = new();
    private readonly TextBox _log = new();
    private readonly Label _status = new();
    private readonly Button _check = new();
    private readonly Button _sync = new();
    private bool _busy;

    public MainForm()
    {
        Text = "Garden Engine Dev Launcher";
        ClientSize = new Size(900, 620);
        MinimumSize = new Size(760, 520);
        BackColor = Backdrop;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = LoadIcon();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Backdrop,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildRepositoryRow(), 0, 1);
        root.Controls.Add(BuildActions(), 0, 2);
        root.Controls.Add(BuildLog(), 0, 3);
        Controls.Add(root);

        _repo.Text = FindRepository();
        UpdateStatus("Ready. Pick a checkout and check it.", Muted);
        Append("Garden Engine Dev Launcher keeps a checkout on top of main.");
        Append("Check shows what is out of date; Sync saves your work and merges main.");
        Append(string.Empty);
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(20) };

        var engine = Logo("engine.png", 72, new Point(20, 20));
        var godot = Logo("godot.png", 72, new Point(108, 20));

        var title = new Label
        {
            Text = "GARDEN ENGINE",
            AutoSize = true,
            Location = new Point(198, 26),
            Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = Leaf,
        };
        var subtitle = new Label
        {
            Text = "DEV LAUNCHER",
            AutoSize = true,
            Location = new Point(202, 68),
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Gold,
        };
        var tagline = new Label
        {
            Text = "saves your uncommitted work, then syncs the checkout with main",
            AutoSize = true,
            Location = new Point(204, 90),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Muted,
        };
        var version = new Label
        {
            // ProductVersion appends the source revision hash; only the number belongs in the header.
            Text = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? ProductVersion}",
            AutoSize = true,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Muted,
        };
        panel.Resize += (_, _) => version.Location = new Point(panel.ClientSize.Width - version.Width - 20, 30);

        panel.Controls.AddRange(new Control[] { engine, godot, title, subtitle, tagline, version });
        return panel;
    }

    private Control BuildRepositoryRow()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Backdrop, Padding = new Padding(20, 6, 20, 4) };
        var label = new Label { Text = "Checkout", AutoSize = true, ForeColor = Muted, Location = new Point(20, 16) };

        _repo.BorderStyle = BorderStyle.FixedSingle;
        _repo.BackColor = Surface;
        _repo.ForeColor = Ink;
        _repo.Font = new Font("Consolas", 9.5f);
        _repo.SetBounds(104, 14, 620, 26);

        var browse = new Button
        {
            Text = "Browse…",
            FlatStyle = FlatStyle.Flat,
            BackColor = Surface,
            ForeColor = Ink,
            Size = new Size(104, 30),
            Location = new Point(742, 13),
        };
        browse.FlatAppearance.BorderColor = Muted;
        browse.Click += OnBrowse;

        panel.Controls.AddRange(new Control[] { label, _repo, browse });
        panel.Resize += (_, _) =>
        {
            browse.Left = panel.ClientSize.Width - browse.Width - 20;
            _repo.Width = browse.Left - _repo.Left - 12;
        };
        return panel;
    }

    private Control BuildActions()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Backdrop, Padding = new Padding(20, 4, 20, 8) };

        StyleButton(_check, "Check status", Leaf);
        _check.SetBounds(104, 8, 150, 40);
        _check.Click += async (_, _) => await ExecuteAsync(checkOnly: true);

        StyleButton(_sync, "Save work & sync with main", GodotBlue);
        _sync.SetBounds(266, 8, 240, 40);
        _sync.Click += async (_, _) => await ExecuteAsync(checkOnly: false);

        _status.SetBounds(522, 10, 340, 36);
        _status.AutoEllipsis = true;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = new Font("Segoe UI", 10f, FontStyle.Bold);

        panel.Controls.AddRange(new Control[] { _check, _sync, _status });
        panel.Resize += (_, _) => _status.Width = panel.ClientSize.Width - _status.Left - 20;
        return panel;
    }

    private Control BuildLog()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Backdrop, Padding = new Padding(20, 0, 20, 18) };
        // A one pixel dark frame reads better against the window than the default light border.
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(1) };
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.WordWrap = false;
        _log.ScrollBars = ScrollBars.Both;
        _log.Dock = DockStyle.Fill;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = ColorTranslator.FromHtml("#0B100B");
        _log.ForeColor = ColorTranslator.FromHtml("#CBD8C6");
        _log.Font = new Font("Consolas", 9.5f);
        frame.Controls.Add(_log);
        panel.Controls.Add(frame);
        return panel;
    }

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose the checkout to keep in step with main",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_repo.Text) ? _repo.Text : Environment.CurrentDirectory,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _repo.Text = dialog.SelectedPath;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            UpdateStatus("A sync is running; close the window when it finishes.", Gold);
            return;
        }

        base.OnFormClosing(e);
    }

    private async Task ExecuteAsync(bool checkOnly)
    {
        if (_busy)
        {
            return;
        }

        var repo = _repo.Text.Trim();
        if (repo.Length == 0)
        {
            UpdateStatus("Pick a checkout first.", Danger);
            return;
        }

        _busy = true;
        _check.Enabled = false;
        _sync.Enabled = false;
        _log.Clear();
        UpdateStatus(checkOnly ? "Checking…" : "Saving and syncing…", Gold);

        var progress = new Progress<string>(Append);
        try
        {
            var engine = new SyncEngine(repo, line => ((IProgress<string>)progress).Report(line));
            var outcome = await Task.Run(() => checkOnly ? engine.Check() : engine.Sync());
            UpdateStatus(outcome.Summary, outcome.Snapshot.UpToDate ? Leaf : Gold);
            Append(string.Empty);
            Append($"garden-dev: {outcome.Summary}");
        }
        catch (RefusedException error)
        {
            UpdateStatus(error.Message, Danger);
            Append(string.Empty);
            Append($"garden-dev: {error.Message}");
        }
        catch (Exception error)
        {
            UpdateStatus(error.Message, Danger);
            Append(string.Empty);
            Append(error.Message);
        }
        finally
        {
            _busy = false;
            _check.Enabled = true;
            _sync.Enabled = true;
        }
    }

    private void Append(string line)
    {
        if (_log.IsDisposed)
        {
            return;
        }

        _log.AppendText(line + Environment.NewLine);
    }

    private void UpdateStatus(string text, Color colour)
    {
        _status.Text = text;
        _status.ForeColor = colour;
    }

    private static void StyleButton(Button button, string text, Color colour)
    {
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = colour;
        button.ForeColor = ButtonInk;
        button.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static PictureBox Logo(string resourceName, int size, Point location)
    {
        var box = new PictureBox
        {
            Size = new Size(size, size),
            Location = location,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
        };
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            box.Image = Image.FromStream(stream);
        }

        return box;
    }

    private static Icon? LoadIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("engine.png");
            if (stream is null)
            {
                return null;
            }

            // The icon PNG is large artwork; Windows wants a small square icon.
            using var source = new Bitmap(stream);
            using var square = new Bitmap(64, 64);
            using (var graphics = Graphics.FromImage(square))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(source, 0, 0, 64, 64);
            }

            return Icon.FromHandle(square.GetHicon());
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FindRepository()
    {
        var candidates = new List<string> { Environment.CurrentDirectory };
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 6 && directory is not null; depth++)
        {
            candidates.Add(directory.FullName);
            directory = directory.Parent;
        }

        foreach (var candidate in candidates)
        {
            if (SyncEngine.IsRepository(candidate))
            {
                return candidate;
            }
        }

        return Environment.CurrentDirectory;
    }
}
