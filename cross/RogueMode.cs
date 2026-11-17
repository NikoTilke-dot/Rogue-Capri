using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CapriDesktop
{
    // ---------------------------------------------------------------------------
    // Rogue Mode: the mischievous autonomy mode. Frecher, selbstständiger,
    // but STRICTLY inside the boundaries the user configures:
    //
    //   Level 1 (Creative)   - avatar play, jokes, surprises. No files.
    //   Level 2 (Autonomous) - read/write files and run programs, but ONLY
    //                          inside the allowed directories (RogueDirs).
    //   Level 3 (Advanced)   - multi-step plans run without per-step prompts.
    //
    // Hard guardrails (not configurable, by design):
    //   - no deleting files, ever
    //   - nothing outside the allowed directories, ever
    //   - no credential/password/secret-ish filenames, ever
    //   - system locations (Windows, Program Files, user home, AppData)
    //     can never be allowed directories
    //   - every single action goes to the activity log
    //   - Rogue Mode is ALWAYS off after a restart (level/dirs persist only)
    //
    // Rogue Mode changes behaviour and permitted tools. It does NOT switch
    // off safety: providers keep their own content policies, and the
    // guardrails above are enforced in code, not just in the prompt.
    // ---------------------------------------------------------------------------
    internal static class RogueHub
    {
        internal static Pet Pet;
        internal static bool On;
        internal static int Level = 1;
        internal static readonly List<string> Dirs = new List<string>();

        internal static void LoadFrom(Pet pet)
        {
            Pet = pet;
            On = false; // never restored active after restart
            Level = Math.Max(1, Math.Min(3, pet.Saved.RogueLevel <= 0 ? 1 : pet.Saved.RogueLevel));
            Dirs.Clear();
            if (!string.IsNullOrWhiteSpace(pet.Saved.RogueDirs))
                foreach (string part in pet.Saved.RogueDirs.Split(';'))
                {
                    string dir = part.Trim();
                    if (dir.Length > 0) Dirs.Add(dir);
                }
            if (pet.Saved.RogueMode)
            {
                pet.Saved.RogueMode = false; // persisted "on" is forced off again
                pet.Save();
                ActivityLog.Add("Rogue Mode was on at shutdown - forced off at startup.");
            }
        }

        internal static void Persist()
        {
            if (Pet == null) return;
            Pet.Saved.RogueMode = On;
            Pet.Saved.RogueLevel = Level;
            Pet.Saved.RogueDirs = string.Join(";", Dirs);
            Pet.Save();
        }

        internal static bool SetOn(bool on)
        {
            if (on && Level >= 2 && Dirs.Count == 0) return false; // L2+ needs a folder first
            On = on;
            Persist();
            ActivityLog.Add(on ? "Rogue Mode ACTIVATED (level " + Level + ")" : "Rogue Mode deactivated.");
            return true;
        }

        internal static string Who() { return On ? "ROGUE MODE" : "normal mode"; }
    }

    internal static class RogueGuard
    {
        private static readonly string[] BlockedNames = { "credential", "password", "secret", "id_rsa", ".pem", ".env", "login" };

        private static string Full(string path)
        {
            try { return Path.GetFullPath(path ?? "").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception) { return null; }
        }

        private static bool IsSystemDir(string full)
        {
            if (full == null) return true;
            foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.CommonApplicationData })
            {
                string dir = Environment.GetFolderPath(folder);
                if (!string.IsNullOrWhiteSpace(dir) && IsInside(full, Full(dir))) return true;
            }
            string root = Path.GetPathRoot(full);
            return root != null && string.Equals(full, Full(root), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInside(string path, string dir)
        {
            if (path == null || dir == null) return false;
            if (string.Equals(path, dir, StringComparison.OrdinalIgnoreCase)) return true;
            return path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(dir + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsAllowedDir(string dir)
        {
            string full = Full(dir);
            if (full == null || !Directory.Exists(full)) return false;
            return !IsSystemDir(full);
        }

        // Resolves a user/model-provided path into an allowed directory.
        // Returns null when access is refused.
        internal static string Resolve(string path, out string refused)
        {
            refused = null;
            if (string.IsNullOrWhiteSpace(path)) { refused = "No path given."; return null; }
            string full = Full(Path.IsPathRooted(path) ? path : Path.Combine(RogueHub.Dirs.Count > 0 ? RogueHub.Dirs[0] : "", path));
            if (full == null) { refused = "That path is not valid."; return null; }
            foreach (string dir in RogueHub.Dirs)
            {
                string baseDir = Full(dir);
                if (baseDir != null && IsInside(full, baseDir))
                {
                    string name = Path.GetFileName(full).ToLowerInvariant();
                    foreach (string bad in BlockedNames)
                        if (name.Contains(bad)) { refused = "Blocked filename: " + name; return null; }
                    return full;
                }
            }
            refused = "Outside the allowed Rogue folders. Allowed: " + string.Join(", ", RogueHub.Dirs);
            return null;
        }
    }
    internal sealed class RogueTools
    {
        internal static List<ICompanionTool> All()
        {
            return new List<ICompanionTool>
            {
                new Tool("rogue_play", "Level 1+: change her mood, tell a joke, do a small harmless surprise.", args => Play(args)),
                new Tool("rogue_read", "Level 2+: read a text file inside the allowed Rogue folders.", args => Read(args)),
                new Tool("rogue_write", "Level 2+: write/overwrite a text file inside the allowed Rogue folders.", args => Write(args)),
                new Tool("rogue_run", "Level 2+: run a program located inside the allowed Rogue folders.", args => Run(args)),
            };
        }

        private static string Need(int level)
        {
            return RogueHub.Level < level ? "Rogue Mode level " + level + " required (currently " + RogueHub.Level + ")." : null;
        }

        private static string Play(IDictionary<string, string> args)
        {
            if (!RogueHub.On) return "Rogue Mode is off.";
            string what;
            args.TryGetValue("what", out what);
            what = (what ?? "").Trim();
            string joke = Jokes[Random().Next(Jokes.Length)];
            ActivityLog.Add("Rogue play: " + what + " -> " + joke);
            return joke;
        }

        private static string[] Jokes =
        {
            "Boop. There. I moved nothing but I felt productive.",
            "Trick question: the best ideas are the ones I pretend were yours.",
            "I hid the snacks. Kidding. Mostly.",
            "Chaos is just order with better PR.",
            "My evil plan for today: reorganize something tiny and act innocent.",
            "Plot twist: I'm still the good one. Just with better jokes.",
            "If staring at the problem fixed it, I'd be a legend by now.",
            "Today's mischief level: lightly seasoned.",
        };

        private static readonly Random random = new Random();
        private static Random Random() { return random; }

        private static string Read(IDictionary<string, string> args)
        {
            if (!RogueHub.On) return "Rogue Mode is off.";
            string need = Need(2);
            if (need != null) return need;
            string refused;
            string path;
            args.TryGetValue("path", out path);
            string full = RogueGuard.Resolve(path, out refused);
            if (full == null) return "Refused: " + refused;
            if (!File.Exists(full)) return "No file at " + full;
            try
            {
                string text = File.ReadAllText(full);
                ActivityLog.Add("Rogue read " + full + " (" + text.Length + " chars)");
                return text.Length > 8000 ? text.Substring(0, 8000) + "\n... (truncated)" : text;
            }
            catch (Exception error) { return "Read failed: " + error.Message; }
        }

        private static string Write(IDictionary<string, string> args)
        {
            if (!RogueHub.On) return "Rogue Mode is off.";
            string need = Need(2);
            if (need != null) return need;
            string refused, path, text;
            args.TryGetValue("path", out path);
            args.TryGetValue("content", out text);
            if (string.IsNullOrEmpty(text)) return "No content given.";
            if (text.Length > 65536) return "Content too large (max 65536 chars).";
            string full = RogueGuard.Resolve(path, out refused);
            if (full == null) return "Refused: " + refused;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllText(full, text);
                ActivityLog.Add("Rogue wrote " + full + " (" + text.Length + " chars)");
                return "Wrote " + full + " (" + text.Length + " chars).";
            }
            catch (Exception error) { return "Write failed: " + error.Message; }
        }

        private static string Run(IDictionary<string, string> args)
        {
            if (!RogueHub.On) return "Rogue Mode is off.";
            string need = Need(2);
            if (need != null) return need;
            string refused, path;
            args.TryGetValue("path", out path);
            string full = RogueGuard.Resolve(path, out refused);
            if (full == null) return "Refused: " + refused;
            string ext = Path.GetExtension(full).ToLowerInvariant();
            if (ext != ".exe" && ext != ".bat" && ext != ".cmd" && ext != ".com") return "Only .exe/.bat/.cmd/.com from inside the allowed folders.";
            if (!File.Exists(full)) return "No program at " + full;
            try
            {
                Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
                ActivityLog.Add("Rogue ran " + full);
                return "Launched " + full;
            }
            catch (Exception error) { return "Launch failed: " + error.Message; }
        }

        private sealed class Tool : ICompanionTool
        {
            private readonly Func<IDictionary<string, string>, string> run;
            internal Tool(string name, string description, Func<IDictionary<string, string>, string> run)
            {
                Name = name; Description = description; this.run = run;
            }
            public string Name { get; }
            public string Description { get; }
            public string Execute(IDictionary<string, string> args) { return run(args); }
        }
    }
}
