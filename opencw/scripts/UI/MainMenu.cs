using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.UI.Swf;

namespace OpenCommonwealth;

public partial class MainMenu : Control
{
    public enum MenuState { Main, QuitConfirm, Settings, Options, Controls, DefaultsConfirm, Credits }

    private const string ArtDir = "res://Converted/UI/MainMenu/";
    private const string WindowTitle = "Fallout 4: Godot";
    private const float CreditsSpeed = 70f;
    private static readonly Vector2 DesignSize = new(1920, 1080);
    private static readonly string[] Categories = { "GAMEPLAY", "CONTROLS", "DISPLAY", "AUDIO" };
    private static readonly Key[] ReservedKeys = { Key.Escape, Key.Tab, Key.Enter, Key.KpEnter };

    private static readonly string[] CreditLines =
    {
        "FALLOUT 4: GODOT", "",
        "ENGINE", "Godot Engine", "godotengine.org", "Godot logo by Andrea Calabró, CC BY 4.0", "",
        "LIBRARIES", "Newtonsoft.Json", "Roboto Condensed", "",
        "Fallout is a trademark of Bethesda Softworks LLC.",
        "This project is not affiliated with or endorsed by Bethesda.",
        "Game assets are read from your own copy of Fallout 4.",
    };

    [Export(PropertyHint.File, "*.tscn")]
    public string NewGameScene { get; set; } = "res://scenes/sanctuary_prewar.tscn";

    [Export] public bool QuitsTree { get; set; } = true;

    [Signal] public delegate void QuitRequestedEventHandler();

    public MenuState Current { get; private set; }
    public MenuAudio Audio { get; private set; } = null!;

    private Control _ui = null!;
    private FoList _main = null!;
    private FoList _second = null!;
    private Control _secondPanel = null!;
    private Label _prompt = null!;
    private Label _version = null!;
    private Label _creditsText = null!;
    private Control _creditsClip = null!;
    private Control _creditsShade = null!;
    private HBoxContainer _hints = null!;
    private Control[] _frontPanels = null!;
    private TitleMenuArt? _art;

    private MenuState _confirmReturn;
    private Action _confirmAccept = () => { };
    private SettingDef[] _optionDefs = Array.Empty<SettingDef>();
    private int _categoryIndex;
    private int _promptToken;
    private string? _remapAction;
    private bool _settingsChanged;
    private bool _saving;
    private bool _remapping;
    private bool _remapCooldown;
    private bool _resetCooldown;
    private bool _closing;
    private Key _swallowRelease = Key.None;

    private static ulong s_newGameSceneChangeTreeId;
    private static readonly PendingSceneLease s_pendingNewGameDestination = new();

    private bool CommandsBlocked => _remapCooldown || _closing;

    public override void _Ready()
    {
        DisplayServer.WindowSetTitle(WindowTitle);
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GameSettings.Load();

        Audio = GetNode<MenuAudio>("Audio");
        _ui = GetNode<Control>("Ui");
        _main = GetNode<FoList>("Ui/MainPanel/List");
        _second = GetNode<FoList>("Ui/SecondPanel/List");
        _secondPanel = GetNode<Control>("Ui/SecondPanel");
        _prompt = GetNode<Label>("Ui/Prompt");
        _version = GetNode<Label>("Ui/Version");
        _creditsText = GetNode<Label>("Ui/CreditsClip/CreditsText");
        _creditsClip = GetNode<Control>("Ui/CreditsClip");
        _creditsShade = GetNode<Control>("CreditsShade");
        _hints = GetNode<HBoxContainer>("Ui/HintBar");
        _frontPanels = new[] { GetNode<Control>("Ui/Lockup"), GetNode<Control>("Ui/Title"), GetNode<Control>("Ui/MainPanel") };

        var background = GetNode<TextureRect>("Background");
        background.Texture = LoadArt("background.jpg");
        background.Resized += () => background.PivotOffset = background.Size / 2;
        var drift = CreateTween().SetLoops().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        drift.TweenProperty(background, "scale", Vector2.One * 1.06f, 24);
        drift.TweenProperty(background, "scale", Vector2.One, 24);

        var logo = GetNode<TextureRect>("Ui/Lockup/FalloutLogo");
        logo.Texture = LoadArt("logo.png");
        logo.Visible = logo.Texture != null;
        GetNode<Control>("Ui/Lockup/FalloutFallback").Visible = logo.Texture == null;

        _version.Text = Engine.GetVersionInfo()["string"].AsString();
        _creditsText.Text = string.Join("\n", CreditLines);

        _main.SetEntries(new[]
        {
            new FoEntry { Id = "new", Text = "NEW" },
            new FoEntry { Id = "load", Text = "LOAD", Disabled = true },
            new FoEntry { Id = "settings", Text = "SETTINGS" },
            new FoEntry { Id = "crew", Text = "CREW" },
            new FoEntry { Id = "quit", Text = "QUIT" },
        });
        _main.SelectionChanged += Audio.Focus;
        _main.SelectionChanged += SyncArt;
        _art = TitleMenuArt.TryCreate(this, _ui, _main);
        _second.SelectionChanged += Audio.Focus;
        _main.Pressed += OnMainPressed;
        _second.Pressed += OnSecondPressed;
        _second.ValueChanged += OnValueChanged;
        var tree = GetTree();
        if (s_newGameSceneChangeTreeId != tree.GetInstanceId())
        {
            tree.Connect(SceneTree.SignalName.SceneChanged, Callable.From(ReleaseOrphanedNewGameDestination));
            s_newGameSceneChangeTreeId = tree.GetInstanceId();
        }

        Resized += Fit;
        Fit();
        SetState(MenuState.Main);
        Modulate = Colors.Transparent;
        CreateTween().TweenProperty(this, "modulate:a", 1.0, 0.8);
    }

