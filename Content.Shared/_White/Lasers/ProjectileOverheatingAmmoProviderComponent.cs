using Content.Shared.Damage;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;


namespace Content.Shared._White.Lasers;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ProjectileOverheatingAmmoProviderComponent : AmmoProviderComponent
{
    [ViewVariables(VVAccess.ReadWrite), DataField("proto", required: true, customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string Prototype = default!;

    [DataField, AutoNetworkedField]
    public float Heat;

    [DataField, AutoNetworkedField]
    public float HeatIncrease = 15f; //15 for pistols, 10 for long guns (better cooling)

    public TimeSpan NextUpdate;

    [DataField, AutoNetworkedField]
    public float CooldownStartTimer = 5f; // seconds

    [DataField, AutoNetworkedField]
    public float CooldownInterval = 2f; // seconds

    [DataField, AutoNetworkedField]
    public float CooldownHeatDecrease = 20f;

    [DataField, AutoNetworkedField]
    public bool EmergencyCooldown = true; // first time we step over the HeatDamageThreshold start to cool off instead of damaging the user

    public TimeSpan EmergencyCooldownUpdate;

    [DataField, AutoNetworkedField]
    public float EmergencyCooldownInterval = 60f; // seconds;

    [DataField, AutoNetworkedField]
    public float HeatDamageThreshold = 100f;

    [ViewVariables(VVAccess.ReadWrite)]
    public DamageSpecifier OverheatDamage = new() { DamageDict = new() { ["Heat"] = 15 } };

    [DataField, AutoNetworkedField]
    public bool ExplodeOnOverheat = true;

    [DataField, AutoNetworkedField]
    public float ExplosionThreshold = 150f;

    [DataField, AutoNetworkedField]
    public string HeatDamagePopup = "Ouch! The overheated gun burns your hands!";

    [DataField, AutoNetworkedField]
    public string ExplosionPopup = "The gun explodes in your hands!";

    [DataField, AutoNetworkedField]
    public string EmergencyCooldownPopup = "The weapon hisses as it begins to cool down";

    [DataField, AutoNetworkedField]
    public SoundSpecifier? EmergencyCooldownSound = new SoundPathSpecifier("/Audio/Weapons/Guns/EmptyAlarm/lmg_empty_alarm.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? OverheatSound = new SoundPathSpecifier("/Audio/Effects/lightburn.ogg");
}
