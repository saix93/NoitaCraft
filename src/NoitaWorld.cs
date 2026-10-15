using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace NoitaCraft
{
    // Runs every frame after Magicraft's spell systems: gives each Noita projectile entity Noita's behavior and look.
    internal sealed class NoitaWorld : MonoBehaviour
    {
        sealed class Tracked
        {
            public NoitaSpell Spell;
            public int Level;
            public float DamageRatio = 1f;
            public NoitaFx.Projectile Fx;
            public int Rebounds;
            public float StartSpeed, Age;
            public float3 Position, Spawn;
            public bool Killed;
        }

        static readonly Dictionary<Entity, Tracked> tracked = new Dictionary<Entity, Tracked>();
        static readonly HashSet<Entity> seen = new HashSet<Entity>();
        static World world;
        static EntityQuery spells;
        static bool queryMade;
        public static readonly HashSet<string> Prefabbed = new HashSet<string>();
        public static float BulletRadius;
        public static int Projectiles, Bounces, SpeedLosses, Explosions, LowSpeedKills;
        static readonly HashSet<string> modifiersUsed = new HashSet<string>();

        public static void ModifierUsed(string id) => modifiersUsed.Add(id);
        public static int ModifiersUsed => modifiersUsed.Count;

        // Magicraft finds a spell's entity as Prefabs["Spell_<id/10>"]: each Noita spell gets its own copy of the plain bullet
        // (Spell_1001; explosions copy Arcane Explosion, Spell_1008), and Magicraft's own look for it is never registered.
        public static bool EnsurePrefab(int newBase)
        {
            try
            {
                EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
                using (EntityQuery q = new EntityQueryBuilder(Allocator.Temp).WithAll<SpellSingleton>().WithOptions(EntityQueryOptions.IncludeSystems).Build(em))
                {
                    if (q.CalculateEntityCount() == 0) return false;
                    SpellSingleton single = q.GetSingleton<SpellSingleton>();
                    var key = new FixedString128Bytes("Spell_" + newBase);
                    Entity have;
                    if (single.Prefabs.TryGetValue(key, out have) && em.Exists(have)) return true;
                    Entity template;
                    var from = new FixedString128Bytes("Spell_1001");
                    if (!single.Prefabs.TryGetValue(from, out template) || !em.Exists(template)) return false;
                    if (BulletRadius <= 0f && em.HasComponent<Unity.Physics.PhysicsCollider>(template))
                    {
                        Unity.Physics.PhysicsCollider pc = em.GetComponentData<Unity.Physics.PhysicsCollider>(template);
                        if (pc.IsValid)
                        {
                            Unity.Physics.Aabb box = pc.Value.Value.CalculateAabb();
                            float3 size = box.Max - box.Min;
                            BulletRadius = Mathf.Max(0.05f, Mathf.Max(size.x, size.y) * 0.5f);
                            Plugin.Log.LogInfo("Magicraft bullet hit radius " + BulletRadius.ToString("0.000"));
                        }
                    }
                    Entity clone = em.Instantiate(template);
                    if (em.HasBuffer<LinkedEntityGroup>(clone))
                    {
                        DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(clone);
                        var members = new NativeArray<Entity>(group.Length, Allocator.Temp);
                        for (int i = 0; i < group.Length; i++) members[i] = group[i].Value;
                        foreach (Entity m in members)
                        {
                            if (!em.HasComponent<Prefab>(m)) em.AddComponent<Prefab>(m);
                            // Noita's look is drawn by NoitaFx; the Magicraft spell's own mesh stays hidden
                            if (!em.HasComponent<Unity.Rendering.DisableRendering>(m)) em.AddComponent<Unity.Rendering.DisableRendering>(m);
                        }
                        members.Dispose();
                    }
                    if (!em.HasComponent<Prefab>(clone)) em.AddComponent<Prefab>(clone);
                    if (!em.HasComponent<Unity.Rendering.DisableRendering>(clone)) em.AddComponent<Unity.Rendering.DisableRendering>(clone);
                    single.Prefabs.Remove(key);
                    single.Prefabs.Add(key, clone);
                    if (Prefabbed.Add(key.ToString()))
                    {
                        var types = em.GetComponentTypes(template, Allocator.Temp);
                        var names = new List<string>();
                        foreach (ComponentType ct in types) names.Add(ct.GetManagedType()?.Name ?? ct.ToString());
                        types.Dispose();
                        Plugin.Log.LogInfo("Noita spell entity " + key + " made from " + from + ": " + string.Join(", ", names));
                    }
                    return true;
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Noita prefab " + newBase + ": " + e.Message); return false; }
        }

        void LateUpdate()
        {
            World w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated) return;
            if (w != world || !queryMade)
            {
                world = w;
                spells = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SpellConfigComponentData>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadWrite<SpellMovementComponentData>());
                queryMade = true;
                tracked.Clear();
            }
            EntityManager em = w.EntityManager;
            NativeArray<Entity> ents;
            try { ents = spells.ToEntityArray(Allocator.Temp); }
            catch (Exception) { queryMade = false; return; }
            seen.Clear();
            float dt = Time.deltaTime;
            float frameScale = NoitaDefs.S("speedScale");
            foreach (Entity e in ents)
            {
                SpellConfigComponentData cfg = em.GetComponentData<SpellConfigComponentData>(e);
                NoitaSpell s;
                if (!NoitaDefs.Spells.TryGetValue(cfg.Id / 10, out s)) continue;
                seen.Add(e);
                Tracked t;
                LocalTransform tr = em.GetComponentData<LocalTransform>(e);
                SpellMovementComponentData mv = em.GetComponentData<SpellMovementComponentData>(e);
                if (!tracked.TryGetValue(e, out t))
                {
                    t = new Tracked { Spell = s, Level = cfg.Level, StartSpeed = mv.Speed, Rebounds = mv.ReboundCount, Spawn = tr.Position };
                    for (int i = NoitaCast.Casts.Count - 1; i >= 0; i--)
                        if (NoitaCast.Casts[i].Id == cfg.Id) { t.DamageRatio = NoitaCast.Casts[i].DamageRatio; NoitaCast.Casts.RemoveAt(i); break; }
                    t.Fx = NoitaFx.Spawn(s, tr.Position);
                    tracked[e] = t;
                    Projectiles++;
                    if (Projectiles == 1) TestRun.Report("noita-projectile", t.Fx != null, "a Noita projectile had no Noita sprite");
                }
                t.Position = tr.Position;
                t.Age += dt;
                bool dying = em.HasComponent<SpellDestroyTag>(e) && em.IsComponentEnabled<SpellDestroyTag>(e);
                if (!dying && !t.Killed)
                {
                    bool changed = false;
                    // bounce: Noita keeps bounce_energy of the speed each time
                    if (mv.ReboundCount < t.Rebounds)
                    {
                        t.Rebounds = mv.ReboundCount;
                        Bounces++;
                        if (s.BounceEnergy < 0.999f) { mv.Speed *= s.BounceEnergy; SpeedLosses++; changed = true; }
                        NoitaFx.Bounce(s, tr.Position);
                        bounceSpots.Add(new float4(tr.Position, Time.time));
                        if (s.Row.BounceSound != "none") NoitaAudio.Play(s.Row.BounceSound, 0.5f);
                        if (Bounces == 1) TestRun.Report("noita-bounce", true, null);
                    }
                    // air friction: positive slows it, negative speeds it up (Magic Missile, Concentrated Light)
                    if (Mathf.Abs(s.AirFriction) > 0.001f && mv.Speed > 0f)
                    {
                        float v = mv.Speed * Mathf.Exp(-s.AirFriction * dt);
                        mv.Speed = Mathf.Min(v, t.StartSpeed * NoitaDefs.S("maxSpeedGain"));
                        changed = true;
                    }
                    if (changed) em.SetComponentData(e, mv);
                    // a thrown item (Bomb) rolls to a stop and waits for its fuse
                    if (s.Row.Special == "thrown" && mv.Speed > 0f) { mv.Speed *= Mathf.Exp(-1.5f * dt); em.SetComponentData(e, mv); }
                    // dies when too slow (Bouncing Burst, Disc, Arrow), and a spent projectile ends where it stopped (Firebolt)
                    bool spent = s.Row.Special != "thrown" && s.Row.Special != "blackhole" && t.StartSpeed > 0f && mv.Speed < t.StartSpeed * NoitaDefs.S("stopFraction");
                    if (t.Age > 0.1f && (spent || (s.LowVelocityPx > 0f && mv.Speed < s.LowVelocityPx * frameScale * 6f)))
                    {
                        t.Killed = true;
                        LowSpeedKills++;
                        if (em.HasComponent<SpellDestroyTag>(e)) em.SetComponentEnabled<SpellDestroyTag>(e, true);
                    }
                }
                if (t.Fx != null) t.Fx.Follow(tr.Position, mv.Direction, t.Spawn);
                if (dying) { Finish(e, t); }
            }
            ents.Dispose();
            RemoveMagicraftBounceEffects(em);
            // gone since last frame
            if (tracked.Count > seen.Count)
            {
                var gone = new List<Entity>();
                foreach (KeyValuePair<Entity, Tracked> kv in tracked) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (Entity e in gone) Finish(e, tracked[e]);
            }
        }

        // Magicraft spawns its own bounce effect (Prefabs["Spell_Rebound"]) on every wall bounce; where a Noita projectile bounced,
        // Noita's sparks are drawn instead, so that effect is removed there (Magicraft's spells keep theirs).
        static readonly List<float4> bounceSpots = new List<float4>();
        static EntityQuery reboundFx;
        static World reboundWorld;
        static int reboundTypeCount;
        public static int MagicraftBounceFxRemoved;

        static void RemoveMagicraftBounceEffects(EntityManager em)
        {
            float now = Time.time;
            bounceSpots.RemoveAll(b => now - b.w > 0.25f);
            if (bounceSpots.Count == 0) return;
            try
            {
                if (reboundWorld != world)
                {
                    reboundWorld = null;
                    using (EntityQuery q = new EntityQueryBuilder(Allocator.Temp).WithAll<SpellSingleton>().WithOptions(EntityQueryOptions.IncludeSystems).Build(em))
                    {
                        if (q.CalculateEntityCount() == 0) return;
                        Entity prefab;
                        if (!q.GetSingleton<SpellSingleton>().Prefabs.TryGetValue(new FixedString128Bytes("Spell_Rebound"), out prefab) || !em.Exists(prefab)) return;
                        var types = em.GetComponentTypes(prefab, Allocator.Temp);
                        reboundTypeCount = types.Length - 1; // an instance has everything but Prefab
                        var want = new List<ComponentType>();
                        var names = new List<string>();
                        foreach (ComponentType ct in types)
                        {
                            Type mt = ct.GetManagedType();
                            names.Add(mt?.Name ?? ct.ToString());
                            if (mt == typeof(Prefab) || mt == typeof(LinkedEntityGroup) || mt == typeof(SceneTag) || mt == typeof(SceneSection)) continue;
                            want.Add(ComponentType.ReadOnly(ct.TypeIndex));
                        }
                        types.Dispose();
                        want.Add(ComponentType.ReadOnly<LocalTransform>());
                        reboundFx = em.CreateEntityQuery(want.ToArray());
                        reboundWorld = world;
                        Plugin.Log.LogInfo("Magicraft bounce effect (Spell_Rebound): " + string.Join(", ", names));
                    }
                }
                NativeArray<Entity> fx = reboundFx.ToEntityArray(Allocator.Temp);
                foreach (Entity f in fx)
                {
                    // only Magicraft's bounce effect itself: exactly its components, nothing more
                    var have = em.GetComponentTypes(f, Allocator.Temp);
                    int n = have.Length;
                    have.Dispose();
                    if (n != reboundTypeCount) continue;
                    float3 p = em.GetComponentData<LocalTransform>(f).Position;
                    foreach (float4 b in bounceSpots)
                        if (math.distancesq(p.xy, b.xyz.xy) < 0.36f)
                        {
                            // through Magicraft's own clean-up when the effect has it
                            if (em.HasComponent<SpellDestroyTag>(f)) em.SetComponentEnabled<SpellDestroyTag>(f, true);
                            else em.DestroyEntity(f);
                            MagicraftBounceFxRemoved++;
                            break;
                        }
                }
                fx.Dispose();
            }
            catch (Exception e) { Plugin.Log.LogWarning("bounce effect: " + e.Message); bounceSpots.Clear(); }
        }

        static void Finish(Entity e, Tracked t)
        {
            tracked.Remove(e);
            NoitaSpell s = t.Spell;
            NoitaFx.Death(s, t.Position, t.Fx);
            NoitaAudio.Play(s.Row.HitSound, s.Explodes ? 1f : 0.7f);
            if (s.Explodes)
            {
                float[] lv = { 1f, NoitaDefs.S("level2Damage"), NoitaDefs.S("level3Damage") };
                float dmg = s.ExplosionDamage * lv[Mathf.Clamp(t.Level, 1, 3) - 1] * Mathf.Max(0.1f, t.DamageRatio);
                NoitaCast.Explode(t.Position, dmg, s.ExplosionRadius, s.Knockback, s);
                Explosions++;
                if (Explosions == 1) TestRun.Report("noita-explosion", true, null);
            }
        }
    }
}
