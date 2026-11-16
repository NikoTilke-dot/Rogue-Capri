using System;
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
    }

    internal sealed partial class Pet
    {
        private CompanionWindow companion;
        internal string FolderPath { get { return folder; } }
        internal MemoryStore CompanionMemory;

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


