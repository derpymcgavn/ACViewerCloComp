using System;
using System.Globalization;
using System.Text.RegularExpressions;
using ACViewer.Utilities;

namespace ACViewer.MobBuilder
{
    public static class MobBuilderSqlParser
    {
        private static readonly Regex WeenieRegex = new(@"INSERT\s+INTO\s+`weenie`[\s\S]*?VALUES\s*\(\s*(?<id>\d+)\s*,\s*'(?<class>[^']*)'\s*,\s*(?<type>-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex StringNameRegex = new(@"\(\s*(?<id>\d+)\s*,\s*1\s*,\s*'(?<name>[^']*)'\s*\)\s*/\*\s*Name\s*\*/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex DidRegex = new(@"\(\s*(?<id>\d+)\s*,\s*(?<type>\d+)\s*,\s*(?<value>0x[0-9A-Fa-f]+|\d+)\s*\)\s*/\*\s*(?<comment>[^*]*)\*/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex IntRegex = new(@"\(\s*(?<id>\d+)\s*,\s*(?<type>\d+)\s*,\s*(?<value>-?\d+)\s*\)\s*/\*\s*(?<comment>[^*]*)\*/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FloatRegex = new(@"\(\s*(?<id>\d+)\s*,\s*(?<type>\d+)\s*,\s*(?<value>-?\d+(?:\.\d+)?)\s*\)\s*/\*\s*(?<comment>[^*]*)\*/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TextureRegex = new(@"\(\s*(?<id>\d+)\s*,\s*(?<index>\d+)\s*,\s*(?<old>0x[0-9A-Fa-f]+|\d+)\s*,\s*(?<new>0x[0-9A-Fa-f]+|\d+)\s*\)\s*/\*\s*(?<part>[^*]*)\*/", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AnimPartRegex = new(@"\(\s*(?<id>\d+)\s*,\s*(?<index>\d+)\s*,\s*(?<anim>0x[0-9A-Fa-f]+|\d+)\s*\)\s*/\*\s*(?<part>[^*]*)\*/", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static MobBuilderDefinition Parse(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new ArgumentException("SQL file was empty.");

            var def = new MobBuilderDefinition();
            var weenie = WeenieRegex.Match(sql);
            if (weenie.Success)
            {
                var id = uint.Parse(weenie.Groups["id"].Value, CultureInfo.InvariantCulture);
                def.WeenieClassId = HexId.Format(id);
                def.CloneFromWcid = HexId.Format(id);
            }

            var name = StringNameRegex.Match(sql);
            if (name.Success)
                def.Name = name.Groups["name"].Value;

            var didBlock = ExtractBlock(sql, "weenie_properties_d_i_d");
            foreach (Match match in DidRegex.Matches(didBlock))
            {
                var type = int.Parse(match.Groups["type"].Value, CultureInfo.InvariantCulture);
                var value = Normalize(match.Groups["value"].Value);
                switch (type)
                {
                    case 1: def.DisplaySetupId = value; break;
                    case 2: def.MotionTableId = value; break;
                    case 3: def.SoundTableId = value; break;
                }
            }

            var intBlock = ExtractBlock(sql, "weenie_properties_int");
            foreach (Match match in IntRegex.Matches(intBlock))
            {
                var type = int.Parse(match.Groups["type"].Value, CultureInfo.InvariantCulture);
                var value = int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
                var comment = match.Groups["comment"].Value.Trim();
                switch (type)
                {
                    case 2: def.CreatureType = CommentValue(comment); break;
                    case 3: def.PaletteBaseId = value == 0 ? "0x00000000" : value.ToString(CultureInfo.InvariantCulture); break;
                    case 25: def.Level = value; break;
                    case 307: def.DamageRating = value; break;
                }
            }

            var floatBlock = ExtractBlock(sql, "weenie_properties_float");
            foreach (Match match in FloatRegex.Matches(floatBlock))
            {
                var type = int.Parse(match.Groups["type"].Value, CultureInfo.InvariantCulture);
                var value = float.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
                switch (type)
                {
                    case 12: def.Shade = value; break;
                    case 39: def.Scale = value; break;
                }
            }

