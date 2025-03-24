using Content.Client.Weapons.Ranged.Components;
using Content.Shared._White.Lasers;
using Content.Shared.Weapons.Ranged.Components;
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
        if (args.Control is not BoxesStatusControl boxes)
            return;

        var denominator = (int)(component.HeatDamageThreshold / component.HeatIncrease);

        boxes.Update((int)(component.Heat / denominator), (int)(component.HeatDamageThreshold) / denominator);
    }

    private void OnControl(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component, AmmoCounterControlEvent args)
    {
        args.Control = new BoxesStatusControl();
    }

    public override void UpdateOverheatingAppearance(EntityUid uid, ProjectileOverheatingAmmoProviderComponent component
    )
    {
        // if (TryComp<AmmoCounterComponent>(uid, out var ammoCounter) && ammoCounter.Control is BoxesStatusControl boxes)
        // {
        //     var denominator = (int)(component.HeatDamageThreshold / component.HeatIncrease);
//
        //     boxes.Update((int)(component.Heat / denominator), (int)(component.HeatDamageThreshold) / denominator);
        // }

        if (!TryComp<AppearanceComponent>(uid, out var appearance))
            return;

        Appearance.SetData(uid, AmmoVisuals.HasAmmo, component.Heat != 0, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoCount, (int) component.Heat, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, (int) component.HeatDamageThreshold, appearance);
    }
}
