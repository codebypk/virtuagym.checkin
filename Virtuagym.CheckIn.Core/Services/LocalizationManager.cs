using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Virtuagym.CheckIn.Core.Helper;

namespace Virtuagym.CheckIn.Core.Services
{
    /// <summary>
    /// Provides localized UI strings loaded from <c>Resources\lang_XX.json</c> files
    /// that are deployed alongside the application executable.
    /// New languages can be added without recompiling â€” just drop a <c>lang_XX.json</c>
    /// file into the Resources folder and pass the matching code to <see cref="Initialize"/>.
    /// Falls back to the built-in English strings when a file is missing or a key is absent.
    /// Call <see cref="Initialize"/> once at application startup.
    /// Access any string via <c>L.T("Key")</c>.
    /// </summary>
    public static class L
    {
        private static Dictionary<string, string> _current;
        /// <summary>
        /// Initialize with a language code (e.g. "en", "de").
        /// Tries to load <c>Resources\lang_{code}.json</c> from the application directory first,
        /// then falls back to the embedded resource compiled into the assembly.
        /// Missing keys are filled from the embedded English resource.
        /// </summary>
        public static void Initialize(string lang)
        {
            if (string.IsNullOrEmpty(lang)) lang = "en";

            // 1. External file takes priority; embedded resource is the fallback
            var loaded = TryLoadFromFile(lang) ?? TryLoadFromEmbeddedResource(lang);

            // 2. Fill any missing keys from English fallback (external file first, embedded as backup)
            var enFallback = lang != "en"
                ? (TryLoadFromFile("en") ?? TryLoadFromEmbeddedResource("en"))
                : loaded;
            if (enFallback != null)
            {
                if (loaded == null)
                {
                    loaded = enFallback;
                }
                else
                {
                    foreach (var kv in enFallback)
                    {
                        if (!loaded.ContainsKey(kv.Key))
                            loaded[kv.Key] = kv.Value;
                    }
                }
            }

            if (loaded != null)
                _current = loaded;
        }

        /// <summary>Returns the localized string for the given key, or [key] if not found.</summary>
        public static string T(string key)
        {
            if (_current == null)
                _current = TryLoadFromFile("en") ?? TryLoadFromEmbeddedResource("en") ?? new Dictionary<string, string>();
            return _current.TryGetValue(key, out string val) ? val : $"[{key}]";
        }

        /// <summary>
        /// Returns the directory where language files are expected:
        /// <c>&lt;ExeDir&gt;\Resources\</c>.
        /// </summary>
        public static string LangFileDirectory
        {
            get
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetEntryAssembly()?.Location
                                ?? Assembly.GetExecutingAssembly().Location)
                                ?? AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(exeDir, Constants.LanguageFilesFolder);
            }
        }

        /// <summary>
        /// Discovers all available languages based on existing <c>lang_*.json</c> files
        /// in the external directory and/or as embedded resources.
        /// Each language file must contain the key <c>"Lang_SelfName"</c> (e.g. "Deutsch", "English")
        /// so the language is displayed correctly in the ComboBox.
        /// New languages are detected automatically – just add a <c>lang_XX.json</c> file
        /// with the key <c>"Lang_SelfName"</c>.
        /// </summary>
        /// <returns>Alphabetisch sortierte Liste von (Code, SelfName)-Tupeln, z.B. [("de", "Deutsch"), ("en", "English")].</returns>
        public static List<KeyValuePair<string, string>> GetAvailableLanguages()
        {
            var languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Externe Dateien im resources\lang-Verzeichnis scannen
            try
            {
                string langDir = LangFileDirectory;
                if (Directory.Exists(langDir))
                {
                    foreach (string file in Directory.GetFiles(langDir, "lang_*.json"))
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        string code = fileName.Substring("lang_".Length);
                        if (string.IsNullOrEmpty(code) || languages.ContainsKey(code))
                            continue;

                        string selfName = ReadSelfNameFromFile(file) ?? code;
                        languages[code] = selfName;
                    }
                }
            }
            catch { /* External language directory not available → Embedded Resources as fallback */ }

            // 2. Embedded Resources als Fallback (falls externes Verzeichnis leer/nicht vorhanden)
            try
            {
                string prefix = "VirtuagymMemberCheckIn.Resources.lang_";
                string suffix = ".json";
                foreach (string resourceName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                {
                    if (!resourceName.StartsWith(prefix, StringComparison.Ordinal) ||
                        !resourceName.EndsWith(suffix, StringComparison.Ordinal))
                        continue;

                    string code = resourceName.Substring(prefix.Length, resourceName.Length - prefix.Length - suffix.Length);
                    if (string.IsNullOrEmpty(code) || languages.ContainsKey(code))
                        continue;

                    var dict = TryLoadFromEmbeddedResource(code);
                    string selfName = dict != null && dict.TryGetValue("Lang_SelfName", out string name) ? name : code;
                    languages[code] = selfName;
                }
            }
            catch { /* Embedded resources not loadable → available languages incomplete */ }

            // Alphabetisch nach SelfName sortieren
            var sorted = languages.ToList();
            sorted.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase));
            return sorted;
        }

        /// <summary>
        /// Liest nur den Key "Lang_SelfName" aus einer Sprachdatei, ohne die gesamte Datei zu parsen.
        /// </summary>
        private static string ReadSelfNameFromFile(string path)
        {
            try
            {
                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                var dict = ParseJsonObject(json);
                return dict.TryGetValue("Lang_SelfName", out string val) ? val : null;
            }
            catch { return null; }
        }

        private static Dictionary<string, string> TryLoadFromFile(string lang)
        {
            try
            {
                string path = Path.Combine(LangFileDirectory, $"lang_{lang}.json");
                if (!File.Exists(path)) return null;

                string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
                return ParseJsonObject(json);
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, string> TryLoadFromEmbeddedResource(string lang)
        {
            try
            {
                string resourceName = $"VirtuagymMemberCheckIn.Resources.lang_{lang}.json";
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                        return ParseJsonObject(reader.ReadToEnd());
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Minimal JSON object parser â€” handles flat string-only objects without
        /// requiring an external JSON library in the Helper assembly.
        /// </summary>
        private static Dictionary<string, string> ParseJsonObject(string json)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(json)) return dict;

            int i = 0;
            SkipWhitespace(json, ref i);
            if (i >= json.Length || json[i] != '{') return dict;
            i++; // skip '{'

            while (i < json.Length)
            {
                SkipWhitespace(json, ref i);
                if (i >= json.Length) break;
                if (json[i] == '}') break;
                if (json[i] == ',') { i++; continue; }

                if (json[i] != '"') { i++; continue; }
                string key = ReadJsonString(json, ref i);

                SkipWhitespace(json, ref i);
                if (i >= json.Length || json[i] != ':') continue;
                i++; // skip ':'

                SkipWhitespace(json, ref i);
                if (i >= json.Length) break;

                string value = json[i] == '"'
                    ? ReadJsonString(json, ref i)
                    : ReadJsonLiteral(json, ref i);

                if (key != null)
                    dict[key] = value ?? "";
            }

            return dict;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }

        private static string ReadJsonString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') return null;
            i++; // skip opening quote
            var sb = new System.Text.StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    char esc = s[i++];
                    switch (esc)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/');  break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= s.Length)
                            {
                                string hex = s.Substring(i, 4);
                                i += 4;
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int cp))
                                    sb.Append((char)cp);
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string ReadJsonLiteral(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && s[i] != ']' && s[i] != '\n')
                i++;
            return s.Substring(start, i - start).Trim();
        }
    }
}
