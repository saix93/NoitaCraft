using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace NoitaCraft
{
    // Where a cast becomes Magicraft spell entities: Noita spells get their own entity (prefab "Spell_<newBase>") and Noita's numbers,
    // and Noita modifiers change the next projectile (Noita's or Magicraft's).
    internal static class NoitaCast
    {
        delegate SpellSpawnParams SipToSspFn(ShootSpellUtils.SpellShootInfo info, SpellInitialParameter sip, Entity aroundCenter, Entity player, EntityManager em);
        static SipToSspFn sipToSsp;

        public struct Pending { public int Id; public float DamageRatio; public float Time; }
        public static readonly List<Pending> Casts = new List<Pending>();
        public static SpellSpawnParams Template;
        public static bool HasTemplate;
        public static int CastCount;

        public static void Patch(Harmony h)
        {
            MethodInfo sip = AccessTools.Method(typeof(ShootSpellUtils), "SipToSSP");
            sipToSsp = (SipToSspFn)Delegate.CreateDelegate(typeof(SipToSspFn), sip);
            h.Patch(AccessTools.Method(typeof(ShootSpellUtils), "ShootToDots"), prefix: new HarmonyMethod(typeof(NoitaCast), nameof(ShootToDots)));
        }

        // Same as Magicraft's ShootToDots, with Noita's changes made to each spawn.
        static bool ShootToDots(IEnumerable<ValueTuple<ShootSpellUtils.SpellShootInfo, SpellInitialParameter>> shootInfos)
        {
            try
            {
                EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
                PlayerController pc = PlayerMgr.Inst.PlayerCtrller;
                var all = new List<SpellSpawnParams>();
                foreach (ValueTuple<ShootSpellUtils.SpellShootInfo, SpellInitialParameter> e in shootInfos)
                {
                    SpellSpawnParams ssp = sipToSsp(e.Item1, e.Item2, pc.PlayerAroundCenterEtt, PlayerMgr.Inst.PlayerEtt, em);
                    int copies = Apply(e.Item1, ref ssp);
                    for (int k = 0; k < copies; k++)
                    {
                        SpellSpawnParams c = ssp;
                        if (copies > 1) c.MovementComponentData.Direction = Rotate(ssp.MovementComponentData.Direction, (k - (copies - 1) * 0.5f) * 8f);
                        all.Add(c);
                    }
                }
                SpellShootSystem.Shoot(all);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Noita cast: " + ex);
                return true; // let Magicraft cast it as it would
            }
            return false;
        }

        static int Apply(ShootSpellUtils.SpellShootInfo info, ref SpellSpawnParams ssp)
        {
            int copies = 1;
            NoitaSpell s;
            bool noita = NoitaDefs.Spells.TryGetValue(info.SpellId / 10, out s);
            float baseDamage = ssp.ConfigComponentData.Damage.Calculate();
            if (noita)
            {
                if (NoitaWorld.EnsurePrefab(s.Row.NewBase)) ssp.PrefabId = s.Row.NewBase;
                ssp.DisableShootSound = true;
                ssp.MovementComponentData.ReboundCount += s.Bounces;
                if (!s.CollideWorld) ssp.MovementComponentData.IsIgnoreWall = true;
                if (s.Pierce) { ssp.ConfigComponentData.Penetrate.Extra += 999; ssp.BonusPenetrate += 999; }
                if (s.Row.Special == "teleport") ssp.SpellEndTeleport = true;
                if (s.Row.Special == "blackhole")
                {
                    ssp.ConfigComponentData.GravitationalAttackRange = 3.5f;
                    ssp.ConfigComponentData.GravitationalAttackPullForce = 10f;
                    ssp.ConfigComponentData.GravitationalMaxApplyCount = 30;
                    ssp.ConfigComponentData.DamageInterval = 0.25f;
                    ssp.ConfigComponentData.Penetrate.Extra += 999;
                    ssp.MovementComponentData.IsIgnoreWall = true;
                }
                switch (s.DamageType)
                {
                    case "fire": ssp.ElementComponentData.FireBurnDuration = Mathf.Max(ssp.ElementComponentData.FireBurnDuration, 3f); ssp.ElementComponentData.FireHpBurnPercent = Mathf.Max(ssp.ElementComponentData.FireHpBurnPercent, 1f); break;
                    case "ice": ssp.ElementComponentData.FrozenDuration = Mathf.Max(ssp.ElementComponentData.FrozenDuration, 1f); break;
                    case "electricity": ssp.ElementComponentData.ThunderHitRadius = Mathf.Max(ssp.ElementComponentData.ThunderHitRadius, 1.5f); ssp.ElementComponentData.ThunderHitDamageRatio = Mathf.Max(ssp.ElementComponentData.ThunderHitDamageRatio, 0.5f); break;
                }
            }
            if (info.ShootData != null && info.ShootData.EnhanceList != null)
                foreach (SlotData e in info.ShootData.EnhanceList)
                {
                    NoitaModifier m;
                    if (e == null || !NoitaDefs.Modifiers.TryGetValue(e.id / 10, out m)) continue;
                    ModifierRow r = m.Row;
                    ssp.MovementComponentData.ReboundCount += r.AddBounces;
                    if (r.Homing)
                    {
                        ssp.MovementComponentData.Type = SpellSpecialMovementType.ChaseEnemy;
                        ssp.MovementComponentData.ChaseRotateSpeed = Mathf.Max(ssp.MovementComponentData.ChaseRotateSpeed, 360f);
                    }
                    if (r.AddPierce > 0) { ssp.ConfigComponentData.Penetrate.Extra += r.AddPierce; ssp.BonusPenetrate += r.AddPierce; }
                    ssp.ConfigComponentData.Damage.AddBase += m.DamageAddMagicraft;
                    if (ssp.ConfigComponentData.Damage.Base + ssp.ConfigComponentData.Damage.AddBase < 0) ssp.ConfigComponentData.Damage.AddBase = -ssp.ConfigComponentData.Damage.Base;
                    ssp.ConfigComponentData.Damage.MulRatio *= r.DamageMul;
                    ssp.MovementComponentData.Speed *= r.SpeedMul;
                    ssp.SpeedAttribute.MulRatio *= r.SpeedMul;
                    if (r.FreezeSec > 0) ssp.ElementComponentData.FrozenDuration = Mathf.Max(ssp.ElementComponentData.FrozenDuration, r.FreezeSec);
                    if (r.Electric) { ssp.ElementComponentData.ThunderHitRadius = Mathf.Max(ssp.ElementComponentData.ThunderHitRadius, 1.5f); ssp.ElementComponentData.ThunderHitDamageRatio = Mathf.Max(ssp.ElementComponentData.ThunderHitDamageRatio, 0.5f); }
                    if (r.SpreadAdd < 0)
                    {
                        // tighten toward where the player aimed
                        float3 aim = new float3(ssp.SourceShootDir.x, ssp.SourceShootDir.y, 0f);
                        if (math.lengthsq(aim) > 0.0001f) ssp.MovementComponentData.Direction = math.normalize(math.lerp(ssp.MovementComponentData.Direction, math.normalize(aim), Mathf.Clamp01(-r.SpreadAdd / 60f)));
                    }
                    else if (r.SpreadAdd > 0) ssp.MovementComponentData.Direction = Rotate(ssp.MovementComponentData.Direction, UnityEngine.Random.Range(-r.SpreadAdd, r.SpreadAdd) * 0.5f);
                    copies *= Math.Max(1, r.Copies);
                    NoitaWorld.ModifierUsed(r.NoitaId);
                }
            if (noita || copies > 1)
            {
                float after = ssp.ConfigComponentData.Damage.Calculate();
                Casts.Add(new Pending { Id = ssp.ConfigComponentData.Id, DamageRatio = baseDamage > 0.01f ? after / baseDamage : 1f, Time = Time.time });
                if (Casts.Count > 64) Casts.RemoveAt(0);
                Template = ssp;
                HasTemplate = true;
                CastCount++;
                if (noita) NoitaAudio.Play(s.Row.ShootSound, 0.8f);
            }
            return copies;
        }

        public static float3 Rotate(float3 d, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), sn = Mathf.Sin(r);
            return new float3(d.x * c - d.y * sn, d.x * sn + d.y * c, d.z);
        }

        // Noita's explosion as a Magicraft area hit: a still, unseen copy of the plain bullet scaled to the explosion's radius,
        // hitting every enemy inside once (player spells never hurt the player). Noita's explosion sprite is drawn by NoitaFx.
        public static void Explode(float3 position, float damage, float radius, float knockback, NoitaSpell spell)
        {
            if (!HasTemplate || !NoitaWorld.EnsurePrefab(Hooks.ExplosionBase)) return;
            SpellSpawnParams e = Template;
            e.PrefabId = Hooks.ExplosionBase;
            e.SpawnPosition = position;
            e.DisableShootSound = true;
            e.IsSplitSpell = false;
            e.SpellEndTeleport = false;
            e.HalfLifeTeleportCount = 0;
            e.SplitComponentData = default;
            e.RefractionData = default;
            e.HitTriggerComponentData = default;
            e.OverTriggerComponentData = default;
            e.MoveTriggerComponentData = default;
            e.TwineTriggerComponentData = default;
            e.ElementComponentData = default;
            switch (spell.DamageType)
            {
                case "fire": e.ElementComponentData.FireBurnDuration = 3f; e.ElementComponentData.FireHpBurnPercent = 1f; break;
                case "ice": e.ElementComponentData.FrozenDuration = 1f; break;
                case "electricity": e.ElementComponentData.ThunderHitRadius = 1.5f; e.ElementComponentData.ThunderHitDamageRatio = 0.5f; break;
            }
            e.ForceScale = radius / Mathf.Max(0.05f, NoitaWorld.BulletRadius > 0f ? NoitaWorld.BulletRadius : 0.25f);
            e.SpellExtraSizeRatio = 0f;
            e.BonusPenetrate = 999;
            SpellMovementComponentData mv = e.MovementComponentData;
            mv.Type = SpellSpecialMovementType.Normal;
            mv.Speed = 0f;
            mv.ReboundCount = 0;
            mv.IsIgnoreWall = true;
            mv.IsFallSpell = false;
            e.MovementComponentData = mv;
            e.SpeedAttribute = new AttributeValue { Base = 0f, MulRatio = 1f };
            SpellConfigComponentData c = e.ConfigComponentData;
            c.Id = Hooks.ExplosionId;
            c.Level = 1;
            c.AbilityType = (SpellAbilityType)Hooks.ExplosionBase;
            c.Damage = new AttributeValue { Base = damage, MulRatio = 1f };
            c.Radius = new RadiusAttributeValue(radius);
            c.Knockback = knockback;
            c.Penetrate = new PenetrateValue(999);
            c.Duration = new AttributeValue { Base = 0.12f, MulRatio = 1f };
            c.DurationTimer = 0f;
            c.DamageInterval = 0f;
            c.GravitationalAttackRange = 0f;
            c.HoverDuration = 0f;
            e.ConfigComponentData = c;
            SpellShootSystem.Shoot(e);
        }
    }
}
