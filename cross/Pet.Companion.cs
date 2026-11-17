using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace CapriDesktop
{
    // Companion window settings, kept in the same settings file as the rest.
    internal sealed partial class Settings
    {
        [DataMember] internal string CompanionProvider;
        [DataMember] internal string CompanionEndpoint;
        [DataMember] internal string CompanionModel;
        [DataMember] internal string CompanionKey;
        [DataMember] internal double CompanionTemperature;
        [DataMember] internal int CompanionMaxTokens;
        [DataMember] internal string CompanionPersonalityId;
        [DataMember] internal string CompanionCustomPrompt;
        [DataMember] internal bool CompanionSpeak;
        [DataMember] internal bool CompanionAvatar;
        [DataMember] internal bool CompanionConfirm;
        // Rogue Mode: active state is NEVER restored across restarts (see
        // StartupRogueOff). Level 1..3 and allowed dirs persist.
        [DataMember] internal bool RogueMode;
        [DataMember] internal int RogueLevel;
        [DataMember] internal string RogueDirs;
    }

    internal sealed partial class Pet
    {
        private CompanionWindow companion;
        internal string FolderPath { get { return folder; } }
        internal MemoryStore CompanionMemory;

        // Where user data lives. Portable installs (Rogue Capri, E:\Rogue Capri)
        // keep everything under <install dir>\Data; dev/plain runs keep the
        // legacy %AppData%\Capri folder. RogueCapriHome overrides both.
        internal static string DataRoot()
        {
            string home = Environment.GetEnvironmentVariable("RogueCapriHome");
            if (!string.IsNullOrWhiteSpace(home))
            {
                try { Directory.CreateDirectory(home); return home; } catch (Exception) { }
            }
            string installDir = Path.GetDirectoryName(typeof(Pet).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(installDir))
            {
                string data = Path.Combine(installDir, "Data");
                if (Directory.Exists(data)) { EnsureRogueLayout(installDir); return data; }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri");
        }

        private static void EnsureRogueLayout(string installDir)
        {
            foreach (string part in new[] { "Config", "Logs", "Cache", "Updates", "Backup" })
            {
                try { Directory.CreateDirectory(Path.Combine(installDir, part)); } catch (Exception) { }
            }
            // One-time migration: portable Data is empty but legacy AppData has data.
            try
            {
                string data = Path.Combine(installDir, "Data");
                string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri");
                if (Directory.Exists(legacy) && !Directory.EnumerateFileSystemEntries(data).Any())
                {
                    foreach (string file in Directory.EnumerateFiles(legacy))
                        File.Copy(file, Path.Combine(data, Path.GetFileName(file)), true);
                    string plugins = Path.Combine(legacy, "plugins");
                    if (Directory.Exists(plugins))
                    {
                        string target = Path.Combine(data, "plugins");
                        Directory.CreateDirectory(target);
                        foreach (string file in Directory.EnumerateFiles(plugins))
                            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                    }
                    Brain.Log("rogue", -1, "Migrated Capri data from AppData to " + data);
                }
            }
            catch (Exception error) { Brain.Log("rogue", -1, "Data migration: " + error.Message); }
        }

        internal void OpenCompanion()
        {
            if (CompanionMemory == null) CompanionMemory = new MemoryStore(folder);
            if (companion == null)
            {
                companion = new CompanionWindow(this);
                companion.Closed += (_, _) => companion = null;
                companion.Show();
            }
            else
            {
                companion.Show();
                companion.WindowState = Avalonia.Controls.WindowState.Normal;
                companion.Activate();
            }
        }

        internal void CloseCompanion() { if (companion != null) companion.Close(); }

        internal void ChangeBodySize(double size)
        {
            body.Size = size;
            Saved.Size = size;
            Fit();
            Save();
        }

        // Speaks text with her current voice (ElevenLabs / local TTS).
        // Returns the WAV bytes, or null when no voice is set up.
        internal byte[] CompanionSpeak(string text)
        {
            try { return voice.Speak(text); }
            catch (Exception error) { Brain.Log("companion", -1, "Voice test: " + error.Message); return null; }
        }

        internal void CompanionPlay(byte[] wav)
        {
            if (wav == null) return;
            try { voice.Play(wav); }
            catch (Exception error) { Brain.Log("companion", -1, "Voice play: " + error.Message); }
        }

        internal void CompanionSay(string text)
        {
            byte[] wav = CompanionSpeak(text);
            if (wav != null) CompanionPlay(wav);
        }
    }
}


