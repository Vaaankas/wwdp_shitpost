using Content.Shared._White.Lasers;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;


namespace Content.Shared.Weapons.Ranged.Systems;


public abstract partial class SharedGunSystem
{
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    protected virtual void InitializeOverheating()
    {
        SubscribeLocalEvent<ProjectileOverheatingAmmoProviderComponent, ComponentInit>(OnOverheatingInit);
        SubscribeLocalEvent<ProjectileOverheatingAmmoProviderComponent, TakeAmmoEvent>(OnOverheatingTakeAmmo);
        SubscribeLocalEvent<ProjectileOverheatingAmmoProviderComponent, ExaminedEvent>(OnOverheatingExamine);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ProjectileOverheatingAmmoProviderComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Heat <= 0)
                continue;

            if (Timing.CurTime < comp.NextUpdate)
                continue;

            comp.Heat = Math.Max(0, comp.Heat - comp.CooldownHeatDecrease);
            comp.NextUpdate = Timing.CurTime + TimeSpan.FromSeconds(comp.CooldownInterval);

            UpdateAmmoCount(uid, prediction: false);
            UpdateOverheatingAppearance(uid, comp);
            Dirty(uid, comp);
        }
    }

    private void OnOverheatingInit(
        EntityUid uid,
        ProjectileOverheatingAmmoProviderComponent component,
        ComponentInit args
    )
    {
        UpdateOverheatingAppearance(uid, component);
        Dirty(uid, component);
    }

    private void OnOverheatingExamine(
        EntityUid uid,
        ProjectileOverheatingAmmoProviderComponent component,
        ExaminedEvent args
    )
    {
        var locale = "gun-overheating-examine";
        var shots = (int)(component.HeatDamageThreshold / component.HeatIncrease);

        if (component.EmergencyCooldown)
            locale += "-emergency";

        args.PushMarkup(Loc.GetString(locale, ("shots", shots)));
    }

    private void OnOverheatingTakeAmmo(
        EntityUid uid,
        ProjectileOverheatingAmmoProviderComponent component,
        TakeAmmoEvent args
    )
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        component.NextUpdate = Timing.CurTime + TimeSpan.FromSeconds(component.CooldownStartTimer);

        var shots = args.Shots;

        // Don't dirty if it's an empty fire.
        if (shots == 0)
            return;

        for (var i = 0; i < shots; i++)
        {
            args.Ammo.Add(OverheatingGetShootable(component, args.Coordinates));
            component.Heat += component.HeatIncrease;
        }

        if (component.ExplodeOnOverheat && component.Heat > component.ExplosionThreshold)
        {
            GunExplode(uid, component);
        }

        if (component.Heat > component.HeatDamageThreshold)
        {
            if (component.EmergencyCooldown && component.EmergencyCooldownUpdate <= Timing.CurTime)
                StartEmergencyCooldown(uid, component);
            else
                DealOverheatDamage(uid, component);
        }

        UpdateOverheatingAppearance(uid, component);
        Dirty(uid, component);
    }

    protected void StartEmergencyCooldown(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component)
    {
        var user = TransformSystem.GetParentUid(uid);

        Audio.PlayPvs(component.EmergencyCooldownSound, user);
        _popup.PopupClient(component.EmergencyCooldownPopup, user.ToCoordinates(), user, PopupType.Medium);
        component.EmergencyCooldownUpdate = Timing.CurTime + TimeSpan.FromSeconds(component.EmergencyCooldownInterval);
        component.NextUpdate = Timing.CurTime;
    }

    protected void DealOverheatDamage(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component)
    {
        var user = TransformSystem.GetParentUid(uid);

        Audio.PlayPvs(component.OverheatSound, user, AudioParams.Default.WithVariation(0.2f));
        _popup.PopupClient(component.HeatDamagePopup, user.ToCoordinates(), user, PopupType.MediumCaution);
        _damage.TryChangeDamage(user, component.OverheatDamage);
    }

    protected void GunExplode(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component)
    {
        var ev = new GunOverheatExplodeEvent();
        RaiseLocalEvent(uid, ev);

        var user = TransformSystem.GetParentUid(uid);

        _popup.PopupClient(component.ExplosionPopup, user.ToCoordinates(), user, PopupType.LargeCaution);
    }

    public abstract void UpdateOverheatingAppearance(
        EntityUid uid,
        ProjectileOverheatingAmmoProviderComponent component
    );

    private (EntityUid? Entity, IShootable) OverheatingGetShootable(
        ProjectileOverheatingAmmoProviderComponent component,
        EntityCoordinates coordinates
    )
    {
        var entity = Spawn(component.Prototype, coordinates);
        return (entity, EnsureShootable(entity));
    }

    public sealed class GunOverheatExplodeEvent : EntityEventArgs { }
}

