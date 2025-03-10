using Content.Server.Damage.Components;
using Content.Shared.Damage;
using Robust.Shared.Player;
using Content.Shared.Weapons.Melee.Events;
using System.Linq;
using Content.Shared._Shitmed.Targeting;


namespace Content.Server.Damage.Systems;

public sealed class DamageOnHitSystem : EntitySystem
{
    [Dependency] private readonly DamageableSystem _damageableSystem = default!;
    [Dependency] private readonly SharedTargetingSystem _targeting = default!; // WWDP
    private readonly Random _random = new Random();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DamageOnHitComponent, MeleeHitEvent>(DamageSelf);
    }

    // Looks for a hit, then damages the entity an appropriate amount.
    private void DamageSelf(EntityUid uid, DamageOnHitComponent component, MeleeHitEvent args)
    {
        if (args.HitEntities.Any()) {
            _damageableSystem.TryChangeDamage(uid, component.Damage, component.IgnoreResistances,
                targetPart: component.TargetParts is not null
                            ? _targeting.GetRandomBodyPart(component.TargetParts) // WWDP
                            : null);
        }
    }
}
