using System.Collections.Generic;
using System.Reflection;

namespace ACViewer.Entity
{
    public class NameFilterLanguageData
    {
        public ACE.DatLoader.Entity.NameFilterLanguageData _nameFilterLanguageData;

        public NameFilterLanguageData(ACE.DatLoader.Entity.NameFilterLanguageData nameFilterLanguageData)
        {
            _nameFilterLanguageData = nameFilterLanguageData;
        }

        public List<TreeNode> BuildTree()
        {
            var treeNode = new List<TreeNode>();

            treeNode.Add(new TreeNode($"MaximumVowelsInARow: {_nameFilterLanguageData.MaximumVowelsInARow}"));
            treeNode.Add(new TreeNode($"FirstNCharactersMustHaveAVowel: {_nameFilterLanguageData.FirstNCharactersMustHaveAVowel}"));
            treeNode.Add(new TreeNode($"VowelContainingSubstringLength: {_nameFilterLanguageData.VowelContainingSubstringLength}"));
            treeNode.Add(new TreeNode($"ExtraAllowedCharacters: {_nameFilterLanguageData.ExtraAllowedCharacters}"));

            // Some ACE revisions removed / renamed the 'Unknown' field. Access via reflection if present.
            try
            {
                var prop = _nameFilterLanguageData.GetType().GetProperty("Unknown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null)
                {
                    var val = prop.GetValue(_nameFilterLanguageData, null);
                    treeNode.Add(new TreeNode($"Unknown: {val}"));
                }
            }
            catch { /* ignore */ }

            var compoundLetterGroups = new TreeNode($"CompoundLetterGroups");
            foreach (var compoundLetterGroup in _nameFilterLanguageData.CompoundLetterGroups)
                compoundLetterGroups.Items.Add(new TreeNode(compoundLetterGroup));
            treeNode.Add(compoundLetterGroups);

            return treeNode;
        }
    }
}
