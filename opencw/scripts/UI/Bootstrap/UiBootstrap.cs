using Godot;
using OpenCommonwealth.Services.Papyrus.Ui;
using OpenCommonwealth.Services.Ui.Hud;
using OpenCommonwealth.Services.Papyrus.Vm;
using OpenCommonwealth.Services.Ui.Flow;
using OpenCommonwealth.Services.Ui.Lists;
using OpenCommonwealth.Services.Ui.MainMenu;
using OpenCommonwealth.Services.Ui.Menus;
using OpenCommonwealth.Services.Ui.Service;
using OpenCommonwealth.Services.Ui.Character;
using OpenCommonwealth.Services.Ui.Settings;
using OpenCommonwealth.UI.Character;
using OpenCommonwealth.UI.Bridge;
#if UI_GALLERY
using OpenCommonwealth.UI.Gallery;
#endif
using OpenCommonwealth.UI.MainMenu;
using OpenCommonwealth.UI.Menus;
using OpenCommonwealth.UI.Service;
using OpenCommonwealth.UI.Settings;
using System;

namespace OpenCommonwealth.UI.Bootstrap;

public partial class UiBootstrap : Node
{
    public const string PauseInputAction = "ui_pause";
    public const string GalleryInputAction = "ui_gallery";
    public const string PipBoyInputAction = "ui_pipboy";

    private readonly ModalSession _modal = new();
    private bool _hasPauseAction;
    private bool _hasGalleryAction;
    private bool _hasPipBoyAction;
    private bool _built;
    private PlayerController? _player;
    private GameUiAttachment? _papyrus;
    private GameSession? _papyrusSession;
    private GameSession? _waitingSession;

    public static UiBootstrap? Instance { get; private set; }

    public static bool OwnsEscape => Instance?._hasPauseAction == true;

    public GameSession? PapyrusSession => _papyrusSession;

    public GameUI Ui { get; private set; } = null!;

    public SettingsModel PlayerSettings { get; private set; } = null!;

    public KeyBindingModel PlayerKeys { get; private set; } = null!;

    public PauseMenuFlow Pause { get; private set; } = null!;

    private LevelUpHost? _levelUp;
    private BrowseProgression? _progression;

#if UI_GALLERY
    public GalleryLauncher Gallery { get; private set; } = null!;
#endif

    public SettingsApplier Effects { get; private set; } = null!;

    public override void _Ready()
    {
        SetProcess(false);
        SetProcessInput(false);
        SetProcessUnhandledInput(false);
        if (!ShouldRun())
            return;
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _hasPauseAction = InputMap.HasAction(PauseInputAction);
        _hasGalleryAction = InputMap.HasAction(GalleryInputAction);
        _hasPipBoyAction = InputMap.HasAction(PipBoyInputAction);
        if (!_hasPauseAction)
            GD.PushWarning($"[UI] input action '{PauseInputAction}' is missing from project.godot; the pause menu cannot open.");
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
        _progression?.Dispose();
    }

    // Requires: called from PlayerController._EnterTree; nothing is built until a gameplay scene has a player.
    public static void Register(PlayerController player) => Instance?.OnPlayerEntered(player);

    public override void _Process(double delta)
    {
        if (_player is null)
            return;
        if (!IsInstanceValid(_player) || !_player.IsInsideTree())
            _player = null;
        else
            PlayerHudBinding.Push(Ui.Hud.State, _player, PlayerKeys);
    }

    // Requires: runs before the menu router sees the key, which would otherwise read Tab as next-tab, so the Pip-Boy key can close the open Pip-Boy.
    public override void _Input(InputEvent input)
    {
        if (_hasPipBoyAction && input.IsActionPressed(PipBoyInputAction) && RouteOf(UiInputAction.PipBoy) == UiRoute.ClosePipBoy
            && Pause.ClosePipBoy())
            GetViewport().SetInputAsHandled();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        UiInputAction? pressed = null;
        if (_hasPauseAction && input.IsActionPressed(PauseInputAction))
            pressed = UiInputAction.Pause;
        else if (_hasGalleryAction && input.IsActionPressed(GalleryInputAction))
            pressed = UiInputAction.Gallery;
        else if (_hasPipBoyAction && input.IsActionPressed(PipBoyInputAction))
            pressed = UiInputAction.PipBoy;
        if (pressed is { } action && Dispatch(action))
            GetViewport().SetInputAsHandled();
    }

    private UiRoute RouteOf(UiInputAction action) =>
        UiInputRouter.Route(action, Pause.Context(DisplayServer.GetName() == "headless", HasPlayer()));

    private bool Dispatch(UiInputAction action)
    {
        return RouteOf(action) switch
        {
            UiRoute.OpenPause => Pause.Open(),
            UiRoute.OpenGallery => Pause.OpenGallery(),
            UiRoute.OpenPipBoy => Pause.OpenPipBoy(),
            UiRoute.ClosePipBoy => Pause.ClosePipBoy(),
            _ => false,
        };
    }

    private bool HasPlayer() => _player is not null && IsInstanceValid(_player) && _player.IsInsideTree();

    // Requires: called on the main thread after the Papyrus session has loaded; a session offered before the UI exists is attached once it does.
    public bool AttachPapyrus(GameSession session)
    {
        if (ReferenceEquals(_papyrusSession, session))
            return false;
        if (!_built || !IsInstanceValid(Ui) || !Ui.IsInsideTree())
        {
            _waitingSession = session;
            return false;
        }
        _waitingSession = null;
        _papyrus?.Dispose();
        _papyrus = null;
        _papyrusSession = session;
        GameUiAttachment attachment;
        try
        {
            attachment = GameUiAttach.Install(Ui, session, new HudToastSink(Ui.Hud.Toasts));
        }
        catch (Exception error)
        {
            GD.PushWarning($"[UI] Papyrus UI bridge was not installed: {error.Message}");
            return false;
        }
        if (attachment.Report.Bound == 0)
        {
            attachment.Dispose();
            return false;
        }
        _papyrus = attachment;
        return true;
    }

