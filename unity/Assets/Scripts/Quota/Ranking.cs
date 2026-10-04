using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quota
{
    /// <summary>
    /// Places read from the rating file. Keyed by the strategy dimensions, so a
    /// change to the naming words or to the packed id keeps the file usable.
    /// </summary>
    public static class Ranking
    {
        public const string FileName = "cpu_ranking_v1.0.json";

        const string Format = "quota-cpu-ranking";
        const int Version = 1;

        [Serializable]
        class Sheet
        {
            public string format;
            public int version;
            public string[] dimensions;
            public int[] sizes;
            public int provisional;
            public Row[] players;
        }

        [Serializable]
        class Row
        {
            public int[] id;
            public float rating;
            public int games;
            public float points;
            public int rank;
        }

        static Dictionary<string, int> places;
        static int provisional;
        static bool tried;

        public static bool IsLoaded => places != null;

        public static string Path =>
            System.IO.Path.Combine(Application.streamingAssetsPath, FileName);

        public static void LoadJson(string json)
        {
            tried = true;
            if (string.IsNullOrEmpty(json)) return;
            var sheet = JsonUtility.FromJson<Sheet>(json);
            if (sheet == null || sheet.format != Format || sheet.version != Version) return;
            if (sheet.players == null) return;
            var table = new Dictionary<string, int>(sheet.players.Length);
            foreach (var row in sheet.players)
            {
                if (row.id == null || row.rank <= 0) continue;
                table[Characters.KeyOf(row.id)] = row.rank;
            }
            places = table;
            provisional = sheet.provisional;
        }

        public static void Forget()
        {
            places = null;
            provisional = 0;
            tried = false;
        }

        /// <summary>A place of 0 means the file is missing, so no place is shown.</summary>
        public static int PlaceOf(int characterId)
        {
            EnsureLoaded();
            if (places == null) return 0;
            int place;
            if (places.TryGetValue(Characters.KeyOf(characterId), out place) && place > 0) return place;
            return provisional;
        }

        public static string DisplayName(int characterId)
        {
            var name = Characters.NameOf(characterId);
            var place = PlaceOf(characterId);
            return place > 0 ? $"{name}({place})" : name;
        }

        static void EnsureLoaded()
        {
            if (tried) return;
            tried = true;
            if (Application.platform == RuntimePlatform.WebGLPlayer) return;
            try
            {
                if (System.IO.File.Exists(Path)) LoadJson(System.IO.File.ReadAllText(Path));
            }
            catch (System.IO.IOException)
            {
            }
        }
    }
}
