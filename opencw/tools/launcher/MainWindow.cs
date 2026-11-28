using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenCommonwealth.Launcher;

public sealed class MainWindow : Window
{
    string _root = "";
    string Root => _root;
    string EngineDir0 => Path.Combine(Root, "engine");
    string EngineCfg => Path.Combine(Root, ".engine-path");
    static readonly string EngineExe = OperatingSystem.IsWindows()
        ? "godot.windows.editor.double.x86_64.mono.exe"
        : "godot.linuxbsd.editor.double.x86_64.mono";
    static readonly string ReleaseAsset = OperatingSystem.IsWindows()
        ? "godot-4.8.0-dev-double-mono-win64.zip"
        : "godot-4.8.0-dev-double-mono-linux64.zip";
    const string SourceUrl = "https://github.com/G-A-R-D-E-N/Garden-Engine.git";
    const int MinDotnet = 10;

    GitService _git = new(AppContext.BaseDirectory, SourceUrl);
    readonly StatusCard _cardDotnet = new(".NET SDK");
    readonly StatusCard _cardAccount = new("GitHub account (G-A-R-D-E-N org)");
    readonly StatusCard _cardSource = new("Source: GitHub (G-A-R-D-E-N/Garden-Engine)");
    readonly TextBlock _next = new() { Text = "Checking your setup…", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Ui.Text, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _steps = new() { FontSize = 12, Foreground = Ui.Muted, Margin = new Thickness(0, 8, 0, 0), IsVisible = false, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock _activity = new() { FontSize = 11, Foreground = Ui.Muted, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    readonly StatusCard _cardEngine = new("Garden Godot engine");
    readonly StatusCard _cardFo4 = new("Fallout 4");
    readonly Button _btnSetup, _btnNoBuild, _btnBuildOnly, _btnEngine;
    readonly ProgressBar _progress;
    readonly TextBlock _statusLine;
    readonly Button _toggleOutput;
    readonly Border _outputPanel;
    readonly TextBox _output;
    readonly string[] _args;
    double _collapsedHeight;

    public MainWindow(string[] args)
    {
        _args = args;

        Title = "Garden Engine dev launcher";
        Width = 900;
        Height = 760;
        MinWidth = 720;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI, Ubuntu, Cantarell, sans-serif");
        Ui.ApplyChrome(this);

        var logo = new Image { Width = 52, Height = 52, Source = Ui.LoadIcon(), Margin = new Thickness(0, 0, 14, 0) };
        var h1 = new TextBlock { Text = "Garden Engine", Foreground = Ui.Text, FontWeight = FontWeight.Bold, FontSize = 22 };
        var h2 = new TextBlock { Text = "Dev launcher: sign in to GitHub, clone the repo (or run this from an existing checkout), install the Garden Godot build, build, and open the editor.", Foreground = Ui.Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var titles = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(h1);
        titles.Children.Add(h2);
        var header = new DockPanel { Margin = new Thickness(24, 18, 24, 10) };
        header.Children.Add(logo);
        header.Children.Add(titles);

        var cardStack = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var c in new[] { _cardDotnet, _cardAccount, _cardSource, _cardEngine, _cardFo4 }) cardStack.Children.Add(c);
        var nextBox = new Border { Child = _next, Background = Ui.SurfaceAlt, BorderBrush = Ui.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 0, 12) };
        var cardPanel = new StackPanel { Orientation = Orientation.Vertical };
        cardPanel.Children.Add(nextBox);
        cardPanel.Children.Add(cardStack);
        var cards = new Border { Child = new ScrollViewer { Content = cardPanel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, Padding = new Thickness(24, 12) };

        _btnSetup = Ui.PillButton("Set up & Open Editor", true, 210);
        _btnNoBuild = Ui.PillButton("Open editor", false, 140);
        _btnBuildOnly = Ui.PillButton("Set up only", false, 120);
        _btnEngine = Ui.PillButton("Choose engine…", false, 150);
        _btnSetup.Click += (_, _) => Start(true, true);
        _btnNoBuild.Click += (_, _) => Start(false, true);
        _btnBuildOnly.Click += (_, _) => Start(true, false);
        _btnEngine.Click += (_, _) => Start(true, true, pickEngine: true);
        ToolTip.SetTip(_btnNoBuild, "Skip the builds and just open the editor");
        ToolTip.SetTip(_btnBuildOnly, "Set up and build, but don't open the editor");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_btnSetup); row.Children.Add(_btnNoBuild); row.Children.Add(_btnBuildOnly); row.Children.Add(_btnEngine);

        _progress = new ProgressBar { IsIndeterminate = false, IsVisible = false, Height = 4, Margin = new Thickness(0, 10, 0, 0), Background = Ui.SurfaceAlt, Foreground = Ui.Blue };
        _statusLine = new TextBlock { Foreground = Ui.Muted, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
        _toggleOutput = new Button
        {
            Content = "Show output ▾", Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = Ui.Blue, FontSize = 12, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Margin = new Thickness(0, 4, 0, 0),
        };
        _toggleOutput.Click += (_, _) => ToggleOutput();
        var barStack = new StackPanel { Orientation = Orientation.Vertical };
        barStack.Children.Add(row);
        barStack.Children.Add(_progress);
        barStack.Children.Add(_statusLine);
        barStack.Children.Add(_steps);
        barStack.Children.Add(_activity);
        barStack.Children.Add(_toggleOutput);
        var bar = new Border { Child = barStack, Padding = new Thickness(24, 12) };

        _output = new TextBox
        {
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            Background = new SolidColorBrush(Color.FromRgb(9, 12, 16)), Foreground = Brushes.Gainsboro,
            BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas, Liberation Mono, monospace"), FontSize = 12,
        };
        var outputScroll = new ScrollViewer { Content = _output, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        _outputPanel = new Border { Child = outputScroll, Padding = new Thickness(24, 0, 24, 12), Height = 190, IsVisible = false };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        Grid.SetRow(header, 0); Grid.SetRow(cards, 1); Grid.SetRow(_outputPanel, 2); Grid.SetRow(bar, 3);
        root.Children.Add(header); root.Children.Add(cards); root.Children.Add(_outputPanel); root.Children.Add(bar);
        Content = root;

        Opened += (_, _) => OnLoaded();
    }

    void ToggleOutput()
    {
        bool show = !_outputPanel.IsVisible;
        double dh = _outputPanel.Height;
        _outputPanel.IsVisible = show;
        _toggleOutput.Content = show ? "Hide output ▴" : "Show output ▾";

        var wa = Screens.Primary?.WorkingArea;
        if (show)
        {
            _collapsedHeight = Height;
            double newHeight = Height + dh;
            if (wa is { } r) newHeight = Math.Min(newHeight, r.Height);
            Height = newHeight;
        }
        else
        {
            Height = _collapsedHeight > 0 ? _collapsedHeight : Math.Max(MinHeight, Height - dh);
        }
    }

    void OnLoaded()
    {
        _root = ResolveRoot() ?? "";
        _git = new GitService(Root.Length > 0 ? Root : AppContext.BaseDirectory, SourceUrl);
        if (Root.Length == 0) { RunWork(() => { CheckDotnet(); CheckClone(); }); return; }
        string a0 = _args.Length > 0 ? _args[0] : "";
        if (Eq(a0, "/build")) { Start(true, false); return; }
        if (Eq(a0, "/nobuild")) { Start(false, true); return; }
        if (Eq(a0, "/engine")) { Start(true, true, pickEngine: true, engineArg: _args.Length > 1 ? _args[1] : null); return; }
        RunChecks();
    }

    void RunChecks() => RunWork(() => { CheckDotnet(); CheckAccount(); CheckSource(); CheckEngine(); CheckFallout4(); Ready(); });

    bool CheckDotnet()
    {
        _cardDotnet.Set(CardState.Busy, "Checking…");
        string? v = LauncherProcess.Capture("dotnet", "--version");
        if (v is null)
        {
            _cardDotnet.Set(CardState.Error, "Not found on PATH.", "Get .NET", () => LauncherProcess.OpenUrl("https://dotnet.microsoft.com/download"));
            return false;
        }
        if (!int.TryParse(v.Trim().Split('.')[0], out int major) || major < MinDotnet)
        {
            _cardDotnet.Set(CardState.Error, $".NET SDK {v.Trim()} is too old - the project needs {MinDotnet}+.", "Get .NET", () => LauncherProcess.OpenUrl("https://dotnet.microsoft.com/download"));
            return false;
        }
        _cardDotnet.Set(CardState.Ok, $".NET SDK {v.Trim()}", extra: $"Builds the C# projects. Needs {MinDotnet}+.");
        return true;
    }

    bool CheckAccount()
    {
        _cardAccount.Set(CardState.Busy, "Checking your GitHub sign-in…");
        var (state, message) = GitService.VerifyOrg();
        switch (state)
        {
            case GitService.OrgState.Member: _cardAccount.Set(CardState.Ok, message, extra: "Lets you clone and pull the private repo."); return true;
            case GitService.OrgState.Unverified: _cardAccount.Set(CardState.Warn, message, "Retry", () => RunWork(() => CheckAccount())); return true;
            default: _cardAccount.Set(CardState.Warn, message, "Sign in", SignIn); return false;
        }
    }

    void CheckSource()
    {
        _cardSource.Set(CardState.Busy, "Contacting GitHub…");
        if (!_git.IsRepo)
        {
            _cardSource.Set(CardState.Warn, "Not a git checkout, so it can't be verified against GitHub.");
            return;
        }
        var s = _git.CheckSource();
        switch (s.Reach)
        {
            case GitService.Reach.Ok when s.Behind == 0:
                _cardSource.Set(CardState.Ok, s.Ahead > 0
                    ? $"Up to date with {s.Branch} (you're {s.Ahead} ahead)."
                    : $"Up to date with GitHub {s.Branch}.", extra: SourceExtra(s));
                break;
            case GitService.Reach.Ok when s.LocalOnlyBranch is not null:
                _cardSource.Set(CardState.Warn,
                    Divergence(s) + $" You're on \"{s.LocalOnlyBranch}\", so switch to {s.Branch} to pull.", extra: SourceExtra(s));
                break;
            case GitService.Reach.Ok:
                _cardSource.Set(CardState.Warn, Divergence(s), "Pull latest", PullLatest, SourceExtra(s));
                break;
            case GitService.Reach.Auth:
                _cardSource.Set(CardState.Warn, "Not signed in to github.com (private repo).", "Sign in", SignIn);
                break;
            case GitService.Reach.Offline:
                _cardSource.Set(CardState.Warn, "GitHub unreachable (offline).", "Retry", () => RunWork(CheckSource));
                break;
            default:
                _cardSource.Set(CardState.Warn, s.Error ?? "Couldn't determine source state.");
                break;
        }
    }

    bool CheckFallout4()
    {
        _cardFo4.Set(CardState.Busy, OperatingSystem.IsWindows() ? "Scanning registry & Steam…" : "Scanning Steam libraries…");
        string? install = Fallout4Locator.FindFallout4();
        if (install is not null)
        {
            _cardFo4.Set(CardState.Ok, install, extra: "Used only for converting game assets.");
            return true;
        }
        _cardFo4.Set(CardState.Warn, "Not found. Conversion needs your own copy.", "Locate…", LocateFo4);
        return false;
    }

    void PullLatest() => RunWork(() =>
    {
        _cardSource.Set(CardState.Busy, "Pulling latest from GitHub…");
        string? err = _git.Pull(out int n, out string? note, out bool attention);
        if (err is null)
        {
            Log($"Pulled {n} commit(s) from GitHub.");
            if (note is not null) Log(note);
            CheckSource();
            if (attention) _cardSource.Set(CardState.Warn, note!); else if (note is not null) Status(note);
        }
        else { _cardSource.Set(CardState.Error, err); Log("Pull failed: " + err); }
    });

    async void SignIn()
    {
        try
        {
            LauncherProcess.OpenUrl("https://github.com/settings/tokens/new?scopes=repo&description=Garden%20dev%20launcher");
            string? token = await LauncherDialogs.PromptTextAsync(this,
                "Easiest: run `gh auth login` in a terminal, then leave this box empty and press OK to re-check.\n\n" +
                "Or a browser opened to GitHub's token page: create a token with the repo scope and paste it here:",
                "Sign in to GitHub");
            if (string.IsNullOrWhiteSpace(token)) { GitService.ResetTokenCache(); RunWork(() => { CheckAccount(); CheckSource(); }); return; }
            string? err = GitService.SaveToken("x-access-token", token.Trim());
            if (err is not null) { await LauncherDialogs.ShowMessageAsync(this, "Couldn't save the token: " + err, "Sign in"); return; }
            RunWork(() => { CheckAccount(); CheckSource(); });
        }
        catch (Exception ex) { Log("[X] Sign in failed: " + ex.Message); }
    }

    async void LocateFo4()
    {
        try
        {
            string? exe = await LauncherDialogs.PromptOpenFileAsync(this, "Locate Fallout4.exe", new[] { "Fallout4.exe", "*.exe" });
            if (exe is null) return;
            string dir = Path.GetDirectoryName(exe)!;
            if (!File.Exists(Path.Combine(dir, "Fallout4.exe")) && !File.Exists(Path.Combine(dir, "Data", "Fallout4.esm")))
            {
                _cardFo4.Set(CardState.Warn, "That folder has no Fallout4.exe / Data/Fallout4.esm.", "Locate…", LocateFo4);
                return;
            }
            string? saveErr = LauncherSettings.SetFallout4Path(dir);
            if (saveErr is not null)
            {
                Log("[!] Found Fallout 4 but couldn't save the path: " + saveErr);
                _cardFo4.Set(CardState.Warn, dir + "  (not saved - see output)", "Locate…", LocateFo4);
                return;
            }
            Log($"Fallout 4 path saved: {dir}");
            _cardFo4.Set(CardState.Ok, dir);
        }
        catch (Exception ex) { Log("[X] Locate failed: " + ex.Message); }
    }

    void Start(bool doBuild, bool doLaunch, bool pickEngine = false, string? engineArg = null)
    {
        RunWork(() =>
        {
            StartSteps(doBuild, doLaunch);
            if (!CheckDotnet()) { Step(0, "fail"); Fail("The .NET SDK is required."); return; }
            if (!CheckAccount()) { Step(0, "fail"); Fail("Sign in to GitHub with an account in the G-A-R-D-E-N org first."); return; }
            if (Root.Length == 0) { Step(0, "fail"); Fail("Clone Garden Engine first (the Source card)."); return; }
            CheckSource();
            CheckFallout4();
            Step(0, "ok"); Step(1, "run");

            Status(pickEngine ? "Choosing engine…" : "Resolving engine…");
            string engineDir = EngineDir0;
            string? godot = ResolveEngine(pickEngine, ref engineArg, ref engineDir, out bool cancelled);
            if (cancelled) { Step(1, "fail"); Fail("Engine setup cancelled."); return; }
            godot ??= DownloadEngine(ref engineDir);
            if (godot is null) { Step(1, "fail"); Fail("Couldn't locate or download the engine."); return; }

            if (!OperatingSystem.IsWindows()) LauncherProcess.MakeExecutable(godot);
            string? ver = LauncherProcess.Capture(godot, "--version");
            if (ver is null || !ver.Contains(".double."))
            {
                Log($"[X] {godot} isn't a double-precision build (reported \"{ver?.Trim()}\").");
                Step(1, "fail");
                Fail("Wrong engine build: it needs the double-precision editor.");
                ShowOutput();
                return;
            }
            Log($"Engine: {godot}");
            WriteVsCodeSettings(godot);
            CheckEngine(godot);

            Step(1, "ok");
            if (doBuild)
            {
                Step(2, "run");
                Status("Building Radicle (native C++, can take several minutes)…");
                if (!BuildRadicle()) { Step(2, "fail"); Fail("Radicle build failed - see output."); ShowOutput(); return; }
                Step(2, "ok"); Step(3, "run");
                Status("Building C#…");
                Log("Building C# (dotnet build)…");
                int rc = LauncherProcess.Run("dotnet", "build -v quiet --nologo", Root, Log);
                if (rc != 0) { Step(3, "fail"); Fail("C# build failed, see output."); ShowOutput(); return; }
                Log("C# build OK.");
                Step(3, "ok");
            }

            if (!doLaunch) { Done("Setup complete."); return; }

            Step(4, "run");
            Status("Opening the editor…");
            try
            {
                if (!OperatingSystem.IsWindows()) LauncherProcess.MakeExecutable(godot);
                var psi = new ProcessStartInfo
                {
                    FileName = godot,
                    Arguments = $"-e --path \"{Root.TrimEnd(Path.DirectorySeparatorChar)}\"",
                    WorkingDirectory = Path.GetDirectoryName(godot)!,
                    UseShellExecute = false,
                };
                GiveGodotDotnet(psi);
                Process.Start(psi);
            }
            catch (Exception ex) { Step(4, "fail"); Fail("Couldn't start the editor: " + ex.Message); return; }
            Step(4, "ok");
            Done("Editor launching. The first open imports assets and may take a while.");
        });
    }

    void GiveGodotDotnet(ProcessStartInfo psi)
    {
        string? exe = LauncherProcess.FindOnPath("dotnet") ?? LauncherProcess.FirstExisting(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "dotnet"),
            "/usr/local/bin/dotnet", "/usr/bin/dotnet",
            Environment.GetEnvironmentVariable("ProgramFiles") + @"\dotnet\dotnet.exe");
        if (exe is null) { Log("[!] dotnet not found - the editor will report \"Unable to load .NET runtime\"."); return; }

        string dir = Path.GetDirectoryName(exe)!;
        try
        {
            var target = new FileInfo(exe).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null) dir = Path.GetDirectoryName(target.FullName) ?? dir;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }

        psi.EnvironmentVariables["DOTNET_ROOT"] = dir;
        string path = psi.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Split(Path.PathSeparator).Contains(dir))
            psi.EnvironmentVariables["PATH"] = dir + Path.PathSeparator + path;
        Log($"Using .NET from {dir}");
    }

    string? ResolveEngine(bool pickEngine, ref string? engineArg, ref string engineDir, out bool cancelled)
    {
        cancelled = false;
        string? ocGodot = null;
        if (!pickEngine)
        {
            string? env = Environment.GetEnvironmentVariable("OC_GODOT")?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(env))
            {
                if (File.Exists(env)) return env;
                Log($"[!] OC_GODOT is set but not a file: {env} - ignoring it.");
            }
        }
        if (!pickEngine && File.Exists(EngineCfg))
        {
            string? p = LauncherProcess.ReadFirstLine(EngineCfg);
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) ocGodot = p;
        }
        if (!pickEngine)
        {
            if (ocGodot is not null) return ocGodot;
            string local = Path.Combine(EngineDir0, EngineExe);
            if (File.Exists(local)) return local;
        }
        if (engineArg is not null)
        {
            if (File.Exists(engineArg)) { LauncherProcess.Save(EngineCfg, engineArg); return engineArg; }
            engineDir = engineArg;
            return null;
        }

