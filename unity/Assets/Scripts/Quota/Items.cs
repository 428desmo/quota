using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Quota
{
    public sealed class ItemFace
    {
        public readonly string KindId;
        public readonly string Name;
        public readonly string Emoji;
        public readonly string Color;

        public ItemFace(string kindId, string name, string emoji, string color)
        {
            KindId = kindId;
            Name = name;
            Emoji = emoji;
            Color = color;
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
        static ItemSet[] sets;

        public static string DefaultPath =>
            Path.Combine(Application.streamingAssetsPath, "quota_item_sets_v1.0.json");

        public static IReadOnlyList<ItemSet> Sets
        {
            get
            {
                EnsureLoaded();
                return sets;
            }
        }

        public static void Load(string path)
        {
            var text = File.ReadAllText(path);
            text = Regex.Replace(text, "\"default\"", "\"isDefault\"");
            var raw = JsonUtility.FromJson<CatalogFile>(text);
            if (raw == null || raw.item_sets == null) throw new InvalidOperationException("item set catalog is empty");
            var parsed = new ItemSet[raw.item_sets.Length];
            var seen = new HashSet<string>();
            var defaults = 0;
            for (var i = 0; i < raw.item_sets.Length; i++)
            {
                parsed[i] = Parse(raw.item_sets[i]);
                if (!seen.Add(parsed[i].Id)) throw new InvalidOperationException("item set ids must be unique");
                if (parsed[i].IsDefault) defaults++;
            }
            if (defaults != 1) throw new InvalidOperationException("item set catalog must have exactly one default");
            sets = parsed;
        }

        public static ItemSet Default()
        {
            EnsureLoaded();
            foreach (var item in sets)
                if (item.IsDefault) return item;
            throw new InvalidOperationException("item set catalog must have exactly one default");
        }

        public static ItemSet Resolve(string key)
        {
            EnsureLoaded();
            var needle = (key ?? "").Trim();
            foreach (var item in sets)
                if (item.Id == needle || item.Name == needle) return item;
            var names = new List<string>();
            foreach (var item in sets) names.Add($"{item.Id}（{item.Name}）");
            throw new ArgumentException($"未知のアイテムセットです: {key}。選べるのは {string.Join("、", names)}");
        }

        static void EnsureLoaded()
        {
            if (sets == null) Load(DefaultPath);
        }

        static ItemSet Parse(ItemSetRaw raw)
        {
            var faces = new Dictionary<string, ItemFace>();
            foreach (var kind in raw.kinds)
                faces[kind.id] = new ItemFace(kind.id, kind.name, kind.emoji, kind.color);
            faces[raw.wild.id] = new ItemFace(raw.wild.id, raw.wild.name, raw.wild.emoji, raw.wild.color);
            if (raw.rank_labels == null || raw.rank_labels.Length != 13)
                throw new InvalidOperationException($"{raw.id}: rank_labels must have 13 entries");
            var expected = new HashSet<string> { "K1", "K2", "K3", "K4", "WILD" };
            if (!expected.SetEquals(faces.Keys))
                throw new InvalidOperationException($"{raw.id}: kinds must be K1..K4 and WILD");
            return new ItemSet(raw.id, raw.name, raw.description, raw.isDefault, faces, raw.rank_labels, raw.wild_rank_label);
        }

        [Serializable]
        class CatalogFile
        {
            public ItemSetRaw[] item_sets;
        }

        [Serializable]
        class ItemSetRaw
        {
            public string id;
            public string name;
            public string description;
            public bool isDefault;
            public KindRaw[] kinds;
            public KindRaw wild;
            public string[] rank_labels;
            public string wild_rank_label;
        }

        [Serializable]
        class KindRaw
        {
            public string id;
            public string name;
            public string emoji;
            public string color;
        }
    }
}
