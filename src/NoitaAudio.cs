using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace NoitaCraft
{
    // Noita's own sounds: decoded once from the player's FMOD banks (data/audio/Desktop/*.bank) with the bundled vgmstream-cli into
    // %LOCALAPPDATA%\NoitaCraft\sounds, then played through Magicraft's sound volume.
    internal static class NoitaAudio
    {
        static readonly string[] banks = { "projectiles", "explosion" };
        static readonly Dictionary<string, List<string>> files = new Dictionary<string, List<string>>(); // "bank:name" -> wav path
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static volatile bool ready;
        static AudioSource[] sources;
        static int next;
        public static int Played;
        public static string Error;

        static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoitaCraft", "sounds");

        public static void Start()
        {
            if (!NoitaData.Ready) return;
            new Thread(() =>
            {
                try
                {
                    string cli = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoitaCraft", "vgmstream", "vgmstream-cli.exe");
                    foreach (string bank in banks)
                    {
                        string dir = Path.Combine(Root, bank);
                        string src = Path.Combine(NoitaData.Folder, "data", "audio", "Desktop", bank + ".bank");
                        if (!File.Exists(src)) { Error = bank + ".bank not in Noita's folder"; continue; }
                        string stamp = Path.Combine(dir, "from.txt");
                        string want = src + "|" + new FileInfo(src).Length;
                        if (!File.Exists(stamp) || File.ReadAllText(stamp) != want)
                        {
                            if (!File.Exists(cli)) { Error = "vgmstream-cli.exe missing"; continue; }
                            Directory.CreateDirectory(dir);
                            var p = Process.Start(new ProcessStartInfo(cli, "-i -S 0 -o \"" + Path.Combine(dir, "?n.wav") + "\" \"" + src + "\"")
                            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
                            p.ErrorDataReceived += (o, e) => { };
                            p.BeginErrorReadLine();
                            p.StandardOutput.ReadToEnd();
                            if (!p.WaitForExit(120000)) { try { p.Kill(); } catch (Exception) { } Error = bank + ".bank took too long to decode"; continue; }
                            File.WriteAllText(stamp, want);
                        }
                        foreach (string wav in Directory.GetFiles(dir, "*.wav"))
                        {
                            string name = bank + ":" + Path.GetFileNameWithoutExtension(wav);
                            List<string> l;
                            if (!files.TryGetValue(name, out l)) files[name] = l = new List<string>();
                            l.Add(wav);
                        }
                    }
                    ready = true;
                    Plugin.Log.LogInfo("Noita sounds ready: " + files.Count + (Error != null ? " (" + Error + ")" : ""));
                }
                catch (Exception e) { Error = e.Message; Plugin.Log.LogWarning("Noita sounds: " + e.Message); }
            }) { IsBackground = true }.Start();
        }

        // "projectiles:bullet_bounce_" plays a random bullet_bounce_NN; an exact name plays that one
        public static void Play(string prefix, float volume)
        {
            if (!ready || string.IsNullOrEmpty(prefix) || prefix == "none") return;
            try
            {
                List<string> exact;
                string pick;
                if (files.TryGetValue(prefix, out exact)) pick = exact[0];
                else
                {
                    string[] any = files.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                    if (any.Length == 0) return;
                    pick = files[any[UnityEngine.Random.Range(0, any.Length)]][0];
                }
                AudioClip clip;
                if (!clips.TryGetValue(pick, out clip)) clips[pick] = clip = Wav(pick);
                if (clip == null) return;
                if (sources == null)
                {
                    var go = new GameObject("NoitaCraft_Audio");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    sources = new AudioSource[12];
                    for (int i = 0; i < sources.Length; i++) { sources[i] = go.AddComponent<AudioSource>(); sources[i].playOnAwake = false; sources[i].spatialBlend = 0f; }
                }
                AudioSource s = sources[next++ % sources.Length];
                float master = 1f;
                try { master = DataMgr.settingData.GetFinalSound(); } catch (Exception) { }
                s.clip = clip;
                s.volume = Mathf.Clamp01(volume * master);
                s.pitch = UnityEngine.Random.Range(0.95f, 1.05f);
                s.Play();
                Played++;
                if (Played == 1) TestRun.Report("noita-sound", true, null);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Noita sound " + prefix + ": " + e.Message); }
        }

        // PCM16 WAV -> AudioClip
        static AudioClip Wav(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int channels = 1, rate = 48000, bits = 16, at = 12, dataAt = -1, dataLen = 0;
            while (at + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, at, 4);
                int len = BitConverter.ToInt32(b, at + 4);
                if (id == "fmt ") { channels = BitConverter.ToInt16(b, at + 10); rate = BitConverter.ToInt32(b, at + 12); bits = BitConverter.ToInt16(b, at + 22); }
                else if (id == "data") { dataAt = at + 8; dataLen = Math.Min(len, b.Length - dataAt); break; }
                at += 8 + len + (len & 1);
            }
            if (dataAt < 0 || bits != 16) return null;
            int samples = dataLen / 2;
            var data = new float[samples];
            for (int i = 0; i < samples; i++) data[i] = BitConverter.ToInt16(b, dataAt + i * 2) / 32768f;
            AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples / channels, channels, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
