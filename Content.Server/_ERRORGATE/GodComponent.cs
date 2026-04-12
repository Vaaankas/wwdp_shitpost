namespace Content.Server._ERRORGATE;


[RegisterComponent, Access(typeof(GodSystem))]
public sealed partial class GodComponent : Component
{
    [DataField]
    public TimeSpan ResponseInterval { get; set; } = TimeSpan.FromSeconds(300);

    [DataField]
    public TimeSpan NextResponse;

    [DataField]
    public GodPersonality? Personality = null;

    /// <summary>
    /// The announcement played when a meteor swarm begins.
    /// </summary>
    [DataField]
    public LocId? Announcement = "station-event-god-announcement";
}
