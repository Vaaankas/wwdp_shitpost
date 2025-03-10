using Robust.Shared.Random;


namespace Content.Shared._Shitmed.Targeting;
public abstract class SharedTargetingSystem : EntitySystem
{
    [Dependency] protected readonly IRobustRandom _random = default!; // WWDP

    /// <summary>
    /// Returns all Valid target body parts as an array.
    /// </summary>
    public static TargetBodyPart[] GetValidParts()
    {
        var parts = new[]
        {
            TargetBodyPart.Head,
            TargetBodyPart.Torso,
            //TargetBodyPart.Groin,
            TargetBodyPart.LeftArm,
            TargetBodyPart.LeftHand,
            TargetBodyPart.LeftLeg,
            TargetBodyPart.LeftFoot,
            TargetBodyPart.RightArm,
            TargetBodyPart.RightHand,
            TargetBodyPart.RightLeg,
            TargetBodyPart.RightFoot,
        };

        return parts;
    }

    // WWDP
    public TargetBodyPart GetRandomBodyPart(TargetBodyPart? bodyPart = null)
    {
        var parts = GetValidParts();

        _random.Shuffle(parts);

        if (bodyPart == null)
            return parts[0];

        foreach (var part in parts)
        {
            if (bodyPart.Value.HasFlag(part))
            {
                Log.Debug($"Rolled body part {part.ToString()}");
                return part;
            }
        }
        Log.Debug("Rolled body part null, defaulting to torso");
        return TargetBodyPart.Torso;
    }
}
