using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // Persistent companion memory: facts, preferences and conversation history,
    // kept in one JSON file next to Capri's settings. Plain file storage keeps
    // it dependency-free and easy to back up by hand.
    internal sealed class MemoryStore
    {
        private readonly object gate = new object();
        private readonly string file;
        private readonly List<string> facts = new List<string>();
        private readonly List<string> preferences = new List<string>();
        private readonly List<string> history = new List<string>(); // "HH:mm role: text"
        private readonly Dictionary<string, string> plugins = new Dictionary<string, string>(); // name -> "on"/"off"

        internal MemoryStore(string folder)
        {
            file = Path.Combine(folder, "companion-memory.json");
            Load();
        }

        internal string FilePath { get { return file; } }
        internal int Count { get { lock (gate) return facts.Count + preferences.Count; } }
        internal int HistoryCount { get { lock (gate) return history.Count; } }

        internal List<string> Facts { get { lock (gate) return new List<string>(facts); } }
        internal List<string> Preferences { get { lock (gate) return new List<string>(preferences); } }
        internal List<string> History { get { lock (gate) return new List<string>(history); } }

        internal void AddFact(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            lock (gate) facts.Add(text);
            Save();
        }

        internal void RemoveFact(int index)
        {
            lock (gate) { if (index >= 0 && index < facts.Count) facts.RemoveAt(index); }
            Save();
        }

        internal void AddPreference(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            lock (gate) preferences.Add(text);
            Save();
        }

        internal void RemovePreference(int index)
        {
            lock (gate) { if (index >= 0 && index < preferences.Count) preferences.RemoveAt(index); }
            Save();
        }

        internal void AddHistory(string role, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 2000) text = text.Substring(0, 2000) + "...";
            lock (gate)
            {
                history.Add(DateTime.Now.ToString("HH:mm") + " " + role + ": " + text);
                while (history.Count > 200) history.RemoveAt(0);
            }
            Save();
        }

        internal void ClearHistory()
        {
            lock (gate) history.Clear();
            Save();
        }

        internal string GetPluginState(string name, string fallback)
        {
            lock (gate) return plugins.TryGetValue(name, out string state) ? state : fallback;
        }

        internal void SetPluginState(string name, bool on)
        {
            lock (gate) plugins[name] = on ? "on" : "off";
            Save();
        }

        internal string ExportJson()
        {
            lock (gate)
            {
                return JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    { "facts", facts }, { "preferences", preferences }, { "history", history }
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        internal void ImportJson(string json)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                lock (gate)
                {
                    if (root.TryGetProperty("facts", out JsonElement f)) foreach (JsonElement item in f.EnumerateArray()) facts.Add(item.GetString());
                    if (root.TryGetProperty("preferences", out JsonElement p)) foreach (JsonElement item in p.EnumerateArray()) preferences.Add(item.GetString());
                    if (root.TryGetProperty("history", out JsonElement h)) foreach (JsonElement item in h.EnumerateArray()) history.Add(item.GetString());
                }
            }
            Save();
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(file)) return;
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file)))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("facts", out JsonElement f)) foreach (JsonElement item in f.EnumerateArray()) facts.Add(item.GetString());
                    if (root.TryGetProperty("preferences", out JsonElement p)) foreach (JsonElement item in p.EnumerateArray()) preferences.Add(item.GetString());
                    if (root.TryGetProperty("history", out JsonElement h)) foreach (JsonElement item in h.EnumerateArray()) history.Add(item.GetString());
                    if (root.TryGetProperty("plugins", out JsonElement pl))
                        foreach (JsonProperty prop in pl.EnumerateObject()) plugins[prop.Name] = prop.Value.GetString();
                }
            }
            catch (Exception error) { ActivityLog.Add("Memory load failed: " + error.Message); }
        }

        private void Save()
        {
            try
            {
                lock (gate)
                {
                    string json = JsonSerializer.Serialize(new Dictionary<string, object>
                    {
                        { "facts", facts }, { "preferences", preferences }, { "history", history }, { "plugins", plugins }
                    }, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(file, json);
                }
            }
            catch (Exception error) { ActivityLog.Add("Memory save failed: " + error.Message); }
        }
    }

    // A shared, human-readable record of what the companion did. Shown on the
    // dashboard, the tools page, and kept in memory for the session.
    internal static class ActivityLog
    {
        private static readonly List<string> entries = new List<string>();
        private static readonly object gate = new object();
        internal static event Action Changed;

        internal static void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (gate)
            {
                entries.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
                while (entries.Count > 300) entries.RemoveAt(0);
            }
            Action handler = Changed;
            if (handler != null) handler();
        }

        internal static string[] Snapshot()
        {
            lock (gate) return entries.ToArray();
        }
    }
    // Permission-controlled desktop tools. Every action is logged; anything
    // that changes the user's computer first asks for confirmation unless the
    // matching SECURITY toggle is on (same toggles the Mainframe uses).
    internal static class DesktopTools
    {
        internal sealed class ToolResult
        {
            internal bool NeedsConfirm;
            internal string Text;
        }

        private static readonly Dictionary<string, string> Apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "notepad", "notepad.exe" }, { "calculator", "calc.exe" }, { "browser", "https://" },
            { "terminal", "cmd.exe" }, { "files", "explorer.exe" }, { "paint", "mspaint.exe" },
            { "settings", "ms-settings:" }, { "music", "https://music.youtube.com" },
        };

        internal static bool Allowed(int toggle) { return Security.On(toggle); }

        internal static ToolResult SystemInfo()
        {
            Process process = Process.GetCurrentProcess();
            string text = "OS: " + Environment.OSVersion + " · " + Environment.OSVersion.Platform + Environment.Version + "\n"
                + "CPU cores: " + Environment.ProcessorCount + "\n"
                + "Capri memory: " + (GC.GetTotalMemory(false) / 1048576) + " MB (managed), " + (process.WorkingSet64 / 1048576) + " MB (process)\n"
                + "Now: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ActivityLog.Add("System info requested");
            return new ToolResult { Text = text };
        }

        internal static ToolResult OpenApp(string name, bool confirmed)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return new ToolResult { Text = "Tell me which app to open, like notepad, calculator or terminal." };
            if (!Allowed(Security.Open) && !confirmed) return new ToolResult { NeedsConfirm = true, Text = "Open \"" + name + "\"?" };
            string target;
            if (Apps.TryGetValue(name, out target))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    ActivityLog.Add("Opened " + name);
                    return new ToolResult { Text = "Opened " + name + "." };
                }
                catch (Exception error) { return new ToolResult { Text = "Couldn't open " + name + ": " + error.Message }; }
            }
            // Unknown name: try it as a raw program/URL, still permission-gated.
            try
            {
                Process.Start(new ProcessStartInfo(name) { UseShellExecute = true });
                ActivityLog.Add("Launched " + name);
                return new ToolResult { Text = "Launched " + name + "." };
            }
            catch (Exception error) { return new ToolResult { Text = "Couldn't open " + name + ": " + error.Message }; }
        }

        internal static ToolResult OpenFolder(string path, bool confirmed)
        {
            path = (path ?? "").Trim();
            if (path.Length == 0) return new ToolResult { Text = "Tell me which folder to open." };
            if (!Allowed(Security.Windows) && !confirmed) return new ToolResult { NeedsConfirm = true, Text = "Open folder \"" + path + "\"?" };
            try
            {
                if (!Directory.Exists(path)) return new ToolResult { Text = "That folder doesn't exist: " + path };
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                ActivityLog.Add("Opened folder " + path);
                return new ToolResult { Text = "Opened " + path + "." };
            }
            catch (Exception error) { return new ToolResult { Text = "Couldn't open the folder: " + error.Message }; }
        }

        internal static ToolResult SearchFiles(string folder, string pattern)
        {
            folder = (folder ?? "").Trim();
            pattern = (pattern ?? "*").Trim();
            if (folder.Length == 0) return new ToolResult { Text = "Tell me which folder to search." };
            if (!Directory.Exists(folder)) return new ToolResult { Text = "That folder doesn't exist: " + folder };
            var found = new List<string>();
            try
            {
                foreach (string file in Directory.EnumerateFiles(folder, pattern, SearchOption.TopDirectoryOnly))
                {
                    found.Add(file);
                    if (found.Count >= 50) break;
                }
            }
            catch (Exception error) { return new ToolResult { Text = "Search failed: " + error.Message }; }
            ActivityLog.Add("Searched " + folder + " for " + pattern + " (" + found.Count + ")");
            if (found.Count == 0) return new ToolResult { Text = "Nothing matched \"" + pattern + "\" in " + folder + "." };
            return new ToolResult { Text = string.Join("\n", found) };
        }

        internal static ToolResult CurrentTime()
        {
            return new ToolResult { Text = DateTime.Now.ToString("dddd, yyyy-MM-dd HH:mm:ss") };
        }
    }
    // Plugin API. A plugin is a .NET assembly dropped into the plugins folder
    // next to Capri's data. It can offer tools the AI can call. Plugins load
    // in their own load context and a crashing plugin never takes Capri down.
    //
    // Plugin contract (implement both interfaces in your assembly):
    //   public sealed class MyPlugin : ICompanionPlugin { ... }
    internal interface ICompanionTool
    {
        string Name { get; }
        string Description { get; }
        string Execute(IDictionary<string, string> args);
    }

    internal interface ICompanionPlugin
    {
        string Name { get; }
        string Version { get; }
        string Description { get; }
        IReadOnlyList<ICompanionTool> Tools { get; }
    }

    internal sealed class LoadedPlugin
    {
        internal ICompanionPlugin Plugin;
        internal string File;
        internal bool Enabled;
        internal string Error;
    }

    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;
        internal PluginLoadContext(string mainAssembly) : base(null, true)
        {
            resolver = new AssemblyDependencyResolver(mainAssembly);
        }

        protected override System.Reflection.Assembly Load(System.Reflection.AssemblyName name)
        {
            string path = resolver.ResolveAssemblyToPath(name);
            if (path != null) return LoadFromAssemblyPath(path);
            if (name.Name == typeof(ICompanionPlugin).Assembly.GetName().Name) return typeof(ICompanionPlugin).Assembly;
            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string path = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }
    }

    internal sealed class PluginManager
    {
        private readonly string folder;
        private readonly MemoryStore memory;
        internal readonly List<LoadedPlugin> Plugins = new List<LoadedPlugin>();
        internal readonly Dictionary<string, ICompanionTool> Tools = new Dictionary<string, ICompanionTool>(StringComparer.OrdinalIgnoreCase);

        internal PluginManager(string capriFolder, MemoryStore memoryStore)
        {
            folder = Path.Combine(capriFolder, "plugins");
            memory = memoryStore;
            Reload();
        }

        internal string FolderPath { get { return folder; } }

        internal void Reload()
        {
            Plugins.Clear(); Tools.Clear();
            Tools["system_info"] = new BuiltInTool("system_info", "Report OS, CPU, memory and time.", args => DesktopTools.SystemInfo().Text);
            Tools["open_app"] = new BuiltInTool("open_app", "Open an app (needs confirmation unless SECURITY allows).", args => DesktopTools.OpenApp(args.TryGetValue("name", out string n) ? n : "", true).Text);
            Tools["open_folder"] = new BuiltInTool("open_folder", "Open a folder (needs confirmation unless SECURITY allows).", args => DesktopTools.OpenFolder(args.TryGetValue("path", out string p) ? p : "", true).Text);
            Tools["search_files"] = new BuiltInTool("search_files", "List files matching a pattern in a folder.", args => DesktopTools.SearchFiles(args.TryGetValue("folder", out string f) ? f : "", args.TryGetValue("pattern", out string pat) ? pat : "*").Text);
            Tools["current_time"] = new BuiltInTool("current_time", "Current date and time.", args => DesktopTools.CurrentTime().Text);
            foreach (ICompanionTool rogue in RogueTools.All()) Tools[rogue.Name] = rogue;
            try { Directory.CreateDirectory(folder); } catch (Exception) { }
            string[] files;
            try { files = Directory.GetFiles(folder, "*.dll"); }
            catch (Exception) { files = new string[0]; }
            foreach (string file in files) LoadOne(file);
            ActivityLog.Add("Plugins loaded: " + Plugins.Count + (Plugins.Any(p => p.Error != null) ? " (some failed)" : ""));
        }

        private void LoadOne(string file)
        {
            var loaded = new LoadedPlugin { File = file };
            try
            {
                var context = new PluginLoadContext(file);
                System.Reflection.Assembly assembly = context.LoadFromAssemblyPath(Path.GetFullPath(file));
                foreach (Type type in assembly.GetTypes())
                {
                    if (!typeof(ICompanionPlugin).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface) continue;
                    var plugin = (ICompanionPlugin)Activator.CreateInstance(type);
                    loaded.Plugin = plugin;
                    loaded.Enabled = memory.GetPluginState(plugin.Name, "on") == "on";
                    if (loaded.Enabled)
                        foreach (ICompanionTool tool in plugin.Tools ?? new List<ICompanionTool>()) Tools[tool.Name] = tool;
                    break;
                }
                if (loaded.Plugin == null) loaded.Error = "No ICompanionPlugin found.";
            }
            catch (Exception error) { loaded.Error = error.Message; }
            Plugins.Add(loaded);
            if (loaded.Error != null) ActivityLog.Add("Plugin failed: " + Path.GetFileName(file) + " - " + loaded.Error);
        }

        internal void SetEnabled(LoadedPlugin loaded, bool on)
        {
            loaded.Enabled = on;
            if (loaded.Plugin != null) memory.SetPluginState(loaded.Plugin.Name, on);
            Reload();
        }

        private sealed class BuiltInTool : ICompanionTool
        {
            private readonly Func<IDictionary<string, string>, string> run;
            internal BuiltInTool(string name, string description, Func<IDictionary<string, string>, string> run)
            {
                Name = name; Description = description; this.run = run;
            }
            public string Name { get; }
            public string Description { get; }
            public string Execute(IDictionary<string, string> args) { return run(args); }
        }
    }
}

