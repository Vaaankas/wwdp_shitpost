using System.Linq;
using Content.Shared.Body.Systems;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.Loadouts.Prototypes;
using Content.Shared.Customization.Systems;
using Content.Shared.Inventory;
using Content.Shared.Paint;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Station;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using JetBrains.Annotations;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Serialization;

namespace Content.Shared.Clothing.Loadouts.Systems;

public sealed class SharedLoadoutSystem : EntitySystem
{
    [Dependency] private readonly SharedStationSpawningSystem _station = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly CharacterRequirementsSystem _characterRequirements = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedTransformSystem _sharedTransformSystem = default!;
    [Dependency] private readonly ILogManager _log = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!; // wWDP

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();

        // Wait until the character has all their organs before we give them their loadout to activate internals
        SubscribeLocalEvent<LoadoutComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedBodySystem)]);

        _sawmill = _log.GetSawmill("loadouts");
    }

    private void OnMapInit(EntityUid uid, LoadoutComponent component, MapInitEvent args)
    {
        if (component.StartingGear is null
            || component.StartingGear.Count <= 0)
            return;

        var proto = _prototype.Index(_random.Pick(component.StartingGear));
        _station.EquipStartingGear(uid, proto);
    }

    public (List<EntityUid>, List<(EntityUid, LoadoutPreference, int)>) ApplyCharacterLoadout(
        EntityUid uid,
        ProtoId<JobPrototype> job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        out List<(EntityUid, LoadoutPreference)> heirlooms)
    {
        var jobPrototype = _prototype.Index(job);
        return ApplyCharacterLoadout(uid, jobPrototype, profile, playTimes, whitelisted, out heirlooms);
    }

    /// <summary>
    ///     Equips entities from a <see cref="HumanoidCharacterProfile"/>'s loadout preferences to a given entity
    /// </summary>
    /// <param name="uid">The entity to give the loadout items to</param>
    /// <param name="job">The job to use for loadout whitelist/blacklist (should be the job of the entity)</param>
    /// <param name="profile">The profile to get loadout items from (should be the entity's, or at least have the same species as the entity)</param>
    /// <param name="playTimes">Playtime for the player for use with playtime requirements</param>
    /// <param name="whitelisted">If the player is whitelisted</param>
    /// <param name="heirlooms">Every entity the player selected as a potential heirloom</param>
    /// <returns>A list of loadout items that couldn't be equipped but passed checks</returns>
    public (List<EntityUid>, List<(EntityUid, LoadoutPreference, int)>) ApplyCharacterLoadout(
        EntityUid uid,
        JobPrototype job,
        HumanoidCharacterProfile profile,
        Dictionary<string, TimeSpan> playTimes,
        bool whitelisted,
        out List<(EntityUid, LoadoutPreference)> heirlooms)
    {
        var failedLoadouts = new List<EntityUid>();
        var allLoadouts = new List<(EntityUid, LoadoutPreference, int)>();
        heirlooms = new();
        if (!job.SpawnLoadout)
            return (failedLoadouts, allLoadouts);

        foreach (var loadout in profile.LoadoutPreferences)
        {
            var slot = "";

            // Ignore loadouts that don't exist
            if (!_prototype.TryIndex<LoadoutPrototype>(loadout.LoadoutName, out var loadoutProto))
                continue;

            if (!_characterRequirements.CheckRequirementsValid(
                loadoutProto.Requirements, job, profile, playTimes, whitelisted, loadoutProto,
                EntityManager, _prototype, _configuration,
                out _))
                continue;

            // Spawn the loadout items
            var spawned = EntityManager.SpawnEntities(
                _sharedTransformSystem.GetMapCoordinates(uid),
                loadoutProto.Items.Select(p => (string?) p.ToString()).ToList()); // Dumb cast

            var i = 0; // If someone wants to add multi-item support to the editor
            foreach (var item in spawned)
            {
                if (item == EntityUid.Invalid || !Exists(item))
                {
                    _sawmill.Warning($"Item {ToPrettyString(item)} failed to spawn or did not exist.");
                    continue;
                }

                allLoadouts.Add((item, loadout, i));
                if (i == 0 && loadout.CustomHeirloom == true) // Only the first item can be an heirloom
                    heirlooms.Add((item, loadout));

                // Equip it
                if (EntityManager.TryGetComponent<ClothingComponent>(item, out var clothingComp)
                    && _characterRequirements.CanEntityWearItem(uid, item, true)
                    && _inventory.TryGetSlots(uid, out var slotDefinitions))
                {
                    var deleted = false;
                    foreach (var curSlot in slotDefinitions)
                    {
                        // If the loadout can't equip here or we've already deleted an item from this slot, skip it
                        if (!clothingComp.Slots.HasFlag(curSlot.SlotFlags) || deleted)
                            continue;

                        slot = curSlot.Name;

                        // If the loadout is exclusive delete the equipped item
                        if (loadoutProto.Exclusive)
                        {
                            // Get the item in the slot
                            if (!_inventory.TryGetSlotEntity(uid, curSlot.Name, out var slotItem))
                                continue;

                            // WWDP edit start - save stored items
                            if (TryComp<StorageComponent>(slotItem, out var storage))
                            {
                                foreach (var storeditem in storage.Container.ContainedEntities.ToArray())

                                    // try to insert into the new container; drop and save as failed if not possible
                                    if (TryComp<StorageComponent>(item, out var newStorage)
                                        && !_storage.Insert(item, storeditem, out _, storageComp: newStorage, playSound: false))
                                    {
                                        _sharedTransformSystem.DropNextTo(storeditem, uid);
                                        failedLoadouts.Add(storeditem);
                                    }
                            }
                            // WWDP edit end

                            EntityManager.DeleteEntity(slotItem.Value);
                            deleted = true;
                        }
                    }
                }

                // Color it
                if (loadout.CustomColorTint != null)
                {
                    EnsureComp<AppearanceComponent>(item);
                    EnsureComp<PaintedComponent>(item, out var paint);
                    paint.Color = Color.FromHex(loadout.CustomColorTint);
                    paint.Enabled = true;
                    _appearance.TryGetData(item, PaintVisuals.Painted, out bool data);
                    _appearance.SetData(item, PaintVisuals.Painted, !data);
                }

                // Equip the loadout
                if (!_inventory.TryEquip(uid, item, slot, true, !string.IsNullOrEmpty(slot), true))
                    failedLoadouts.Add(item);

                RaiseLocalEvent(item, new SpawnedViaLoadoutEvent(uid, job.ID, profile));

                i++;
            }
        }

        // Return a list of items that couldn't be equipped so the server can handle it if it wants
        // The server has more information about the inventory system than the client does and the client doesn't need to put loadouts in backpacks
        return (failedLoadouts, allLoadouts);
    }
}

