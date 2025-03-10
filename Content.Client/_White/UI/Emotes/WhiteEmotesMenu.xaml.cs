using System.Linq;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Speech;
using Content.Shared.Whitelist;
using Robust.Client.AutoGenerated;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._White.UI.Emotes;

[GenerateTypedNameReferences]
public sealed partial class WhiteEmotesMenu : DefaultWindow, IBaseEmoteMenu
{
    [Dependency] private readonly EntityManager _entManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ISharedPlayerManager _playerManager = default!;

    public event Action<ProtoId<EmotePrototype>>? OnPlayEmote;

    public WhiteEmotesMenu()
    {
        IoCManager.InjectDependencies(this);
        RobustXamlLoader.Load(this);

        var whitelistSystem = _entManager.System<EntityWhitelistSystem>();

        var emotes = _prototypeManager.EnumeratePrototypes<EmotePrototype>().ToList();
        emotes.Sort((a,b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        foreach (var emote in emotes)
        {
            var player = _playerManager.LocalEntity;
            if (emote.Category == EmoteCategory.Invalid ||
                emote.ChatTriggers.Count == 0 ||
                !(player.HasValue && whitelistSystem.IsWhitelistPassOrNull(emote.Whitelist, player.Value)) ||
                whitelistSystem.IsBlacklistPass(emote.Blacklist, player.Value))
                continue;

            if (!emote.Available &&
                _entManager.TryGetComponent<SpeechComponent>(player.Value, out var speech) &&
                !speech.AllowedEmotes.Contains(emote.ID))
                continue;

            var button = new EmoteMenuButton
            {
                ClipText = true,
                HorizontalExpand = true,
                VerticalExpand = true,
                MinWidth = 120,
                MaxWidth = 250,
                MaxHeight = 35,
                TextAlign = Label.AlignMode.Left,
                Text = Loc.GetString(emote.Name),
                ProtoId = emote,
            };

            button.OnPressed += _ => OnPlayEmote?.Invoke(emote);
            EmotionsContainer.AddChild(button);
        }
    }
}

public sealed class EmoteMenuButton : Button
{
    public ProtoId<EmotePrototype> ProtoId { get; set; }
}
