using System;
using System.Collections.Generic;
using System.Linq;
using OpenCommonwealth.Services.Ui.Dialogs;
using Radicle.Interop;

namespace OpenCommonwealth.Services.Dialogue;

public sealed class DialogueConversation
{
    public const int ExitSlot = 1;
    public const int TradeSlot = 3;
    public const byte PlayerCategory = 0;
    public const byte MiscCategory = 7;
    public const string GreetingCode = "GREE";
    public const string HelloCode = "HELO";
    public const string PlayerSpeakerName = "Player";
    public const string BarterText = "Barter";
    public const string CommandText = "Give orders";

    private static readonly int[] TopicSlots = [0, 2, 3];

    private readonly FormKey _npc;
    private readonly IDialogueRecords _records;
    private readonly bool _offersTrade;
    private readonly bool _offersCommand;
    private readonly string _speaker;
    private readonly FormKey? _voiceType;
    private readonly List<DialogueTopicData> _topics;
    private readonly HashSet<FormKey> _said = new();
    private readonly Dictionary<string, Func<int, DialogueNode?>> _next = new();
    private int _serial;

    private sealed record Option(DialogueTopicData Topic, DialogueInfoData Info, string Text);

    private sealed record Menu(IReadOnlyList<DialogueChoice> Choices, Func<int, DialogueNode?> Pick);

    public DialogueConversation(FormKey npc, IDialogueRecords records, Func<bool>? offersTrade = null, Func<bool>? offersCommand = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        _npc = npc;
        _records = records;
        _offersTrade = offersTrade?.Invoke() ?? false;
        _offersCommand = offersCommand?.Invoke() ?? false;
        _speaker = records.SpeakerName(npc);
        _voiceType = records.VoiceType(npc);
        _topics = records.Topics(npc).ToList();
        First = Opening();
    }

    public DialogueNode First { get; }

    public DialogueNode? Next(string nodeId, int slot) => _next.TryGetValue(nodeId, out var pick) ? pick(slot) : null;

    public static IReadOnlyList<DialogueInfoData> InChainOrder(IReadOnlyList<DialogueInfoData> infos)
    {
        var inTopic = infos.Select(i => i.Key).ToHashSet();
        var placed = new HashSet<FormKey>();
        var result = new List<DialogueInfoData>(infos.Count);
        var rest = infos.ToList();
        while (rest.Count > 0)
        {
            var ready = rest.Where(i => i.Info.PreviousInfo is not { } prev || !inTopic.Contains(prev) || placed.Contains(prev)).ToList();
            if (ready.Count == 0)
                ready = rest.ToList();
            foreach (var info in ready)
            {
                placed.Add(info.Key);
                result.Add(info);
                rest.Remove(info);
            }
        }
        return result;
    }

    private DialogueNode Opening()
    {
        var greeting = Greeting(GreetingCode) ?? Greeting(HelloCode);
        var menu = Hub();
        return greeting is null ? Silent(menu) : Speak(greeting, menu);
    }

    private DialogueInfoData? Greeting(string code) =>
        Ranked(t => t.Category == MiscCategory && t.Subtype == code).Select(t => FirstSpoken(t)).FirstOrDefault(i => i is not null);

    private IEnumerable<DialogueTopicData> Ranked(Func<DialogueTopicData, bool> keep) =>
        _topics.Where(keep).Select((t, i) => (t, i)).OrderByDescending(x => x.t.Priority).ThenBy(x => x.i).Select(x => x.t);

    private DialogueInfoData? FirstSpoken(DialogueTopicData topic) =>
        InChainOrder(topic.Infos).FirstOrDefault(i => Spoken(i).Count > 0 && _records.Eligible(_npc, i));

    private static IReadOnlyList<int> Spoken(DialogueInfoData info) =>
        Enumerable.Range(0, Math.Min(info.Info.Responses.Count, info.Texts.Count)).Where(i => !string.IsNullOrWhiteSpace(info.Texts[i])).ToList();

    private Menu Hub()
    {
        bool trade = _offersTrade, command = _offersCommand;
        int extras = (trade ? 1 : 0) + (command ? 1 : 0);
        var options = Ranked(t => t.Category == PlayerCategory)
            .Select(t => (Topic: t, Info: FirstSpoken(t)))
            .Select(x => (x.Topic, x.Info, Text: string.IsNullOrWhiteSpace(x.Info?.Prompt) ? x.Topic.Name : x.Info!.Prompt))
            .Where(x => x.Info is not null && !string.IsNullOrWhiteSpace(x.Text))
            .Select(x => new Option(x.Topic, x.Info!, x.Text!))
            .OrderBy(o => _said.Contains(o.Topic.Key))
            .Take(TopicSlots.Length - extras)
            .ToList();
        var choices = options.Select((o, i) => new DialogueChoice(o.Text, Used: _said.Contains(o.Topic.Key), Slot: TopicSlots[i])).ToList();
        if (command)
            choices.Add(new DialogueChoice(CommandText, Kind: DialogueChoiceKind.Command, Slot: TopicSlots[^extras]));
        if (trade)
            choices.Add(new DialogueChoice(BarterText, Kind: DialogueChoiceKind.Trade, Slot: TradeSlot));
        choices.Add(new DialogueChoice("Goodbye", Kind: DialogueChoiceKind.Exit, Slot: ExitSlot));
        return new Menu(choices, slot =>
        {
            int index = Array.IndexOf(TopicSlots, slot);
            if (index < 0 || index >= options.Count)
                return null;
            _said.Add(options[index].Topic.Key);
            return Speak(options[index].Info, Hub());
        });
    }

    private DialogueNode Silent(Menu menu)
    {
        string id = NextId("hub");
        _next[id] = menu.Pick;
        return new DialogueNode(id, _speaker, "", menu.Choices, 0, true);
    }

    private DialogueNode Speak(DialogueInfoData info, Menu menu)
    {
        var spoken = Spoken(info);
        string speaker = info.PlayerSpoken ? PlayerSpeakerName : _speaker;
        var voiceType = info.PlayerSpoken ? null : _voiceType;
        var nodes = new List<DialogueNode>(spoken.Count);
        for (int n = 0; n < spoken.Count; n++)
        {
            var response = info.Info.Responses[spoken[n]];
            string id = NextId(info.Key.LocalId.ToString("X8"));
            bool last = n == spoken.Count - 1;
            var choices = last ? menu.Choices : [new DialogueChoice("Continue", Slot: 0)];
            nodes.Add(new DialogueNode(id, speaker, info.Texts[spoken[n]]!, choices, 0, true, true)
            {
                Voice = new VoiceRef(info.Key, response.Number, voiceType, info.PlayerSpoken, info.Info.SharedInfo),
            });
            if (n > 0)
            {
                var self = nodes[n];
                _next[nodes[n - 1].Id] = _ => self;
            }
        }
        _next[nodes[^1].Id] = menu.Pick;
        return nodes[0];
    }

    private string NextId(string stem) => $"{stem}.{_serial++}";
}
