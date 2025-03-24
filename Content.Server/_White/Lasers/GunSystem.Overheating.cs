using Content.Server.Explosion.EntitySystems;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Shared._White.Lasers;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    [Dependency] private readonly ExplosionSystem _explosionSystem = default!;
    protected override void InitializeOverheating()
    {
        base.InitializeOverheating();

        SubscribeLocalEvent<ProjectileOverheatingAmmoProviderComponent, DamageExamineEvent>(OnOverheatingDamageExamine);
        SubscribeLocalEvent<ProjectileOverheatingAmmoProviderComponent, GunOverheatExplodeEvent>(OnOverheatExplode);
    }

    private void OnOverheatingDamageExamine(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component, ref DamageExamineEvent args)
    {
        var damageSpec = GetDamage(component);

        if (damageSpec == null)
            return;

        var damageType = Loc.GetString("damage-projectile");

        _damageExamine.AddDamageExamine(args.Message, damageSpec, damageType);
    }

    private DamageSpecifier? GetDamage(ProjectileOverheatingAmmoProviderComponent component)
    {
        if (!ProtoManager.Index<EntityPrototype>(component.Prototype)
            .Components
            .TryGetValue(_factory.GetComponentName(typeof(ProjectileComponent)), out var projectile))
            return null;

        var p = (ProjectileComponent) projectile.Component;

        if (!p.Damage.Empty)
        {
            return p.Damage;
        }

        return null;
    }

    private void OnOverheatExplode(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component, ref GunOverheatExplodeEvent args)
    {
        _explosionSystem.QueueExplosion(
            uid,
            "Default",
            50,
            2,
            10);
    }

    public override void UpdateOverheatingAppearance(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component
    )
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.HasAmmo, component.Heat != 0, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoCount, (int) component.Heat, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, (int) component.HeatDamageThreshold, appearance);
    }
}
