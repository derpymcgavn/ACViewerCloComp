using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ACViewer.MobBuilder
{
    public sealed class MobBuilderDefinition
    {
        public string Schema { get; set; } = "DerpACE.MobBuilder.v1";
        public string Name { get; set; } = "New Mob";
        public string WeenieClassId { get; set; } = "0x00000000";
        public string CloneFromWcid { get; set; } = "0x00000000";
        public string WeenieType { get; set; } = "Creature";
        public string CreatureType { get; set; } = "Unknown";
        public string DisplaySetupId { get; set; } = "0x02000001";
        public string MotionTableId { get; set; } = "0x09000001";
        public string SoundTableId { get; set; } = "0x20000000";
        public string PaletteBaseId { get; set; } = "0x0400007E";
        public float Shade { get; set; } = 0.0f;
        public double Scale { get; set; } = 1.0;
        public int Level { get; set; } = 1;
        public int MaxHealth { get; set; } = 100;
        public int MaxStamina { get; set; } = 100;
        public int MaxMana { get; set; } = 0;
        public int ArmorLevel { get; set; } = 0;
        public int DamageRating { get; set; } = 0;
        public int AttackSkill { get; set; } = 100;
        public int DefenseSkill { get; set; } = 100;
        public int MagicDefenseSkill { get; set; } = 100;
        public string AiProfile { get; set; } = "Melee";
        public string Faction { get; set; } = string.Empty;
        public string Tolerance { get; set; } = "Aggressive";
        public List<MobBuilderTextureMap> TextureMaps { get; set; } = new();
        public List<MobBuilderAnimPart> AnimParts { get; set; } = new();
        public List<MobBuilderSkill> Skills { get; set; } = new();
        public List<MobBuilderSpell> Spells { get; set; } = new();
        public List<MobBuilderLootEntry> Loot { get; set; } = new();
        public List<MobBuilderSpawnEntry> Spawns { get; set; } = new();
        public Dictionary<string, string> DerpAceProperties { get; set; } = new();
        public string Notes { get; set; } = string.Empty;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public sealed class MobBuilderTextureMap
    {
        public int Index { get; set; }
        public string OldId { get; set; } = "0x00000000";
        public string NewId { get; set; } = "0x00000000";
        public string Part { get; set; } = string.Empty;
    }

    public sealed class MobBuilderAnimPart
    {
        public int Index { get; set; }
        public string AnimationId { get; set; } = "0x00000000";
        public string Part { get; set; } = string.Empty;
    }

    public sealed class MobBuilderSkill
    {
        public string Name { get; set; } = "MeleeDefense";
        public int Value { get; set; } = 100;
    }

    public sealed class MobBuilderSpell
    {
        public string SpellId { get; set; } = "0x00000000";
        public double Chance { get; set; } = 1.0;
    }

    public sealed class MobBuilderLootEntry
    {
        public string WeenieClassId { get; set; } = "0x00000000";
        public double Chance { get; set; } = 1.0;
        public int Min { get; set; } = 1;
        public int Max { get; set; } = 1;
    }

    public sealed class MobBuilderSpawnEntry
    {
        public string Landblock { get; set; } = "0x0000";
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public int Count { get; set; } = 1;
        public double RespawnSeconds { get; set; } = 300;
    }
}


