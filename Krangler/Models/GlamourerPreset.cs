using System.Collections.Generic;

namespace Krangler.Models;

public class GlamourerPreset
{
    public int FileVersion { get; set; }
    public string Identifier { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool ForcedRedraw { get; set; }
    public Dictionary<string, EquipmentSlotData> Equipment { get; set; } = new();
    public Dictionary<string, BonusItemData> Bonus { get; set; } = new();
    public CustomizeData Customize { get; set; } = new();
}

public class EquipmentSlotData
{
    public ulong ItemId { get; set; }
    public uint Stain { get; set; }
    public uint Stain2 { get; set; }
    public bool Apply { get; set; }
    public bool ApplyStain { get; set; }
    public bool Crest { get; set; }
    public bool ApplyCrest { get; set; }
    // Hat/Visor/Weapon toggle fields
    public bool Show { get; set; }
    public bool IsToggled { get; set; }
}

public class BonusItemData
{
    public uint BonusId { get; set; }
    public bool Apply { get; set; }
}

public class CustomizeData
{
    public int ModelId { get; set; }
    public CustomValue Race { get; set; } = new();
    public CustomValue Gender { get; set; } = new();
    public CustomValue BodyType { get; set; } = new();
    public CustomValue Height { get; set; } = new();
    public CustomValue Clan { get; set; } = new();
    public CustomValue Face { get; set; } = new();
    public CustomValue Hairstyle { get; set; } = new();
    public CustomValue Highlights { get; set; } = new();
    public CustomValue SkinColor { get; set; } = new();
    public CustomValue EyeColorRight { get; set; } = new();
    public CustomValue HairColor { get; set; } = new();
    public CustomValue HighlightsColor { get; set; } = new();
    public CustomValue FacialFeature1 { get; set; } = new();
    public CustomValue FacialFeature2 { get; set; } = new();
    public CustomValue FacialFeature3 { get; set; } = new();
    public CustomValue FacialFeature4 { get; set; } = new();
    public CustomValue FacialFeature5 { get; set; } = new();
    public CustomValue FacialFeature6 { get; set; } = new();
    public CustomValue FacialFeature7 { get; set; } = new();
    public CustomValue LegacyTattoo { get; set; } = new();
    public CustomValue TattooColor { get; set; } = new();
    public CustomValue Eyebrows { get; set; } = new();
    public CustomValue EyeColorLeft { get; set; } = new();
    public CustomValue EyeShape { get; set; } = new();
    public CustomValue SmallIris { get; set; } = new();
    public CustomValue Nose { get; set; } = new();
    public CustomValue Jaw { get; set; } = new();
    public CustomValue Mouth { get; set; } = new();
    public CustomValue Lipstick { get; set; } = new();
    public CustomValue LipColor { get; set; } = new();
    public CustomValue MuscleMass { get; set; } = new();
    public CustomValue TailShape { get; set; } = new();
    public CustomValue BustSize { get; set; } = new();
    public CustomValue FacePaint { get; set; } = new();
    public CustomValue FacePaintReversed { get; set; } = new();
    public CustomValue FacePaintColor { get; set; } = new();
}

public class CustomValue
{
    public byte Value { get; set; }
    public bool Apply { get; set; }
}
