using System.Linq;
using System.Numerics;
using Content.Server.Cargo.Systems;
using Content.Shared.Contests;
using Content.Server.Power.EntitySystems;
using Content.Server.Weapons.Ranged.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Database;
using Content.Shared.Effects;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Reflect;
using Content.Shared.Damage.Components;
using Robust.Shared.Audio;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Containers;
using ProjectileShotEvent = Content.Shared._Lavaland.Weapons.Ranged.Events.ProjectileShotEvent;
using Content.Shared.Mech.Components;

namespace Content.Server.Weapons.Ranged.Systems;

public sealed partial class GunSystem : SharedGunSystem
{
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly BatterySystem _battery = default!;
    [Dependency] private readonly DamageExamineSystem _damageExamine = default!;
    [Dependency] private readonly PricingSystem _pricing = default!;
    [Dependency] private readonly SharedColorFlashEffectSystem _color = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly StaminaSystem _stamina = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly ContestsSystem _contests = default!;
    [Dependency] private readonly PvsOverrideSystem _pvsOverride = default!;

    private const float DamagePitchVariation = 0.05f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BallisticAmmoProviderComponent, PriceCalculationEvent>(OnBallisticPrice);
    }

    private void OnBallisticPrice(EntityUid uid, BallisticAmmoProviderComponent component, ref PriceCalculationEvent args)
    {
        if (string.IsNullOrEmpty(component.Proto) || component.UnspawnedCount == 0)
            return;

        if (!ProtoManager.TryIndex<EntityPrototype>(component.Proto, out var proto))
        {
            Log.Error($"Unable to find fill prototype for price on {component.Proto} on {ToPrettyString(uid)}");
            return;
        }

        // Probably good enough for most.
        var price = _pricing.GetEstimatedPrice(proto);
        args.Price += price * component.UnspawnedCount;
    }

    public override void Shoot(EntityUid gunUid, GunComponent gun, List<(EntityUid? Entity, IShootable Shootable)> ammo,
        EntityCoordinates fromCoordinates, EntityCoordinates toCoordinates, out bool userImpulse, EntityUid? user = null, bool throwItems = false)
    {
        userImpulse = true;
        // WWDP EDIT START // All code related to mechs is fucking vile.
        var trueUser = user;
        if(TryComp<MechComponent>(user, out var mechComp))
        {
            trueUser = mechComp.PilotSlot.ContainedEntity;
        }
        // WWDP EDIT END

        if (user != null)
        {
            var selfEvent = new SelfBeforeGunShotEvent(user.Value, (gunUid, gun), ammo);
            RaiseLocalEvent(user.Value, selfEvent);
            if (selfEvent.Cancelled)
            {
                userImpulse = false;
                return;
            }
        }

        var fromMap = fromCoordinates.ToMap(EntityManager, TransformSystem);
        var toMap = toCoordinates.ToMapPos(EntityManager, TransformSystem);
        var mapDirection = toMap - fromMap.Position;
        var mapAngle = mapDirection.ToAngle();
        var angle = GetRecoilAngle(Timing.CurTime, gun, mapDirection.ToAngle(), user);

        // If applicable, this ensures the projectile is parented to grid on spawn, instead of the map.
        var fromEnt = MapManager.TryFindGridAt(fromMap, out var gridUid, out var grid)
            ? fromCoordinates.WithEntityId(gridUid, EntityManager)
            : new EntityCoordinates(MapManager.GetMapEntityId(fromMap.MapId), fromMap.Position);

        // Update shot based on the recoil
        toMap = fromMap.Position + angle.ToVec() * mapDirection.Length();
        mapDirection = toMap - fromMap.Position;
        mapAngle = mapDirection.ToAngle(); // WWDP
        var gunVelocity = Physics.GetMapLinearVelocity(fromEnt);

        // I must be high because this was getting tripped even when true.
        // DebugTools.Assert(direction != Vector2.Zero);
        var shotProjectiles = new List<EntityUid>(ammo.Count);

        foreach (var (ent, shootable) in ammo)
        {
            // pneumatic cannon doesn't shoot bullets it just throws them, ignore ammo handling
            if (throwItems && ent != null)
            {
                ShootOrThrow(ent.Value, mapDirection, gunVelocity, gun, gunUid, user);
                continue;
            }

            switch (shootable)
            {
                // Cartridge shoots something else
                case CartridgeAmmoComponent cartridge:
                    if (!cartridge.Spent)
                    {
                        var uid = Spawn(cartridge.Prototype, fromEnt);
                        CreateAndFireProjectiles(uid, cartridge);

                        RaiseLocalEvent(ent!.Value, new AmmoShotEvent()
                        {
                            FiredProjectiles = shotProjectiles,
                        });

                        SetCartridgeSpent(ent.Value, cartridge, true);

                        if (cartridge.DeleteOnSpawn)
                            Del(ent.Value);
                    }
                    else
                    {
                        userImpulse = false;
                        Audio.PlayPredicted(gun.SoundEmpty, gunUid, user);
                    }

                    // Something like ballistic might want to leave it in the container still
                    if (!cartridge.DeleteOnSpawn && !Containers.IsEntityInContainer(ent!.Value) &&
                        (!TryComp(gunUid, out BallisticAmmoProviderComponent? ballistic) || ballistic.AutoCycle)) // WD EDIT
                        EjectCartridge(ent.Value, angle, gunComp: gun);

                    Dirty(ent!.Value, cartridge);
                    break;
                // Ammo shoots itself
                case AmmoComponent newAmmo:
                    if (ent == null)
                        break;
                    CreateAndFireProjectiles(ent.Value, newAmmo);

                    break;
                case HitscanPrototype hitscan:

                    EntityUid? lastHit = null;

                    var from = fromMap;
                    // can't use map coords above because funny FireEffects // my brother in christ, you wrote that method
                    var dir = mapDirection.Normalized();
                    //in the situation when user == null, means that the cannon fires on its own (via signals). And we need the gun to not fire by itself in this case
                    var lastUser = user ?? gunUid;
                    RayCastResults? lastResult = null; // WWDP EDIT
                    for (var reflectAttempt = 0; reflectAttempt < 3; reflectAttempt++)
                    {
                        var ray = new CollisionRay(from.Position, dir, hitscan.CollisionMask);
                        var rayCastResults =
                            Physics.IntersectRay(from.MapId, ray, hitscan.MaxLength, lastUser, false).ToList();
                        if (!rayCastResults.Any())
                            break;

                        var raycastEvent = new HitScanAfterRayCastEvent(rayCastResults);
                        RaiseLocalEvent(lastUser, ref raycastEvent);

                        if (raycastEvent.RayCastResults == null)
                            break;

                        var result = raycastEvent.RayCastResults[0];
                        lastResult = result; // WWDP EDIT
                        // Check if laser is shot from in a container
                        if (!_container.IsEntityOrParentInContainer(lastUser))
                        {
                            // Checks if the laser should pass over unless targeted by its user
                            foreach (var collide in rayCastResults)
                            {
                                if (collide.HitEntity != gun.Target &&
                                    CompOrNull<RequireProjectileTargetComponent>(collide.HitEntity)?.Active == true)
                                {
                                    continue;
                                }

                                result = collide;
                                break;
                            }
                        }

                        var hit = result.HitEntity;
                        lastHit = hit;

                        FireEffects(from, result.Distance, dir.Normalized().ToAngle(), hitscan, hit);

                        if (hitscan.Reflective == ReflectType.None) // WWDP EDIT
                            break; // WWDP EDIT

                        var ev = new HitScanReflectAttemptEvent(user, gunUid, hitscan.Reflective, dir, false, hitscan.Damage); // WD EDIT
                        RaiseLocalEvent(hit, ref ev);

                        if (!ev.Reflected)
                            break;

                        from = _transform.GetMapCoordinates(hit); // WWDP EDIT
                        dir = ev.Direction;
                        lastUser = hit;
                    }


                    if (lastHit != null)
                    {
                        var hitEntity = lastHit.Value;
                        if (hitscan.StaminaDamage > 0f)
                            _stamina.TakeStaminaDamage(hitEntity, hitscan.StaminaDamage, source: user);

                        var dmg = hitscan.Damage;

                        var hitName = ToPrettyString(hitEntity);
                        if (dmg != null)
                            dmg = Damageable.TryChangeDamage(hitEntity, dmg, origin: user);

                        // check null again, as TryChangeDamage returns modified damage values
                        if (dmg != null)
                        {
                            if (!Deleted(hitEntity))
                            {
                                if (dmg.AnyPositive())
                                {
                                    _color.RaiseEffect(Color.Red, new List<EntityUid>() { hitEntity }, Filter.Pvs(hitEntity, entityManager: EntityManager));
                                }

                                // TODO get fallback position for playing hit sound.
                                PlayImpactSound(hitEntity, dmg, hitscan.Sound, hitscan.ForceSound);
                            }

                            if (user != null)
                            {
                                Logs.Add(LogType.HitScanHit,
                                    $"{ToPrettyString(user.Value):user} hit {hitName:target} using hitscan ({hitscan.ID}) and dealt {dmg.GetTotal():damage} damage");
                            }
                            else
                            {
                                Logs.Add(LogType.HitScanHit,
                                    $"{hitName:target} hit by hitscan ({hitscan.ID}) dealing {dmg.GetTotal():damage} damage");
                            }
                        }
                        // WWDP EDIT START
                        if (hitscan.SpawnAtImpact is EntProtoId impactEntity)
                        {
                            Angle ang = hitscan.ImpactSpawnRandomAngle ? Random.NextAngle() : dir.ToAngle() - Math.PI/2;
                            Spawn(impactEntity, new MapCoordinates(lastResult!.Value.HitPos, from.MapId), rotation: ang);
                        }
                        // WWDP EDIT END
                    }
                    else
                    {
                        FireEffects(from, hitscan.MaxLength, dir.ToAngle(), hitscan);
                        // WWDP EDIT START
                        Angle ang = hitscan.ImpactSpawnRandomAngle ? Random.NextAngle() : dir.ToAngle() - Math.PI/2;
                        if (hitscan.SpawnAtMaxLength is EntProtoId impactEntity)
                            Spawn(impactEntity, from.Offset(dir * hitscan.MaxLength), rotation: ang);
                        // WWDP EDIT END
                    }

                    Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, trueUser); // WWDP EDIT
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        RaiseLocalEvent(gunUid, new AmmoShotEvent()
        {
            FiredProjectiles = shotProjectiles,
        });

        void CreateAndFireProjectiles(EntityUid ammoEnt, AmmoComponent ammoComp)
        {
            RaiseLocalEvent(ammoEnt, new Content.Shared._White.Weapons.Ranged.Events.ProjectileShotEvent()); // WWDP

            if (TryComp<ProjectileSpreadComponent>(ammoEnt, out var ammoSpreadComp))
            {
                var spreadEvent = new GunGetAmmoSpreadEvent(ammoSpreadComp.Spread);
                RaiseLocalEvent(gunUid, ref spreadEvent);

                var plusminusSpread = spreadEvent.Spread * gun.ShotgunSpreadMultiplier / 2;
                var projectileCount = (int) (ammoSpreadComp.Count * gun.ShotgunProjectileCountModifier);

                if (gun.UniformSpread)
                {
                    var angles = LinearSpread(mapAngle - plusminusSpread,
                        mapAngle + plusminusSpread, ammoSpreadComp.Count);

                    ShootOrThrow(ammoEnt, angles[0].ToVec(), gunVelocity, gun, gunUid, user);
                    shotProjectiles.Add(ammoEnt);

                    for (var i = 1; i < projectileCount; i++)
                    {
                        var newuid = Spawn(ammoSpreadComp.Proto, fromEnt);
                        // Lavaland Change: Raise event when a projectile/pellet is fired from a gun.
                        RaiseLocalEvent(gunUid, new ProjectileShotEvent()
                        {
                            FiredProjectile = newuid
                        });
                        ShootOrThrow(newuid, angles[i].ToVec(), gunVelocity, gun, gunUid, user);
                        shotProjectiles.Add(newuid);
                    }
                    goto SpreadBreak;
                }

                // !WHY DOES THE FIRST ONE NEED TO BE SPAWNED SEPARATELY? IF I DON'T DO THIS, THE FIRST ONE HITS THE USER!
                ShootOrThrow(ammoEnt, mapAngle.ToVec(), gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);

                for (var i = 1; i < projectileCount; i++)
                {
                    var angle = Random.NextAngle(mapAngle - plusminusSpread, mapAngle + plusminusSpread);
                    var newuid = Spawn(ammoSpreadComp.Proto, fromEnt);
                    // Lavaland Change: Raise event when a projectile/pellet is fired from a gun.
                    RaiseLocalEvent(gunUid, new ProjectileShotEvent()
                    {
                        FiredProjectile = newuid
                    });
                    ShootOrThrow(newuid, angle.ToVec(), gunVelocity, gun, gunUid, user);
                    shotProjectiles.Add(newuid);
                }
            }
            else
            {
                ShootOrThrow(ammoEnt, mapDirection, gunVelocity, gun, gunUid, user);
                shotProjectiles.Add(ammoEnt);
            }

            SpreadBreak:
                MuzzleFlash(gunUid, ammoComp, mapDirection.ToAngle(), trueUser); // WWDP EDIT
                Audio.PlayPredicted(gun.SoundGunshotModified, gunUid, trueUser); // WWDP EDIT
        }
    }

    private void ShootOrThrow(EntityUid uid, Vector2 mapDirection, Vector2 gunVelocity, GunComponent gun, EntityUid gunUid, EntityUid? user)
    {
        if (gun.Target is { } target && !TerminatingOrDeleted(target))
        {
            var targeted = EnsureComp<TargetedProjectileComponent>(uid);
            targeted.Target = target;
            Dirty(uid, targeted);
        }

        // Do a throw
        if (!TryComp(uid, out ProjectileComponent? projectileComp))
        {
            RemoveShootable(uid);
            // TODO: Someone can probably yeet this a billion miles so need to pre-validate input somewhere up the call stack.
            // WD EDIT START
            if (gun.ThrowAngle.HasValue)
                EnsureComp<ThrowingAngleComponent>(uid).Angle = gun.ThrowAngle.Value;
            // WD EDIT END
            ThrowingSystem.TryThrow(uid, mapDirection, gun.ProjectileSpeedModified, user);
            RemComp<ThrowingAngleComponent>(uid); // WD EDIT
            return;
        }

        projectileComp.Damage *= gun.DamageModifier;
        if(gun.ShipWeapon) // WWDP EDIT
            _pvsOverride.AddGlobalOverride(uid); // WWDP EDIT
        ShootProjectile(uid, mapDirection, gunVelocity, gunUid, user, gun.ProjectileSpeedModified);
    }

    /// <summary>
    /// Gets a linear spread of angles between start and end.
    /// </summary>
    /// <param name="start">Start angle in degrees</param>
    /// <param name="end">End angle in degrees</param>
    /// <param name="intervals">How many shots there are</param>
    private Angle[] LinearSpread(Angle start, Angle end, int intervals)
    {
        var angles = new Angle[intervals];
        DebugTools.Assert(intervals > 1);

        for (var i = 0; i <= intervals - 1; i++)
        {
            angles[i] = new Angle(start + (end - start) * i / (intervals - 1));
        }

        return angles;
    }

    private Angle GetRecoilAngle(TimeSpan curTime, GunComponent component, Angle direction, EntityUid? user)
    {
        // WWDP EDIT START
        UpdateAngles(curTime, component);
        UpdateBonusAngles(curTime, component);
        // WWDP EDIT END
        // Convert it so angle can go either side.
        var random = Random.NextFloat(-0.5f, 0.5f) / _contests.MassContest(user);
        var spread = component.CurrentAngle.Theta * random;
        var angle = new Angle(direction.Theta + (component.CurrentAngle.Theta + component.BonusAngle.Theta) * random); // WWDP EDIT
        // DebugTools.Assert(spread <= component.MaxAngleModified.Theta); // WWDP
        return angle;
    }

    protected override void Popup(string message, EntityUid? uid, EntityUid? user) { }

    protected override void CreateEffect(EntityUid gunUid, MuzzleFlashEvent message, EntityUid? user = null)
    {
        var filter = Filter.Pvs(gunUid, entityManager: EntityManager);

        if (TryComp<ActorComponent>(user, out var actor))
            filter.RemovePlayer(actor.PlayerSession);

        RaiseNetworkEvent(message, filter);
    }

    public void PlayImpactSound(EntityUid otherEntity, DamageSpecifier? modifiedDamage, SoundSpecifier? weaponSound, bool forceWeaponSound)
    {
        DebugTools.Assert(!Deleted(otherEntity), "Impact sound entity was deleted");

        // Like projectiles and melee,
        // 1. Entity specific sound
        // 2. Ammo's sound
        // 3. Nothing
        var playedSound = false;

        if (!forceWeaponSound && modifiedDamage != null && modifiedDamage.GetTotal() > 0 && TryComp<RangedDamageSoundComponent>(otherEntity, out var rangedSound))
        {
            var type = SharedMeleeWeaponSystem.GetHighestDamageSound(modifiedDamage, ProtoManager);

            if (type != null && rangedSound.SoundTypes?.TryGetValue(type, out var damageSoundType) == true)
            {
                Audio.PlayPvs(damageSoundType, otherEntity, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
            else if (type != null && rangedSound.SoundGroups?.TryGetValue(type, out var damageSoundGroup) == true)
            {
                Audio.PlayPvs(damageSoundGroup, otherEntity, AudioParams.Default.WithVariation(DamagePitchVariation));
                playedSound = true;
            }
        }

        if (!playedSound && weaponSound != null)
        {
            Audio.PlayPvs(weaponSound, otherEntity);
        }
    }

    // TODO: Pseudo RNG so the client can predict these.
    #region Hitscan effects

    private void FireEffects(MapCoordinates fromCoordinates, float distance, Angle mapDirection, HitscanPrototype hitscan, EntityUid? hitEntity = null)
    {
        // Lord
        // Forgive me for the shitcode I am about to do // https://www.youtube.com/watch?v=sCAdVQNaDTE go fuck yourself
        // Effects tempt me not
        var sprites = new List<(MapCoordinates coordinates, Angle angle, SpriteSpecifier sprite, float scale)>();

        var angle = mapDirection;

        if (distance >= 1f)
        {
            if (hitscan.MuzzleFlash != null)
            {
                var coords = fromCoordinates.Offset(angle.ToVec().Normalized() / 2);

                sprites.Add((coords, angle, hitscan.MuzzleFlash, 1f));
            }

            if (hitscan.TravelFlash != null)
            {
                var coords = fromCoordinates.Offset(angle.ToVec() * (distance + 0.5f) / 2);

                sprites.Add((coords, angle, hitscan.TravelFlash, distance - 1.5f));
            }
        }

        if (hitscan.ImpactFlash != null)
        {
            var coords = fromCoordinates.Offset(angle.ToVec() * distance);

            sprites.Add((coords, angle.FlipPositive(), hitscan.ImpactFlash, 1f));
        }

        if (sprites.Count > 0)
        {
            RaiseNetworkEvent(new HitscanEvent
            {
                Sprites = sprites,
            }, Filter.Pvs(fromCoordinates));
        }
    }

    #endregion
}
