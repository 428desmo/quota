using System.IO;
using UnityEngine;

namespace Quota
{
    public static class GuideCopy
    {
        public static string Title(string page) => page == "details" ? "詳細ルール" : "遊び方";
        public static string Body(string page)
        {
            var name = page == "details" ? "detailed_rule_v1.0" : "how_to_play_v1.0";
            var asset = Resources.Load<TextAsset>("QuotaWebGenerated/" + name);
            string text;
            if (asset != null) text = asset.text;
            else
            {
                var source = Path.GetFullPath(Path.Combine(Application.dataPath, "../../" + name + ".txt"));
                if (!File.Exists(source)) source = Path.Combine(Application.streamingAssetsPath, name + ".txt");
                text = File.ReadAllText(source);
            }
            return text.Replace("**", "");
        }
    }
}
