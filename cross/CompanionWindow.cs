using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Animated avatar preview used on the dashboard and character pages.
    // Reuses Capri's sprite strips; states map to idle / talking (mouth
    // overlay) / happy (grin) / sleeping.
    internal sealed class AvatarView : Control
    {
        private readonly Clip idle = new Clip("idle", 16, 8, 1.11);
        private readonly Clip grin = new Clip("grin", 16, 8, 1.11);
        private readonly Clip sleep = new Clip("sleep", 16, 6, 1.11);
        private readonly Clip mouth = new Clip("mouth", 5, 12, 1.11);
        private readonly DispatcherTimer timer;
        private double cell;
        private string state = "idle";

        internal AvatarView()
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) => { cell += 0.1; InvalidateVisual(); };
            timer.Start();
        }

        internal string State
        {
            get { return state; }
            set { state = value ?? "idle"; InvalidateVisual(); }
        }

        internal void Stop() { timer.Stop(); }

        public override void Render(DrawingContext context)
        {
            Clip shown = state == "happy" ? grin : state == "sleep" ? sleep : idle;
            double tall = Bounds.Height;
            if (tall <= 0) return;
            double wide = tall * shown.Wide / shown.Strip.PixelSize.Height;
            double x = (Bounds.Width - wide) / 2;
            var place = new Rect(x, 0, wide, tall);
            int frame = (int)(cell * shown.Fps) % shown.Count;
            context.DrawImage(shown.Strip, new Rect(frame * shown.Wide, 0, shown.Wide, shown.Strip.PixelSize.Height), place);
            if (state == "talk")
            {
                int m = (int)(cell * mouth.Fps) % mouth.Count;
                context.DrawImage(mouth.Strip, new Rect(m * mouth.Wide, 0, mouth.Wide, mouth.Strip.PixelSize.Height), place);
            }
        }
    }

    // The Capri Companion window: dashboard, chat, character, AI config,
    // memory, voice, tools/plugins and system pages in one modern window.
    // Open it from the tray menu: "Open Capri Companion".
    internal sealed class CompanionWindow : MainframeWindow
    {
        private readonly Pet pet;
        private readonly MemoryStore memory;
        private readonly PluginManager plugins;
        private readonly AvatarView avatar = new AvatarView();
        private readonly ContentControl content = new ContentControl();
        private readonly string[] pages = { "dashboard", "chat", "character", "ai", "memory", "voice", "tools", "system" };
        private readonly string[] pageNames = { "Dashboard", "Chat", "Character", "AI & Models", "Memory", "Voice", "Tools & Plugins", "System" };
        private readonly List<NeonButton> navButtons = new List<NeonButton>();
        private string statusText = "Ready.";
        private Control chatPage;
        private TextBlock chatStatus;
        private TextBlock dashboardLog, toolsLog;
        private readonly DispatcherTimer refresher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        internal CompanionWindow(Pet owner) : base("Companion")
        {
            pet = owner;
            RogueHub.LoadFrom(pet);
            memory = pet.CompanionMemory ?? (pet.CompanionMemory = new MemoryStore(pet.FolderPath));
            plugins = new PluginManager(pet.FolderPath, memory);
            BuildShell();
            Fit(980, 660, 1);
            ShowPage("dashboard");
            refresher.Tick += (_, _) => RefreshLogs();
            refresher.Start();
            Closed += (_, _) => { refresher.Stop(); avatar.Stop(); if (chatSource != null) chatSource.Cancel(); };
            ActivityLog.Add("Companion opened");
        }

        private void RefreshLogs()
        {
            try
            {
                string joined = string.Join("\n", Tail(ActivityLog.Snapshot(), 8));
                if (dashboardLog != null) dashboardLog.Text = joined;
                if (toolsLog != null) toolsLog.Text = string.Join("\n", Tail(ActivityLog.Snapshot(), 12));
            }
            catch (Exception) { }
        }

        private static IEnumerable<string> Tail(string[] all, int count)
        {
            int from = Math.Max(0, all.Length - count);
            for (int i = from; i < all.Length; i++) yield return all[i];
        }

        private void BuildShell()
        {
            var root = new Grid
            {
                Width = 980, Height = 660,
                ColumnDefinitions = new ColumnDefinitions("200,*")
            };
            Canvas.SetLeft(root, 0); Canvas.SetTop(root, 0);
            Page.Children.Add(root);

            var sidebar = new Border { Background = MainframeTheme.Brush("#1A0D16"), Padding = new Thickness(12) };
            Grid.SetColumn(sidebar, 0);
            var nav = new StackPanel { Spacing = 6, Margin = new Thickness(0, 34, 0, 0) };
            sidebar.Child = nav;
            root.Children.Add(sidebar);

            var caption = new NeonCaption { Text = "CAPRI COMPANION", Ink = MainframeTheme.Rose, Size = 17, Bold = true, Display = true, Width = 176, Height = 24 };
            var sideHost = new Canvas();
            sideHost.Children.Add(caption);
            Canvas.SetLeft(nav, 0); Canvas.SetTop(nav, 30);
            sideHost.Children.Add(nav);
            sidebar.Child = sideHost;

            for (int i = 0; i < pages.Length; i++)
            {
                int at = i;
                var button = new NeonButton { Text = pageNames[i], Width = 176, Height = 30 };
                button.Click += (_, _) => ShowPage(pages[at]);
                nav.Children.Add(button);
                navButtons.Add(button);
            }
            rogueBadge = new NeonCaption { Text = "normal mode", Ink = MainframeTheme.Faint, Size = 12, Bold = true, Width = 176, Height = 20 };
            nav.Children.Add(rogueBadge);
            RefreshRogueBadge();

            var main = new Border { Background = MainframeTheme.Brush("#120810"), Padding = new Thickness(16) };
            Grid.SetColumn(main, 1);
            main.Child = content;
            root.Children.Add(main);
        }

        private void ShowPage(string name)
        {
            for (int i = 0; i < navButtons.Count; i++) navButtons[i].Picked = pages[i] == name;
            if (name == "chat")
            {
                if (chatPage == null) chatPage = BuildChat();
                content.Content = chatPage;
                return;
            }
            Control page;
            switch (name)
            {
                case "dashboard": page = BuildDashboard(); break;
                case "character": page = BuildCharacter(); break;
                case "ai": page = BuildAi(); break;
                case "memory": page = BuildMemory(); break;
                case "voice": page = BuildVoice(); break;
                case "tools": page = BuildTools(); break;
                case "system": page = BuildSystem(); break;
                default: page = BuildDashboard(); break;
            }
            content.Content = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            RefreshLogs();
        }

        private void SetStatus(string text)
        {
            statusText = text;
            ActivityLog.Add(text);
            if (chatStatus != null) chatStatus.Text = text;
        }

        private NeonCaption rogueBadge;

        private void RefreshRogueBadge()
        {
            if (rogueBadge == null) return;
            rogueBadge.Text = RogueHub.On ? "ROGUE MODE · LEVEL " + RogueHub.Level : "normal mode";
            rogueBadge.Ink = RogueHub.On ? MainframeTheme.Hot : MainframeTheme.Faint;
        }

        private bool HandleRogueCommand(string text)
        {
            string t = (text ?? "").ToLowerInvariant();
            bool on = t.Contains("rogue mode aktivieren") || t.Contains("rogue mode an") || t.Contains("rogue mode on")
                || t.Contains("activate rogue mode") || t.Contains("werde frecher") || t.Contains("werde heute etwas frecher")
                || t.Contains("become more mischievous") || t.Contains("rogue mode ein");
            bool off = t.Contains("rogue mode aus") || t.Contains("rogue mode off") || t.Contains("rogue mode deaktivieren")
                || t.Contains("deactivate rogue mode") || t.Contains("zurueck zum normalen modus") || t.Contains("zurück zum normalen modus")
                || t.Contains("back to normal mode");
            if (!on && !off) return false;
            if (on)
            {
                if (!RogueHub.SetOn(true))
                {
                    SetStatus("Rogue Mode needs at least one allowed folder for level " + RogueHub.Level + ". Add one on the Character page.");
                    AppendBubble("assistant", "Level " + RogueHub.Level + " needs an allowed folder first. Tell me where I may play: Character page, add a folder.");
                    return true;
                }
                AppendBubble("assistant", "Rogue Mode ist AN (Stufe " + RogueHub.Level + "). Freche Sachen erlaubt - aber weiterhin nur in deinen freigegebenen Ordnern, alles wird geloggt und rückgängig = alles ohne Löschen.");
            }
            else
            {
                RogueHub.SetOn(false);
                AppendBubble("assistant", "Rogue Mode ist AUS. Zurück zum normalen Modus.");
            }
            RefreshRogueBadge();
            return true;
        }

        // ---- small control helpers -------------------------------------------

        internal static TextBlock Label(string text, IBrush ink = null, double size = 13, bool bold = false)
        {
            return new TextBlock
            {
                Text = text ?? "", Foreground = ink ?? MainframeTheme.White, FontFamily = MainframeTheme.Text,
                FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap
            };
        }

        internal static TextBox Entry(string cue, string text = "", bool secret = false, bool multiline = false)
        {
            return new TextBox
            {
                Text = text ?? "", Watermark = cue, PasswordChar = secret ? '*' : '\0',
                FontFamily = MainframeTheme.Text, FontSize = 13, Foreground = MainframeTheme.White,
                Background = MainframeTheme.Well, BorderBrush = MainframeTheme.WellEdge, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
        }

        internal static NeonButton Button(string text, Action action, double width = 140)
        {
            var button = new NeonButton { Text = text, Width = width, Height = 30 };
            button.Click += (_, _) => action();
            return button;
        }

        internal static Border Card(Control child, double width = double.NaN)
        {
            var card = new Border { Background = MainframeTheme.Field, CornerRadius = new CornerRadius(12), Padding = new Thickness(12), Margin = new Thickness(0, 0, 10, 10) };
            if (!double.IsNaN(width)) card.Width = width;
            card.Child = child;
            return card;
        }

        internal static StackPanel Column(params Control[] children)
        {
            var panel = new StackPanel { Spacing = 8 };
            foreach (Control child in children) panel.Children.Add(child);
            return panel;
        }

        internal static StackPanel Row(params Control[] children)
        {
            var panel = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
            foreach (Control child in children) panel.Children.Add(child);
            return panel;
        }
        private Control BuildDashboard()
        {
            IAiProvider provider = CurrentProvider();
            Personality personality = Personalities.Find(pet.Saved.CompanionPersonalityId, pet.Saved);
            var cards = new StackPanel { Spacing = 0, Orientation = Orientation.Horizontal };
            cards.Children.Add(Card(Column(Label("AI STATUS", MainframeTheme.Faint, 11, true), Label(provider.Name, MainframeTheme.White, 15, true), Label(CurrentModel(provider), MainframeTheme.Cyan, 12)), 210));
            cards.Children.Add(Card(Column(Label("PERSONALITY", MainframeTheme.Faint, 11, true), Label(personality.Name, MainframeTheme.White, 15, true), Label(memory.Count + " memories", MainframeTheme.Cyan, 12)), 210));
            cards.Children.Add(Card(Column(Label("VOICE", MainframeTheme.Faint, 11, true), Label(VoiceModeName(), MainframeTheme.White, 15, true), Label(pet.MainframeVoiceName ?? "no voice", MainframeTheme.Cyan, 12)), 210));
            cards.Children.Add(Card(Column(Label("SESSION", MainframeTheme.Faint, 11, true), Label(Uptime(), MainframeTheme.White, 15, true), Label(Environment.ProcessorCount + " CPU cores", MainframeTheme.Cyan, 12)), 210));
            var quick = Row(
                Button("New chat", () => ShowPage("chat"), 110),
                Button("Test brain", TestBrain, 110),
                Button("Open Mainframe", () => pet.OpenMainframe(), 150),
                Button("Open Mods", () => { pet.OpenMainframe(); pet.Frame.OpenPage("mods"); }, 110));
            var status = Label(statusText, MainframeTheme.Cyan, 13);
            dashboardLog = Label("", MainframeTheme.Faint, 12);
            var avatarBox = new Border { Background = MainframeTheme.Well, CornerRadius = new CornerRadius(12), Width = 220, Height = 220, Child = avatar };
            var top = Row(avatarBox, Column(Label("Welcome back.", MainframeTheme.Rose, 24, true), status, quick, Label("Capri lives on your desktop. Chat with her, teach her memory, or let her run tools when you say so.", MainframeTheme.Faint, 13)));
            var stack = Column(Label("DASHBOARD", MainframeTheme.Rose, 20, true), top, Label("RECENT ACTIVITY", MainframeTheme.Faint, 11, true), dashboardLog);
            stack.Children.Insert(1, cards);
            return stack;
        }

        private string CurrentModel(IAiProvider provider)
        {
            if (provider.Id == "capri") return pet.Brain.Model ?? "no brain set up";
            return string.IsNullOrWhiteSpace(provider.Model) ? "(default model)" : provider.Model.Trim();
        }

        private string VoiceModeName()
        {
            switch (pet.MainframeVoiceMode) { case 0: return "Off (text only)"; case 2: return "Local TTS"; case 3: return "Cloud voice"; default: return "Off"; }
        }

        private string Uptime()
        {
            try
            {
                TimeSpan up = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
                return up.TotalHours >= 1 ? ((int)up.TotalHours) + "h " + up.Minutes + "m" : up.Minutes + "m " + up.Seconds + "s";
            }
            catch (Exception) { return "-"; }
        }

        private void TestBrain()
        {
            IAiProvider provider = CurrentProvider();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string detail;
                bool ok = provider.TestConnection(out detail);
                Dispatcher.UIThread.Post(() => SetStatus((ok ? "Brain OK: " : "Brain issue: ") + detail));
            });
        }

        private IAiProvider CurrentProvider()
        {
            return ProviderRegistry.Create(pet.Saved.CompanionProvider ?? "capri", pet, pet.Saved);
        }

        // ---- chat -----------------------------------------------------------

        private readonly List<ChatMsg> session = new List<ChatMsg>();
        private CancellationTokenSource chatSource;
        private bool sending;
        private TextBox chatInput;
        private StackPanel messages;
        private NeonButton sendButton, stopButton;
        private readonly StringBuilder streamingText = new StringBuilder();
        private TextBlock streamingBubble;

        private Control BuildChat()
        {
            messages = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 0, 10) };
            var scroll = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            chatInput = Entry("Message Capri...", "", false, false);
            chatInput.KeyDown += (_, e) => { if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { SendChat(); e.Handled = true; } };
            chatStatus = Label("Ready.", MainframeTheme.Faint, 12);
            sendButton = Button("Send", SendChat, 90);
            stopButton = Button("Stop", () => { if (chatSource != null) chatSource.Cancel(); }, 80);
            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto,Auto")
            };
            Grid.SetRow(scroll, 0); grid.Children.Add(scroll);
            var inputRow = Row(chatInput, sendButton, stopButton);
            inputRow.Children[0] = chatInput;
            Grid.SetRow(inputRow, 1); grid.Children.Add(inputRow);
            var tools = Row(Button("New chat", NewChat, 100), Button("Regenerate", Regenerate, 110), Button("Export", ExportChat, 90), chatStatus);
            Grid.SetRow(tools, 2); grid.Children.Add(tools);
            return grid;
        }

        private void NewChat()
        {
            session.Clear();
            if (messages != null) messages.Children.Clear();
            SetStatus("New chat started.");
        }

        private void Regenerate()
        {
            if (sending) return;
            if (session.Count > 0 && session[session.Count - 1].Role == "assistant") session.RemoveAt(session.Count - 1);
            if (session.Count == 0) return;
            RunTurn(0);
        }

        private void SendChat()
        {
            if (sending) return;
            string text = chatInput.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            chatInput.Text = "";
            if (HandleRogueCommand(text)) return;
            AppendBubble("user", text.Trim());
            session.Add(new ChatMsg("user", text.Trim()));
            memory.AddHistory("user", text.Trim());
            RunTurn(0);
        }

        private void RunTurn(int toolRound)
        {
            sending = true; UpdateChatButtons();
            avatar.State = "think";
            SetStatus("Thinking...");
            var messagesToSend = BuildMessages();
            IAiProvider provider = CurrentProvider();
            chatSource = new CancellationTokenSource();
            CancellationTokenSource source = chatSource;
            streamingText.Clear();
            streamingBubble = AppendBubble("assistant", "", true);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string error = provider.Stream(messagesToSend, token =>
                {
                    if (source.IsCancellationRequested) return;
                    streamingText.Append(token);
                    Dispatcher.UIThread.Post(() => { if (streamingBubble != null) streamingBubble.Text = CleanText(streamingText.ToString()); });
                }, source.Token);
                Dispatcher.UIThread.Post(() => FinishTurn(streamingText.ToString(), error, toolRound));
            });
        }

        private List<ChatMsg> BuildMessages()
        {
            var list = new List<ChatMsg>();
            Personality personality = RogueHub.On ? Personalities.Find("rogue", pet.Saved) : Personalities.Find(pet.Saved.CompanionPersonalityId, pet.Saved);
            string system = Personalities.BuildSystemPrompt(personality, memory);
            if (!string.IsNullOrWhiteSpace(system)) list.Add(new ChatMsg("system", system));
            int from = Math.Max(0, session.Count - 20);
            for (int i = from; i < session.Count; i++) list.Add(new ChatMsg(session[i].Role, session[i].Content));
            return list;
        }

        private void FinishTurn(string full, string error, int toolRound)
        {
            sending = false; UpdateChatButtons();
            if (error == "cancelled")
            {
                if (streamingBubble != null) streamingBubble.Text = CleanText(full) + " (cancelled)";
                avatar.State = "idle"; SetStatus("Cancelled.");
                return;
            }
            if (error != null)
            {
                if (streamingBubble != null) streamingBubble.Text = "Error: " + error;
                avatar.State = "idle"; SetStatus("Reply failed: " + error);
                return;
            }
            string cleaned = CleanText(full);
            if (streamingBubble != null) streamingBubble.Text = cleaned;
            session.Add(new ChatMsg("assistant", cleaned));
            memory.AddHistory("assistant", cleaned);
            avatar.State = "idle";
            SetStatus("Reply received.");
            if (pet.Saved.CompanionSpeak && !string.IsNullOrWhiteSpace(cleaned))
            {
                avatar.State = "talk";
                string say = cleaned.Length > 280 ? cleaned.Substring(0, 280) : cleaned;
                ThreadPool.QueueUserWorkItem(_ => pet.CompanionSay(say));
                DispatcherTimer.RunOnce(() => { if (avatar.State == "talk") avatar.State = "idle"; }, TimeSpan.FromSeconds(3));
            }
            var tools = ExtractTools(full);
            int maxRounds = RogueHub.On && RogueHub.Level >= 3 ? 5 : 3;
            if (tools.Count > 0 && toolRound < maxRounds) OfferTools(tools, toolRound);
        }

        private void UpdateChatButtons()
        {
            if (sendButton != null) sendButton.IsEnabled = !sending;
            if (stopButton != null) stopButton.IsEnabled = sending;
        }

        private TextBlock AppendBubble(string role, string text, bool streaming = false)
        {
            var body = new TextBlock
            {
                Text = text, FontFamily = MainframeTheme.Text, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Foreground = role == "user" ? MainframeTheme.Rose : MainframeTheme.White
            };
            var who = Label(role == "user" ? "YOU" : "CAPRI", role == "user" ? MainframeTheme.Rose : MainframeTheme.Cyan, 11, true);
            var bubble = new Border
            {
                Background = role == "user" ? MainframeTheme.Brush("#2A1220") : MainframeTheme.Brush("#101820"),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12), MaxWidth = 640,
                HorizontalAlignment = role == "user" ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
                Child = Column(who, body)
            };
            messages.Children.Add(bubble);
            return body;
        }
        // ---- AI tool protocol: [[TOOL name arg="value"]] -----------------------
        private sealed class ToolCall
        {
            internal string Name;
            internal Dictionary<string, string> Args = new Dictionary<string, string>();
            internal string Raw;
        }

        private static readonly Regex ToolPattern = new Regex(@"\[\[TOOL\s+([a-z_]+)([^\]]*)\]\]", RegexOptions.IgnoreCase);
        private static readonly Regex ArgPattern = new Regex(@"(\w+)=""([^""]*)""|(\w+)=(\S+)");

        private static string CleanText(string text)
        {
            return ToolPattern.Replace(text ?? "", "").Trim();
        }

        private static List<ToolCall> ExtractTools(string text)
        {
            var calls = new List<ToolCall>();
            if (string.IsNullOrEmpty(text)) return calls;
            foreach (Match match in ToolPattern.Matches(text))
            {
                var call = new ToolCall { Name = match.Groups[1].Value.ToLowerInvariant(), Raw = match.Value };
                foreach (Match arg in ArgPattern.Matches(match.Groups[2].Value))
                {
                    string key = arg.Groups[1].Success ? arg.Groups[1].Value : arg.Groups[3].Value;
                    string value = arg.Groups[1].Success ? arg.Groups[2].Value : arg.Groups[4].Value;
                    call.Args[key.ToLowerInvariant()] = value;
                }
                calls.Add(call);
            }
            return calls;
        }

        private bool ToolAllowed(string name)
        {
            if (RogueHub.On && name.StartsWith("rogue_"))
            {
                if (name == "rogue_play") return true;
                return RogueHub.Level >= 2;
            }
            if (pet.Saved.CompanionConfirm) return false;
            switch (name)
            {
                case "open_app": return Security.On(Security.Open);
                case "open_folder": return Security.On(Security.Windows);
                default: return true;
            }
        }

        private void OfferTools(List<ToolCall> calls, int toolRound)
        {
            foreach (ToolCall call in calls)
            {
                if (!plugins.Tools.ContainsKey(call.Name))
                {
                    AppendBubble("assistant", "(unknown tool requested: " + call.Name + ")");
                    continue;
                }
                if (call.Name.StartsWith("rogue_") && !RogueHub.On)
                {
                    AppendBubble("assistant", "(rogue tools are only active in Rogue Mode - say \"rogue mode aktivieren\" to turn it on)");
                    continue;
                }
                if (ToolAllowed(call.Name)) { RunTool(call, toolRound); continue; }
                ShowToolConfirm(call, toolRound);
            }
        }

        private void ShowToolConfirm(ToolCall call, int toolRound)
        {
            var row = new Border { Background = MainframeTheme.Brush("#2A1220"), CornerRadius = new CornerRadius(10), Padding = new Thickness(10), MaxWidth = 640 };
            var buttons = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
            var run = new NeonButton { Text = "Run", Width = 70, Height = 26 };
            var skip = new NeonButton { Text = "Skip", Width = 70, Height = 26 };
            buttons.Children.Add(run); buttons.Children.Add(skip);
            row.Child = Column(Label("Capri wants to run: " + call.Raw, MainframeTheme.Yellow, 13), buttons);
            messages.Children.Add(row);
            run.Click += (_, _) => { messages.Children.Remove(row); RunTool(call, toolRound); };
            skip.Click += (_, _) => { messages.Children.Remove(row); SetStatus("Tool skipped: " + call.Name); };
        }

        private void RunTool(ToolCall call, int toolRound)
        {
            string result;
            try { result = plugins.Tools[call.Name].Execute(call.Args); }
            catch (Exception error) { result = "Tool failed: " + error.Message; }
            messages.Children.Add(Label("Tool " + call.Name + " -> " + result, MainframeTheme.Faint, 12));
            ActivityLog.Add("Tool " + call.Name + " ran");
            session.Add(new ChatMsg("user", "[tool " + call.Name + " result] " + result));
            RunTurn(toolRound + 1);
        }

        private async void ExportChat()
        {
            try
            {
                var top = TopLevel.GetTopLevel(this);
                if (top == null || !top.StorageProvider.CanSave) { SetStatus("Save picker unavailable here."); return; }
                IStorageFile file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "capri-chat.txt" });
                if (file == null) return;
                var sb = new StringBuilder();
                foreach (ChatMsg m in session) sb.AppendLine(m.Role + ": " + m.Content).AppendLine();
                await File.WriteAllTextAsync(file.Path.LocalPath, sb.ToString());
                SetStatus("Chat exported to " + file.Path.LocalPath);
            }
            catch (Exception error) { SetStatus("Export failed: " + error.Message); }
        }
        private Control BuildAi()
        {
            var provider = new NeonPicker { Width = 340, Height = 30 };
            provider.Options(ProviderRegistry.Names);
            provider.Selected = Math.Max(0, Array.IndexOf(ProviderRegistry.Ids, pet.Saved.CompanionProvider ?? "capri"));
            var endpoint = Entry("Server address, e.g. http://127.0.0.1:11434", pet.Saved.CompanionEndpoint ?? "");
            var model = Entry("Model, e.g. gemma4:e4b", pet.Saved.CompanionModel ?? "");
            var key = Entry("API key (only used by OpenAI-compatible servers)", pet.Saved.CompanionKey ?? "", true);
            var temp = new Slider { Minimum = 0, Maximum = 2, Value = pet.Saved.CompanionTemperature > 0 ? pet.Saved.CompanionTemperature : 0.7, Width = 260 };
            var tempLabel = Label("Temperature: " + temp.Value.ToString("0.0"), MainframeTheme.Faint, 12);
            var maxTokens = Entry("Max tokens", (pet.Saved.CompanionMaxTokens > 0 ? pet.Saved.CompanionMaxTokens : 2048).ToString());
            var result = Label("", MainframeTheme.Cyan, 13);
            var modelList = new ListBox { Height = 130, Background = MainframeTheme.Well, Foreground = MainframeTheme.White };
            var personality = new NeonPicker { Width = 340, Height = 30 };
            var names = new List<string>();
            foreach (Personality p in Personalities.BuiltIns) names.Add(p.Name);
            personality.Options(names.ToArray());
            personality.Selected = Math.Max(0, Array.IndexOf(Personalities.BuiltIns, Personalities.Find(pet.Saved.CompanionPersonalityId, pet.Saved)));
            var customPrompt = Entry("Custom personality instructions...", pet.Saved.CompanionCustomPrompt ?? "", false, true);
            customPrompt.Height = 90;
            Action save = () =>
            {
                IAiProvider draft = CurrentProvider();
                draft.Endpoint = endpoint.Text; draft.Model = model.Text; draft.ApiKey = key.Text;
                draft.Temperature = temp.Value;
                int tokens; draft.MaxTokens = int.TryParse(maxTokens.Text, out tokens) && tokens > 0 ? tokens : 2048;
                ProviderRegistry.Save(draft, ProviderRegistry.Ids[provider.Selected], pet.Saved);
                pet.Saved.CompanionPersonalityId = Personalities.BuiltIns[personality.Selected].Id;
                pet.Saved.CompanionCustomPrompt = customPrompt.Text;
                pet.Save();
            };
            provider.Changed += _ => save();
            personality.Changed += _ => save();
            temp.ValueChanged += (_, e) => { tempLabel.Text = "Temperature: " + e.NewValue.ToString("0.0"); save(); };
            var test = Button("Test connection", () =>
            {
                save();
                IAiProvider live = CurrentProvider();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string detail; bool ok = live.TestConnection(out detail);
                    Dispatcher.UIThread.Post(() => { result.Text = (ok ? "OK: " : "FAILED: ") + detail; });
                });
            }, 160);
            var refresh = Button("Refresh models", () =>
            {
                save();
                IAiProvider live = CurrentProvider();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    string error; string[] found = live.ListModels(out error);
                    Dispatcher.UIThread.Post(() =>
                    {
                        modelList.Items.Clear();
                        foreach (string m in found) modelList.Items.Add(m);
                        result.Text = found.Length > 0 ? found.Length + " model(s) found." : "No models: " + error;
                    });
                });
            }, 160);
            modelList.SelectionChanged += (_, _) => { if (modelList.SelectedItem != null) { model.Text = modelList.SelectedItem.ToString(); save(); } };
            var applyNote = Label("Settings save automatically. Capri Brain uses whatever the Mainframe has set up.", MainframeTheme.Faint, 12);
            return Column(
                Label("AI & MODELS", MainframeTheme.Rose, 20, true),
                Label("Provider", MainframeTheme.Faint, 12, true), provider,
                Label("Server address", MainframeTheme.Faint, 12, true), endpoint,
                Label("Model", MainframeTheme.Faint, 12, true), model,
                Label("API key", MainframeTheme.Faint, 12, true), key,
                tempLabel, temp,
                Label("Max tokens", MainframeTheme.Faint, 12, true), maxTokens,
                Row(test, refresh), result, modelList,
                Label("Personality", MainframeTheme.Faint, 12, true), personality,
                Label("Custom instructions (only for the Custom personality)", MainframeTheme.Faint, 12, true), customPrompt,
                applyNote);
        }

        private Control BuildMemory()
        {
            var facts = new ListBox { Height = 150, Background = MainframeTheme.Well, Foreground = MainframeTheme.White };
            foreach (string f in memory.Facts) facts.Items.Add(f);
            var prefs = new ListBox { Height = 110, Background = MainframeTheme.Well, Foreground = MainframeTheme.White };
            foreach (string p in memory.Preferences) prefs.Items.Add(p);
            var addFact = Entry("New fact about the user...");
            var addPref = Entry("New preference...");
            var note = Label(memory.Count + " stored items Â· " + memory.HistoryCount + " history entries", MainframeTheme.Faint, 12);
            var addFactButton = Button("Add fact", () => { memory.AddFact(addFact.Text); facts.Items.Add(addFact.Text); addFact.Text = ""; note.Text = memory.Count + " stored items"; }, 110);
            var removeFactButton = Button("Remove fact", () => { if (facts.SelectedIndex >= 0) { memory.RemoveFact(facts.SelectedIndex); facts.Items.RemoveAt(facts.SelectedIndex); note.Text = memory.Count + " stored items"; } }, 130);
            var addPrefButton = Button("Add preference", () => { memory.AddPreference(addPref.Text); prefs.Items.Add(addPref.Text); addPref.Text = ""; note.Text = memory.Count + " stored items"; }, 140);
            var removePrefButton = Button("Remove preference", () => { if (prefs.SelectedIndex >= 0) { memory.RemovePreference(prefs.SelectedIndex); prefs.Items.RemoveAt(prefs.SelectedIndex); note.Text = memory.Count + " stored items"; } }, 160);
            var clearHistory = Button("Clear history", () => { memory.ClearHistory(); note.Text = memory.Count + " stored items"; SetStatus("Conversation history cleared."); }, 140);
            var export = Button("Export memory", ExportMemory, 140);
            var import = Button("Import memory", ImportMemory, 140);
            var injected = Label("Facts and preferences are injected into the system prompt for Ollama / OpenAI-compatible providers. Capri's own brain keeps its memory through the Mainframe.", MainframeTheme.Faint, 12);
            return Column(
                Label("MEMORY", MainframeTheme.Rose, 20, true), note, injected,
                Label("Facts", MainframeTheme.Faint, 12, true), facts, Row(addFact, addFactButton, removeFactButton),
                Label("Preferences", MainframeTheme.Faint, 12, true), prefs, Row(addPref, addPrefButton, removePrefButton),
                Row(clearHistory, export, import));
        }

        private async void ExportMemory()
        {
            try
            {
                var top = TopLevel.GetTopLevel(this);
                if (top == null || !top.StorageProvider.CanSave) { SetStatus("Save picker unavailable here."); return; }
                IStorageFile file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "capri-memory.json" });
                if (file == null) return;
                await File.WriteAllTextAsync(file.Path.LocalPath, memory.ExportJson());
                SetStatus("Memory exported to " + file.Path.LocalPath);
            }
            catch (Exception error) { SetStatus("Export failed: " + error.Message); }
        }

        private async void ImportMemory()
        {
            try
            {
                var top = TopLevel.GetTopLevel(this);
                if (top == null || !top.StorageProvider.CanOpen) { SetStatus("Open picker unavailable here."); return; }
                IReadOnlyList<IStorageFile> picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
                if (picked.Count == 0) return;
                string json = await File.ReadAllTextAsync(picked[0].Path.LocalPath);
                memory.ImportJson(json);
                SetStatus("Memory imported. Reopen this page to see it.");
            }
            catch (Exception error) { SetStatus("Import failed: " + error.Message); }
        }
        private Control BuildVoice()
        {
            var mode = Label("Current: " + VoiceModeName() + " Â· " + (pet.MainframeVoiceName ?? "no voice set up"), MainframeTheme.Cyan, 14);
            var off = Button("Off (text only)", () => { pet.MainframeVoiceModePick(0); ShowPage("voice"); }, 180);
            var local = Button("Local TTS", () => { pet.MainframeVoiceModePick(2); ShowPage("voice"); }, 130);
            var cloud = Button("Cloud voice", () => { pet.MainframeVoiceModePick(3); ShowPage("voice"); }, 140);
            var test = Button("Say hello", () =>
            {
                avatar.State = "talk";
                ThreadPool.QueueUserWorkItem(_ => { pet.CompanionSay("Hello! I'm Capri, your desktop companion."); Dispatcher.UIThread.Post(() => { if (avatar.State == "talk") avatar.State = "idle"; }); });
            }, 130);
            var speak = new CheckBox { Content = "Speak chat replies aloud", IsChecked = pet.Saved.CompanionSpeak };
            speak.IsCheckedChanged += (_, _) => { pet.Saved.CompanionSpeak = speak.IsChecked == true; pet.Save(); };
            var avatarBox = new Border { Background = MainframeTheme.Well, CornerRadius = new CornerRadius(12), Width = 160, Height = 160, Child = avatar };
            var note = Label("Voice uses Capri's existing voice setup (ElevenLabs key or local TTS server) â€” configure it in the Mainframe VOICE page. Microphone speech recognition isn't available in the cross-platform build; use the Windows app for voice calls.", MainframeTheme.Faint, 13);
            var problem = Label(pet.MainframeVoiceProblem ?? "", MainframeTheme.Yellow, 12);
            return Column(
                Label("VOICE", MainframeTheme.Rose, 20, true), mode, problem,
                Row(off, local, cloud),
                Row(test, speak),
                Row(avatarBox, note));
        }

        private Control BuildCharacter()
        {
            var avatarBox = new Border { Background = MainframeTheme.Well, CornerRadius = new CornerRadius(12), Width = 300, Height = 300, Child = avatar };
            var stateRow = Row(
                Button("Idle", () => avatar.State = "idle", 80),
                Button("Talking", () => avatar.State = "talk", 90),
                Button("Happy", () => avatar.State = "happy", 80),
                Button("Sleeping", () => avatar.State = "sleep", 100));
            var sizeRow = new StackPanel { Spacing = 8, Orientation = Orientation.Horizontal };
            for (int i = 0; i < Pet.Sizes.Length; i++)
            {
                double size = Pet.Sizes[i];
                var b = Button(Pet.SizeNames[i], () => { pet.ChangeBodySize(size); ShowPage("character"); }, 90);
                b.Picked = Math.Abs(pet.Saved.Size - size) < 0.5;
                sizeRow.Children.Add(b);
            }
            var note = Label("These control her desktop avatar. The preview here mirrors her sprite animations; the chat window above shows her state while she thinks and talks.", MainframeTheme.Faint, 13);
            return Column(
                Label("CHARACTER", MainframeTheme.Rose, 20, true), avatarBox, stateRow,
                Label("Desktop avatar size: " + pet.Saved.Size, MainframeTheme.Faint, 12, true), sizeRow, note,
                Label("ROGUE MODE", MainframeTheme.Hot, 20, true), BuildRogueSection());
        }

        private Control BuildRogueSection()
        {
            var on = new CheckBox { Content = "Rogue Mode aktivieren", IsChecked = RogueHub.On };
            on.IsCheckedChanged += (_, _) =>
            {
                if (!RogueHub.SetOn(on.IsChecked == true))
                {
                    on.IsChecked = false;
                    SetStatus("Level " + RogueHub.Level + " needs an allowed folder. Add one below.");
                }
                RefreshRogueBadge();
            };
            var level = new NeonPicker { Width = 340, Height = 30 };
            level.Options(new[] { "1 · Creative (play & jokes)", "2 · Autonomous (files & programs in allowed folders)", "3 · Advanced (multi-step, no per-step prompts)" });
            level.Selected = RogueHub.Level - 1;
            level.Changed += i => { RogueHub.Level = i + 1; RogueHub.Persist(); RefreshRogueBadge(); SetStatus("Rogue level " + RogueHub.Level + " active."); };
            var dirs = new ListBox { Height = 110, Background = MainframeTheme.Well, Foreground = MainframeTheme.White };
            foreach (string d in RogueHub.Dirs) dirs.Items.Add(d);
            var addDir = Entry("Folder path, e.g. E:\\Rogue Capri\\Data\\Workspace");
            var addButton = Button("Allow folder", () =>
            {
                string path = addDir.Text.Trim();
                if (!RogueGuard.IsAllowedDir(path)) { SetStatus("Refused: not an existing, usable folder (system folders are not allowed)."); return; }
                RogueHub.Dirs.Add(path);
                RogueHub.Persist();
                dirs.Items.Add(path);
                addDir.Text = "";
                SetStatus("Allowed folder added: " + path);
            }, 140);
            var removeButton = Button("Remove selected", () =>
            {
                if (dirs.SelectedIndex < 0) return;
                string removed = dirs.SelectedItem.ToString();
                RogueHub.Dirs.RemoveAll(d => string.Equals(d, removed, StringComparison.OrdinalIgnoreCase));
                dirs.Items.RemoveAt(dirs.SelectedIndex);
                RogueHub.Persist();
                SetStatus("Allowed folder removed: " + removed);
            }, 150);
            var kill = Button("NOTAUS: Rogue Mode aus", () => { RogueHub.SetOn(false); RefreshRogueBadge(); ShowPage("character"); }, 220);
            var guard = Label("Guardrails: only inside the allowed folders, never delete, no credential files, everything logged. Rogue Mode is always off after a restart.", MainframeTheme.Faint, 12);
            return Column(
                Label("Status: " + RogueHub.Who() + " · Level " + RogueHub.Level, MainframeTheme.Cyan, 14),
                on, Label("Stufe", MainframeTheme.Faint, 12, true), level,
                Label("Freigegebene Ordner", MainframeTheme.Faint, 12, true), dirs,
                Row(addDir, addButton), removeButton,
                kill, guard);
        }

        private Control BuildTools()
        {
            var confirm = new CheckBox { Content = "Always confirm tools before running", IsChecked = pet.Saved.CompanionConfirm };
            confirm.IsCheckedChanged += (_, _) => { pet.Saved.CompanionConfirm = confirm.IsChecked == true; pet.Save(); };
            var open = new CheckBox { Content = Security.Names[Security.Open], IsChecked = Security.On(Security.Open) };
            var windows = new CheckBox { Content = Security.Names[Security.Windows], IsChecked = Security.On(Security.Windows) };
            var sends = new CheckBox { Content = Security.Names[Security.Sends], IsChecked = Security.On(Security.Sends) };
            open.IsCheckedChanged += (_, _) => Security.Set(Security.Open, open.IsChecked == true);
            windows.IsCheckedChanged += (_, _) => Security.Set(Security.Windows, windows.IsChecked == true);
            sends.IsCheckedChanged += (_, _) => Security.Set(Security.Sends, sends.IsChecked == true);
            var output = Label("", MainframeTheme.Cyan, 13);
            var appName = Entry("App to open, e.g. notepad");
            var runApp = Button("Open app", () => ShowToolResult(output, DesktopTools.OpenApp(appName.Text, false)), 110);
            var info = Button("System info", () => ShowToolResult(output, DesktopTools.SystemInfo()), 130);
            var searchFolder = Entry("Folder to search");
            var pattern = Entry("Pattern, e.g. *.txt");
            var search = Button("Search files", () => ShowToolResult(output, DesktopTools.SearchFiles(searchFolder.Text, pattern.Text)), 130);
            toolsLog = Label("", MainframeTheme.Faint, 12);
            var pluginRows = new StackPanel { Spacing = 6 };
            foreach (LoadedPlugin loaded in plugins.Plugins)
            {
                TrackPluginRow(pluginRows, loaded);
            }
            var reload = Button("Reload plugins", () => { plugins.Reload(); ShowPage("tools"); }, 150);
            var pluginNote = Label("Drop plugin .dll files into " + plugins.FolderPath + " and press Reload. A plugin offers tools Capri can call from chat.", MainframeTheme.Faint, 12);
            return Column(
                Label("TOOLS & PLUGINS", MainframeTheme.Rose, 20, true),
                Label("Permissions (same toggles the Mainframe uses)", MainframeTheme.Faint, 12, true), confirm, open, windows, sends,
                Label("Try a tool", MainframeTheme.Faint, 12, true), Row(appName, runApp), Row(info, searchFolder, pattern, search), output,
                Label("Action log", MainframeTheme.Faint, 12, true), toolsLog,
                Label("Installed plugins (" + plugins.Plugins.Count + ")", MainframeTheme.Faint, 12, true), pluginRows, reload, pluginNote);
        }

        private void TrackPluginRow(StackPanel rows, LoadedPlugin loaded)
        {
            string title = loaded.Plugin != null ? loaded.Plugin.Name + " v" + loaded.Plugin.Version : loaded.File;
            string sub = loaded.Error != null ? "Failed: " + loaded.Error : (loaded.Plugin != null ? loaded.Plugin.Description : "");
            var on = new CheckBox { Content = title + " â€” " + sub, IsChecked = loaded.Plugin != null && loaded.Enabled, IsEnabled = loaded.Plugin != null };
            on.IsCheckedChanged += (_, _) => { plugins.SetEnabled(loaded, on.IsChecked == true); ShowPage("tools"); };
            rows.Children.Add(on);
        }

        private void ShowToolResult(TextBlock output, DesktopTools.ToolResult result)
        {
            if (result.NeedsConfirm)
            {
                output.Text = "Needs confirmation: " + result.Text + " â€” enable the matching permission above, or accept in chat.";
                return;
            }
            output.Text = result.Text;
        }

        private Control BuildSystem()
        {
            string version = "v" + typeof(Pet).Assembly.GetName().Version.ToString(3);
            long memoryBytes = 0;
            try { memoryBytes = new FileInfo(memory.FilePath).Length; } catch (Exception) { }
            var info = Label(
                "Version: " + version + "\n"
                + "Data folder: " + pet.FolderPath + "\n"
                + "Memory file: " + memory.FilePath + " (" + (memoryBytes / 1024) + " KB)\n"
                + "Plugins folder: " + plugins.FolderPath + "\n"
                + "Brain: " + (pet.Brain.Model ?? "none") + "\nPlatform: " + Environment.OSVersion, MainframeTheme.White, 14);
            var openFolder = Button("Open data folder", () => DesktopTools.OpenFolder(pet.FolderPath, true), 170);
            var openMainframe = Button("Open Mainframe", () => pet.OpenMainframe(), 160);
            var openMods = Button("Open Mods", () => { pet.OpenMainframe(); pet.Frame.OpenPage("mods"); }, 130);
            var about = Label("Capri Companion adds a dashboard, streaming chat over Ollama / OpenAI-compatible providers, persistent memory, personalities, permission-gated desktop tools and a plugin system on top of the existing Mainframe, voice and character.", MainframeTheme.Faint, 13);
            return Column(
                Label("SYSTEM", MainframeTheme.Rose, 20, true), info,
                Row(openFolder, openMainframe, openMods), about);
        }
    }
}


