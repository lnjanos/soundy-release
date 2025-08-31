using ECommons;
using Newtonsoft.Json;
using Soundy.FileAnalyzer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ECommons.DalamudServices;
using VfxEditor.AvfxFormat;
using Dalamud.Utility;

namespace Soundy.Vfx
{
    public static class VfxManager
    {
        /// <summary>
        /// Sucht im angegebenen Verzeichnis (und optional in Unterordnern) nach PAP-Dateien,
        /// lädt diese und extrahiert die zugewiesenen SCDs aus den Animationen.
        /// </summary>
        /// <param name="modDirPath">Pfad zum Mod-Ordner (z. B. Penumbra Mod Pfad)</param>
        /// <returns>Liste gruppierter PAP-Einträge mit den extrahierten SCD-Details.</returns>
        public class GroupedPap2Entry
        {
            /// <summary>
            /// Enthält den in den JSONs gefundenen PAP-Pfad.
            /// </summary>
            public string Pap2Path { get; set; } = "";

            /// <summary>
            /// Alle Referenzen aus den JSONs (Dateiname + OptionName), die auf diesen PAP verweisen.
            /// </summary>
            public List<Pap2Reference> References { get; set; } = new List<Pap2Reference>();

            /// <summary>
            /// Hier werden zusätzlich die in der PAP-Datei gefundenen SCD-Einträge abgelegt.
            /// </summary>
            public List<Pap2ScdDetail> ScdDetails { get; set; } = new List<Pap2ScdDetail>();

            /// <summary>
            /// Für das UI, ob dieser Eintrag selektiert wurde.
            /// </summary>
            public bool Selected { get; set; } = false;
        }

        public class Pap2Reference
        {
            public string JsonFile { get; set; } = "";
            public string OptionName { get; set; } = "";
            public string GroupName { get; set; } = "";
        }

        public class Pap2ScdDetail
        {
            /// <summary>
            /// Der in der PAP gefunden SCD-Pfad (kann leer sein, wenn nicht vorhanden).
            /// </summary>
            public string SCDPath { get; set; } = "";

            /// <summary>
            /// Name der zugehörigen Animation.
            /// </summary>
            public string AnimationName { get; set; } = "";

            /// <summary>
            /// Name des Actors (falls relevant).
            /// </summary>
            public string ActorName { get; set; } = "";
        }

        public unsafe static List<GroupedPap2Entry> ScanForPap2DetailsGrouped(string dirPath, Action<string>? stateUpdate = null)
        {
            // Zuerst: Durchsuche alle JSON-Dateien im Verzeichnis und sammle die PAP-Referenzen.
            var rawList = new List<(string JsonFile, string OptionName, string GroupName, string Pap2Path)>();

            var jsonFiles = Directory.GetFiles(dirPath, "*.json", SearchOption.TopDirectoryOnly);
            var count = 0;
            var countMax = jsonFiles.Length;
            foreach (var file in jsonFiles)
            {
                count++;
                stateUpdate?.Invoke($"Scanning files... ({count}/{countMax})");
                try
                {
                    string json = File.ReadAllText(file);
                    var root = JsonConvert.DeserializeObject<SoundyJsonRoot>(json);
                    if (root == null) continue;

                    // 1) Suche im root.Files
                    if (root.Files != null)
                    {
                        foreach (var key in root.Files.Keys)
                        {
                            if (key.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase))
                            {
                                rawList.Add((file, "(root)", root.Name ?? "", root.Files[key]));
                            }
                        }
                    }

                    // 2) Suche in den Options
                    if (root.Options != null)
                    {
                        foreach (var opt in root.Options)
                        {
                            if (opt.Files == null) continue;
                            foreach (var key in opt.Files.Keys)
                            {
                                if (key.EndsWith(".avfx", StringComparison.OrdinalIgnoreCase))
                                {
                                    rawList.Add((file, opt.Name ?? "(no name)", root.Name ?? "", opt.Files[key]));
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Ignorieren, wenn eine JSON "kaputt" ist.
                }
            }

            // Gruppieren der Einträge nach PAP-Pfad:
            var groups = rawList
                .GroupBy(x => x.Pap2Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => new GroupedPap2Entry
                {
                    Pap2Path = g.Key,
                    References = g.Select(x => new Pap2Reference
                    {
                        JsonFile = x.JsonFile,
                        OptionName = x.OptionName,
                        GroupName = x.GroupName
                    }).ToList(),
                    ScdDetails = new List<Pap2ScdDetail>()
                })
                .Where(g => File.Exists(Path.Combine(dirPath, g.Pap2Path)))
                .ToList();

            count = 0;
            countMax = groups.Count;

            // Nun: Für jede gefundene PAP-Datei den Inhalt laden und darin nach SCD-Einträgen suchen.
            foreach (var group in groups)
            {
                count++;
                stateUpdate?.Invoke($"Scanning animations... ({count}/{countMax})");
                // Falls der PAP-Pfad relativ ist, kombinieren wir ihn mit dem Mod-Verzeichnis:
                string absolutePap2Path = Path.Combine(dirPath, group.Pap2Path);
                var random = new Random();
                if (!File.Exists(absolutePap2Path))
                    continue;

                try
                {
                    // Hier nehmen wir an, dass du in deinem Pap2Injector eine Methode implementierst,
                    // die öffentlich zugänglich ist, z. B. LoadPap2ForScanning, die einen Pap2File zurückgibt.
                    var loaded = LoadPap2OnMainThread(absolutePap2Path);
                    AvfxMain vfx = loaded.Main;

                    // Durchlaufe alle Animationen im PAP.
                    foreach (var emi in vfx.Emitters)
                    {
                        // Falls Animationen keinen Namen haben, kannst du hier einen Standardwert vergeben.
                        string emiName = string.IsNullOrEmpty(emi.GetAvfxName()) ? "Unnamed Avfx" : emi.GetAvfxName();


                        // Ersetze die fehlerhafte Zeile:
                        // if (emi.Sound ?? emi.Sound.Value != null ?? !emi.Sound.Value.ToString().IsNullOrWhitespace() : false)

                        // Durch die folgende, korrekte Überprüfung:
                        if (emi.Sound != null && emi.Sound.Value != null && !emi.Sound.Value.ToString().IsNullOrWhitespace())
                        {
                            string scdPath = emi.Sound.Value.ToString();
                            group.ScdDetails.Add(new Pap2ScdDetail
                            {
                                SCDPath = scdPath,
                                AnimationName = emiName,
                                ActorName = ""
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    stateUpdate?.Invoke($"{ex}");
                }
            }
            stateUpdate?.Invoke($"");

            return groups.Where(x => x.ScdDetails.Count > 0).ToList();
        }

        public static Task<List<GroupedPap2Entry>> ScanForPap2DetailsGroupedAsync(string dirPath, Action<string>? stateUpdate = null)
        {
            return Task.Run(() => ScanForPap2DetailsGrouped(dirPath, stateUpdate));
        }

        private static VfxEdit.LoadedVfx LoadPap2OnMainThread(string path)
        {
            var tcs = new TaskCompletionSource<VfxEdit.LoadedVfx>();
            Svc.Framework?.RunOnFrameworkThread(() =>
            {
                try
                {
                    tcs.SetResult(VfxEdit.LoadPap2(path));
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task.GetAwaiter().GetResult();
        }

    }
}