    public override void _Process(double delta)
    {
        if (Current != MenuState.Credits) return;
        _creditsText.Position -= new Vector2(0, CreditsSpeed * (float)delta);
        if (_creditsText.Position.Y + _creditsText.Size.Y < 0) EndCredits();
    }

    public override void _Input(InputEvent @event)
    {
        var viewport = GetViewport();
        if (_remapping)
        {
            CaptureRemap(@event);
            return;
        }
        if (_swallowRelease != Key.None && @event is InputEventKey held && KeyCodeOf(held) == _swallowRelease)
        {
            if (!held.Pressed) _swallowRelease = Key.None;
            viewport.SetInputAsHandled();
            return;
        }
        if (Current == MenuState.Credits && @event is InputEventMouseButton { Pressed: true } wheel
            && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            _creditsText.Position += new Vector2(0, wheel.ButtonIndex == MouseButton.WheelUp ? 60 : -60);
        }

        if (Pressed(@event, "ui_up", "move_forward")) Step(-1);
        else if (Pressed(@event, "ui_down", "move_back")) Step(1);
        else if (Pressed(@event, "ui_left", "move_left")) Side(-1);
        else if (Pressed(@event, "ui_right", "move_right")) Side(1);
        else if (@event is not InputEventMouseButton
            && (@event.IsActionReleased("ui_accept") || @event.IsActionReleased("activate"))) { if (!CommandsBlocked) Accept(); }
        else if (@event is not InputEventMouseButton
            && (@event.IsActionReleased("ui_cancel") || IsKeyReleased(@event, Key.Tab))) { if (!CommandsBlocked) Cancel(); }
        else if (IsKeyReleased(@event, Key.T) || @event is InputEventJoypadButton { Pressed: false, ButtonIndex: JoyButton.Y }) { if (!CommandsBlocked) Defaults(); }
        else return;
        viewport.SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        ReleaseNewGameDestinationOnExit();
    }

    public void Accept()
    {
        switch (Current)
        {
            case MenuState.Main: _main.Press(); break;
            case MenuState.Settings or MenuState.Options or MenuState.Controls: _second.Press(); break;
            case MenuState.QuitConfirm or MenuState.DefaultsConfirm: _confirmAccept(); break;
        }
    }

    public void Cancel()
    {
        if (_saving || _remapping) return;
        switch (Current)
        {
            case MenuState.QuitConfirm:
                Audio.Cancel();
                SetState(MenuState.Main);
                break;
            case MenuState.DefaultsConfirm:
                Audio.Cancel();
                SetState(_confirmReturn);
                break;
            case MenuState.Options or MenuState.Controls:
                Audio.Cancel();
                OpenCategories();
                break;
            case MenuState.Settings:
                Audio.Cancel();
                LeaveSettings();
                break;
            case MenuState.Credits:
                Audio.Cancel();
                EndCredits();
                break;
        }
    }

