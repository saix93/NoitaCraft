using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NoitaCraft
{
    // A Noita spell as read from the player's Noita: its action (gun_actions.lua) and the projectile it adds, then turned into Magicraft numbers.
    internal sealed class NoitaSpell
    {
        public SpellRow Row;
        public NoitaData.Action Action;
        public string ProjectileXml;

        // Noita's own numbers
        public float SpeedPx, LifetimeFrames, DirectHP, ExplosionHP, ExplosionRadiusPx, BounceEnergy = 1f, LowVelocityPx, AirFriction, KnockbackForce;
        public int Bounces, FireRateWait, Price;
        public float SpreadDeg, CritChance;
        public bool CollideWorld = true, Pierce, ExplodeOnDeath, ExplodeOnLifetime, Rotate = true, Additive;
        public string Sprite, TrailMaterial, ExplosionSprite, BounceFx, DamageType = "projectile";

        // Magicraft numbers (level 1)
        public float Speed, Duration, Damage, ExplosionDamage, ExplosionRadius, Knockback, CastDelay;
        public int Mp, Coins;

        public bool Explodes => ExplosionHP > 0f && (ExplodeOnDeath || ExplodeOnLifetime);
    }

    internal sealed class NoitaModifier
    {
        public ModifierRow Row;
        public NoitaData.Action Action;
        public float DamageAddMagicraft, CastDelay;
        public int Coins;
    }

    internal static class NoitaDefs
    {
        public static readonly Dictionary<int, NoitaSpell> Spells = new Dictionary<int, NoitaSpell>();       // by newBase
        public static readonly Dictionary<int, NoitaModifier> Modifiers = new Dictionary<int, NoitaModifier>(); // by newBase
        public static readonly List<string> Problems = new List<string>();

        public static float S(string key) => Sheets.Scaling.TryGetValue(key, out float v) ? v : throw new KeyNotFoundException("scaling." + key);

        public static float DamageFromHP(float hp)
        {
            if (hp <= 0f) return 0f;
            return S("damageBase") * (float)Math.Pow(hp / 3f, S("damageExponent"));
        }

        public static int ManaFrom(int mana) => mana <= 0 ? 0 : Math.Max(1, (int)Math.Round(S("manaBase") * Math.Pow(mana / 5.0, S("manaExponent"))));

        public static void Build()
        {
            foreach (SpellRow r in Sheets.Spells)
            {
                try { var s = Spell(r); if (s != null) Spells[r.NewBase] = s; }
                catch (Exception e) { Problems.Add(r.NoitaId + ": " + e.Message); }
            }
            foreach (ModifierRow r in Sheets.Modifiers)
            {
                NoitaData.Action a;
                if (!NoitaData.Actions.TryGetValue(r.NoitaId, out a)) { Problems.Add(r.NoitaId + ": not in gun_actions.lua"); continue; }
                Modifiers[r.NewBase] = new NoitaModifier
                {
                    Row = r, Action = a,
                    DamageAddMagicraft = Math.Sign(r.DamageAdd) * DamageFromHP(Math.Abs(r.DamageAdd)),
                    CastDelay = r.CastDelay * S("frameSeconds"),
                    Coins = Math.Max(1, (int)Math.Round(a.Price * S("priceScale")))
                };
            }
            foreach (string p in Problems) Plugin.Log.LogWarning("Noita spell: " + p);
        }

        static NoitaSpell Spell(SpellRow r)
        {
            NoitaData.Action a;
            if (!NoitaData.Actions.TryGetValue(r.NoitaId, out a)) throw new Exception("not in gun_actions.lua");
            var s = new NoitaSpell { Row = r, Action = a };
            string body = a.Body;
            Match pm = Regex.Match(body, "add_projectile\\w*\\(\\s*\"([^\"]+)\"");
            if (!pm.Success) throw new Exception("adds no projectile");
            s.ProjectileXml = pm.Groups[1].Value;
            s.FireRateWait = Frames(body, "fire_rate_wait");
            s.SpreadDeg = Add(body, "spread_degrees");
            s.CritChance = Add(body, "damage_critical_chance");
            s.Price = a.Price;

            Dictionary<string, string> proj = Component(s.ProjectileXml, "ProjectileComponent");
            Dictionary<string, string> expl = Component(s.ProjectileXml, "config_explosion");
            Dictionary<string, string> vel = Component(s.ProjectileXml, "VelocityComponent");
            Dictionary<string, string> types = Component(s.ProjectileXml, "damage_by_type");
            float speedMin = F(proj, "speed_min", 0), speedMax = F(proj, "speed_max", speedMin);
            s.SpeedPx = r.ThrowSpeed > 0 ? r.ThrowSpeed : (speedMin + speedMax) * 0.5f;
            s.LifetimeFrames = F(proj, "lifetime", 60);
            float typed = 0f; string best = null; float bestV = 0f;
            foreach (KeyValuePair<string, string> t in types)
            {
                float v = Num(t.Value);
                typed += v;
                if (v > bestV) { bestV = v; best = t.Key; }
            }
            if (best != null) s.DamageType = best;
            s.DirectHP = (F(proj, "damage", 0) + typed) * 25f;
            s.ExplosionHP = F(expl, "damage", 0) * 25f;
            s.ExplosionRadiusPx = F(expl, "explosion_radius", 0);
            s.ExplosionSprite = Str(expl, "explosion_sprite");
            s.Bounces = (int)F(proj, "bounces_left", 0);
            s.BounceEnergy = s.Bounces > 0 ? F(proj, "bounce_energy", 0.5f) : 1f;
            s.LowVelocityPx = F(proj, "die_on_low_velocity", 0) > 0 || proj.ContainsKey("die_on_low_velocity_limit") ? F(proj, "die_on_low_velocity_limit", 0) : 0f;
            s.CollideWorld = F(proj, "collide_with_world", 1) > 0;
            s.Pierce = F(proj, "penetrate_entities", 0) > 0;
            s.ExplodeOnDeath = F(proj, "on_death_explode", 1) > 0;
            s.ExplodeOnLifetime = F(proj, "on_lifetime_out_explode", 1) > 0;
            s.KnockbackForce = F(proj, "knockback_force", 0);
            s.Rotate = F(proj, "velocity_sets_rotation", 1) > 0;
            s.AirFriction = F(vel, "air_friction", 0);
            s.BounceFx = Str(proj, "bounce_fx_file");

            Dictionary<string, string> sprite = Component(s.ProjectileXml, "SpriteComponent");
            s.Sprite = Str(sprite, "image_file");
            if (string.IsNullOrEmpty(s.Sprite)) s.Sprite = Str(Component(s.ProjectileXml, "PhysicsImageShapeComponent"), "image_file");
            s.Additive = F(sprite, "additive", 0) > 0;
            s.TrailMaterial = Str(Component(s.ProjectileXml, "ParticleEmitterComponent"), "emitted_material_name");

            // Magicraft numbers
            float frame = S("frameSeconds");
            s.Speed = s.SpeedPx * S("speedScale");
            s.Duration = Math.Min(S("maxDuration"), Math.Max(S("minDuration"), s.LifetimeFrames * frame));
            s.Damage = DamageFromHP(s.DirectHP);
            s.ExplosionDamage = DamageFromHP(s.ExplosionHP);
            s.ExplosionRadius = Math.Min(S("maxExplosionRadius"), s.ExplosionRadiusPx * S("pixelToMeter"));
            s.Knockback = s.KnockbackForce * S("knockbackScale");
            s.CastDelay = s.FireRateWait * frame;
            s.Mp = ManaFrom(a.Mana);
            s.Coins = Math.Max(1, (int)Math.Round(a.Price * S("priceScale")));
            return s;
        }

        // ---- reading Noita's entity XML: a component's attributes, its <Base file> first, then the file's own ----
        static readonly Dictionary<string, string> xmlCache = new Dictionary<string, string>();

        static string Xml(string path)
        {
            string x;
            if (!xmlCache.TryGetValue(path, out x)) xmlCache[path] = x = NoitaData.Has(path) ? Encoding.UTF8.GetString(NoitaData.Read(path)) : "";
            return x;
        }

        public static Dictionary<string, string> Component(string path, string tag, int depth = 0)
        {
            var result = new Dictionary<string, string>();
            string x = Xml(path);
            if (depth < 4)
                foreach (Match b in Regex.Matches(x, "<Base\\s+file=\"([^\"]+)\""))
                    foreach (KeyValuePair<string, string> kv in Component(b.Groups[1].Value, tag, depth + 1)) result[kv.Key] = kv.Value;
            Match m = Regex.Match(x, "<" + tag + "\\b([^>]*)>", RegexOptions.Singleline);
            if (m.Success)
                foreach (Match at in Regex.Matches(m.Groups[1].Value, "([\\w.]+)\\s*=\\s*\"([^\"]*)\"")) result[at.Groups[1].Value] = at.Groups[2].Value;
            return result;
        }

        static float Num(string v) { float f; return float.TryParse(v?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : 0f; }
        static float F(Dictionary<string, string> d, string k, float def) { string v; return d.TryGetValue(k, out v) && v.Trim() != "" ? Num(v) : def; }
        static string Str(Dictionary<string, string> d, string k) { string v; return d.TryGetValue(k, out v) ? v.Trim() : ""; }

        // "c.fire_rate_wait = c.fire_rate_wait + 3" or "c.fire_rate_wait = 20"
        static int Frames(string body, string field)
        {
            Match add = Regex.Match(body, "c\\." + field + "\\s*=\\s*c\\." + field + "\\s*([+-])\\s*([\\d.]+)");
            if (add.Success) return (int)Math.Round(Num(add.Groups[2].Value) * (add.Groups[1].Value == "-" ? -1 : 1));
            Match set = Regex.Match(body, "c\\." + field + "\\s*=\\s*([\\d.]+)");
            return set.Success ? (int)Math.Round(Num(set.Groups[1].Value)) : 0;
        }

        static float Add(string body, string field)
        {
            Match add = Regex.Match(body, "c\\." + field + "\\s*=\\s*c\\." + field + "\\s*([+-])\\s*([\\d.]+)");
            return add.Success ? Num(add.Groups[2].Value) * (add.Groups[1].Value == "-" ? -1 : 1) : 0f;
        }

        // ---- text shown in the spell's tooltip, under Noita's own description ----
        public static string RulesText(NoitaSpell s)
        {
            var sb = new StringBuilder();
            Action<string> line = t => { if (sb.Length > 0) sb.Append("\\"); sb.Append(t); };
            if (s.Row.Copies > 1) line("Casts " + s.Row.Copies + " projectiles");
            if (s.Bounces > 0) line("Bounces " + s.Bounces + " times" + (s.BounceEnergy < 0.999f ? ", losing " + Math.Round((1 - s.BounceEnergy) * 100) + "% speed each time" : ""));
            if (s.LowVelocityPx > 0) line("Stops when it slows down too much");
            if (s.AirFriction > 0.05f) line("Slows down in flight");
            if (s.AirFriction < -0.05f) line("Speeds up in flight");
            if (s.Pierce) line("Passes through enemies");
            if (!s.CollideWorld) line("Passes through walls");
            if (s.Explodes) line("Explodes: " + Math.Round(s.ExplosionDamage) + " damage, " + s.ExplosionRadius.ToString("0.#", CultureInfo.InvariantCulture) + " m radius");
            if (s.Row.Special == "teleport") line("Teleports you to where it ends");
            if (s.Row.Special == "blackhole") line("Pulls enemies in and grinds them");
            if (s.DamageType != "projectile" && s.DirectHP > 0) line(char.ToUpper(s.DamageType[0]) + s.DamageType.Substring(1) + " damage");
            return sb.ToString();
        }

        public static string RulesText(NoitaModifier m)
        {
            ModifierRow r = m.Row;
            var parts = new List<string>();
            if (r.AddBounces > 0) parts.Add("Bounces +" + r.AddBounces);
            if (r.Homing) parts.Add("Homes in on enemies");
            if (r.AddPierce > 0) parts.Add("Passes through enemies");
            if (Math.Abs(m.DamageAddMagicraft) > 0.01f) parts.Add("Damage " + (m.DamageAddMagicraft > 0 ? "+" : "") + Math.Round(m.DamageAddMagicraft));
            if (Math.Abs(r.SpeedMul - 1f) > 0.01f) parts.Add("Speed x" + r.SpeedMul.ToString("0.##", CultureInfo.InvariantCulture));
            if (Math.Abs(r.SpreadAdd) > 0.01f) parts.Add("Spread " + (r.SpreadAdd > 0 ? "+" : "") + r.SpreadAdd + "°");
            if (r.FreezeSec > 0) parts.Add("Freezes for " + r.FreezeSec.ToString("0.#", CultureInfo.InvariantCulture) + " s");
            if (r.Electric) parts.Add("Electrifies what it hits");
            if (r.Copies > 1) parts.Add("Casts the next projectile " + r.Copies + " times");
            if (r.CastDelay != 0) parts.Add("Cast delay +" + (r.CastDelay / 60f).ToString("0.##", CultureInfo.InvariantCulture) + " s");
            parts.Add("Works on the next projectile spell");
            return string.Join("\\", parts.ToArray());
        }
    }
}
