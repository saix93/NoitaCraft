using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Mathematics;
using UnityEngine;

namespace NoitaCraft
{
    // Noita's look, read from the player's data.wak: projectile sprites and animations, trails in the emitted material's colour,
    // bounce sparks, and the explosion sprite each projectile names.
    internal static class NoitaFx
    {
        sealed class Anim { public Sprite[] Frames; public float Wait; public bool Loop; }

        static readonly Dictionary<string, Anim> anims = new Dictionary<string, Anim>();
        static readonly Dictionary<string, Color> materials = new Dictionary<string, Color>();
        static bool materialsRead;
        static Sprite pixel;
        static Material material;
        static Transform root;
        static int sortingLayer, layer;
        static bool placed;
        public static int Drawn, ExplosionsDrawn;

        static float Ppu => 1f / NoitaDefs.S("pixelToMeter");

        static void Setup()
        {
            if (root == null)
            {
                var go = new GameObject("NoitaCraft_Fx");
                UnityEngine.Object.DontDestroyOnLoad(go);
                root = go.transform;
                go.AddComponent<Particles>();
            }
            if (material == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
                if (sh != null) material = new Material(sh);
                Plugin.Log.LogInfo("Noita fx shader: " + (sh != null ? sh.name : "none"));
            }
            if (!placed)
            {
                // draw where Magicraft draws the player's wand
                try
                {
                    if (PlayerMgr.Inst != null && PlayerMgr.Inst.Wands != null && PlayerMgr.Inst.Wands.Count > 0 && PlayerMgr.Inst.Wands[0] != null)
                    {
                        SpriteRenderer wr = PlayerMgr.Inst.Wands[0].GetComponentInChildren<SpriteRenderer>(true);
                        if (wr != null) { sortingLayer = wr.sortingLayerID; layer = wr.gameObject.layer; placed = true; Plugin.Log.LogInfo("Noita fx on sorting layer " + SortingLayer.IDToName(sortingLayer) + ", layer " + LayerMask.LayerToName(layer)); }
                    }
                }
                catch (Exception) { }
            }
            if (pixel == null)
            {
                var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                t.SetPixel(0, 0, Color.white); t.Apply();
                pixel = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), Ppu);
            }
        }

        static SpriteRenderer Renderer(string name, int order)
        {
            Setup();
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.layer = layer;
            var sr = go.AddComponent<SpriteRenderer>();
            if (material != null) sr.sharedMaterial = material;
            sr.sortingLayerID = sortingLayer;
            sr.sortingOrder = order;
            return sr;
        }

        static Vector3 Screen(float3 p) => Tool2D.GetLayerPoint(new Vector3(p.x, p.y, p.z)) + new Vector3(0f, 0f, -0.05f);

        // "data/particles/spark_0$[1-3].xml" picks one of 1..3
        static string Pick(string path)
        {
            Match m = Regex.Match(path, "\\$\\[(\\d+)-(\\d+)\\]");
            if (!m.Success) return path;
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
            return path.Substring(0, m.Index) + UnityEngine.Random.Range(a, b + 1) + path.Substring(m.Index + m.Length);
        }

        static Anim Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Anim a;
            if (anims.TryGetValue(path, out a)) return a;
            try
            {
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    Texture2D tex = NoitaResources.Png(path);
                    a = new Anim { Frames = new[] { Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), Ppu) }, Wait = 1f, Loop = true };
                }
                else if (NoitaData.Has(path))
                {
                    string png; float ox, oy;
                    Dictionary<string, NoitaData.Anim> all = NoitaData.SpriteAnims(path, out png, out ox, out oy);
                    string xml = Encoding.UTF8.GetString(NoitaData.Read(path));
                    Match def = Regex.Match(xml, "default_animation=\"([^\"]+)\"");
                    NoitaData.Anim ra = null;
                    if (def.Success) all.TryGetValue(def.Groups[1].Value, out ra);
                    if (ra == null) foreach (NoitaData.Anim x in all.Values) { ra = x; break; }
                    Texture2D tex = NoitaResources.Png(png);
                    if (ra == null)
                        a = new Anim { Frames = new[] { Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(ox / tex.width, 1f - oy / tex.height), Ppu) }, Wait = 1f, Loop = true };
                    else
                    {
                        var frames = new List<Sprite>();
                        // Noita's frames sit on a grid of frame_width x frame_height from pos_x/pos_y; with shrink_by_one_pixel
                        // the picture is one pixel smaller than its cell
                        int fw = ra.FrameWidth - (ra.Shrink ? 1 : 0), fh = ra.FrameHeight - (ra.Shrink ? 1 : 0);
                        for (int f = 0; f < ra.FrameCount; f++)
                        {
                            int x = ra.PosX + f % ra.FramesPerRow * ra.FrameWidth, yTop = ra.PosY + f / ra.FramesPerRow * ra.FrameHeight;
                            int w = Mathf.Min(fw, tex.width - x), h = Mathf.Min(fh, tex.height - yTop);
                            if (w <= 0 || h <= 0) break;
                            frames.Add(Sprite.Create(tex, new Rect(x, tex.height - yTop - h, w, h),
                                new Vector2(Mathf.Clamp01(ox / w), Mathf.Clamp01(1f - oy / h)), Ppu));
                        }
                        Match loop = Regex.Match(xml, "loop=\"0\"");
                        a = new Anim { Frames = frames.ToArray(), Wait = Mathf.Max(0.016f, ra.FrameWait), Loop = !loop.Success };
                    }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Noita sprite " + path + ": " + e.Message); a = null; }
            if (a != null && a.Frames.Length == 0) a = null;
            anims[path] = a;
            return a;
        }

        // material colour from data/materials.xml (wang_color is ARGB)
        public static Color MaterialColor(string name)
        {
            if (!materialsRead)
            {
                materialsRead = true;
                try
                {
                    string xml = Encoding.UTF8.GetString(NoitaData.Read("data/materials.xml"));
                    foreach (Match m in Regex.Matches(xml, "<CellData(?:Child)?\\b([^>]*)>", RegexOptions.Singleline))
                    {
                        Match n = Regex.Match(m.Groups[1].Value, "\\bname=\"([^\"]+)\""), c = Regex.Match(m.Groups[1].Value, "wang_color=\"([0-9a-fA-F]{8})\"");
                        if (!n.Success || !c.Success || materials.ContainsKey(n.Groups[1].Value)) continue;
                        uint v = uint.Parse(c.Groups[1].Value, NumberStyles.HexNumber);
                        materials[n.Groups[1].Value] = new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("Noita materials: " + e.Message); }
            }
            Color col;
            return name != null && materials.TryGetValue(name, out col) ? col : new Color(1f, 0.9f, 0.6f, 1f);
        }

        public sealed class Projectile
        {
            public SpriteRenderer Sr;
            public LineRenderer Beam;
            Anim anim;
            float clock, trailClock;
            NoitaSpell spell;
            Color trail;
            Vector3 last;

            public Projectile(NoitaSpell s, float3 p)
            {
                spell = s;
                anim = Load(Pick(s.Sprite));
                trail = MaterialColor(s.TrailMaterial);
                Sr = Renderer("Noita_" + s.Row.NoitaId, 3000);
                if (anim != null) Sr.sprite = anim.Frames[0];
                else { Sr.sprite = pixel; Sr.color = trail; Sr.transform.localScale = Vector3.one * 3f; Plugin.Log.LogWarning("no sprite for " + s.Row.NoitaId + " (" + s.Sprite + ")"); }
                last = Screen(p);
                Sr.transform.position = last;
                if (s.Row.Special == "beam")
                {
                    Beam = Sr.gameObject.AddComponent<LineRenderer>();
                    if (material != null) Beam.sharedMaterial = material;
                    Beam.sortingLayerID = Sr.sortingLayerID; Beam.sortingOrder = 2999;
                    Beam.positionCount = 2;
                    Beam.startWidth = Beam.endWidth = NoitaDefs.S("pixelToMeter") * 2f;
                    Beam.startColor = Beam.endColor = trail;
                    Beam.useWorldSpace = true;
                }
                Drawn++;
            }

            public void Follow(float3 p, float3 dir, float3 spawn)
            {
                if (Sr == null) return;
                Vector3 now = Screen(p);
                float dt = Time.deltaTime;
                clock += dt;
                if (anim != null && anim.Frames.Length > 1)
                {
                    int f = (int)(clock / anim.Wait);
                    Sr.sprite = anim.Frames[anim.Loop ? f % anim.Frames.Length : Mathf.Min(f, anim.Frames.Length - 1)];
                }
                Sr.transform.position = now;
                if (spell.Rotate && (dir.x != 0f || dir.y != 0f)) Sr.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
                if (Beam != null) { Beam.SetPosition(0, Screen(spawn)); Beam.SetPosition(1, now); }
                // Noita's trail: the projectile's emitted material, a pixel at a time
                trailClock += dt;
                if (!string.IsNullOrEmpty(spell.TrailMaterial) && trailClock > 0.016f)
                {
                    trailClock = 0f;
                    int n = Mathf.Clamp((int)((now - last).magnitude / NoitaDefs.S("pixelToMeter")), 1, 6);
                    for (int i = 0; i < n; i++)
                        Particles.Emit(Vector3.Lerp(last, now, i / (float)n), UnityEngine.Random.insideUnitCircle * 0.3f, UnityEngine.Random.Range(0.1f, 0.35f), trail);
                }
                last = now;
            }

            public void End()
            {
                if (Sr != null) UnityEngine.Object.Destroy(Sr.gameObject);
                Sr = null;
            }
        }

        // every spell's projectile and explosion sprite, loaded once; returns the ones that failed
        public static List<string> CheckAll()
        {
            Setup();
            var bad = new List<string>();
            foreach (NoitaSpell s in NoitaDefs.Spells.Values)
            {
                if (!string.IsNullOrEmpty(s.Sprite) && Load(s.Sprite.Contains("$[") ? Pick(s.Sprite) : s.Sprite) == null) bad.Add(s.Row.NoitaId + " sprite " + s.Sprite);
                if (!string.IsNullOrEmpty(s.ExplosionSprite) && Load(Pick(s.ExplosionSprite)) == null) bad.Add(s.Row.NoitaId + " explosion " + s.ExplosionSprite);
            }
            return bad;
        }

        public static Projectile Spawn(NoitaSpell s, float3 p)
        {
            try { return new Projectile(s, p); }
            catch (Exception e) { Plugin.Log.LogWarning("Noita projectile look: " + e.Message); return null; }
        }

        public static void Bounce(NoitaSpell s, float3 p)
        {
            Color c = MaterialColor(string.IsNullOrEmpty(s.BounceFx) ? s.TrailMaterial : BounceMaterial(s.BounceFx) ?? s.TrailMaterial);
            Vector3 at = Screen(p);
            for (int i = 0; i < 8; i++) Particles.Emit(at, UnityEngine.Random.insideUnitCircle * 4f, UnityEngine.Random.Range(0.05f, 0.15f), c);
        }

        static string BounceMaterial(string fxXml)
        {
            Dictionary<string, string> em = NoitaDefs.Component(fxXml, "ParticleEmitterComponent");
            string v;
            return em.TryGetValue("emitted_material_name", out v) ? v : null;
        }

        // the projectile's end: Noita's explosion sprite where it has one, and a puff of its material
        public static void Death(NoitaSpell s, float3 p, Projectile fx)
        {
            Vector3 at = Screen(p);
            if (fx != null)
            {
                if (fx.Beam != null) { var go = new GameObject("Noita_BeamFade"); go.transform.SetParent(root, false); var b = go.AddComponent<BeamFade>(); b.From(fx.Beam); }
                fx.End();
            }
            Color c = MaterialColor(s.TrailMaterial);
            for (int i = 0; i < 10; i++) Particles.Emit(at, UnityEngine.Random.insideUnitCircle * 3f, UnityEngine.Random.Range(0.1f, 0.4f), c);
            Anim ex = Load(Pick(s.ExplosionSprite));
            if (ex == null) return;
            SpriteRenderer sr = Renderer("Noita_Explosion_" + s.Row.NoitaId, 3001);
            sr.transform.position = at;
            // an explosion sprite is drawn the size of its radius
            float px = ex.Frames[0].rect.width;
            if (s.Explodes && px > 0) sr.transform.localScale = Vector3.one * Mathf.Clamp(s.ExplosionRadius * 2f / (px * NoitaDefs.S("pixelToMeter")), 0.5f, 3f);
            sr.gameObject.AddComponent<Player>().Play(ex, sr);
            ExplosionsDrawn++;
        }

        sealed class Player : MonoBehaviour
        {
            Anim a; SpriteRenderer sr; float t;
            public void Play(Anim anim, SpriteRenderer r) { a = anim; sr = r; sr.sprite = a.Frames[0]; }
            void Update()
            {
                t += Time.deltaTime;
                int f = (int)(t / a.Wait);
                if (f >= a.Frames.Length) { Destroy(gameObject); return; }
                sr.sprite = a.Frames[f];
            }
        }

        sealed class BeamFade : MonoBehaviour
        {
            LineRenderer lr; float t;
            public void From(LineRenderer src)
            {
                lr = gameObject.AddComponent<LineRenderer>();
                lr.sharedMaterial = src.sharedMaterial; lr.sortingLayerID = src.sortingLayerID; lr.sortingOrder = src.sortingOrder;
                lr.positionCount = 2; lr.SetPosition(0, src.GetPosition(0)); lr.SetPosition(1, src.GetPosition(1));
                lr.startWidth = lr.endWidth = src.startWidth; lr.startColor = lr.endColor = src.startColor; lr.useWorldSpace = true;
                gameObject.layer = src.gameObject.layer;
            }
            void Update()
            {
                t += Time.deltaTime;
                Color c = lr.startColor; c.a = Mathf.Clamp01(1f - t / 0.2f);
                lr.startColor = lr.endColor = c;
                if (t > 0.2f) Destroy(gameObject);
            }
        }

        // Noita's cosmetic pixels
        sealed class Particles : MonoBehaviour
        {
            struct P { public SpriteRenderer Sr; public Vector3 V; public float Life, Max; public Color C; }
            static readonly List<P> live = new List<P>();
            static readonly Stack<SpriteRenderer> pool = new Stack<SpriteRenderer>();

            public static void Emit(Vector3 at, Vector2 v, float life, Color c)
            {
                if (live.Count > 600) return;
                SpriteRenderer sr = pool.Count > 0 ? pool.Pop() : Renderer("Noita_Pixel", 2998);
                sr.gameObject.SetActive(true);
                sr.sprite = pixel;
                sr.color = c;
                sr.transform.position = at;
                live.Add(new P { Sr = sr, V = v, Life = life, Max = life, C = c });
            }

            void Update()
            {
                float dt = Time.deltaTime;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    P p = live[i];
                    p.Life -= dt;
                    if (p.Life <= 0f || p.Sr == null)
                    {
                        if (p.Sr != null) { p.Sr.gameObject.SetActive(false); pool.Push(p.Sr); }
                        live.RemoveAt(i);
                        continue;
                    }
                    p.Sr.transform.position += p.V * dt;
                    Color c = p.C; c.a = p.C.a * (p.Life / p.Max);
                    p.Sr.color = c;
                    live[i] = p;
                }
            }
        }
    }
}
