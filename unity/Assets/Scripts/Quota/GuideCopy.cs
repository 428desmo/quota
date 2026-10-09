using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Quota
{
    public static class GuideCopy
    {
        public enum BlockKind { Section, Paragraph, Bullet, Table }

        public sealed class Block
        {
            public BlockKind Kind;
            public string Text;
            public string Note;
            public List<string[]> Rows;
        }

        public static string Title(string page) => page == "details" ? "詳細ルール" : "遊び方";

        public static List<Block> Blocks(string page)
        {
            var lines = Body(page).Split('\n');
            var blocks = new List<Block>();
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line == "遊び方" || line == "ルールブック") continue;
                if (line.StartsWith("【") && line.EndsWith("】"))
                {
                    blocks.Add(new Block { Kind = BlockKind.Section, Text = line.Substring(1, line.Length - 2) });
                    continue;
                }
                if (line == "（6枚以下はボーナスなし）" && blocks.Count > 0 && blocks[blocks.Count - 1].Kind == BlockKind.Bullet)
                {
                    blocks[blocks.Count - 1].Note = line;
                    continue;
                }
                if (line.StartsWith("|"))
                {
                    var rows = new List<string[]>();
                    while (i < lines.Length && lines[i].TrimStart().StartsWith("|"))
                    {
                        var cells = lines[i].Trim().Trim('|').Split('|');
                        if (cells.Length == 2 && !cells[0].Trim().StartsWith("---"))
                            rows.Add(new[] { cells[0].Trim(), cells[1].Trim() });
                        i++;
                    }
                    i--;
                    if (blocks.Count > 0 && blocks[blocks.Count - 1].Kind == BlockKind.Bullet)
                        blocks[blocks.Count - 1].Rows = rows;
                    else blocks.Add(new Block { Kind = BlockKind.Table, Rows = rows });
                    continue;
                }
                if (lines[i].StartsWith("  •\u00a0") && line.Contains(":"))
                {
                    var rows = new List<string[]> { new[] { "称号", "条件・得点" } };
                    while (i < lines.Length && lines[i].StartsWith("  •\u00a0"))
                    {
                        var entry = lines[i].Trim().Substring(2);
                        var split = entry.IndexOf(':');
                        if (split > 0) rows.Add(new[] { entry.Substring(0, split).Trim(), entry.Substring(split + 1).Trim() });
                        i++;
                    }
                    i--;
                    if (blocks.Count > 0 && blocks[blocks.Count - 1].Kind == BlockKind.Bullet)
                        blocks[blocks.Count - 1].Rows = rows;
                    else blocks.Add(new Block { Kind = BlockKind.Table, Rows = rows });
                    continue;
                }
                blocks.Add(new Block { Kind = line.StartsWith("•\u00a0") ? BlockKind.Bullet : BlockKind.Paragraph,
                    Text = line.StartsWith("•\u00a0") ? line.Substring(2) : line });
            }
            return blocks;
        }

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