    public void Defaults()
    {
        if (_saving || _remapping || Current is not (MenuState.Options or MenuState.Controls)) return;
        var returnTo = Current;
        Confirm("Reset settings to default values?", MenuState.DefaultsConfirm, returnTo, () =>
        {
            Audio.Ok();
            if (returnTo == MenuState.Controls)
            {
                GameSettings.ResetBindings();
                _second.SetEntries(ControlEntries(), _second.Selected);
            }
            else
            {
                GameSettings.ResetSettings(_optionDefs);
                foreach (var entry in _second.Entries) entry.Value = GameSettings.Get(entry.Id);
                RefreshLocks();
            }
            _settingsChanged = true;
            _resetCooldown = true;
            RefreshInputs();
            GetTree().CreateTimer(0.2).Timeout += () =>
            {
                _resetCooldown = false;
                RefreshInputs();
            };
            SetState(returnTo);
        });
    }

    private void Step(int direction)
    {
        var list = Current == MenuState.Main ? _main : _second;
        list.MoveSelection(direction);
    }

    private void Side(int direction)
    {
        if (Current == MenuState.Options) _second.Adjust(direction);
    }

    private void OnMainPressed(FoEntry entry)
    {
        if (entry.Disabled)
        {
            Audio.Cancel();
            return;
        }
        Audio.Ok();
        switch (entry.Id)
        {
            case "new":
                if (s_pendingNewGameDestination.ChangeInFlight) break;
                _closing = true;
                ReleaseTrackedNewGameDestination(false);
                var scene = ResourceLoader.Load<PackedScene>(NewGameScene)?.Instantiate();
                if (scene == null)
                {
                    _closing = false;
                    RefreshInputs();
                    break;
                }
                scene.Connect(Node.SignalName.TreeEntered, Callable.From(OnPendingNewGameDestinationEnteredTree), (uint)GodotObject.ConnectFlags.OneShot);
                if (GetTree().ChangeSceneToNode(scene) != Error.Ok)
                {
                    scene.Free();
                    _closing = false;
                    RefreshInputs();
                    break;
                }
                s_pendingNewGameDestination.HandToEngine(scene.GetInstanceId());
                break;
            case "settings":
                OpenCategories();
                break;
            case "crew":
                StartCredits();
                break;
            case "quit":
                Confirm("Quit to desktop?", MenuState.QuitConfirm, MenuState.Main, () =>
                {
                    Audio.Ok();
                    EmitSignal(SignalName.QuitRequested);
                    if (!QuitsTree) return;
                    _closing = true;
                    GetTree().Quit();
                });
                break;
        }
    }

    private static void OnPendingNewGameDestinationEnteredTree()
    {
        s_pendingNewGameDestination.Adopted();
        GameSaveService.ResetForNewGame();
    }

    private static void ReleaseOrphanedNewGameDestination()
    {
        if (!s_pendingNewGameDestination.IsArmed) return;
        Callable.From(SettleNewGameDestination).CallDeferred();
    }

    private static void SettleNewGameDestination()
    {
        s_pendingNewGameDestination.Settled();
        ReleaseTrackedNewGameDestination(false);
    }

    private static void ReleaseNewGameDestinationOnExit()
    {
        ReleaseTrackedNewGameDestination(true);
    }

    private static void ReleaseTrackedNewGameDestination(bool exitingTree)
    {
        if (!s_pendingNewGameDestination.IsArmed) return;
        var id = s_pendingNewGameDestination.PendingId;
        bool valid = GodotObject.IsInstanceIdValid(id);
        bool insideTree = valid && GodotObject.InstanceFromId(id) is Node pending && pending.IsInsideTree();
        var verdict = exitingTree
            ? s_pendingNewGameDestination.ReleaseOnExit(valid, insideTree)
            : s_pendingNewGameDestination.ReleaseOrphan(valid, insideTree);
        if (verdict != PendingSceneVerdict.Free) return;
        if (GodotObject.InstanceFromId(id) is Node orphan && GodotObject.IsInstanceValid(orphan) && !orphan.IsInsideTree()) orphan.QueueFree();
    }

