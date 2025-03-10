using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Silicons.Borgs;
using Content.Shared.Verbs;
using Robust.Shared.Utility;

namespace Content.Shared.Armor;

/// <summary>
///     This handles logic relating to <see cref="ArmorComponent" />
/// </summary>
public abstract class SharedArmorSystem : EntitySystem
{
    [Dependency] private readonly ExamineSystemShared _examine = default!;
    [Dependency] private readonly SharedPopupSystem _popupSystem = default!; // WWDP

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArmorComponent, InventoryRelayedEvent<DamageModifyEvent>>(OnDamageModify);
        SubscribeLocalEvent<ArmorComponent, BorgModuleRelayedEvent<DamageModifyEvent>>(OnBorgDamageModify);
        SubscribeLocalEvent<ArmorComponent, GetVerbsEvent<ExamineVerb>>(OnArmorVerbExamine);
    }

    private void OnDamageModify(EntityUid uid, ArmorComponent component, InventoryRelayedEvent<DamageModifyEvent> args)
    {
        // WWDP; armor now only protects bodyparts it is covering as defined in ArmorComponent.ProtectedArea
        var blockable = false;

        foreach (var damage in args.Args.Damage.DamageDict)
        {
            if (component.Modifiers.Coefficients.ContainsKey(damage.Key) && damage.Value > 0
                || component.Modifiers.FlatReduction.ContainsKey(damage.Key) && damage.Value > 0)
            {
                blockable = true;
                break;
            }
        }

        if (!blockable)
            return;

        if (args.Args.TargetPart == null)
            return;

        if (!component.ProtectedArea.HasFlag(args.Args.TargetPart.Value))
            return;

        _popupSystem.PopupClient($"{Name(uid)} softens the blow!", uid);
        // WWDP edit end
        args.Args.Damage = DamageSpecifier.ApplyModifierSet(args.Args.Damage, component.Modifiers);
    }

    private void OnBorgDamageModify(EntityUid uid, ArmorComponent component,
        ref BorgModuleRelayedEvent<DamageModifyEvent> args)
    {
        args.Args.Damage = DamageSpecifier.ApplyModifierSet(args.Args.Damage, component.Modifiers);
    }

    private void OnArmorVerbExamine(EntityUid uid, ArmorComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var examineMarkup = GetArmorExamine(component.Modifiers);

        var ev = new ArmorExamineEvent(examineMarkup);
        RaiseLocalEvent(uid, ref ev);

        _examine.AddDetailedExamineVerb(args, component, examineMarkup,
            Loc.GetString("armor-examinable-verb-text"), "/Textures/Interface/VerbIcons/dot.svg.192dpi.png",
            Loc.GetString("armor-examinable-verb-message"));
    }

    private FormattedMessage GetArmorExamine(DamageModifierSet armorModifiers)
    {
        var msg = new FormattedMessage();

        msg.AddMarkup(Loc.GetString("armor-examine"));

        foreach (var coefficientArmor in armorModifiers.Coefficients)
        {
            msg.PushNewline();

            var armorType = Loc.GetString("armor-damage-type-" + coefficientArmor.Key.ToLower());
            msg.AddMarkup(Loc.GetString("armor-coefficient-value",
                ("type", armorType),
                ("value", MathF.Round((1f - coefficientArmor.Value) * 100, 1))
            ));
        }

        foreach (var flatArmor in armorModifiers.FlatReduction)
        {
            msg.PushNewline();

            var armorType = Loc.GetString("armor-damage-type-" + flatArmor.Key.ToLower());
            msg.AddMarkup(Loc.GetString("armor-reduction-value",
                ("type", armorType),
                ("value", flatArmor.Value)
            ));
        }

        return msg;
    }
}
