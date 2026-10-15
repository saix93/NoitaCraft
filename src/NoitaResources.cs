using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NoitaCraft
{
    // Unity's own hook for Resources.Load: Magicraft loads every icon by path, and Noita's are made here from the player's data.wak.
    internal sealed class NoitaResources : ResourcesAPI
    {
        const string SpellIcons = "Textures/SpellIcons/", WandIcons = "Textures/WandIcons/";
        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        public static bool Served;

        public static void Install()
        {
            inst = new NoitaResources();
            ResourcesAPI.overrideAPI = inst;
        }

        protected override Object Load(string path, Type systemTypeInstance)
        {
            if (path != null && path.IndexOf("noita_", StringComparison.Ordinal) >= 0 && NoitaData.Ready)
            {
                Sprite s = Get(path);
                if (s != null)
                {
                    if (!Served) { Served = true; TestRun.Report("noita-icons", true, null); }
                    if (systemTypeInstance == typeof(Texture2D) || systemTypeInstance == typeof(Texture)) return s.texture;
                    return s;
                }
            }
            return base.Load(path, systemTypeInstance);
        }

        static Sprite Get(string path)
        {
            Sprite s;
            if (cache.TryGetValue(path, out s) && s != null) return s;
            try
            {
                if (path.StartsWith(SpellIcons, StringComparison.Ordinal))
                {
                    string noitaId = path.Substring(SpellIcons.Length + "noita_".Length);
                    NoitaData.Action a;
                    if (!NoitaData.Actions.TryGetValue(noitaId, out a)) return null;
                    // Magicraft spell icons: 128x128, 100 px per unit, pivot (0.5, 0.1)
                    s = Fit(Png(a.Sprite), 128, 128, false, new Vector2(0.5f, 0.1f), 100f);
                }
                else if (path.StartsWith(WandIcons, StringComparison.Ordinal))
                {
                    int id;
                    if (!int.TryParse(path.Substring(WandIcons.Length + "noita_wand_".Length), out id)) return null;
                    WandRow w = Sheets.Wands.FirstOrDefault(x => x.WandId == id);
                    if (w.IconPath == null) return null;
                    // Magicraft draws a wand (in hand and in the UI) along the diagonal of its sprite, tip toward the top right, and turns it
                    // so that diagonal follows the aim. The Noita wand (lying pointing right) is laid on that diagonal, in a canvas the size,
                    // pivot and scale of Magicraft's own first wand sprite.
                    var refSprite = inst.Magicraft(WandIcons + "1", typeof(Sprite)) as Sprite;
                    int cw = refSprite != null ? (int)refSprite.rect.width : 128, ch = refSprite != null ? (int)refSprite.rect.height : 128;
                    Vector2 pivot = refSprite != null ? new Vector2(refSprite.pivot.x / refSprite.rect.width, refSprite.pivot.y / refSprite.rect.height) : new Vector2(0.5f, 0.5f);
                    float ppu = refSprite != null ? refSprite.pixelsPerUnit : 100f;
                    if (!loggedWandRef) { loggedWandRef = true; Plugin.Log.LogInfo("Magicraft wand sprite: " + cw + "x" + ch + ", pivot " + pivot + ", " + ppu + " px/unit"); }
                    s = Diagonal(Png(w.IconPath), cw, ch, pivot, ppu);
                }
            }
            catch (Exception e) { Plugin.Log.LogError("icon " + path + ": " + e.Message); }
            if (s != null) { s.name = path; cache[path] = s; }
            return s;
        }

        static bool loggedWandRef;
        static NoitaResources inst;
        Object Magicraft(string path, Type type) => base.Load(path, type);

        // the source (pointing right) turned 45° so it runs from bottom left to top right, nearest-neighbour, filling the diagonal
        static Sprite Diagonal(Texture2D src, int w, int h, Vector2 pivot, float ppu)
        {
            Color32[] sp = src.GetPixels32();
            var px = new Color32[w * h];
            float diag = Mathf.Sqrt(w * w + h * h) * 0.92f;
            float scale = diag / src.width; // canvas pixels per Noita pixel along the wand
            float cx = w * 0.5f, cy = h * 0.5f, c = Mathf.Cos(Mathf.PI / 4f), sn = Mathf.Sin(Mathf.PI / 4f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // canvas -> wand frame (undo the 45° turn), then -> source pixel
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    float u = dx * c + dy * sn, v = -dx * sn + dy * c;
                    int su = Mathf.FloorToInt(u / scale + src.width * 0.5f), sv = Mathf.FloorToInt(v / scale + src.height * 0.5f);
                    if (su < 0 || sv < 0 || su >= src.width || sv >= src.height) continue;
                    px[y * w + x] = sp[sv * src.width + su];
                }
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            dst.SetPixels32(px);
            dst.Apply(false, false);
            Object.Destroy(src);
            return Sprite.Create(dst, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        }

        public static Texture2D Png(string wakPath)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            byte[] png = NoitaData.Read(wakPath);
            if (!ImageConversion.LoadImage(t, png, false)) throw new Exception("not a PNG: " + wakPath);
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;
            return t;
        }

        // Nearest-neighbour, whole-number scale onto a canvas of Magicraft's icon size.
        static Sprite Fit(Texture2D src, int w, int h, bool rotateUpright, Vector2 pivot, float ppu)
        {
            int sw = rotateUpright ? src.height : src.width, sh = rotateUpright ? src.width : src.height;
            int scale = Math.Max(1, Math.Min(w / sw, h / sh));
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            Color32[] sp = src.GetPixels32();
            int ox = (w - sw * scale) / 2, oy = (h - sh * scale) / 2;
            for (int y = 0; y < sh * scale; y++)
                for (int x = 0; x < sw * scale; x++)
                {
                    int u = x / scale, v = y / scale, su, sv;
                    // upright: the wand's tip (right side) goes to the top
                    if (rotateUpright) { su = v; sv = src.height - 1 - u; } else { su = u; sv = v; }
                    px[(oy + y) * w + ox + x] = sp[sv * src.width + su];
                }
            dst.SetPixels32(px);
            dst.Apply(false, false);
            Object.Destroy(src);
            return Sprite.Create(dst, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
        }
    }
}
