using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Quota
{
    public sealed class ItemFace
    {
        public readonly string KindId;
        public readonly string Name;
        public readonly string Emoji;
        public readonly string Color;
        public readonly string File;

        public ItemFace(string kindId, string name, string emoji, string color, string file)
        {
            KindId = kindId;
            Name = name;
            Emoji = emoji;
            Color = color;
            File = file;
        }
    }

    public sealed class ItemSet
    {
        public readonly string Id;
        public readonly string Name;
        public readonly string Description;
        public readonly bool IsDefault;
        public readonly Dictionary<string, ItemFace> Faces;
        public readonly string[] RankLabels;
        public readonly string WildRankLabel;

        public ItemSet(string id, string name, string description, bool isDefault, Dictionary<string, ItemFace> faces, string[] rankLabels, string wildRankLabel)
        {
            Id = id;
            Name = name;
            Description = description;
            IsDefault = isDefault;
            Faces = faces;
            RankLabels = rankLabels;
            WildRankLabel = wildRankLabel;
        }

        public ItemFace FaceFor(Card card)
        {
            return Faces[KindOf(card.Suit)];
        }

        public string RankLabel(Card card)
        {
            if (card.Rank == null) return WildRankLabel;
            return RankLabels[card.Rank.Value - 1];
        }

        public string Label(Card card)
        {
            var face = FaceFor(card);
            return $"{face.Emoji}{RankLabel(card)} {face.Name} #{card.Id}";
        }

        public static string KindOf(Suit suit)
        {
            switch (suit)
            {
                case Suit.S: return "K1";
                case Suit.H: return "K2";
                case Suit.C: return "K3";
                case Suit.D: return "K4";
                case Suit.Joker: return "WILD";
                default: throw new ArgumentOutOfRangeException(nameof(suit), suit, null);
            }
        }
    }

    public static class ItemCatalog
    {
        static readonly string[] SlotKinds = { "K1", "K2", "K3", "K4" };
        static readonly string[] SlotColors = { "#A0522D", "#7B3FA0", "#2E7D32", "#1E5AA8" };
        const string WildColor = "#C4A035";
        static readonly string[] RankLabels =
        {
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12", "13",
        };

        static GoodRaw[] goods;
        static GoodRaw wild;

        public static string GoodsPath =>
            Path.Combine(Application.streamingAssetsPath, "quota_goods_v1.0.json");

        public static int Count
        {
            get
            {
                EnsureLoaded();
                return goods.Length;
            }
        }

        public static void Load(string path)
        {
            var raw = JsonUtility.FromJson<GoodsFile>(File.ReadAllText(path));
            if (raw == null || raw.goods == null || raw.goods.Length != 27)
                throw new InvalidOperationException("trade goods must be numbered 1 through 27");
            if (raw.wild == null || raw.wild.name != "金貨")
                throw new InvalidOperationException("wild card must be 金貨");
            for (var i = 0; i < raw.goods.Length; i++)
            {
                if (raw.goods[i].number != i + 1)
                    throw new InvalidOperationException("trade goods must be numbered 1 through 27");
            }
            goods = raw.goods;
            wild = raw.wild;
        }

        public static ItemSet Theme(int[] indices)
        {
            EnsureLoaded();
            if (indices == null || indices.Length != 4)
                throw new ArgumentException("a game needs four distinct goods");
            var faces = new Dictionary<string, ItemFace>();
            var seen = new HashSet<int>();
            for (var i = 0; i < 4; i++)
            {
                var index = indices[i];
                if (index < 0 || index >= goods.Length || !seen.Add(index))
                    throw new ArgumentException("a game needs four distinct goods");
                var good = goods[index];
                faces[SlotKinds[i]] = new ItemFace(SlotKinds[i], good.name, "", SlotColors[i], good.file);
            }
            faces["WILD"] = new ItemFace("WILD", wild.name, "", WildColor, wild.file);
            return new ItemSet("trade", "交易品", "交易品。対局ごとに4品目。", true, faces, RankLabels, "＊");
        }

        public static ItemSet Default()
        {
            return Theme(new[] { 0, 1, 2, 3 });
        }

        public static ItemSet Resolve(string key)
        {
            var needle = (key ?? "").Trim();
            if (needle == "" || needle == "trade" || needle == "交易品") return Default();
            throw new ArgumentException("アイテムセットは交易品だけです");
        }

        public static string NameAt(int index)
        {
            EnsureLoaded();
            return goods[index].name;
        }

        static void EnsureLoaded()
        {
            if (goods == null) Load(GoodsPath);
        }

        [Serializable]
        class GoodsFile
        {
            public GoodRaw[] goods;
            public GoodRaw wild;
        }

        [Serializable]
        class GoodRaw
        {
            public int number;
            public string name;
            public string file;
        }
    }
}
