using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using VfxEditor.AvfxFormat;
using VfxEditor.Formats.ScdFormat.Utils;
using VfxEditor.PapFormat;
using VfxEditor.Parsing;
using VfxEditor.TmbFormat;
using VfxEditor.TmbFormat.Entries; // für den C063-Typ

namespace Soundy.Vfx
{
    public static class VfxEdit
    {
        /// <summary>
        /// Liest die gesamte Pap2-Datei in einen Byte-Array, modifiziert den Havok-Datenbereich (falls vorhanden) 
        /// so, dass er höchstens 8 Bytes lang ist, und erzeugt dann ein Pap2File aus diesen modifizierten Daten.
        /// </summary>
        public struct LoadedVfx
        {
            public AvfxFile File;
            public AvfxMain Main;
            public string TempHkxPath;
        }

        public static LoadedVfx LoadPap2(string path)
        {
            try
            {
                BinaryReader srcReader;
                AvfxMain result;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    srcReader = new BinaryReader(fs);
                    result = AvfxMain.FromStream(srcReader);

                }
                // The temp HKX file is required later when the Pap2File is
                // written back to disk. Deleting it here caused a
                // FileNotFoundException during the write step if a PAP was
                // created for an animation that previously had no sound.
                // Cleanup is handled separately by TempFileCleaner, so we
                // keep the file around for now.
                return new LoadedVfx { File = null, Main = result, TempHkxPath = null };
            }
            catch (Exception ex)
            {
                Svc.Chat.PrintError($"Error loading Avfx: {ex}");
                throw ex;
            }

        }

        /// <summary>
        /// Hilfsmethode, um per Reflection den Value einer ParsedInt-Instanz zu setzen.
        /// </summary>
        private static void SetParsedIntValue(object instance, string fieldName, int newValue)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new Exception($"Feld '{fieldName}' nicht gefunden in {instance.GetType().Name}.");

            var parsedInt = field.GetValue(instance);
            if (parsedInt == null)
                throw new Exception($"Feld '{fieldName}' in {instance.GetType().Name} ist null.");

            // Zuerst nach einer öffentlichen Property "Value" suchen
            var prop = parsedInt.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(parsedInt, newValue);
                return;
            }

            // Falls die Property nicht existiert, versuche ein öffentliches Feld "Value"
            var publicField = parsedInt.GetType().GetField("Value", BindingFlags.Public | BindingFlags.Instance);
            if (publicField != null)
            {
                publicField.SetValue(parsedInt, newValue);
                return;
            }

            throw new Exception($"Keine Eigenschaft oder Feld 'Value' gefunden in {parsedInt.GetType().Name} (Feld '{fieldName}').");
        }


        /// <summary>
        /// Hilfsmethode, um per Reflection den Value einer TmbOffsetString-Instanz zu setzen.
        /// </summary>
        private static void SetTmbOffsetStringValue(object instance, string fieldName, string newValue)
        {
            // Hole das private Feld (z. B. "Path") aus der Instanz (in C063)
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new Exception($"Feld '{fieldName}' nicht gefunden in {instance.GetType().Name}.");

            var tmbOffsetString = field.GetValue(instance);
            if (tmbOffsetString == null)
                throw new Exception($"Feld '{fieldName}' in {instance.GetType().Name} ist null.");

            // Versuche zuerst, eine öffentliche Property "Value" zu bekommen.
            var prop = tmbOffsetString.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(tmbOffsetString, newValue);
                return;
            }

            // Falls nicht vorhanden, prüfe, ob es ein öffentliches Feld "Value" gibt.
            var fieldValue = tmbOffsetString.GetType().GetField("Value", BindingFlags.Public | BindingFlags.Instance);
            if (fieldValue != null)
            {
                fieldValue.SetValue(tmbOffsetString, newValue);
                return;
            }

            // Falls auch das nicht klappt, versuche die Property "Text"
            prop = tmbOffsetString.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(tmbOffsetString, newValue);
                return;
            }

            throw new Exception($"Keine Eigenschaft oder Feld 'Value' oder 'Text' gefunden in {tmbOffsetString.GetType().Name} (Feld '{fieldName}').");
        }

        public static string GetSCDPathFromC063(C063 entry)
        {
            // Hier per Reflection (ähnlich wie bei SetTmbOffsetStringValue) den Wert auslesen.
            // Das konkrete Vorgehen hängt von der internen Struktur des C063-Typs ab.
            // Beispiel (sehr vereinfacht):

            var fieldName = "Path";
            var instance = entry;

            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new Exception($"Feld 'Path' nicht gefunden in {instance.GetType().Name}.");

            var tmbOffsetString = field.GetValue(entry);
            if (tmbOffsetString == null)
                throw new Exception($"Feld '{fieldName}' in {instance.GetType().Name} ist null.");

            // Versuche zuerst, eine öffentliche Property "Value" zu bekommen.
            var prop = tmbOffsetString.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                return prop.GetValue(tmbOffsetString)?.ToString() ?? "not found 2";
            }

            // Falls nicht vorhanden, prüfe, ob es ein öffentliches Feld "Value" gibt.
            var fieldValue = tmbOffsetString.GetType().GetField("Value", BindingFlags.Public | BindingFlags.Instance);
            if (fieldValue != null)
            {
                return fieldValue.GetValue(tmbOffsetString)?.ToString() ?? "not found 2";
            }

            // Falls auch das nicht klappt, versuche die Property "Text"
            prop = tmbOffsetString.GetType().GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                return prop.GetValue(tmbOffsetString)?.ToString() ?? "not found 2";
            }

            return "not found 1";
        }

    }

}