            var secondAttrBlock = ExtractBlock(sql, "weenie_properties_attribute_2nd");
            var secondAttrRegex = new Regex(@"\(\s*\d+\s*,\s*(?<type>\d+)\s*,\s*(?<init>-?\d+)", RegexOptions.IgnoreCase);
            foreach (Match match in secondAttrRegex.Matches(secondAttrBlock))
            {
                var type = int.Parse(match.Groups["type"].Value, CultureInfo.InvariantCulture);
                var value = int.Parse(match.Groups["init"].Value, CultureInfo.InvariantCulture);
                switch (type)
                {
                    case 1: def.MaxHealth = value; break;
                    case 3: def.MaxStamina = value; break;
                    case 5: def.MaxMana = value; break;
                }
            }

            var skillBlock = ExtractBlock(sql, "weenie_properties_skill");
            var skillRegex = new Regex(@"\(\s*\d+\s*,\s*(?<type>\d+)\s*,\s*\d+\s*,\s*\d+\s*,\s*\d+\s*,\s*(?<level>-?\d+)[^)]*\)\s*/\*\s*(?<name>[^*]*)\*/", RegexOptions.IgnoreCase);
            def.Skills.Clear();
            foreach (Match match in skillRegex.Matches(skillBlock))
                def.Skills.Add(new MobBuilderSkill { Name = match.Groups["name"].Value.Replace("Trained", string.Empty).Trim(), Value = int.Parse(match.Groups["level"].Value, CultureInfo.InvariantCulture) });

            var spellBlock = ExtractBlock(sql, "weenie_properties_spell_book");
            var spellRegex = new Regex(@"\(\s*\d+\s*,\s*(?<spell>\d+)\s*,\s*(?<prob>-?\d+(?:\.\d+)?)\s*\)", RegexOptions.IgnoreCase);
            def.Spells.Clear();
            foreach (Match match in spellRegex.Matches(spellBlock))
                def.Spells.Add(new MobBuilderSpell { SpellId = match.Groups["spell"].Value, Chance = double.Parse(match.Groups["prob"].Value, CultureInfo.InvariantCulture) });

            var textureBlock = ExtractBlock(sql, "weenie_properties_texture_map");
            def.TextureMaps.Clear();
            foreach (Match match in TextureRegex.Matches(textureBlock))
                def.TextureMaps.Add(new MobBuilderTextureMap
                {
                    Index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture),
                    OldId = Normalize(match.Groups["old"].Value),
                    NewId = Normalize(match.Groups["new"].Value),
                    Part = match.Groups["part"].Value.Trim()
                });

            var animBlock = ExtractBlock(sql, "weenie_properties_anim_part");
            def.AnimParts.Clear();
            foreach (Match match in AnimPartRegex.Matches(animBlock))
                def.AnimParts.Add(new MobBuilderAnimPart
                {
                    Index = int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture),
                    AnimationId = Normalize(match.Groups["anim"].Value),
                    Part = match.Groups["part"].Value.Trim()
                });

            def.Notes = "Imported from ACE weenie SQL. Visual rows came from weenie_properties_texture_map and weenie_properties_anim_part.";
            return def;
        }

        private static string ExtractBlock(string sql, string table)
        {
            var pattern = $@"INSERT\s+INTO\s+`{Regex.Escape(table)}`[\s\S]*?;";
            var match = Regex.Match(sql, pattern, RegexOptions.IgnoreCase);
            return match.Success ? match.Value : string.Empty;
        }

        private static string Normalize(string text) => HexId.Format(HexId.Parse(text));

        private static string CommentValue(string comment)
        {
            var dash = comment.IndexOf('-');
            return dash >= 0 ? comment[(dash + 1)..].Trim() : comment.Trim();
        }
    }
}
