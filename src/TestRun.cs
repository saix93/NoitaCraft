using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoitaCraft
{
    // Melty test mode: only while %LOCALAPPDATA%\Melty\test\noitacraft-2.json exists. Picks Mina, starts a run, fires her Noita wand.
    // Nothing is saved during a test (Hooks.BlockInTest / SaveBefore).
    internal sealed class TestRun : MonoBehaviour
    {
        public static bool Active;
        public static int Run = 1;
        // test mode only: the group of Noita spells Mina's first wand holds this run (sheets/tests.json testWands)
        public static int[] WandSlots => Sheets.TestWands[(Math.Max(1, Run) - 1) % Sheets.TestWands.Length];
        static readonly HashSet<string> reported = new HashSet<string>();
        static readonly HashSet<int> cast = new HashSet<int>();

        public static void Init()
        {
            string test = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", "noitacraft-2.json");
            Active = File.Exists(test);
            if (Active)
            {
                var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(test), "\"run\"\\s*:\\s*(\\d+)");
                if (m.Success) Run = int.Parse(m.Groups[1].Value);
                WatchErrors();
            }
            MeltyTest.Start();
        }

        public static void Report(string check, bool passed, string why)
        {
            if (passed) Plugin.Log?.LogInfo("check " + check + ": pass");
            else Plugin.Log?.LogWarning("check " + check + ": " + why);
            lock (reported)
            {
                if (reported.Contains(check)) return;
                reported.Add(check);
            }
            MeltyTest.Report(check, passed, why);
        }

        public static void NoitaCast(int id)
        {
            if (cast.Add(id))
            {
                Plugin.Log.LogInfo("Noita spell fired: " + id);
                Report("noita-spell-cast", true, null);
            }
        }

        static string statePath;
        static float nextState;
        public static string Step = "starting";

        // about once a second during a test: what Melty's test_look shows as the mod's state
        public static void Tick()
        {
            if (!Active || Time.realtimeSinceStartup < nextState) return;
            nextState = Time.realtimeSinceStartup + 1f;
            try
            {
                if (statePath == null)
                {
                    string test = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", "noitacraft-2.json"));
                    var m = System.Text.RegularExpressions.Regex.Match(test, "\"state\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
                    statePath = m.Success ? System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value) : "";
                }
                if (statePath == "") return;
                PlayerMgr pm = PlayerMgr.Inst;
                Vector3 p = pm != null ? pm.transform.position : Vector3.zero;
                int pool = pm != null && pm.BaData != null ? pm.BaData.poolOfSpellID.Count(Hooks.IsNoitaSpell) : 0;
                string json = "{\"scene\":\"" + SceneManager.GetActiveScene().name + "\",\"step\":\"" + Step + "\",\"set\":" + (DataMgr.selectedWorldData != null ? DataMgr.selectedWorldData.selectedSetID : 0)
                    + ",\"minaShown\":" + (MinaLook.Shown ? "true" : "false") + ",\"noitaSpellsInPool\":" + pool + ",\"noitaSpellsFired\":" + cast.Count
                    + ",\"x\":" + p.x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ",\"y\":" + p.y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "}";
                File.WriteAllText(statePath, json);
            }
            catch (Exception) { }
        }

        void Start()
        {
            if (Active) StartCoroutine(Play());
        }

        static IEnumerator Wait(Func<bool> until, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!until() && Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Play()
        {
            // the main menu, with the saves loaded
            Step = "main menu";
            yield return Wait(() => UIMainMenuMgr.Inst != null && DataMgr.WorldData0 != null, 180f);
            yield return new WaitForSecondsRealtime(4f);
            Report("mina-set", SetConfig.dic != null && SetConfig.dic.ContainsKey(Sheets.Mina.SetId) && WandConfig.dic != null && Sheets.Wands.All(w => WandConfig.dic.ContainsKey(w.WandId)),
                "Mina's set or wands are missing from Magicraft's tables");
            WorldData[] worlds = { DataMgr.WorldData0, DataMgr.WorldData1, DataMgr.WorldData2 };
            int slot = Array.FindIndex(worlds, w => w != null && w.haveUsed);
            if (slot < 0)
            {
                Report("noita-in-pool", false, "no Magicraft save slot on this PC has finished the tutorial, so a run can't start");
                Report("noita-spell-cast", false, "no run started");
                Report("mina-look", MinaLook.Shown, "no run started");
                foreach (string c in Sheets.Checks.Concat(Sheets.Combos.Select(x => "combo-" + x.Id))) Report(c, false, "no run started");
                yield break;
            }
            DataMgr.SetSelectedWorldData(slot);
            WorldData world = DataMgr.selectedWorldData;
            Hooks.EnsureMinaUnlocked(world);
            world.inBattle10 = false;
            world.selectedSetID = Sheets.Mina.SetId;
            world.directEnterCampByLoadSave = true;
            Plugin.Log.LogInfo("test: save slot " + slot + " (not written), Mina picked, going to camp");
            Step = "camp";
            SceneManager.LoadScene("Camp");
            yield return Wait(() => SceneManager.GetActiveScene().name == "Camp" && PlayerMgr.Inst != null, 120f);
            yield return new WaitForSecondsRealtime(8f);
            MinaLook.Attach();
            yield return new WaitForSecondsRealtime(2f);
            MinaLook.Shown = false; // must be seen again in the run

            Plugin.Log.LogInfo("test: starting a run");
            GameMgr.Inst.ClearAllPool();
            world.timeuse = 1f;
            Step = "run";
            SceneManager.LoadScene("Battle");
            yield return Wait(() => SceneManager.GetActiveScene().name == "Battle" && PlayerMgr.Inst != null && PlayerMgr.Inst.BaData != null && PlayerMgr.Inst.BaData.poolOfSpellID.Count > 0, 180f);
            yield return new WaitForSecondsRealtime(6f);
            List<int> pool = PlayerMgr.Inst != null && PlayerMgr.Inst.BaData != null ? PlayerMgr.Inst.BaData.poolOfSpellID : new List<int>();
            int noita = pool.Count(Hooks.IsNoitaSpell), magicraft = pool.Count - noita;
            Report("noita-in-pool", noita > 0 && magicraft > 0, "spell pool has " + noita + " Noita and " + magicraft + " Magicraft spells");
            MinaLook.Attach();
            yield return Wait(() => MinaLook.Shown, 10f);
            Report("mina-look", MinaLook.Shown, "Mina's frames never drew on the player");
            // a wand slot holding Bouncing Burst must resolve to Bouncing Burst itself
            try
            {
                int id = Sheets.Spells.First(r => r.NoitaId == "RUBBER_BALL").NewBase * 10 + 1;
                var probe = new SlotData { id = id };
                SpellConfig shown = probe.GetConfigIgnoreMimic();
                string name = shown.GetName(false), noitaName = NoitaData.Text(NoitaData.Actions["RUBBER_BALL"].NameKey, "en");
                Plugin.Log.LogInfo("tooltip: slot " + id + " shows " + shown.id + " '" + name + "' icon " + shown.icon);
                Report("own-tooltip", shown.id == id && shown.icon == "noita_RUBBER_BALL", "slot " + id + " resolves to " + shown.id + " '" + name + "' (Noita: '" + noitaName + "')");
            }
            catch (Exception e) { Report("own-tooltip", false, e.GetType().Name + ": " + e.Message); }

            List<string> badSprites = NoitaFx.CheckAll();
            Plugin.Log.LogInfo("Noita sprites: " + (badSprites.Count == 0 ? "all " + NoitaDefs.Spells.Count + " spells' projectile and explosion sprites loaded" : "failed: " + string.Join("; ", badSprites.ToArray())));
            if (badSprites.Count > 0) Report("noita-projectile", false, "sprites that didn't load: " + string.Join("; ", badSprites.ToArray()));

            // Mina can't be hurt during a test, and walks up so all of her is on screen
            try { PlayerMgr.Inst.InvincibleRegister(); Invincible = true; } catch (Exception e) { Plugin.Log.LogWarning("test: invincible: " + e.Message); }
            float y0 = PlayerMgr.Inst.PlayerCtrller.transform.position.y;
            Step = "walking up";
            MeltyPlay.Hold("w", true);
            yield return new WaitForSecondsRealtime(0.9f);
            MeltyPlay.Hold("w", false);
            Plugin.Log.LogInfo("test: walked up " + (PlayerMgr.Inst.PlayerCtrller.transform.position.y - y0).ToString("0.00") + " m");

            // the wand turns toward the aim: Noita wand, then a Magicraft wand for comparison
            yield return WandLook("noita");
            int[] keep = SlotIds(0);
            WandConfig own = PlayerMgr.Inst.BaData.wandCfgs[0];
            PlayerMgr.Inst.BaData.wandCfgs[0] = WandConfig.GetConfig(11);
            Refresh();
            yield return WandLook("magicraft");
            PlayerMgr.Inst.BaData.wandCfgs[0] = own;
            Refresh();

            Step = "firing Mina's wand";
            float end = Time.realtimeSinceStartup + 120f;
            while (cast.Count == 0 && Time.realtimeSinceStartup < end) { Cast(); yield return null; }
            Report("noita-spell-cast", cast.Count > 0, "Mina's wand never fired a Noita spell in 2 minutes");
            // keep firing until bounces, an explosion and sounds were seen (at least 30 s, at most 2 minutes)
            end = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < end && !(NoitaWorld.Bounces > 0 && NoitaWorld.Explosions > 0 && NoitaAudio.Played > 0 && Time.realtimeSinceStartup > end - 90f)) { Cast(); yield return null; }
            MeltyPlay.Button(0, false);
            Plugin.Log.LogInfo("test: projectiles " + NoitaWorld.Projectiles + ", bounces " + NoitaWorld.Bounces + " (speed lost " + NoitaWorld.SpeedLosses + "), slow deaths " + NoitaWorld.LowSpeedKills
                + ", explosions " + NoitaWorld.Explosions + ", drawn " + NoitaFx.Drawn + "/" + NoitaFx.ExplosionsDrawn + ", Magicraft bounce fx removed " + NoitaWorld.MagicraftBounceFxRemoved + ", sounds " + NoitaAudio.Played + (NoitaAudio.Error != null ? " (" + NoitaAudio.Error + ")" : ""));
            Report("noita-projectile", NoitaWorld.Projectiles > 0 && NoitaFx.Drawn > 0, "no Noita projectile entity was seen (" + string.Join(",", NoitaWorld.Prefabbed) + ")");
            Report("noita-bounce", NoitaWorld.Bounces > 0, "no Noita projectile bounced");
            Report("noita-explosion", NoitaWorld.Explosions > 0, "no Noita explosion");
            Report("noita-sound", NoitaAudio.Played > 0, "no Noita sound played: " + (NoitaAudio.Error ?? "not decoded"));
            // Noita and Magicraft spells and modifiers mixed in one wand (sheets/tests.json combos), each held down
            foreach (var combo in Sheets.Combos)
            {
                Step = "combo " + combo.Id;
                SetSlots(0, combo.Slots);
                yield return new WaitForSecondsRealtime(0.5f);
                int errorsBefore = Errors, castsBefore = anyCast.Count;
                anyCast.Clear();
                float stop = Time.realtimeSinceStartup + combo.Seconds;
                while (Time.realtimeSinceStartup < stop) { Cast(); yield return null; }
                MeltyPlay.Button(0, false);
                yield return new WaitForSecondsRealtime(1.5f); // let what was cast land
                int newErrors = Errors - errorsBefore;
                var expected = combo.Slots.Where(s => s != 0 && SpellConfig.dic.ContainsKey(s) && SpellConfig.dic[s].useType == SpellType.Missile).ToArray();
                var missing = expected.Where(s => !anyCast.Contains(s)).ToArray();
                string why = (newErrors > 0 ? newErrors + " game errors, first: " + LastError : "")
                    + (missing.Length > 0 ? (newErrors > 0 ? "; " : "") + "never cast " + string.Join(",", missing.Select(x => x.ToString()).ToArray()) : "");
                Plugin.Log.LogInfo("test: combo " + combo.Id + " cast [" + string.Join(",", anyCast.Select(x => x.ToString()).ToArray()) + "], errors " + newErrors + ", Noita projectiles " + NoitaWorld.Projectiles);
                Report("combo-" + combo.Id, newErrors == 0 && missing.Length == 0, why);
            }
            SetSlots(0, keep);
            Report("no-errors", Errors == 0, Errors + " game errors during the run, first: " + FirstError);
            Plugin.Log.LogInfo("test: game errors " + Errors + ", Magicraft sound requests dropped " + Hooks.SoundRequestsDropped + ", sound errors caught " + Hooks.SoundErrorsCaught);

            // keep casting in bursts for the rest of the run, so the run shows the spells in play
            Step = "casting";
            while (true)
            {
                for (float t = 0f; t < 2.5f; t += Time.unscaledDeltaTime) { Cast(); yield return null; }
                MeltyPlay.Button(0, false);
                yield return new WaitForSecondsRealtime(1.5f);
            }
        }

        // ---- aiming, casting and the wand, all through Magicraft's own state ----
        public static bool AimActive, Invincible;
        public static Vector3 AimScreen;
        static readonly HashSet<int> anyCast = new HashSet<int>();
        public static void AnyCast(int id) => anyCast.Add(id);

        // aim at a point circling the player (on screen), hold the cast button, keep the wand's MP up
        static void Cast()
        {
            float a = Time.realtimeSinceStartup * 0.8f;
            AimAt(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f));
            MeltyPlay.Button(0, true);
            try { foreach (Wand w in PlayerMgr.Inst.Wands) if (w != null) w.CurrentMP = w.MaxMP; } catch (Exception) { }
        }

        static void AimAt(Vector3 dir)
        {
            try
            {
                Vector3 p = PlayerMgr.Inst.PlayerCtrller.transform.position;
                Camera cam = CamController.Inst.cam_Main;
                AimScreen = cam.WorldToScreenPoint(p + dir.normalized * 4f);
                AimActive = true;
                MeltyPlay.Point(AimScreen.x / Screen.width, 1f - AimScreen.y / Screen.height);
            }
            catch (Exception) { }
        }

        static float wandOff;

        static IEnumerator WandLook(string which)
        {
            Vector3[] dirs = { Vector3.right, Vector3.up, Vector3.left, Vector3.down };
            string[] names = { "right", "up", "left", "down" };
            for (int i = 0; i < dirs.Length; i++)
            {
                Step = "wand " + which + " " + names[i];
                float stop = Time.realtimeSinceStartup + 1.6f;
                while (Time.realtimeSinceStartup < stop) { AimAt(dirs[i]); yield return null; }
                try
                {
                    PlayerController pc = PlayerMgr.Inst.PlayerCtrller;
                    Wand w = PlayerMgr.Inst.SelectedWand;
                    var root = (Transform)HarmonyLib.AccessTools.Field(typeof(PlayerController), "tsf_WandRoot").GetValue(pc);
                    SpriteRenderer sr = w != null ? (SpriteRenderer)HarmonyLib.AccessTools.Field(typeof(Wand), "sr_Wand").GetValue(w) : null;
                    Vector3 tipUp = sr != null ? sr.transform.TransformDirection(Vector3.up) : Vector3.zero;
                    Vector3 tipRight = sr != null ? sr.transform.TransformDirection(Vector3.right) : Vector3.zero;
                    float aimDeg = Mathf.Atan2(pc.CurrentDir.y, pc.CurrentDir.x) * Mathf.Rad2Deg;
                    Plugin.Log.LogInfo("test: wand " + which + " aim " + names[i] + ": game aim " + aimDeg.ToString("0") + "°, wand root " + (root != null ? root.eulerAngles.z.ToString("0") : "?")
                        + "°, sprite " + (sr != null && sr.sprite != null ? sr.sprite.name + " " + sr.sprite.rect.width + "x" + sr.sprite.rect.height : "?")
                        + ", sprite up points " + (Mathf.Atan2(tipUp.y, tipUp.x) * Mathf.Rad2Deg).ToString("0") + "°, sprite right points " + (Mathf.Atan2(tipRight.y, tipRight.x) * Mathf.Rad2Deg).ToString("0") + "°, flipX " + (sr != null && sr.flipX));
                    if (which == "noita" && i == 0) Report("aim-follows", pc.CurrentDir.x > 0.9f, "the game's aim was " + aimDeg.ToString("0") + "° while the test aimed right");
                    // Magicraft wand art runs along its sprite's diagonal (tip top right); that diagonal must point where the player aims
                    if (which == "noita" && sr != null)
                    {
                        Vector3 diag = sr.transform.TransformDirection(new Vector3(1f, 1f, 0f));
                        float off = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(diag.y, diag.x) * Mathf.Rad2Deg, aimDeg));
                        if (off > 15f) wandOff = Mathf.Max(wandOff, off);
                        if (i == dirs.Length - 1) Report("wand-points-aim", wandOff <= 15f, "the Noita wand pointed up to " + wandOff.ToString("0") + "° away from the aim");
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("test: wand look: " + e.Message); }
            }
        }

        static int[] SlotIds(int wand)
        {
            WandConfig c = PlayerMgr.Inst.BaData.wandCfgs[wand];
            return c.normalSlots.Select(s => s == null ? 0 : s.id).ToArray();
        }

        static void SetSlots(int wand, int[] ids)
        {
            WandConfig c = PlayerMgr.Inst.BaData.wandCfgs[wand];
            int n = c.normalSlots.Length;
            c.normalSlots = Enumerable.Range(0, n).Select(i => i < ids.Length && ids[i] != 0 ? new SlotData { id = ids[i] } : null).ToArray();
            Refresh();
        }

        static void Refresh()
        {
            try { foreach (Wand w in PlayerMgr.Inst.Wands) if (w != null) { w.ResetAndRecheck(); w.Display_UpdateShowOrHide(); } } catch (Exception e) { Plugin.Log.LogWarning("test: wand refresh: " + e.Message); }
        }

        // ---- every game error during the test ----
        public static int Errors;
        public static string FirstError, LastError;

        public static void WatchErrors()
        {
            Application.logMessageReceivedThreaded += (msg, stack, type) =>
            {
                if (type != LogType.Exception && !(type == LogType.Error && msg.Contains("Exception"))) return;
                Errors++;
                LastError = msg.Length > 200 ? msg.Substring(0, 200) : msg;
                if (FirstError == null) { FirstError = LastError + " @ " + (stack ?? "").Split('\n')[0]; Plugin.Log?.LogWarning("test: first game error: " + FirstError); }
            };
        }
    }
}
