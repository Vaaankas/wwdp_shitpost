using Content.Client.Weapons.Ranged.Components;
using Content.Shared._White.Lasers;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;


namespace Content.Client.Weapons.Ranged.Systems;

public sealed partial class GunSystem
{
    protected override void InitializeOverheating()
    {
        base.InitializeOverheating();

        SubscribeLocalEvent<ProjectileOverheatingAmmoProviderComponent, AmmoCounterControlEvent>(OnControl);
        SubscribeLocalEvent<ProjectileOverheatingAmmoProviderComponent, UpdateAmmoCounterEvent>(OnAmmoCountUpdate);
    }

    private void OnAmmoCountUpdate(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component, UpdateAmmoCounterEvent args)
    {
        if (args.Control is not OverheatingBoxesStatusControl boxes)
            return;

        var denominator = (int)(component.HeatDamageThreshold / component.HeatIncrease);

        boxes.Update((int)(component.Heat / component.HeatIncrease), denominator);
    }

    private void OnControl(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component, AmmoCounterControlEvent args)
    {
        args.Control = new OverheatingBoxesStatusControl();
    }

    public override void UpdateOverheatingAppearance(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component)
    {
        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.HasAmmo, component.Heat != 0, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoCount, (int) component.Heat, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, (int) component.HeatDamageThreshold, appearance);
    }
}