        int picked = Block(() => LauncherDialogs.ChooseAsync(this, "No engine found. Where should the double-precision Godot editor go?", "Engine setup",
            $"Download into this repo\n({EngineDir0})", "Download into a folder I choose", "Use an editor executable I already have"));
        var step = EngineSetup.FromChoice(picked, EngineDir0,
            () => Block(() => LauncherDialogs.PromptOpenFileAsync(this, "Select " + EngineExe, new[] { EngineExe, "*" })),
            () => Block(() => LauncherDialogs.PromptFolderAsync(this, "Choose a folder to install the engine into")));
        switch (step.Step)
        {
            case EngineStep.Cancelled:
                cancelled = true;
                Log("[X] Engine setup cancelled.");
                return null;
            case EngineStep.UseExisting:
                LauncherProcess.Save(EngineCfg, step.Path!);
                return step.Path;
            default:
                engineDir = step.Path!;
                Log($"Installing engine to: {engineDir}");
                return null;
        }
    }

    string SourceExtra(GitService.SourceStatus s)
    {
        static string Short(string? sha) => string.IsNullOrEmpty(sha) ? "?" : sha[..Math.Min(7, sha.Length)];
        return $"{Root}  |  local {Short(s.LocalSha)}, GitHub {Short(s.RemoteSha)}";
    }

    static string Divergence(GitService.SourceStatus s)
    {
        string behind = $"Behind GitHub {s.Branch} by {s.Behind} commit{(s.Behind == 1 ? "" : "s")}";
        return s.Ahead > 0 ? $"{behind}, ahead by {s.Ahead} - pulling merges the two." : behind + ".";
    }

    bool CheckEngine(string? godot = null)
    {
        _cardEngine.Set(CardState.Busy, "Checking…");
        godot ??= FindEngine();
        if (godot is null)
        {
            _cardEngine.Set(CardState.Warn, "Not installed.", "Download", () => Start(false, false));
            return false;
        }
        string? want = PinnedSdkVersion();
        string? have = InstalledSdkVersion(godot, want);
        string tag = LauncherProcess.ReadFirstLine(Path.Combine(Path.GetDirectoryName(godot)!, ".engine-release")) ?? "unknown release";
        if (have is null || want is null)
        {
            _cardEngine.Set(CardState.Warn, $"{godot} ({tag}) - couldn't compare its .NET SDK with the checkout.");
            return true;
        }
        if (have == want) { _cardEngine.Set(CardState.Ok, $"{tag} - .NET SDK {have} matches the checkout.", extra: godot); return true; }
        _cardEngine.Set(CardState.Warn, $"Engine has .NET SDK {have}, checkout pins {want}.", "Update engine", UpdateEngine);
        return true;
    }

    void UpdateEngine() => RunWork(() =>
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OC_GODOT")))
        {
            Fail("OC_GODOT points at your own engine; update that one or unset OC_GODOT.");
            return;
        }
        string? current = FindEngine();
        string dir = current is null ? EngineDir0 : Path.GetDirectoryName(current)!;
        if (DownloadEngine(ref dir) is null) { Fail("Engine update failed."); ShowOutput(); return; }
        CheckEngine();
        Done("Engine updated.");
    });

    string? FindEngine()
    {
        string? env = Environment.GetEnvironmentVariable("OC_GODOT")?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        string? saved = LauncherProcess.ReadFirstLine(EngineCfg);
        if (saved is not null && File.Exists(saved)) return saved;
        string local = Path.Combine(EngineDir0, EngineExe);
        return File.Exists(local) ? local : null;
    }

    static string? InstalledSdkVersion(string godot, string? want)
    {
        try
        {
            string dir = Path.Combine(Path.GetDirectoryName(godot)!, "GodotSharp", "Tools", "nupkgs");
            var found = Directory.EnumerateFiles(dir, "Godot.NET.Sdk.*.nupkg")
                .Select(p => Path.GetFileName(p)["Godot.NET.Sdk.".Length..^".nupkg".Length]).ToList();
            return found.Contains(want ?? "") ? want : found.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    string? PinnedSdkVersion()
    {
        try
        {
            string text = File.ReadAllText(Path.Combine(Root, "OpenCommonwealth.csproj"));
            var m = System.Text.RegularExpressions.Regex.Match(text, "Sdk=\"Godot\\.NET\\.Sdk/([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    static string? ResolveRoot()
    {
        string here = AppContext.BaseDirectory;
        string? saved = LauncherSettings.Get("ProjectPath");
        foreach (string? dir in new[] { here, saved })
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "project.godot")))
                return dir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return null;
    }

    void CheckClone()
    {
        _cardSource.Set(CardState.Warn, "No Garden Engine checkout found next to the launcher.", "Clone…", CloneRepo);
        _cardEngine.Set(CardState.Idle, "Clone Garden Engine first.");
        _cardFo4.Set(CardState.Idle, "Clone Garden Engine first.");
        Ready();
    }

    async void CloneRepo()
    {
        try
        {
            string? parent = await LauncherDialogs.PromptFolderAsync(this, "Choose where to put the Garden-Engine folder");
            if (parent is null) return;
            string dest = Path.Combine(parent, "Garden-Engine");
            RunWork(() =>
            {
                _cardSource.Set(CardState.Busy, "Cloning Garden-Engine…");
                string? err = _git.Clone(dest, m => { _cardSource.Set(CardState.Busy, m); Status(m); });
                if (err is not null)
                {
                    bool auth = err.Contains("sign-in", StringComparison.OrdinalIgnoreCase);
                    _cardSource.Set(CardState.Warn, err, auth ? "Sign in" : "Clone…", auth ? (Action)SignIn : CloneRepo);
                    Log("[X] Clone failed: " + err);
                    return;
                }
                string project = Path.Combine(dest, "opencw");
                string? saveErr = LauncherSettings.Set("ProjectPath", project);
                if (saveErr is not null) Log("[!] Cloned, but couldn't remember the location: " + saveErr);
                _root = project + Path.DirectorySeparatorChar;
                _git = new GitService(Root, SourceUrl);
                Log("Cloned to " + dest);
                CheckSource(); CheckEngine(); CheckFallout4(); Ready();
            });
        }
        catch (Exception ex) { Log("[X] Clone failed: " + ex.Message); }
    }

    string? DownloadEngine(ref string engineDir)
    {
        Status("Downloading the editor…");
        Log("Downloading the editor. This takes a few minutes.");
        ShowOutput();

        var release = EngineReleases.Latest(EngineReleases.Project, out string? error);
        if (release is null)
        {
            Log($"[X] Couldn't read the Garden Godot release from GitHub ({EngineReleases.Project}): {error}");
            Log("    Releases are published by CI on every green engine tag build of the godot repo;");
            Log("    if none exist yet, ask a maintainer to run it.");
            return null;
        }

        string? dest = EngineInstaller.ResolveDestination(engineDir, out string? destError);
        if (dest is null) { Log("[X] " + destError); return null; }
        engineDir = dest;
        if (!FetchEngine(release, engineDir)) return null;

        string exe = Path.Combine(engineDir, EngineExe);
        if (File.Exists(exe))
        {
            if (!OperatingSystem.IsWindows()) LauncherProcess.MakeExecutable(exe);
            LauncherProcess.Save(EngineCfg, exe);
            return exe;
        }
        Log($"[X] {ReleaseAsset} unpacked but has no {EngineExe} in it.");
        return null;
    }

    bool FetchEngine(EngineReleases.Release release, string dir)
    {
        string staging = EngineInstaller.UniqueSibling(dir, "staging");
        try { Directory.CreateDirectory(staging); }
        catch (Exception ex) { Log("[X] Couldn't prepare the staging folder: " + ex.Message); return false; }

        bool swapped = false;
        try
        {
            string zip = Path.Combine(staging, ReleaseAsset);
            string? error = EngineReleases.Download(release, ReleaseAsset, zip);
            if (error is not null) { Log($"[X] Download failed: {error}"); return false; }
            Log("Extracting…");
            if (!LauncherProcess.Extract(zip, staging)) { Log($"[X] Could not unzip {ReleaseAsset}."); return false; }
            LauncherProcess.TryDelete(zip);
            if (!File.Exists(Path.Combine(staging, EngineExe))) { Log($"[X] {ReleaseAsset} unpacked but has no {EngineExe} in it."); return false; }
            File.WriteAllText(Path.Combine(staging, EngineInstaller.ReleaseMarker), release.Tag + Environment.NewLine);

            string? swapError = EngineInstaller.Swap(staging, dir);
            if (swapError is not null) { Log("[X] " + swapError); return false; }
            swapped = true;
            return true;
        }
        catch (Exception ex) { Log("[X] Engine install failed: " + ex.Message); return false; }
        finally
        {
            if (!swapped)
            {
                try { Directory.Delete(staging, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    bool BuildRadicle()
    {
        string radicle = Path.Combine(Root, "native", "radicle");
        string? cmake = LauncherProcess.FindOnPath("cmake") ?? (OperatingSystem.IsWindows() ? LauncherProcess.VsCmake() : null);
        if (cmake is null)
        {
            if (File.Exists(Path.Combine(radicle, "bin", "Radicle.props"))) { Log("cmake not found - using the Radicle already built."); return true; }
            Log(OperatingSystem.IsWindows()
                ? "[X] cmake not found. Install Visual Studio 2022 with \"Desktop development with C++\" (includes CMake) and Python 3."
                : "[X] cmake not found. Install CMake 3.25+, GCC 13+ or Clang 17+, and Python 3.");
            return false;
        }
        string build = Path.Combine(radicle, "build");
        Log("Building Radicle (cmake)…");
        if (LauncherProcess.Run(cmake, $"-S \"{radicle}\" -B \"{build}\" -DCMAKE_BUILD_TYPE=Release", Root, Log) != 0) return false;
        if (LauncherProcess.Run(cmake, $"--build \"{build}\" --config Release --target radicle_c --parallel", Root, Log) != 0) return false;
        Log("Radicle built.");
        return true;
    }

    void WriteVsCodeSettings(string godot)
    {
        try
        {
            string settings = Path.Combine(Root, ".vscode", "settings.json");
            if (File.Exists(settings)) return;
            Directory.CreateDirectory(Path.Combine(Root, ".vscode"));
            File.WriteAllText(settings,
                "{\n    \"godotTools.editorPath.godot4\": \"" + godot.Replace("\\", "/") + "\"\n}\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    int _busyFlag;

    void RunWork(Action work)
    {
        if (Interlocked.CompareExchange(ref _busyFlag, 1, 0) != 0) return;
        UI(() => { SetButtons(false); _progress.IsVisible = true; _progress.IsIndeterminate = true; }); ClockStart();
        Task.Run(() =>
        {
            try { work(); }
            catch (Exception ex) { Log("[X] Unexpected error: " + ex.Message); Status("Error, see output."); UI(() => { for (int i = 0; i < _stepState.Length; i++) if (_stepState[i] == "run") Step(i, "fail"); }); }
            finally { ClockStop(); UpdateNext(); Interlocked.Exchange(ref _busyFlag, 0); UI(() => { SetButtons(true); _progress.IsIndeterminate = false; _progress.IsVisible = false; }); }
        });
    }

    void SetButtons(bool on)
    {
        foreach (var b in new[] { _btnSetup, _btnNoBuild, _btnBuildOnly, _btnEngine }) b.IsEnabled = on;
    }

    void UI(Action a) { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a); }

    static T Block<T>(Func<Task<T>> uiWork)
    {
        if (Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("Block() called on the UI thread; await the *Async method instead.");
        return Dispatcher.UIThread.InvokeAsync(uiWork).GetAwaiter().GetResult();
    }

    const int MaxOutputChars = 200_000;
    readonly System.Text.StringBuilder _outputBuf = new();

    void Log(string line) => UI(() =>
    {
        if (!string.IsNullOrWhiteSpace(line)) _lastLine = line.Trim();
        _outputBuf.Append(line).Append('\n');
        if (_outputBuf.Length > MaxOutputChars) _outputBuf.Remove(0, _outputBuf.Length - MaxOutputChars);
        _output.Text = _outputBuf.ToString();
        _output.CaretIndex = _output.Text.Length;
    });
    void Status(string s) => UI(() => _statusLine.Text = s);
    void Ready() { Status("Ready."); UpdateNext(); }

    void UpdateNext() => UI(() =>
    {
        var blockers = new (StatusCard card, string msg)[]
        {
            (_cardDotnet, "install the .NET SDK."),
            (_cardAccount, "sign in to GitHub with an account in the G-A-R-D-E-N org."),
            (_cardSource, "bring the source up to date."),
            (_cardEngine, "install or update the Garden Godot engine."),
        };
        if (blockers.Any(x => x.card.State == CardState.Busy)) { _next.Text = "Checking your setup…"; return; }
        foreach (var (card, msg) in blockers)
            if (card.State is CardState.Warn or CardState.Error) { _next.Text = $"Next: {msg} ({card.Detail})"; return; }
        var idle = blockers.FirstOrDefault(x => x.card.State == CardState.Idle);
        if (idle.card is not null) { _next.Text = "Next: " + idle.card.Detail; return; }
        _next.Text = _cardFo4.State == CardState.Warn
            ? "All set. Press Set up & Open Editor. (Fallout 4 not found: only needed for asset conversion.)"
            : "All set. Press Set up & Open Editor.";
    });

    static readonly string[] StepNames = { "Checks", "Engine", "Radicle", "C# build", "Editor" };
    readonly string[] _stepState = new string[StepNames.Length];
    readonly Stopwatch _clock = new();
    DispatcherTimer? _tick;
    string _lastLine = "";

    void Step(int i, string state) => UI(() =>
    {
        _stepState[i] = state;
        _steps.IsVisible = true;
        _steps.Text = string.Join("   ", StepNames.Select((n, k) => (_stepState[k] switch
        {
            "run" => "▶ ", "ok" => "✔ ", "fail" => "✖ ", "skip" => "– ", _ => "○ ",
        }) + n));
    });

    void StartSteps(bool doBuild, bool doLaunch)
    {
        UI(() =>
        {
            for (int i = 0; i < StepNames.Length; i++) _stepState[i] = "wait";
            if (!doBuild) { _stepState[2] = "skip"; _stepState[3] = "skip"; }
            if (!doLaunch) _stepState[4] = "skip";
        });
        Step(0, "run");
        ShowOutput();
    }

    void ClockStart() => UI(() =>
    {
        _clock.Restart();
        _lastLine = "";
        _steps.IsVisible = false;
        _tick ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tick.Tick -= OnTick; _tick.Tick += OnTick; _tick.Start();
    });

    void ClockStop() => UI(() => { _tick?.Stop(); _clock.Stop(); _activity.Text = ""; });

    void OnTick(object? s, EventArgs e) =>
        _activity.Text = $"{(int)_clock.Elapsed.TotalMinutes}:{_clock.Elapsed.Seconds:00} elapsed" + (_lastLine.Length > 0 ? "  |  " + _lastLine : "");
    void Done(string s) { Status(s); Log(s); }
    void Fail(string s) { Status(s); Log("[X] " + s); }
    void ShowOutput() => UI(() => { if (!_outputPanel.IsVisible) ToggleOutput(); });

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
