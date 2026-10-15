using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace NoitaCraft
{
    // Every patch here is a row in sheets/hooks.json.
    internal static class Hooks
    {
        static readonly MethodInfo clone = AccessTools.Method(typeof(object), "MemberwiseClone");
        static T Clone<T>(T o) => (T)clone.Invoke(o, null);

        public static readonly HashSet<int> SpellBases = new HashSet<int>(Sheets.Spells.Select(r => r.NewBase).Concat(Sheets.Modifiers.Select(r => r.NewBase)));
        public static readonly HashSet<int> WandIds = new HashSet<int>(Sheets.Wands.Select(w => w.WandId));
        public static bool IsNoitaSpell(int id) => SpellBases.Contains(id / 10) || id / 10 == ExplosionBase;
        public static readonly List<int> AddedSpells = new List<int>();
        public static bool WandsAdded, SetAdded;
        // a hidden Magicraft spell row for Noita's explosions (never dropped)
        public const int ExplosionBase = 1890, ExplosionId = 18901;

        // Magicraft text field -> Noita translation column
        static readonly string[][] languages =
        {
            new[] { "chineseS", "zh-cn" }, new[] { "chineseT", "zh-cn" }, new[] { "english", "en" }, new[] { "japanese", "jp" },
            new[] { "german", "de" }, new[] { "korean", "ko" }, new[] { "russian", "ru" }, new[] { "spanish_spain", "es-es" },
            new[] { "french", "fr-fr" }, new[] { "portuguese_brazil", "pt-br" }, new[] { "swedish", "en" }, new[] { "polish", "pl" },
            new[] { "turkish", "en" }, new[] { "italian", "it" }, new[] { "czech", "en" }, new[] { "chineseH", "zh-cn" }
        };

        // ---- spell-rows ----
        // Each Noita spell is a new Magicraft spell: its own id and ability type (Magicraft finds a wand slot's spell by ability type),
        // with Noita's numbers. A Magicraft row is only the blank form for fields Noita has no say in.
        public static void SpellRows(List<SpellConfig> result)
        {
            if (!NoitaData.Ready) return;
            var byId = new Dictionary<int, SpellConfig>();
            foreach (SpellConfig c in result) byId[c.id] = c;
            SpellConfig missileForm, enhanceForm;
            if (!byId.TryGetValue(10011, out missileForm) || !byId.TryGetValue(31021, out enhanceForm)) return;
            NoitaDefs.Build();
            float[] dmg = { 1f, NoitaDefs.S("level2Damage"), NoitaDefs.S("level3Damage") };
            float[] mp = { 1f, NoitaDefs.S("level2Mana"), NoitaDefs.S("level3Mana") };
            int[] price = { 1, 3, 9 };
            foreach (NoitaSpell s in NoitaDefs.Spells.Values)
                for (int level = 1; level <= 3; level++)
                {
                    int id = s.Row.NewBase * 10 + level;
                    if (byId.ContainsKey(id)) continue;
                    SpellConfig c = Clone(missileForm);
                    Blank(c);
                    c.id = id; c.level = level; c.icon = s.Row.Icon; c.iconH = ""; c.prefab = "";
                    c.abilityType = (SpellAbilityType)s.Row.NewBase;
                    c.useType = SpellType.Missile;
                    c.dropType = (ItemDropType)s.Row.DropType;
                    c.priceCoin = s.Coins * price[level - 1];
                    c.damage = (float)Math.Round(s.Damage * dmg[level - 1], 1);
                    c.mpCost = Math.Max(s.Mp > 0 ? 1 : 0, (int)Math.Round(s.Mp * mp[level - 1]));
                    c.speed = s.Speed;
                    c.duration = s.Duration;
                    c.knockback = s.Knockback;
                    c.radius = s.Explodes ? s.ExplosionRadius : 0f;
                    c.criticalChance = s.CritChance;
                    c.angle = s.SpreadDeg;
                    c.shootIntervalAddSubRevise = s.CastDelay;
                    c.shootCount = Math.Max(1, s.Row.Copies);
                    c.playShootSE = false; // Noita's own sound plays instead
                    if (s.Row.Special == "blackhole") { c.isDPS = true; c.DPSDamageInterval = 0.25f; c.damage = (float)Math.Round(Math.Max(c.damage, 6f * dmg[level - 1]), 1); }
                    result.Add(c);
                    AddedSpells.Add(id);
                }
            foreach (NoitaModifier m in NoitaDefs.Modifiers.Values)
                for (int level = 1; level <= 3; level++)
                {
                    int id = m.Row.NewBase * 10 + level;
                    if (byId.ContainsKey(id)) continue;
                    SpellConfig c = Clone(enhanceForm);
                    Blank(c);
                    c.id = id; c.level = level; c.icon = m.Row.Icon; c.iconH = ""; c.prefab = "";
                    c.abilityType = (SpellAbilityType)m.Row.NewBase;
                    c.useType = SpellType.Enhance;
                    c.dropType = (ItemDropType)m.Row.DropType;
                    c.priceCoin = m.Coins * price[level - 1];
                    c.mpCost = 0; // charged on the modified spell (ModifierMana)
                    c.shootIntervalAddSubRevise = m.CastDelay;
                    c.angle = m.Row.SpreadAdd;
                    result.Add(c);
                    AddedSpells.Add(id);
                }
            if (!byId.ContainsKey(ExplosionId))
            {
                // Noita's explosions: a still bullet of their own ability type, never dropped or shown
                SpellConfig e = Clone(missileForm);
                Blank(e);
                e.id = ExplosionId; e.level = 1; e.dropType = ItemDropType.None; e.icon = "noita_LIGHT_BULLET"; e.prefab = "";
                e.abilityType = (SpellAbilityType)ExplosionBase; e.useType = SpellType.Missile; e.canCompound = false;
                e.damage = 1f; e.duration = 0.12f; e.playShootSE = false;
                result.Add(e);
            }
            Plugin.Log.LogInfo("Noita spells added to Magicraft's spell table: " + NoitaDefs.Spells.Count + " spells, " + NoitaDefs.Modifiers.Count + " modifiers (" + AddedSpells.Count + " rows with levels)");
            int missing = Sheets.Spells.Count(r => !NoitaDefs.Spells.ContainsKey(r.NewBase)) + Sheets.Modifiers.Count(r => !NoitaDefs.Modifiers.ContainsKey(r.NewBase));
            TestRun.Report("spells-added", missing == 0, missing + " sheet rows were not added: " + string.Join("; ", NoitaDefs.Problems.ToArray()));
        }

        static void Blank(SpellConfig c)
        {
            c.needActivate = false; c.canCompound = true; c.isDPS = false; c.isKeepCasting = false; c.canCancelCasting = false; c.isParentTypeSpell = false;
            c.haveEffecforMissileSpell = false; c.haveEffecforSummonSpell = false; c.isSplitSpell = false;
            c.shootIntervalAddSubRevise = 0; c.coolDownAddSubRevise = 0; c.coolDownRatio = 1f; c.criticalChance = 0; c.angle = 0;
            c.slotCost = 1; c.slotNumModifyValue = 0; c.shootCount = 1; c.damage = 0; c.speed = 0; c.duration = 0; c.knockback = 0; c.recoil = 0;
            c.mpCostAddSubCorrection = 0; c.mpCostMulDivCorrection = 0; c.wandFinalDamageRatio = 100f; c.upSpeed = 0; c.gravity = 0; c.radius = 0;
            c.summonID = 0; c.summonLimit = 0; c.float1 = c.float2 = c.float3 = 0; c.int1 = c.int2 = c.int3 = 0; c.priceHP = 0;
        }

        // ---- modifier-mana ----
        // Magicraft charges only the projectile's MP; a Noita modifier adds its own mana, and its copies cost again.
        public static void ModifierMana(SpellShootData __instance, ref RatioValue __result)
        {
            if (__result == null || __instance.EnhanceList == null) return;
            foreach (SlotData e in __instance.EnhanceList)
            {
                NoitaModifier m;
                if (e == null || !NoitaDefs.Modifiers.TryGetValue(e.id / 10, out m)) continue;
                int mana = NoitaDefs.ManaFrom(m.Action.Mana);
                if (mana > 0) __result.AddBase(mana);
                if (m.Row.Copies > 1) __result.MulRatio(m.Row.Copies);
            }
        }

        // ---- wand-rows ----
        public static void WandRows(List<WandConfig> result)
        {
            if (!NoitaData.Ready) return;
            var byId = result.ToDictionary(w => w.id);
            foreach (WandRow r in Sheets.Wands)
            {
                WandConfig b;
                if (!byId.TryGetValue(r.CloneOf, out b) || byId.ContainsKey(r.WandId)) continue;
                WandConfig w = Clone(b);
                w.id = r.WandId;
                w.icon = r.Icon;
                w.iconH = "";
                w.priceCoin = b.priceCoin;
                int[] slots = TestRun.Active && r.WandId == Sheets.Wands[0].WandId ? TestRun.WandSlots : r.Slots;
                w.normalSlots = slots.Select(s => s == 0 ? null : new SlotData { id = s }).ToArray();
                w.normalSlotIsLock = new bool[slots.Length];
                w.postSlots = new SlotData[0];
                w.postSlotIsLock = new bool[0];
                result.Add(w);
            }
            WandsAdded = true;
        }

        // ---- set-rows ----
        public static void SetRows(List<SetConfig> result)
        {
            if (!NoitaData.Ready || result.Any(s => s.id == Sheets.Mina.SetId)) return;
            result.Add(new SetConfig
            {
                id = Sheets.Mina.SetId,
                WandIDs = Sheets.Mina.WandIds,
                relicID = Sheets.Mina.RelicId,
                unlockCost = 0,
                unlockInt1 = 0,
                upgradeCosts = Sheets.Mina.UpgradeCosts
            });
            SetAdded = true;
        }

        // ---- text-rows ----
        static readonly FieldInfo partField = AccessTools.Field(typeof(TextConfig.Initializer), "part");

        public static void TextRows(TextConfig.Initializer __instance, List<TextConfig> result)
        {
            if (!NoitaData.Ready) return;
            string part = (string)partField.GetValue(__instance);
            var byId = new Dictionary<int, TextConfig>();
            foreach (TextConfig t in result) byId[t.id] = t;
            Action<int, Func<string, string>> add = (id, text) =>
            {
                if (byId.ContainsKey(id)) return;
                var t = new TextConfig { id = id, applyAlogia = true };
                foreach (string[] l in languages) AccessTools.Field(typeof(TextConfig), l[0]).SetValue(t, text(l[1]) ?? "");
                result.Add(t);
                byId[id] = t;
            };
            Action<int, int> copy = (id, from) =>
            {
                TextConfig b;
                if (byId.ContainsKey(id) || !byId.TryGetValue(from, out b)) return;
                TextConfig t = Clone(b);
                t.id = id;
                result.Add(t);
                byId[id] = t;
            };
            if (part == "TextConfig_Spell")
            {
                foreach (int id in AddedSpells)
                {
                    int nb = id / 10, l1 = nb * 10 + 1;
                    NoitaSpell sp; NoitaModifier md;
                    NoitaData.Action a; string rules;
                    if (NoitaDefs.Spells.TryGetValue(nb, out sp)) { a = sp.Action; rules = NoitaDefs.RulesText(sp); }
                    else if (NoitaDefs.Modifiers.TryGetValue(nb, out md)) { a = md.Action; rules = NoitaDefs.RulesText(md); }
                    else continue;
                    add(7000000 + id, col => NoitaData.Text(a.NameKey, col));
                    // Noita's own description, then what it does here ("\" is Magicraft's line break)
                    add(7100000 + id, col => NoitaData.Text(a.DescKey, col) + (rules.Length > 0 ? "\\" + rules : ""));
                    add(7200000 + l1, col => NoitaData.Text(a.DescKey, col));
                    add(7300000 + l1, col => NoitaData.Text(a.DescKey, col));
                }
                add(7000000 + ExplosionId, col => "Explosion");
                add(7100000 + ExplosionId, col => "");
            }
            else if (part == "TextConfig_Wand")
            {
                foreach (WandRow w in Sheets.Wands)
                {
                    add(5000000 + w.WandId, col => NoitaData.Text(w.NameKey, col));
                    copy(5100000 + w.WandId, 5100000 + w.CloneOf);
                }
            }
            else if (part == "TextConfig_Set")
            {
                add(10000000 + Sheets.Mina.SetId, col => Sheets.Mina.Name + "/" + Sheets.Mina.Description);
                add(10100000 + Sheets.Mina.SetId, col => "");
            }
        }

        // ---- set-unlocked ----
        public static bool SetUnlocked(int id, ref bool __result)
        {
            if (id != Sheets.Mina.SetId || !SetAdded) return true;
            __result = true;
            return false;
        }

        public static void EnsureMinaUnlocked(WorldData w)
        {
            if (w != null && SetAdded && !w.setUnlockedSets.ContainsKey(Sheets.Mina.SetId)) w.setUnlockedSets[Sheets.Mina.SetId] = 1;
        }

        // ---- save-clean / save-restore ----
        // Mina's set lives in NoitaCraft's own file, so a save never depends on the mod.
        static string StatePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoitaCraft", "picked.txt");
        static readonly Dictionary<WorldData, int> stripped = new Dictionary<WorldData, int>();

        public static bool SaveBefore(WorldData worldData)
        {
            if (TestRun.Active) return false; // a Melty test never writes the player's saves
            if (worldData == null) return true;
            int level;
            bool mina = worldData.selectedSetID == Sheets.Mina.SetId;
            if (worldData.setUnlockedSets.TryGetValue(Sheets.Mina.SetId, out level))
            {
                worldData.setUnlockedSets.Remove(Sheets.Mina.SetId);
                stripped[worldData] = level;
            }
            if (mina) worldData.selectedSetID = 1;
            Remember(worldData, mina);
            return true;
        }

        public static void SaveAfter(WorldData worldData)
        {
            if (worldData == null) return;
            int level;
            if (stripped.TryGetValue(worldData, out level)) { worldData.setUnlockedSets[Sheets.Mina.SetId] = level; stripped.Remove(worldData); }
            if (Picked(worldData)) worldData.selectedSetID = Sheets.Mina.SetId;
        }

        static string Key(WorldData w) =>
            w == DataMgr.WorldData0 ? "world0" : w == DataMgr.WorldData1 ? "world1" : w == DataMgr.WorldData2 ? "world2" : null;

        static void Remember(WorldData w, bool mina)
        {
            string key = Key(w);
            if (key == null) return;
            try
            {
                var lines = File.Exists(StatePath) ? File.ReadAllLines(StatePath).Where(l => !l.StartsWith(key + "=")).ToList() : new List<string>();
                if (mina) lines.Add(key + "=mina");
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
                File.WriteAllLines(StatePath, lines.ToArray());
            }
            catch (Exception e) { Plugin.Log.LogWarning("couldn't keep Mina's pick: " + e.Message); }
        }

        static bool Picked(WorldData w)
        {
            string key = Key(w);
            try { return key != null && File.Exists(StatePath) && File.ReadAllLines(StatePath).Contains(key + "=mina"); }
            catch (Exception) { return false; }
        }

        public static void WorldSelected()
        {
            WorldData w = DataMgr.selectedWorldData;
            EnsureMinaUnlocked(w);
            if (w != null && SetAdded && Picked(w)) w.selectedSetID = Sheets.Mina.SetId;
        }

        public static bool BlockInTest() => !TestRun.Active;

        // ---- gallery ----
        public static bool Gallery(GalleryCategory category, int id)
        {
            if (category == GalleryCategory.Spell && IsNoitaSpell(id)) return false;
            if (category == GalleryCategory.Wand && WandIds.Contains(id)) return false;
            return true;
        }

        // ---- set-card ----
        public static void SetCard(UISetSlot __instance, int id)
        {
            if (id != Sheets.Mina.SetId) return;
            try { MinaLook.DressCard(__instance); }
            catch (Exception e) { Plugin.Log.LogError("Mina's card: " + e); }
        }

        // ---- player-look ----
        public static void PlayerLook()
        {
            MinaLook.Attach();
        }

        // ---- spell-sounds ----
        // Magicraft asks for "SE_Spell<prefab id><event>" sounds; Noita spells have none (Noita's own play instead). A missing sound
        // makes Magicraft's sound system throw before it clears its queue, so the error repeats every frame and the spell systems
        // eventually crash (held fire with Triplicate Bolt). Noita spells' requests are dropped, and any throw clears the queue.
        static EntityQuery seQuery, loopSeQuery;
        static World seWorld;
        public static int SoundRequestsDropped, SoundErrorsCaught;

        static bool OursSound(FixedString32Bytes name)
        {
            string s = name.ToString();
            if (!s.StartsWith("SE_Spell", StringComparison.Ordinal) || s.Length < 12) return false;
            int id;
            return int.TryParse(s.Substring(8, 4), out id) && (SpellBases.Contains(id) || id == ExplosionBase);
        }

        public static void SoundsBefore()
        {
            try
            {
                World w = World.DefaultGameObjectInjectionWorld;
                if (w != seWorld)
                {
                    seWorld = w;
                    seQuery = new EntityQueryBuilder(Allocator.Temp).WithAllRW<SEData>().WithOptions(EntityQueryOptions.IncludeSystems).Build(w.EntityManager);
                    loopSeQuery = new EntityQueryBuilder(Allocator.Temp).WithAllRW<LoopSEData>().WithOptions(EntityQueryOptions.IncludeSystems).Build(w.EntityManager);
                }
                if (seQuery.CalculateEntityCount() != 1) return;
                DynamicBuffer<SEData> buf = seQuery.GetSingletonBuffer<SEData>();
                for (int i = buf.Length - 1; i >= 0; i--)
                    if (OursSound(buf[i].SEName)) { buf.RemoveAt(i); SoundRequestsDropped++; }
            }
            catch (Exception e) { Plugin.Log.LogWarning("spell sounds: " + e.Message); }
        }

        public static Exception SoundsFinalizer(Exception __exception)
        {
            if (__exception == null) return null;
            SoundErrorsCaught++;
            if (SoundErrorsCaught <= 3) Plugin.Log.LogWarning("Magicraft sound system error caught, queue cleared: " + __exception.Message);
            try
            {
                if (seQuery.CalculateEntityCount() == 1) seQuery.GetSingletonBuffer<SEData>().Clear();
                if (loopSeQuery.CalculateEntityCount() == 1) loopSeQuery.GetSingletonBuffer<LoopSEData>().Clear();
            }
            catch (Exception) { }
            return null;
        }

        // ---- test-aim ----
        // Magicraft aims at Input.mousePosition (the real cursor); in a Melty test it aims where the test points.
        public static Vector3 MousePosition() => TestRun.Active && TestRun.AimActive ? TestRun.AimScreen : Input.mousePosition;

        public static IEnumerable<CodeInstruction> TestAim(IEnumerable<CodeInstruction> code)
        {
            MethodInfo real = AccessTools.PropertyGetter(typeof(Input), nameof(Input.mousePosition));
            foreach (CodeInstruction c in code)
            {
                if (c.Calls(real)) c.operand = AccessTools.Method(typeof(Hooks), nameof(MousePosition));
                yield return c;
            }
        }

        // ---- cast oracle (test) ----
        public static void Shot(SpellShootGroup group)
        {
            if (!TestRun.Active || group == null) return;
            foreach (SpellShootData s in group.Shoots)
                if (s != null && s.Spell != null)
                {
                    TestRun.AnyCast(s.Spell.id);
                    if (IsNoitaSpell(s.Spell.id)) TestRun.NoitaCast(s.Spell.id);
                }
        }

        // ---- test-keeps-running ----
        // Magicraft pauses itself in the background; a Melty test runs with its window behind the creator's, so it keeps going.
        public static void RunInBackground(bool value)
        {
            Application.runInBackground = value || TestRun.Active;
        }

        static readonly MethodInfo setRunInBackground = AccessTools.PropertySetter(typeof(Application), nameof(Application.runInBackground));

        public static IEnumerable<CodeInstruction> KeepRunning(IEnumerable<CodeInstruction> code)
        {
            foreach (CodeInstruction c in code)
            {
                if (c.Calls(setRunInBackground)) c.operand = AccessTools.Method(typeof(Hooks), nameof(RunInBackground));
                yield return c;
            }
        }

        public static void Patch(Harmony h)
        {
            Func<Type, string, MethodInfo> m = (t, n) => AccessTools.Method(t, n) ?? throw new MissingMethodException(t.Name, n);
            Func<string, HarmonyMethod> hm = n => new HarmonyMethod(typeof(Hooks), n);
            h.Patch(m(typeof(SpellConfig.Initializer), "ApplyResult"), prefix: hm(nameof(SpellRows)));
            h.Patch(m(typeof(WandConfig.Initializer), "ApplyResult"), prefix: hm(nameof(WandRows)));
            h.Patch(m(typeof(SetConfig.Initializer), "ApplyResult"), prefix: hm(nameof(SetRows)));
            h.Patch(m(typeof(TextConfig.Initializer), "ApplyResult"), prefix: hm(nameof(TextRows)));
            h.Patch(m(typeof(WorldData), "IsSetUnlocked"), prefix: hm(nameof(SetUnlocked)));
            h.Patch(m(typeof(DataMgr), "SaveWorldData"), prefix: hm(nameof(SaveBefore)), postfix: hm(nameof(SaveAfter)));
            h.Patch(m(typeof(DataMgr), "SetSelectedWorldData"), postfix: hm(nameof(WorldSelected)));
            h.Patch(m(typeof(WorldData), "GalleryUnlock"), prefix: hm(nameof(Gallery)));
            h.Patch(m(typeof(UISetSlot), "Initialize"), postfix: hm(nameof(SetCard)));
            h.Patch(AccessTools.Method(typeof(PlayerSkinMgr), "SetSkin", new[] { typeof(Spine.Skeleton), typeof(global::PlayerLook), typeof(List<RelicConfig>), typeof(bool), typeof(bool) }), postfix: hm(nameof(PlayerLook)));
            h.Patch(m(typeof(ShootSpellUtils), "ShootSpellGroup"), prefix: hm(nameof(Shot)));
            h.Patch(m(typeof(SpellShootData), "GetSpellManaCost"), postfix: hm(nameof(ModifierMana)));
            NoitaCast.Patch(h);
            h.Patch(m(typeof(SEPlayerSystem), "OnUpdate"), prefix: hm(nameof(SoundsBefore)), finalizer: hm(nameof(SoundsFinalizer)));
            h.Patch(m(typeof(PlayerController), "Update"), transpiler: hm(nameof(TestAim)));
            foreach (Type nested in typeof(GameMgr).GetNestedTypes(AccessTools.all))
            {
                MethodInfo next = AccessTools.Method(nested, "MoveNext");
                if (next == null) continue;
                try
                {
                    if (PatchProcessor.GetOriginalInstructions(next).Any(c => c.Calls(setRunInBackground)))
                        h.Patch(next, transpiler: hm(nameof(KeepRunning)));
                }
                catch (Exception e) { Plugin.Log.LogWarning("run-in-background patch on " + nested.Name + ": " + e.Message); }
            }
            foreach (string save in new[] { "SaveSelectedWorldData", "SaveWorldDataBackup", "SaveBuildDatas", "SaveBuildBackUp", "SaveEndlessBuildDatas", "SaveEndlessBuildBackUp", "SaveSettingData" })
            {
                MethodInfo s = AccessTools.Method(typeof(DataMgr), save);
                if (s != null) h.Patch(s, prefix: hm(nameof(BlockInTest)));
            }
        }
    }
}