    private void OnSecondPressed(FoEntry entry)
    {
        if (Current == MenuState.Settings)
        {
            Audio.Ok();
            OpenOptions(_second.Selected);
        }
        else if (Current == MenuState.Options && entry.Disabled)
        {
            Audio.Cancel();
        }
        else if (Current == MenuState.Controls && !_remapCooldown)
        {
            _remapping = true;
            _remapAction = entry.Id;
            ShowPrompt("Press a button to map to this action.");
            Audio.Ok();
            RefreshInputs();
        }
    }

    private void OnValueChanged(FoEntry entry)
    {
        GameSettings.Set(entry.Id, entry.Value);
        _settingsChanged = true;
        RefreshLocks();
    }

    private void RefreshLocks()
    {
        foreach (var entry in _second.Entries)
        {
            entry.Locked = GameSettings.LockedText(entry.Id);
            entry.Disabled = entry.Locked != null;
        }
        _second.Refresh();
    }

    private void OpenCategories()
    {
        _second.SetEntries(Categories.Select(name => new FoEntry { Id = name, Text = name }), _categoryIndex);
        SetState(MenuState.Settings);
    }

    private void OpenOptions(int category)
    {
        _categoryIndex = category;
        if (category == 1)
        {
            _second.SetEntries(ControlEntries());
            SetState(MenuState.Controls);
            return;
        }
        _optionDefs = category switch { 0 => GameSettings.Gameplay, 2 => GameSettings.Display, _ => GameSettings.Audio };
        _second.SetEntries(_optionDefs.Select(def => new FoEntry
        {
            Id = def.Id,
            Text = def.Label,
            Kind = def.Kind switch
            {
                OptionKind.Slider => FoKind.Slider,
                OptionKind.Stepper => FoKind.Stepper,
                _ => FoKind.Checkbox,
            },
            Options = def.Options ?? Array.Empty<string>(),
            Value = GameSettings.Get(def.Id),
            Min = def.Min,
            Max = def.Max,
            Step = def.Step,
            Readout = def.Readout,
        }));
        RefreshLocks();
        SetState(MenuState.Options);
    }

    private static IEnumerable<FoEntry> ControlEntries() => GameSettings.BindableActions.Select(a => new FoEntry
    {
        Id = a.Action,
        Text = a.Label,
        Kind = FoKind.Binding,
        BindingText = GameSettings.BindingText(a.Action),
    });

    private void LeaveSettings()
    {
        if (!_settingsChanged)
        {
            FinishSettings();
            return;
        }
        _saving = true;
        ShowPrompt("Saving...");
        RefreshInputs();
        GetTree().CreateTimer(1.0).Timeout += () =>
        {
            GameSettings.Save();
            FinishSettings();
        };
    }

    private void FinishSettings()
    {
        _saving = false;
        _settingsChanged = false;
        ShowPrompt("");
        SetState(MenuState.Main);
    }

    private void CaptureRemap(InputEvent @event)
    {
        InputEvent? binding = null;
        bool reserved = false;
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            var code = KeyCodeOf(key);
            reserved = ReservedKeys.Contains(code);
            binding = new InputEventKey { PhysicalKeycode = code };
            _swallowRelease = code;
        }
        else if (@event is InputEventMouseButton { Pressed: true } click
            && click.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle or MouseButton.Xbutton1 or MouseButton.Xbutton2)
        {
            binding = new InputEventMouseButton { ButtonIndex = click.ButtonIndex };
        }
        if (binding == null) return;
        GetViewport().SetInputAsHandled();

