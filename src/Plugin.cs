using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NoitaCraft
{
    [BepInPlugin("gg.melty.noitacraft", "NoitaCraft", "0.2.8")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        void Awake()
        {
            Log = Logger;
            TestRun.Init();
            NoitaData.Load();
            bool allFound = NoitaData.Ready && Array.TrueForAll(Sheets.Spells, r => NoitaData.Actions.ContainsKey(r.NoitaId));
            TestRun.Report("noita-data", allFound, NoitaData.Error ?? "some spells.json noitaIds are not in this Noita's gun_actions.lua");
            if (NoitaData.Ready) Log.LogInfo("Noita read from " + NoitaData.Folder + ": " + NoitaData.Actions.Count + " spells");
            else Log.LogError("Noita not read, Magicraft stays as it is: " + NoitaData.Error);
            NoitaResources.Install();
            NoitaAudio.Start();
            Hooks.Patch(new Harmony("gg.melty.noitacraft"));
            // the game clears stray objects between scenes, so the mod keeps one of its own
            var host = new GameObject("NoitaCraft");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<Runner>();
            host.AddComponent<TestRun>();
            host.AddComponent<NoitaWorld>();
        }
    }

    internal sealed class Runner : MonoBehaviour
    {
        void Update()
        {
            MeltyPlay.Tick();
            MinaLook.Attach();
            TestRun.Tick();
        }
    }
}
