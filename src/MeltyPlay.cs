// Melty's in-game input (C#, Unity games on the Input System: Unity.InputSystem.dll in <Game>_Data/Managed; reference it
// and UnityEngine.CoreModule). While Melty tests the release, it plays the keys and mouse its creator's agent sends
// (test_input) inside the game, through a virtual keyboard and mouse, so the game can stay behind the creator's windows
// and their own keyboard and mouse stay theirs. Call MeltyPlay.Tick() every frame (your plugin's Update(), or
// MelonMod.OnUpdate()). Outside a Melty test it does nothing.
//
// For real-time play, also tell it, every frame (or as they change), the values its steps can wait on:
//   MeltyPlay.Report("player.z", position.z);  MeltyPlay.Report("chest.open", chestOpen);
// and once, at start, your actions, each run every frame until it returns true (done), given its args and the seconds
// since it started (time, not frames: frame rates differ):
//   MeltyPlay.Action("walk_to", "walk to a spot: x,z", (args, seconds) => { MeltyPlay.Hold("w", true); return arrived; });
// Inside an action: MeltyPlay.Hold(key, down), MeltyPlay.Turn(dx, dy) (the mouse: a camera), MeltyPlay.Point(x, y)
// (fractions of the screen), MeltyPlay.Button(0 left, 1 right, 2 middle, down). What it holds is let go when it ends.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class MeltyPlay
{
    const string Slug = "noitacraft-2";
    static bool looked, active;
    static string inputPath, playedPath;
    static Keyboard keyboard;
    static Mouse mouse;
    static long inputRead;
    static readonly Queue<string[]> batches = new Queue<string[]>();
    static string[] batch;
    static int next, opsDone;
    static float waitUntil, nextBeat, batchStart;
    static readonly HashSet<Key> held = new HashSet<Key>();
    static readonly HashSet<int> buttons = new HashSet<int>();
    static Vector2 point = new Vector2(0.5f, 0.5f), delta;
    static float scroll;
    static string lastId = "";
    static int lastDone, lastOf;
    static bool lastFinished = true, lastStopped, stopNow;
    // What the mod reports, its actions, and the step under way that waits on them.
    static readonly Dictionary<string, double> values = new Dictionary<string, double>();
    static readonly Dictionary<string, Func<string, float, bool>> actions = new Dictionary<string, Func<string, float, bool>>();
    static readonly Dictionary<string, string> helps = new Dictionary<string, string>();
    static string[] until;
    static string doing, doArgs;
    static float doStarted, stepEnds;
    static readonly List<string> outcomes = new List<string>();
    // The game standing still between the agent's batches (single player), and since when.
    static bool freezeAfter, frozen;
    static float savedScale = 1f, frozenAt, guardUntil;

    public static void Report(string name, double value) { values[name] = value; }
    public static void Report(string name, bool value) { values[name] = value ? 1 : 0; }
    public static void Action(string name, string help, Func<string, float, bool> step) { actions[name] = step; helps[name] = help ?? ""; }
    public static void Hold(string key, bool down) { Key k; if (KeyOf(key, out k)) { if (down) held.Add(k); else held.Remove(k); } }
    public static void Turn(float dx, float dy) { delta += new Vector2(dx, -dy); }
    public static void Point(float x, float y) { point = new Vector2(x, y); }
    public static void Button(int which, bool down) { if (down) buttons.Add(which); else buttons.Remove(which); }

    public static void Tick()
    {
        if (!looked) Look();
        if (!active) return;
        Application.runInBackground = true;
        if (keyboard == null && !Devices()) return;
        Read();
        Run();
        Send();
        // Never left standing still: a batch it waited for never came; and a game that kept the 0 it saw while frozen and
        // put it back once moving again (PEAK did, 2026-10-14) moves again.
        if (frozen && Time.unscaledTime - frozenAt > 90f) Unfreeze();
        if (!frozen && Time.unscaledTime < guardUntil && Time.timeScale == 0f) Time.timeScale = savedScale;
        if (Time.unscaledTime >= nextBeat) Beat();
    }

    // Melty's test file names where the agent's input comes from ("input") and where to say how far it got ("played").
    static void Look()
    {
        looked = true;
        try
        {
            string test = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", Slug + ".json");
            if (!File.Exists(test)) return; // not a test run
            string json = File.ReadAllText(test);
            inputPath = Field(json, "input");
            playedPath = Field(json, "played");
            active = inputPath != null && playedPath != null;
        }
        catch (Exception) { }
    }

    // Made once Unity's input is up (a loader can start a mod before it is). Input keeps coming when the game isn't in front.
    static bool Devices()
    {
        try
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>("MeltyPlayKeyboard");
            mouse = InputSystem.AddDevice<Mouse>("MeltyPlayMouse");
            inputRead = File.Exists(inputPath) ? new FileInfo(inputPath).Length : 0;
            Beat();
            return true;
        }
        catch (Exception)
        {
            keyboard = null;
            return false;
        }
    }

    // Each line Melty adds is a batch: its id, then its steps ("kd:w w:1000 ku:w"); or "stop", or "go" (moving again).
    static void Read()
    {
        try
        {
            if (!File.Exists(inputPath)) return;
            using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < inputRead) inputRead = 0;
                if (stream.Length == inputRead) return;
                stream.Position = inputRead;
                var bytes = new byte[stream.Length - inputRead];
                int got = stream.Read(bytes, 0, bytes.Length);
                string text = Encoding.UTF8.GetString(bytes, 0, got);
                int end = text.LastIndexOf((char)10);
                if (end < 0) return;
                inputRead += Encoding.UTF8.GetByteCount(text.Substring(0, end + 1));
                foreach (string line in text.Substring(0, end).Split((char)10))
                {
                    string[] parts = line.Trim().Split(' ');
                    if (parts[0] == "stop") stopNow = true; // the creator took over, or Melty gave up on the batch
                    else if (parts[0] == "go") { Unfreeze(); Beat(); }
                    else if (parts[0].Length > 0) batches.Enqueue(parts);
                }
            }
        }
        catch (IOException) { }
    }

    static void Run()
    {
        float now = Time.unscaledTime;
        if (stopNow)
        {
            stopNow = false;
            batches.Clear();
            if (batch != null)
            {
                batch = null;
                until = null;
                doing = null;
                freezeAfter = false;
                held.Clear();
                buttons.Clear();
                lastFinished = true;
                lastStopped = true;
                Beat();
            }
        }
        while (true)
        {
            if (batch == null)
            {
                if (batches.Count == 0) return;
                batch = batches.Dequeue();
                next = 1;
                opsDone = 0;
                waitUntil = 0f;
                until = null;
                doing = null;
                freezeAfter = false;
                outcomes.Clear();
                Unfreeze();
                batchStart = now;
                lastId = batch[0];
                lastOf = batch.Length - 1;
                lastDone = 0;
                lastFinished = false;
                lastStopped = false;
                Beat();
            }
            if (waitUntil > now) return;
            // A step waiting on the mod's values: checked every frame, until they say so or its time is up.
            if (until != null)
            {
                if (Holds(until)) outcomes.Add("met@" + Since(now));
                else if (now >= stepEnds) outcomes.Add("timeout");
                else return;
                until = null;
            }
            // One of the mod's actions: run every frame until it says it's done, or its time is up.
            if (doing != null)
            {
                string result = null;
                try { if (actions[doing](doArgs, now - doStarted)) result = "done@" + Since(now); }
                catch (Exception error) { result = "error:" + error.GetType().Name; }
                if (result == null && now >= stepEnds) result = "timeout";
                if (result == null) return;
                outcomes.Add(result);
                doing = null;
                held.Clear();
                buttons.Clear();
            }
            if (next >= batch.Length)
            {
                held.Clear();
                buttons.Clear();
                lastDone = opsDone;
                lastFinished = true;
                batch = null;
                if (freezeAfter) Freeze();
                Beat();
                continue;
            }
            Do(batch[next], now);
            next++;
            opsDone++;
            lastDone = opsDone;
        }
    }

    static float Num(string[] parts, int at)
    {
        float value;
        return parts.Length > at && float.TryParse(parts[at], NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0f;
    }

    static string Decode(string text)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(text)); }
        catch (Exception) { return ""; }
    }

    static void Do(string step, float now)
    {
        string[] p = step.Split(':');
        Key key;
        switch (p[0])
        {
            case "kd": if (p.Length > 1 && KeyOf(p[1], out key)) held.Add(key); break;
            case "ku": if (p.Length > 1 && KeyOf(p[1], out key)) held.Remove(key); break;
            case "w": waitUntil = now + Num(p, 1) / 1000f; break;
            case "mv": point = new Vector2(Num(p, 1), Num(p, 2)); break;
            case "rl": delta += new Vector2(Num(p, 1), -Num(p, 2)); break;
            case "bd": buttons.Add((int)Num(p, 1)); break;
            case "bu": buttons.Remove((int)Num(p, 1)); break;
            case "wh": scroll += Num(p, 1); break;
            case "tx":
                if (p.Length > 1) foreach (char c in Decode(p[1])) InputSystem.QueueTextEvent(keyboard, c);
                break;
            case "wu":
                until = Decode(p.Length > 1 ? p[1] : "").Split(';');
                stepEnds = now + Num(p, 2) / 1000f;
                break;
            case "do":
                doing = p.Length > 1 && actions.ContainsKey(p[1]) ? p[1] : null;
                if (doing == null) outcomes.Add("missing:" + (p.Length > 1 ? p[1] : ""));
                doArgs = p.Length > 2 ? Decode(p[2]) : "";
                doStarted = now;
                stepEnds = now + Num(p, 3) / 1000f;
                break;
            case "fz": freezeAfter = p.Length > 1 && p[1] == "1"; break;
        }
    }

    // Each clause "name|op|number", all of them true of what the mod last reported.
    static bool Holds(string[] clauses)
    {
        foreach (string clause in clauses)
        {
            string[] c = clause.Split('|');
            double have, want;
            if (c.Length != 3 || !values.TryGetValue(c[0], out have) || !double.TryParse(c[2], NumberStyles.Float, CultureInfo.InvariantCulture, out want)) return false;
            bool ok;
            switch (c[1])
            {
                case "<": ok = have < want; break;
                case "<=": ok = have <= want; break;
                case ">": ok = have > want; break;
                case ">=": ok = have >= want; break;
                case "==": ok = Math.Abs(have - want) < 0.000001; break;
                case "!=": ok = Math.Abs(have - want) >= 0.000001; break;
                default: ok = false; break;
            }
            if (!ok) return false;
        }
        return true;
    }

    static string Since(float now) { return (now - batchStart).ToString("0.00", CultureInfo.InvariantCulture); }

    static void Freeze()
    {
        if (frozen) return;
        savedScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        frozen = true;
        frozenAt = Time.unscaledTime;
    }

    static void Unfreeze()
    {
        if (!frozen) return;
        Time.timeScale = savedScale;
        frozen = false;
        guardUntil = Time.unscaledTime + 3f;
    }

    // What's held down, and where the pointer is, every frame (spots are fractions of the picture, from its top left).
    static void Send()
    {
        var keys = new Key[held.Count];
        held.CopyTo(keys);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        var state = new MouseState { position = new Vector2(point.x * Screen.width, (1f - point.y) * Screen.height), delta = delta, scroll = new Vector2(0f, scroll) };
        state = state.WithButton(MouseButton.Left, buttons.Contains(0)).WithButton(MouseButton.Right, buttons.Contains(1)).WithButton(MouseButton.Middle, buttons.Contains(2));
        InputSystem.QueueStateEvent(mouse, state);
        delta = Vector2.zero;
        scroll = 0f;
    }

    // How far it got, what the mod reports and its actions, and that it's here: four times a second while a batch plays.
    static void Beat()
    {
        nextBeat = Time.unscaledTime + (batch != null ? 0.25f : 1f);
        try
        {
            var json = new StringBuilder();
            json.Append("{").Append(Q("ready")).Append(":true,");
            json.Append(Q("at")).Append(":").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Append(",");
            json.Append(Q("batch")).Append(":").Append(Q(lastId)).Append(",");
            json.Append(Q("done")).Append(":").Append(lastDone).Append(",");
            json.Append(Q("of")).Append(":").Append(lastOf).Append(",");
            json.Append(Q("finished")).Append(":").Append(lastFinished ? "true" : "false").Append(",");
            json.Append(Q("stopped")).Append(":").Append(lastStopped ? "true" : "false").Append(",");
            json.Append(Q("frozen")).Append(":").Append(frozen ? "true" : "false").Append(",");
            json.Append(Q("scale")).Append(":").Append(Time.timeScale.ToString("0.###", CultureInfo.InvariantCulture)).Append(",");
            json.Append(Q("outcomes")).Append(":").Append(Q(string.Join(",", outcomes.ToArray()))).Append(",");
            json.Append(Q("values")).Append(":{");
            int n = 0;
            foreach (var pair in values)
            {
                if (n >= 40 || double.IsNaN(pair.Value) || double.IsInfinity(pair.Value)) continue;
                if (n++ > 0) json.Append(",");
                json.Append(Q(pair.Key)).Append(":").Append(pair.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
            json.Append("},").Append(Q("actions")).Append(":{");
            n = 0;
            foreach (var pair in helps)
            {
                if (n >= 30) break;
                if (n++ > 0) json.Append(",");
                json.Append(Q(pair.Key)).Append(":").Append(Q(pair.Value));
            }
            json.Append("}}");
            string temp = playedPath + ".tmp";
            File.WriteAllText(temp, json.ToString());
            // Replaced whole, so Melty never finds it missing or half written.
            if (!File.Exists(playedPath)) File.Move(temp, playedPath);
            else
            {
                try { File.Replace(temp, playedPath, null); }
                catch (Exception) { File.Delete(playedPath); File.Move(temp, playedPath); }
            }
        }
        catch (Exception) { }
    }

    // A JSON string: its quotes and backslashes escaped, control characters left out.
    static string Q(string text)
    {
        var quoted = new StringBuilder();
        quoted.Append((char)34);
        foreach (char c in text ?? "")
        {
            if (c == (char)34 || c == (char)92) quoted.Append((char)92).Append(c);
            else if (c >= ' ') quoted.Append(c);
        }
        return quoted.Append((char)34).ToString();
    }

    static string Field(string json, string name)
    {
        int at = json.IndexOf((char)34 + name + (char)34 + ":", StringComparison.Ordinal);
        if (at < 0) return null;
        at = json.IndexOf((char)34, json.IndexOf(':', at) + 1);
        if (at < 0) return null;
        var value = new StringBuilder();
        for (int i = at + 1; i < json.Length; i++)
        {
            char c = json[i];
            if (c == (char)92 && i + 1 < json.Length) { value.Append(json[++i]); continue; }
            if (c == (char)34) return value.ToString();
            value.Append(c);
        }
        return null;
    }

    static bool KeyOf(string name, out Key key)
    {
        string unity;
        switch (name)
        {
            case "left": unity = "LeftArrow"; break;
            case "right": unity = "RightArrow"; break;
            case "up": unity = "UpArrow"; break;
            case "down": unity = "DownArrow"; break;
            case "shift": case "lshift": unity = "LeftShift"; break;
            case "rshift": unity = "RightShift"; break;
            case "ctrl": case "lctrl": unity = "LeftCtrl"; break;
            case "rctrl": unity = "RightCtrl"; break;
            case "alt": case "lalt": unity = "LeftAlt"; break;
            case "ralt": unity = "RightAlt"; break;
            case "bracketleft": unity = "LeftBracket"; break;
            case "bracketright": unity = "RightBracket"; break;
            case "multiply": unity = "NumpadMultiply"; break;
            case "add": unity = "NumpadPlus"; break;
            case "subtract": unity = "NumpadMinus"; break;
            case "decimal": unity = "NumpadPeriod"; break;
            case "divide": unity = "NumpadDivide"; break;
            default: unity = name.Length == 1 && char.IsDigit(name[0]) ? "Digit" + name : name; break;
        }
        return Enum.TryParse(unity, true, out key) && key != Key.None;
    }
}
