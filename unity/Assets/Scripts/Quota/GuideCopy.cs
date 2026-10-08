using System.IO;
using System.Text;
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
            var formatted = new StringBuilder();
            using (var lines = new StringReader(text.Replace("**", "")))
            {
                string line;
                while ((line = lines.ReadLine()) != null)
                {
                    if (formatted.Length > 0) formatted.Append('\n');
                    var indent = 0;
                    while (indent < line.Length && line[indent] == ' ') indent++;
                    if (line.Length >= indent + 2 && line[indent] == '-' && line[indent + 1] == ' ')
                        formatted.Append(line.Substring(0, indent)).Append("•\u00a0").Append(line.Substring(indent + 2));
                    else formatted.Append(line);
                }
            }
            return formatted.ToString();
        }
    }
}
