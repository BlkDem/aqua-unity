// Settings remembered between launches (the web version's localStorage "aq.settings").
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Aquarium.App
{
    public static class Prefs
    {
        const string Prefix = "aq.";

        public static float Get(string key, float fallback) => PlayerPrefs.GetFloat(Prefix + key, fallback);
        public static string Get(string key, string fallback) => PlayerPrefs.GetString(Prefix + key, fallback);
        public static bool Has(string key) => PlayerPrefs.HasKey(Prefix + key);
        public static void Set(string key, float v) { PlayerPrefs.SetFloat(Prefix + key, v); PlayerPrefs.Save(); }
        public static void Set(string key, string v) { PlayerPrefs.SetString(Prefix + key, v); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// Copies of the aquarium in a folder of their own (Documents/Живой аквариум on a computer):
    /// the same file as the web version's «Сохранить в файл», so a copy moves either way.
    /// </summary>
    public static class Backup
    {
        public static string Dir
        {
            get
            {
                bool desktop = Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor
                    || Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor
                    || Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor;
                string docs = desktop ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : null;
                return string.IsNullOrEmpty(docs) ? Path.Combine(Application.persistentDataPath, "backups") : Path.Combine(docs, "Живой аквариум");
            }
        }

        public static string Save(string json)
        {
            Directory.CreateDirectory(Dir);
            var now = DateTime.Now;
            string path = Path.Combine(Dir, $"аквариум-{now:yyyy-MM-dd}.json");
            if (File.Exists(path)) path = Path.Combine(Dir, $"аквариум-{now:yyyy-MM-dd-HHmm}.json");
            File.WriteAllText(path, json);
            return path;
        }

        /// <summary>The copies in the folder that really are aquariums: path, number of fish, when saved.</summary>
        public static List<(string path, int fish, DateTime when)> List()
        {
            var list = new List<(string, int, DateTime)>();
            if (!Directory.Exists(Dir)) return list;
            foreach (var f in Directory.GetFiles(Dir, "*.json").OrderByDescending(File.GetLastWriteTime).Take(20))
            {
                try
                {
                    var st = Aquarium.Life.World.Parse(File.ReadAllText(f));
                    if (st != null && st.Fish.Count > 0) list.Add((f, st.Fish.Count, File.GetLastWriteTime(f)));
                }
                catch (IOException) { }
            }
            return list;
        }
    }
}