        if (reserved)
        {
            ShowPrompt("That button is reserved.");
            ClearPromptAfter(1.0);
            Audio.Cancel();
        }
        else
        {
            GameSettings.Rebind(_remapAction!, binding);
            foreach (var entry in _second.Entries) entry.BindingText = GameSettings.BindingText(entry.Id);
            _second.Refresh();
            _settingsChanged = true;
            ShowPrompt("");
            Audio.Ok();
        }
        _remapping = false;
        _remapCooldown = true;
        RefreshInputs();
        GetTree().CreateTimer(0.2).Timeout += () => _remapCooldown = false;
    }

    private void StartCredits()
    {
        _creditsText.Position = new Vector2(410, _creditsClip.Size.Y);
        SetState(MenuState.Credits);
    }

    private void EndCredits() => SetState(MenuState.Main);

    private void Confirm(string text, MenuState state, MenuState returnTo, Action accept)
    {
        _confirmReturn = returnTo;
        _confirmAccept = accept;
        ShowPrompt(text);
        SetState(state);
    }

    private void SetState(MenuState state)
    {
        Current = state;
        bool credits = state == MenuState.Credits;
        bool sub = state is MenuState.Settings or MenuState.Options or MenuState.Controls or MenuState.DefaultsConfirm;
        foreach (var panel in _frontPanels) panel.Visible = !credits;
        if (_art != null)
        {
            var on = state is MenuState.Main or MenuState.QuitConfirm;
            if (!_art.SetActive(on)) _art = null;
            else if (on) _frontPanels[2].Visible = false;
        }
        _secondPanel.Visible = sub;
        _version.Visible = sub;
        _creditsShade.Visible = credits;
        _creditsClip.Visible = credits;
        if (state is MenuState.Main or MenuState.Settings or MenuState.Options or MenuState.Controls or MenuState.Credits
            && !_saving && !_remapping) ShowPrompt("");
        RefreshInputs();
        UpdateHints();
    }

    private void SyncArt()
    {
        if (_art != null && !_art.Refresh())
        {
            _art = null;
            _frontPanels[2].Visible = Current != MenuState.Credits;
        }
    }

    private void RefreshInputs()
    {
        bool idle = !_saving && !_remapping && !_resetCooldown;
        _main.InputEnabled = idle && Current == MenuState.Main;
        _second.InputEnabled = idle && Current is MenuState.Settings or MenuState.Options or MenuState.Controls;
    }

    private void UpdateHints()
    {
        foreach (var child in _hints.GetChildren())
        {
            _hints.RemoveChild(child);
            child.QueueFree();
        }
        var confirm = ("Enter", "CONFIRM", (Action)Accept);
        var defaults = ("T", "DEFAULTS", (Action)Defaults);
        Control[] items = Current switch
        {
            MenuState.QuitConfirm or MenuState.DefaultsConfirm => new[] { Hint(confirm), Hint(("Esc", "CANCEL", Cancel)) },
            MenuState.Settings => new[] { Hint(("Esc", "BACK", Cancel)) },
            MenuState.Options or MenuState.Controls => new[] { Hint(defaults), Hint(("Esc", "BACK", Cancel)) },
            MenuState.Credits => new[] { Hint(("Esc", "CANCEL", Cancel)) },
            _ => Array.Empty<Control>(),
        };
        foreach (var item in items) _hints.AddChild(item);
    }

    private Control Hint((string Key, string Label, Action Act) hint)
    {
        var font = _main.Font;
        var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
        box.AddThemeConstantOverride("separation", 10);

        var cap = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Colors.Transparent,
            BorderColor = Colors.White,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 2,
            ContentMarginBottom = 2,
        });
        cap.AddChild(HintLabel(hint.Key, font));

        box.AddChild(cap);
        box.AddChild(HintLabel(hint.Label, font));
        box.GuiInput += e =>
        {
            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }) hint.Act();
        };
        return box;
    }

    private static Label HintLabel(string text, Font? font)
    {
        var label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        if (font != null) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", 22);
        return label;
    }

    private void ShowPrompt(string text)
    {
        _promptToken++;
        _prompt.Text = text;
    }

    private void ClearPromptAfter(double seconds)
    {
        int token = _promptToken;
        GetTree().CreateTimer(seconds).Timeout += () =>
        {
            if (token == _promptToken) _prompt.Text = "";
        };
    }

    private void Fit()
    {
        var scale = Mathf.Min(Size.X / DesignSize.X, Size.Y / DesignSize.Y);
        _ui.Scale = Vector2.One * scale;
        _ui.Position = (Size - DesignSize * scale) / 2;
    }

    private static bool Pressed(InputEvent e, string uiAction, string moveAction)
    {
        bool echo = e is InputEventKey;
        return e.IsActionPressed(uiAction, echo) || e.IsActionPressed(moveAction, echo);
    }

    private static bool IsKeyReleased(InputEvent e, Key key) =>
        e is InputEventKey { Pressed: false } released && released.Keycode == key;

    private static Key KeyCodeOf(InputEventKey key) =>
        key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;

    private static ImageTexture? LoadArt(string file)
    {
        var path = ProjectSettings.GlobalizePath(ArtDir + file);
        if (!System.IO.File.Exists(path)) return null;
        var image = Image.LoadFromFile(path);
        return image == null ? null : ImageTexture.CreateFromImage(image);
    }
}
