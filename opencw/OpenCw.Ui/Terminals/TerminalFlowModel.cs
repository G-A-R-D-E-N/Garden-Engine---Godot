using OpenCommonwealth.Services.Ui.Menus;

namespace OpenCommonwealth.Services.Ui.Terminals;

public enum TerminalFlowPhase
{
    Login,
    Hacking,
    Menu,
    Closed,
}

public sealed class TerminalFlowModel
{
    public const string DefaultFailure = "This terminal cannot be opened.";

    private readonly ITerminalSource _source;
    private readonly string _id;
    private readonly TerminalOptions _terminalOptions;
    private readonly HackingOptions _hackingOptions;
    private readonly TypeWriter _login;
    private string _loginText = string.Empty;
    private int _loginShown;

    private TerminalFlowModel(ITerminalSource source, string terminalId, TerminalOptions terminalOptions, HackingOptions hackingOptions)
    {
        _source = source;
        _id = terminalId;
        _terminalOptions = terminalOptions;
        _hackingOptions = hackingOptions;
        _login = new TypeWriter(hackingOptions.CharsPerSecond);
    }

    public event Action? Changed;

    public event Action? Closed;

    public event Action<string>? SoundRequested;

    public TerminalFlowPhase Phase { get; private set; } = TerminalFlowPhase.Closed;

    public TerminalMenuModel? Menu { get; private set; }

    public HackingMenuModel? Hack { get; private set; }

    public HackingOutcome? LastHackOutcome { get; private set; }

    public string TerminalId => _id;

    public string LoginText => _loginText[.._loginShown];

    public IReadOnlyList<MenuHint> Hints => Phase switch
    {
        TerminalFlowPhase.Menu => Menu!.Hints,
        TerminalFlowPhase.Hacking => Hack!.Hints,
        TerminalFlowPhase.Login => new[] { new MenuHint(MenuInputAction.Back, "Exit") },
        _ => Array.Empty<MenuHint>(),
    };

    public static TerminalFlowModel? TryOpen(
        ITerminalSource source,
        string terminalId,
        out string reason,
        TerminalOptions? terminalOptions = null,
        HackingOptions? hackingOptions = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        reason = string.Empty;
        var flow = new TerminalFlowModel(source, terminalId, terminalOptions ?? new TerminalOptions(), hackingOptions ?? new HackingOptions());
        TerminalOpening opening = source.Open(terminalId);
        switch (opening.Status)
        {
            case TerminalOpenStatus.Open:
                if (flow.EnterMenu(opening.RootNode))
                    return flow;
                break;
            case TerminalOpenStatus.Locked when opening.Hack is not null:
                flow.EnterLogin(opening.Hack);
                return flow;
        }
        reason = string.IsNullOrEmpty(opening.Reason) ? DefaultFailure : opening.Reason;
        return null;
    }

    public void Tick(double seconds)
    {
        switch (Phase)
        {
            case TerminalFlowPhase.Login:
                if (!_source.IsValid(_id))
                {
                    Close();
                    break;
                }
                _loginShown = Math.Min(_loginText.Length, _loginShown + _login.Advance(seconds));
                if (_loginShown >= _loginText.Length)
                    EnterHacking();
                else
                    Changed?.Invoke();
                break;
            case TerminalFlowPhase.Hacking:
                if (!_source.IsValid(_id))
                {
                    Hack!.Cancel();
                    break;
                }
                Hack!.Tick(seconds);
                break;
            case TerminalFlowPhase.Menu:
                Menu!.Tick(seconds);
                break;
        }
    }

    public bool Handle(MenuInputAction action)
    {
        switch (Phase)
        {
            case TerminalFlowPhase.Login:
                if (action == MenuInputAction.Back)
                    Close();
                else if (action == MenuInputAction.Accept)
                    EnterHacking();
                return true;
            case TerminalFlowPhase.Hacking:
                return Hack!.Handle(action);
            case TerminalFlowPhase.Menu:
                return Menu!.Handle(action);
            default:
                return false;
        }
    }

    public void Close()
    {
        if (Phase == TerminalFlowPhase.Closed)
            return;
        TerminalFlowPhase previous = Phase;
        Phase = TerminalFlowPhase.Closed;
        if (previous == TerminalFlowPhase.Hacking)
            Hack!.Cancel();
        Menu?.Close();
        Closed?.Invoke();
        Changed?.Invoke();
    }

    private void EnterLogin(IHackingSession session)
    {
        _loginText = _hackingOptions.Strings.Login;
        Hack = new HackingMenuModel(session, _hackingOptions);
        Phase = TerminalFlowPhase.Login;
        if (_loginText.Length == 0)
            Phase = TerminalFlowPhase.Hacking;
        BindHack();
    }

    private void EnterHacking()
    {
        _loginShown = _loginText.Length;
        Phase = TerminalFlowPhase.Hacking;
        Changed?.Invoke();
    }

    private void BindHack()
    {
        Hack!.Changed += RaiseChanged;
        Hack.SoundRequested += RaiseSound;
        Hack.Finished += OnHackFinished;
    }

    private void OnHackFinished(HackingOutcome outcome)
    {
        LastHackOutcome = outcome;
        _source.HackFinished(_id, outcome);
        if (outcome == HackingOutcome.Accepted)
        {
            TerminalOpening opening = _source.Open(_id);
            if (opening.Status == TerminalOpenStatus.Open && EnterMenu(opening.RootNode))
            {
                Changed?.Invoke();
                return;
            }
        }
        Close();
    }

    private bool EnterMenu(string rootNode)
    {
        if (string.IsNullOrEmpty(rootNode))
            return false;
        var menu = new TerminalMenuModel(_source, _id, rootNode, _terminalOptions);
        if (menu.Phase == TerminalPhase.Closed)
            return false;
        Menu = menu;
        menu.Changed += RaiseChanged;
        menu.SoundRequested += RaiseSound;
        menu.Closed += Close;
        Phase = TerminalFlowPhase.Menu;
        return true;
    }

    private void RaiseChanged() => Changed?.Invoke();

    private void RaiseSound(string name) => SoundRequested?.Invoke(name);
}
