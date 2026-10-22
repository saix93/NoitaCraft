using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace NoitaCraft
{
    // Reads the player's own Noita install: data/data.wak (spells, icons, Mina's sprite) and data/translations/common.csv.
    internal static class NoitaData
    {
        public sealed class Action
        {
            public string Id, NameKey, DescKey, Sprite, Body;
            public int Mana, Price;
        }

        public sealed class Anim
        {
            public int FrameCount, FrameWidth, FrameHeight, FramesPerRow, PosX, PosY;
            public bool Shrink;
            public float FrameWait;
        }

        public static string Folder;
        public static string Error;
        public static readonly Dictionary<string, Action> Actions = new Dictionary<string, Action>();
        // translation key -> column (en, ru, ...) -> text
        static readonly Dictionary<string, string[]> texts = new Dictionary<string, string[]>();
        static string[] columns = new string[0];
        static readonly Dictionary<string, KeyValuePair<long, int>> toc = new Dictionary<string, KeyValuePair<long, int>>();
        static string wakPath;

        public static bool Ready => Error == null && Actions.Count > 0;

        public static void Load()
        {
            try
            {
                Folder = FindFolder();
                if (Folder == null) { Error = "Noita's folder was not given (NOITA_DIR in %LOCALAPPDATA%\\NoitaCraft\\noita.env)"; return; }
                wakPath = Path.Combine(Folder, "data", "data.wak");
                if (!File.Exists(wakPath)) { Error = "data/data.wak not found in " + Folder; return; }
                ReadToc();
                ParseActions(Encoding.UTF8.GetString(Read("data/scripts/gun/gun_actions.lua")));
                ParseTranslations(Path.Combine(Folder, "data", "translations", "common.csv"));
            }
            catch (Exception e) { Error = e.GetType().Name + ": " + e.Message; }
        }

        static string FindFolder()
        {
            string env = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoitaCraft", "noita.env");
            if (File.Exists(env))
                foreach (string line in File.ReadAllLines(env))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim() == "NOITA_DIR")
                    {
                        string dir = line.Substring(eq + 1).Trim().Trim('"');
                        if (Directory.Exists(dir)) return dir;
                    }
                }
            string v = Environment.GetEnvironmentVariable("NOITA_DIR");
            if (!string.IsNullOrEmpty(v) && Directory.Exists(v)) return v;
            return FromSteam();
        }

        // Noita in any of the player's Steam libraries, when the folder Melty gave isn't there
        static string FromSteam()
        {
            try
            {
                var roots = new List<string>();
                foreach (string s in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles") })
                    if (!string.IsNullOrEmpty(s)) roots.Add(Path.Combine(s, "Steam"));
                // Magicraft's own library: <library>/steamapps/common/Magicraft
                var common = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Parent;
                if (common != null && common.Parent != null && common.Parent.Parent != null) roots.Add(common.Parent.Parent.FullName);
                foreach (string root in roots.ToArray())
                {
                    var vdf = Path.Combine(Path.Combine(root, "steamapps"), "libraryfolders.vdf");
                    if (!File.Exists(vdf)) continue;
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                        roots.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                foreach (string root in roots)
                {
                    var dir = Path.Combine(Path.Combine(Path.Combine(root, "steamapps"), "common"), "Noita");
                    if (File.Exists(Path.Combine(Path.Combine(dir, "data"), "data.wak"))) return dir;
                }
            }
            catch (Exception) { }
            return null;
        }

        // WAK: int32 0, int32 fileCount, int32 tocEnd, int32 0; then per file int32 offset, int32 size, int32 nameLength, name.
        static void ReadToc()
        {
            using (var fs = new FileStream(wakPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var br = new BinaryReader(fs))
            {
                br.ReadInt32();
                int count = br.ReadInt32();
                br.ReadInt32(); br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int offset = br.ReadInt32(), size = br.ReadInt32(), nameLength = br.ReadInt32();
                    string name = Encoding.UTF8.GetString(br.ReadBytes(nameLength));
                    toc[name] = new KeyValuePair<long, int>((uint)offset, size);
                }
            }
        }

        public static bool Has(string path) => toc.ContainsKey(path);

        public static byte[] Read(string path)
        {
            KeyValuePair<long, int> e;
            if (!toc.TryGetValue(path, out e)) throw new FileNotFoundException("not in data.wak: " + path);
            using (var fs = new FileStream(wakPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Position = e.Key;
                var bytes = new byte[e.Value];
                int got = 0;
                while (got < bytes.Length) { int n = fs.Read(bytes, got, bytes.Length - got); if (n <= 0) break; got += n; }
                return bytes;
            }
        }

        static readonly Regex actionStart = new Regex("\\{\\s*id\\s*=\\s*\"([A-Z0-9_]+)\"", RegexOptions.Compiled);

        static void ParseActions(string lua)
        {
            MatchCollection starts = actionStart.Matches(lua);
            for (int i = 0; i < starts.Count; i++)
            {
                int from = starts[i].Index;
                int to = i + 1 < starts.Count ? starts[i + 1].Index : lua.Length;
                // fields before the action function
                int fnAt = lua.IndexOf("function", from, to - from, StringComparison.Ordinal);
                string head = lua.Substring(from, (fnAt > 0 ? fnAt : to) - from);
                var a = new Action { Id = starts[i].Groups[1].Value, Body = lua.Substring(from, to - from) };
                a.NameKey = Field(head, "name").TrimStart('$');
                a.DescKey = Field(head, "description").TrimStart('$');
                a.Sprite = Field(head, "sprite");
                int mana;
                int.TryParse(Field(head, "mana"), NumberStyles.Integer, CultureInfo.InvariantCulture, out mana);
                a.Mana = mana;
                int price;
                int.TryParse(Field(head, "price"), NumberStyles.Integer, CultureInfo.InvariantCulture, out price);
                a.Price = price;
                if (!Actions.ContainsKey(a.Id)) Actions[a.Id] = a;
            }
        }

        static string Field(string head, string key)
        {
            Match m = Regex.Match(head, "(?m)^\\s*" + key + "\\s*=\\s*\"?([^\",\\r\\n]*)\"?");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        static void ParseTranslations(string csv)
        {
            bool first = true;
            foreach (string[] row in Csv(File.ReadAllText(csv, Encoding.UTF8)))
            {
                if (row.Length == 0) continue;
                if (first) { columns = row; first = false; continue; }
                texts[row[0]] = row;
            }
        }

        static IEnumerable<string[]> Csv(string text)
        {
            var row = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else quoted = false; }
                    else cell.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { row.Add(cell.ToString()); cell.Length = 0; }
                else if (c == '\n' || c == '\r')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Length = 0;
                    yield return row.ToArray(); row.Clear();
                }
                else cell.Append(c);
            }
            if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); yield return row.ToArray(); }
        }

        // Noita text in one of its columns (en, ru, pt-br, es-es, de, fr-fr, it, pl, zh-cn, jp, ko); English when missing.
        public static string Text(string key, string column)
        {
            string[] row;
            if (key == null || !texts.TryGetValue(key, out row)) return null;
            int col = Array.IndexOf(columns, column);
            string s = col > 0 && col < row.Length ? row[col] : "";
            if (string.IsNullOrEmpty(s)) { int en = Array.IndexOf(columns, "en"); s = en > 0 && en < row.Length ? row[en] : ""; }
            return s.Replace("\\n", "\\");
        }

        public static Dictionary<string, Anim> SpriteAnims(string xmlPath, out string pngPath, out float offsetX, out float offsetY)
        {
            string xml = Encoding.UTF8.GetString(Read(xmlPath));
            Match fm = Regex.Match(xml, "filename=\"([^\"]+)\"");
            pngPath = fm.Success ? fm.Groups[1].Value : null;
            Match ox = Regex.Match(xml, "offset_x=\"([^\"]+)\""), oy = Regex.Match(xml, "offset_y=\"([^\"]+)\"");
            offsetX = ox.Success ? float.Parse(ox.Groups[1].Value, CultureInfo.InvariantCulture) : 0f;
            offsetY = oy.Success ? float.Parse(oy.Groups[1].Value, CultureInfo.InvariantCulture) : 0f;
            var anims = new Dictionary<string, Anim>();
            foreach (Match m in Regex.Matches(xml, "<RectAnimation(.*?)>", RegexOptions.Singleline))
            {
                string attrs = m.Groups[1].Value;
                Func<string, string> at = n => { Match x = Regex.Match(attrs, "\\b" + n + "=\"([^\"]*)\""); return x.Success ? x.Groups[1].Value : "0"; };
                var a = new Anim
                {
                    FrameCount = int.Parse(at("frame_count"), CultureInfo.InvariantCulture),
                    FrameWidth = int.Parse(at("frame_width"), CultureInfo.InvariantCulture),
                    FrameHeight = int.Parse(at("frame_height"), CultureInfo.InvariantCulture),
                    FramesPerRow = Math.Max(1, int.Parse(at("frames_per_row"), CultureInfo.InvariantCulture)),
                    PosX = int.Parse(at("pos_x"), CultureInfo.InvariantCulture),
                    PosY = int.Parse(at("pos_y"), CultureInfo.InvariantCulture),
                    FrameWait = float.Parse(at("frame_wait"), CultureInfo.InvariantCulture),
                    Shrink = at("shrink_by_one_pixel") == "1"
                };
                string name = at("name");
                if (!anims.ContainsKey(name)) anims[name] = a;
            }
            return anims;
        }
    }
}
