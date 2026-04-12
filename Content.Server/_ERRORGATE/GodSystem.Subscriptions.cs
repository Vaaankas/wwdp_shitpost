using Content.Server.Chat.Systems;


namespace Content.Server._ERRORGATE;


public sealed partial class GodSystem
{
    private void SubscribeEvents()
    {
        SubscribeLocalEvent<EntitySpokeEvent>(OnSpeak);
    }

    private void OnSpeak(EntitySpokeEvent ev)
    {
        var log = $"{ToPrettyString(ev.Source):user} says: {ev.Message}.";
        _actionLogs.Add(log);
    }
}