[Serializable, NetSerializable, ImplicitDataDefinitionForInheritors]
public abstract partial class Loadout
{
    [DataField] public string LoadoutName { get; set; }
    [DataField] public string? CustomName { get; set; }
    [DataField] public string? CustomDescription { get; set; }
    [DataField] public string? CustomColorTint { get; set; }
    [DataField] public bool? CustomHeirloom { get; set; }

    protected Loadout(
        string loadoutName,
        string? customName = null,
        string? customDescription = null,
        string? customColorTint = null,
        bool? customHeirloom = null
    )
    {
        LoadoutName = loadoutName;
        CustomName = customName;
        CustomDescription = customDescription;
        CustomColorTint = customColorTint;
        CustomHeirloom = customHeirloom;
    }
}

[Serializable, NetSerializable]
public sealed partial class LoadoutPreference : Loadout
{
    [DataField] public bool Selected;

    public LoadoutPreference(
        string loadoutName,
        string? customName = null,
        string? customDescription = null,
        string? customColorTint = null,
        bool? customHeirloom = null
    ) : base(loadoutName, customName, customDescription, customColorTint, customHeirloom) { }
}

/// <summary>
///     Event raised both directed and broadcast when a player has been spawned and then given a Loadout.
///     Most useful to delay actions that should happen on spawn for when the loadouts have been handled.
/// </summary>
[PublicAPI]
public sealed class PlayerLoadoutAppliedEvent : EntityEventArgs
{
    public EntityUid Mob { get; }
    public ICommonSession Player { get; }
    public string? JobId { get; }
    public bool LateJoin { get; }
    public bool Silent { get; }
    public EntityUid Station { get; }
    public HumanoidCharacterProfile Profile { get; }

    // Ex. If this is the 27th person to join, this will be 27.
    public int JoinOrder { get; }

    public PlayerLoadoutAppliedEvent(EntityUid mob,
        ICommonSession player,
        string? jobId,
        bool lateJoin,
        bool silent,
        int joinOrder,
        EntityUid station,
        HumanoidCharacterProfile profile)
    {
        Mob = mob;
        Player = player;
        JobId = jobId;
        LateJoin = lateJoin;
        Silent = silent;
        Station = station;
        Profile = profile;
        JoinOrder = joinOrder;
    }
}

/// <summary>
///     Event raised when a player's loadout item is spawned on said item.
/// </summary>
[PublicAPI]
public sealed class SpawnedViaLoadoutEvent : EntityEventArgs
{
    public EntityUid Mob { get; }
    public string? JobId { get; }
    public HumanoidCharacterProfile Profile { get; }

    public SpawnedViaLoadoutEvent(EntityUid mob,
        string? jobId,
        HumanoidCharacterProfile profile)
    {
        Mob = mob;
        JobId = jobId;
        Profile = profile;
    }
}
