namespace Content.Shared._Shitmed.Targeting;


/// <summary>
/// Represents and enum of possible target parts.
/// </summary>
/// <remarks>
/// To get all body parts as an Array, use static
/// method in SharedTargetingSystem GetValidParts.
/// </remarks>
[Flags]
public enum TargetBodyPart : ushort
{
    Head = 1,
    Torso = 1 << 1,
    Groin = 1 << 2,
    LeftArm = 1 << 3,
    LeftHand = 1 << 4,
    RightArm = 1 << 5,
    RightHand = 1 << 6,
    LeftLeg = 1 << 7,
    LeftFoot = 1 << 8,
    RightLeg = 1 << 9,
    RightFoot = 1 << 10,

    Hands = LeftHand | RightHand,
    Arms = LeftArm | RightArm,
    Legs = LeftLeg | RightLeg,
    Feet = LeftFoot | RightFoot,

    // WWDP edit; more groups for Armor ProtectedArea
    Body = Torso | Groin,
    UpperBody = Torso | LeftArm | RightArm,
    UpperBodyHands = Torso | LeftArm | RightArm | LeftHand | RightHand,
    LowerBody = Groin | LeftLeg | RightLeg,
    LowerBodyFeet = Groin | LeftLeg | RightLeg | LeftFoot | RightFoot,
    FullBody = Torso | Groin | LeftArm | RightArm | LeftLeg | RightLeg,
    FullBodyHandsFeet = Torso | Groin | LeftArm | RightArm | LeftHand | RightHand | LeftLeg | RightLeg | LeftFoot | RightFoot,
    // WWDP edit end

    All = Head | Torso | Groin | LeftArm | LeftHand | RightArm | RightHand | LeftLeg | LeftFoot | RightLeg | RightFoot,
}