    public void DetachPapyrus(GameSession session)
    {
        if (ReferenceEquals(_waitingSession, session)) _waitingSession = null;
        if (!ReferenceEquals(_papyrusSession, session)) return;
        _papyrus?.Dispose();
        _papyrus = null;
        _papyrusSession = null;
    }

    private static bool ShouldRun()
    {
        if (DisplayServer.GetName() == "headless")
            return false;
        var args = OS.GetCmdlineUserArgs();
        return Array.IndexOf(args, "--nocapture") < 0 && Array.IndexOf(args, "--no-ui") < 0;
    }

    private void OnPlayerEntered(PlayerController player) =>
        Callable.From(() => Adopt(player)).CallDeferred();

    private void Adopt(PlayerController player)
    {
        if (!IsInstanceValid(player) || !player.IsInsideTree())
            return;
        Build();
        Effects.Track(player);
        if (_player is not null && IsInstanceValid(_player) && _player.IsInsideTree())
            return;
        _player = player;
        PlayerHudBinding.Configure(Ui.Hud.State, player);
        player.ActivationNoticed += text => Ui.Hud.Toasts.Post(text, ToastKind.Info);
        Ui.Hud.PowerArmorSource = player.PowerArmorSession;
        Pause.Player = player;
    }

    private void ImportTitleBindings()
    {
        if (!GameSettings.Loaded)
            return;
        var raw = new Dictionary<string, string>();
        foreach (var action in DefaultBindableActions.All)
        {
            string name = KeyBindingInputMap.ActionNameFor(action.Id);
            if (Array.Exists(GameSettings.BindableActions, b => b.Action == name) && GameSettings.ChangedBindingText(name) is { } text)
                raw[action.Id] = text;
        }
        PlayerKeys.ImportLegacyKeyboard(KeyBindingLegacy.Normalize(raw));
    }

    private void Build()
    {
        if (_built)
            return;
        _built = true;
        var store = new DiskFileStore();
        PlayerSettings = new SettingsModel(DefaultSettingDefs.All, store);
        PlayerKeys = new KeyBindingModel(DefaultBindableActions.All, store);
        ImportTitleBindings();
        Ui = new GameUI { Name = "GameUI", Layer = 5 };
        AddChild(Ui);
        Ui.Menus.Layouts = MenuLayoutLoader.Load();
        Effects = new SettingsApplier(PlayerSettings, GetTree(), Ui);
        AddChild(new KeyBindingApplier(PlayerKeys) { Name = "KeyBindingApplier" });
        var page = new SettingsPageModel(PlayerSettings, PlayerKeys);
#if UI_GALLERY
        Gallery = new GalleryLauncher(Ui, page) { Sources = UiRecordSources.Current };
        Func<MenuView>? galleryMenu = () => new GalleryMenu(Gallery);
        var services = OS.IsDebugBuild() ? new ServiceMenuLauncher(Ui, InMemoryServiceBackends.Create(), true) : null;
#else
        Func<MenuView>? galleryMenu = null;
        ServiceMenuLauncher? services = null;
#endif
        Pause = new PauseMenuFlow(Ui, page, () => PlayCellMain.PauseSaveDirectory(_player), new InMemoryFavouriteStore(), () => { PlayCellMain.AutosaveActive(); GetTree().Quit(); }, galleryMenu, OS.IsDebugBuild(), services)
        {
            Blocked = IsLootOpen,
            LoadReader = directory => PlayCellMain.PauseLoadReader(_player, directory),
            SaveWriter = directory => PlayCellMain.PauseSaveWriter(_player, directory),
            QuestLog = () => PipBoyQuestFeed.Build(_papyrusSession),
            Radio = () => PipBoyRadioFeed.Build(_papyrusSession),
            MapFeed = PipBoyMapFeed.Build,
            LocalMapFeed = PipBoyMapFeed.BuildLocal,
        };
        PlayerStatTracker.Install();
        _levelUp = new LevelUpHost(Ui, _progression = new BrowseProgression(PipBoyStatusFeed.Special, PipBoyStatusFeed.Perks)) { Name = "LevelUpHost" };
        AddChild(_levelUp);
        Ui.Menus.Stack.TopChanged += OnTopChanged;
        SetProcess(true);
        SetProcessInput(true);
        SetProcessUnhandledInput(true);
        if (_waitingSession is { } waiting)
            AttachPapyrus(waiting);
    }

    private bool IsLootOpen() =>
        _player is not null && IsInstanceValid(_player) && _player.GetNodeOrNull<LootMenu>("LootMenu") is { IsOpen: true };

    private void OnTopChanged(IMenu? previous, IMenu? current)
    {
        var tree = GetTree();
        switch (_modal.Update(Ui.IsAnyMenuOpen, Input.MouseMode == Input.MouseModeEnum.Captured))
        {
            case ModalChange.Entered:
                tree.Paused = true;
                Input.MouseMode = Input.MouseModeEnum.Visible;
                break;
            case ModalChange.Exited:
                tree.Paused = false;
                break;
            case ModalChange.ExitedRestoreCapture:
                tree.Paused = false;
                Input.MouseMode = Input.MouseModeEnum.Captured;
                break;
        }
    }
}
