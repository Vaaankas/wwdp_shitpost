using Content.Server.Chat.Managers;
using Content.Server.IdentityManagement;
using Content.Shared.Chat;
using Content.Shared.Damage;
using Content.Shared.IdentityManagement.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Player;


namespace Content.Server._White.Chat.CombatLogs;

public sealed class CombatLogsSystem: EntitySystem
{
    //[Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IdentitySystem _identitySystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CombatLogsComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
        SubscribeLocalEvent<DamageableComponent, HitScanReflectAttemptEvent>(OnHitscan);
    }

    public void OnAttacked(EntityUid entity, CombatLogsComponent comp, AttackedEvent args)
    {
        if (!TryComp<ActorComponent>(entity, out var actor))
            return;

        if (!TryComp<MobStateComponent>(entity, out var playermobstate) || playermobstate.CurrentState == MobState.Dead)
            return;

        // Get attacker's identity or entity metadata name if there is none
        string attacker;

        if (HasComp<IdentityComponent>(args.User))
            attacker = _identitySystem.GetEntityIdentity(args.User);
        else
            attacker = Name(args.User);

        // Get weapon's name
        string weapon = Name(args.Used);

        // Get the attack verb from the melee weapon if there is one
        string verbRoot = "hit";
        string verbPresent = "hits";

        TryComp<MeleeWeaponComponent>(args.Used, out var weaponComponent);

        if (playermobstate.CurrentState == MobState.Critical) // Cant see shit in crit
        {
            LogMessage($"Something {verbPresent} you in the {args.TargetPart.ToString()}!");
            return;
        }

        bool unarmed = weapon == Name(args.User);

        if (args.User == entity)
        {
            if (unarmed)
            {
                LogMessage($"You {verbRoot} yourself in the {args.TargetPart.ToString()}!");
                return;
            }
            LogMessage($"You {verbRoot} yourself in the {args.TargetPart.ToString()} with a {weapon}!");
            return;
        }

        if (unarmed)
        {
            LogMessage($"{attacker} {verbPresent} you in the {args.TargetPart.ToString()}!");
            return;
        }
        LogMessage($"{attacker} {verbPresent} you in the {args.TargetPart.ToString()} with a {weapon}!");
        return;

        void LogMessage(string message)
        {
            LogToChat(message, actor);
        }

    }

    public void OnProjectileHit(EntityUid entity, ProjectileComponent comp, ProjectileHitEvent args)
    {
        if (!TryComp<ActorComponent>(args.Target, out var actor))
            return;

        if (!TryComp<MobStateComponent>(args.Target, out var playermobstate) || playermobstate.CurrentState == MobState.Dead)
            return;

        string projectile = MetaData(entity).EntityName;
        string message = $"{projectile} hits you in the {args.TargetPart.ToString()}!";

        var intensity = 12; // Base font size (i think)

        if (playermobstate.CurrentState == MobState.Critical) // Cant see shit in crit
            message = $"Something hits you in the {args.TargetPart.ToString()}!";

        LogToChat(message, actor);
    }

    public void OnHitscan(EntityUid entity, DamageableComponent comp, HitScanReflectAttemptEvent args)
    {
        if (!TryComp<ActorComponent>(args.Target, out var actor))
            return;

        if (!TryComp<MobStateComponent>(entity, out var playermobstate) || playermobstate.CurrentState == MobState.Dead)
            return;

        string message = args.Reflected
            ? $"A beam of energy reflects off of you!"
            : $"A beam of energy hits you in the {args.TargetPart.ToString()}!"; // todo WD lasers

        LogToChat(message, actor);

    }

    public void LogToChat(string message, ActorComponent actor)
        {
            var wrap = $"[font size=12][bold]{message}[/bold][/font]";
            _chatManager.ChatMessageToOne(ChatChannel.Local, message, wrap, EntityUid.Invalid, false, actor.PlayerSession.Channel,
                    Color.Red);
        }
}
