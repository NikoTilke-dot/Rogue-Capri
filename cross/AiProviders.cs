using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CapriDesktop
{
    // Companion AI providers: talk to Capri's own brain, a standalone Ollama
    // server, or any OpenAI-compatible endpoint. All work runs on worker
    // threads; nothing here touches the UI thread.
    internal sealed class ChatMsg
    {
        internal string Role;
        internal string Content;
        internal ChatMsg(string role, string content) { Role = role; Content = content ?? ""; }
    }

    internal interface IAiProvider
    {
        string Id { get; }
        string Name { get; }
        string Model { get; set; }
        string Endpoint { get; set; }
        string ApiKey { get; set; }
        double Temperature { get; set; }
        int MaxTokens { get; set; }
        bool Streams { get; }
        string Stream(List<ChatMsg> messages, Action<string> onToken, CancellationToken cancel);
        bool TestConnection(out string detail);
        string[] ListModels(out string error);
    }

    internal abstract class ProviderBase : IAiProvider
    {
        protected static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        public string Model { get; set; }
        public string Endpoint { get; set; }
        public string ApiKey { get; set; }
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 2048;
        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract bool Streams { get; }
        public abstract string Stream(List<ChatMsg> messages, Action<string> onToken, CancellationToken cancel);
        public abstract bool TestConnection(out string detail);
        public abstract string[] ListModels(out string error);

        internal static Dictionary<string, object> MessagesJson(List<ChatMsg> messages)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (ChatMsg m in messages) list.Add(new Dictionary<string, object> { { "role", m.Role }, { "content", m.Content } });
            return new Dictionary<string, object> { { "messages", list } };
        }

        internal static string ReadError(HttpResponseMessage response)
        {
            try
            {
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return "HTTP " + (int)response.StatusCode + ": " + Truncate(body, 300);
            }
            catch (Exception) { return "HTTP " + (int)response.StatusCode; }
        }

        internal static string Truncate(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        internal static string FirstUser(List<ChatMsg> messages)
        {
            for (int i = messages.Count - 1; i >= 0; i--) if (messages[i].Role == "user") return messages[i].Content;
            return messages.Count > 0 ? messages[messages.Count - 1].Content : "";
        }

        internal string BaseUrl(string fallback)
        {
            string url = string.IsNullOrWhiteSpace(Endpoint) ? fallback : Endpoint.Trim();
            while (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);
            return url;
        }

        internal string GetJson(string url, string key, out string errorOut)
        {
            errorOut = null;
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                if (!string.IsNullOrEmpty(key)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                try
                {
                    using (HttpResponseMessage response = Http.SendAsync(request).GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode) { errorOut = ReadError(response); return null; }
                        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception error) { errorOut = error.Message; return null; }
            }
        }

        internal bool Ping(string url, string key, out string detail)
        {
            detail = null;
            string error;
            string body = GetJson(url, key, out error);
            if (body == null) { detail = error; return false; }
            detail = "Connected.";
            return true;
        }
    }
    internal sealed class CapriBrainProvider : ProviderBase
    {
        private readonly Pet pet;
        internal CapriBrainProvider(Pet owner) { pet = owner; }
        public override string Id { get { return "capri"; } }
        public override string Name { get { return "Capri Brain (Mainframe setup)"; } }
        public override bool Streams { get { return false; } }

        public override string Stream(List<ChatMsg> messages, Action<string> onToken, CancellationToken cancel)
        {
            if (pet.Brain.Model == null) return "No brain is set up yet. Open Capri Mainframe and pick a brain first.";
            try
            {
                string asked = FirstUser(messages);
                cancel.ThrowIfCancellationRequested();
                string reply = pet.Brain.Ask(asked);
                if (cancel.IsCancellationRequested) return "cancelled";
                if (string.IsNullOrWhiteSpace(reply)) return "The brain sent an empty reply. Try again.";
                onToken(reply);
                return null;
            }
            catch (OperationCanceledException) { return "cancelled"; }
            catch (BrainException error) { return error.Message; }
            catch (Exception error) { return error.Message; }
        }

        public override bool TestConnection(out string detail)
        {
            if (pet.Brain.Model == null) { detail = "No brain set up. Open Capri Mainframe."; return false; }
            string where = pet.Brain.Remote != null ? pet.Brain.Remote.Company : "local Ollama";
            detail = "Brain ready: " + pet.Brain.Model + " (" + where + ").";
            return true;
        }

        public override string[] ListModels(out string error)
        {
            error = null;
            return pet.MainframeModels ?? new string[0];
        }
    }

    internal sealed class OllamaProvider : ProviderBase
    {
        public override string Id { get { return "ollama"; } }
        public override string Name { get { return "Ollama (local server)"; } }
        public override bool Streams { get { return true; } }

        public override string Stream(List<ChatMsg> messages, Action<string> onToken, CancellationToken cancel)
        {
            string baseUrl = BaseUrl("http://127.0.0.1:11434");
            var payload = new Dictionary<string, object>
            {
                { "model", string.IsNullOrWhiteSpace(Model) ? "gemma4:e4b" : Model.Trim() },
                { "stream", true },
                { "options", new Dictionary<string, object> { { "temperature", Temperature }, { "num_predict", MaxTokens } } }
            };
            foreach (var pair in MessagesJson(messages)) payload[pair.Key] = pair.Value;
            string json = JsonSerializer.Serialize(payload);
            using (var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/chat"))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                try
                {
                    using (HttpResponseMessage response = Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode) return ReadError(response);
                        using (Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                        using (var reader = new StreamReader(stream))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                cancel.ThrowIfCancellationRequested();
                                if (string.IsNullOrWhiteSpace(line)) continue;
                                string token; bool done;
                                string problem = ParseLine(line, out token, out done);
                                if (problem != null) return problem;
                                if (!string.IsNullOrEmpty(token)) onToken(token);
                                if (done) break;
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { return "cancelled"; }
                catch (HttpRequestException error) { return "Can't reach Ollama at " + baseUrl + ". Is it running? (" + error.Message + ")"; }
                catch (Exception error) { return error.Message; }
            }
            return null;
        }

        private static string ParseLine(string line, out string token, out bool done)
        {
            token = null; done = false;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(line))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("error", out JsonElement error)) return error.GetString() ?? "Ollama error.";
                    if (root.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement content))
                        token = content.GetString();
                    if (root.TryGetProperty("done", out JsonElement d) && d.ValueKind == JsonValueKind.True) done = true;
                    return null;
                }
            }
            catch (JsonException) { return "Ollama sent something unreadable."; }
        }

        public override bool TestConnection(out string detail)
        {
            string error;
            string body = GetJson(BaseUrl("http://127.0.0.1:11434") + "/api/tags", null, out error);
            if (body == null) { detail = error; return false; }
            detail = "Ollama is running.";
            return true;
        }

        public override string[] ListModels(out string error)
        {
            string body = GetJson(BaseUrl("http://127.0.0.1:11434") + "/api/tags", null, out error);
            if (body == null) return new string[0];
            var names = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("models", out JsonElement models))
                        foreach (JsonElement model in models.EnumerateArray())
                            if (model.TryGetProperty("name", out JsonElement name)) names.Add(name.GetString());
                }
            }
            catch (JsonException) { error = "Ollama sent something unreadable."; }
            return names.ToArray();
        }
    }
    internal sealed class OpenAiProvider : ProviderBase
    {
        public override string Id { get { return "openai"; } }
        public override string Name { get { return "OpenAI-compatible (LM Studio, OpenAI, ...)"; } }
        public override bool Streams { get { return true; } }

        private string ChatUrl()
        {
            string url = BaseUrl("http://127.0.0.1:1234/v1");
            if (url.EndsWith("/chat/completions")) return url;
            if (url.EndsWith("/v1")) return url + "/chat/completions";
            return url + "/v1/chat/completions";
        }

        private string ModelsUrl()
        {
            string url = BaseUrl("http://127.0.0.1:1234/v1");
            if (url.EndsWith("/chat/completions")) url = url.Substring(0, url.Length - "/chat/completions".Length);
            if (url.EndsWith("/v1")) return url + "/models";
            return url + "/v1/models";
        }

        public override string Stream(List<ChatMsg> messages, Action<string> onToken, CancellationToken cancel)
        {
            var payload = new Dictionary<string, object>
            {
                { "model", string.IsNullOrWhiteSpace(Model) ? "local-model" : Model.Trim() },
                { "stream", true },
                { "temperature", Temperature },
                { "max_tokens", MaxTokens }
            };
            foreach (var pair in MessagesJson(messages)) payload[pair.Key] = pair.Value;
            string json = JsonSerializer.Serialize(payload);
            using (var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl()))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(ApiKey)) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey.Trim());
                try
                {
                    using (HttpResponseMessage response = Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).GetAwaiter().GetResult())
                    {
                        if (!response.IsSuccessStatusCode) return ReadError(response);
                        using (Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
                        using (var reader = new StreamReader(stream))
                        {
                            string line;
                            while ((line = reader.ReadLine()) != null)
                            {
                                cancel.ThrowIfCancellationRequested();
                                string token;
                                string problem = ParseSseLine(line, out token);
                                if (problem != null) return problem;
                                if (token == "\0done") break;
                                if (!string.IsNullOrEmpty(token)) onToken(token);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { return "cancelled"; }
                catch (HttpRequestException error) { return "Can't reach the provider. Is the server running? (" + error.Message + ")"; }
                catch (Exception error) { return error.Message; }
            }
            return null;
        }

        private static string ParseSseLine(string line, out string token)
        {
            token = null;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:")) return null;
            string data = line.Substring(5).Trim();
            if (data == "[DONE]") { token = "\0done"; return null; }
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(data))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("error", out JsonElement error))
                    {
                        if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement msg)) return msg.GetString();
                        return error.GetString() ?? "Provider error.";
                    }
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                    {
                        JsonElement first = choices[0];
                        if (first.TryGetProperty("delta", out JsonElement delta) && delta.TryGetProperty("content", out JsonElement content))
                            token = content.GetString();
                        else if (first.TryGetProperty("message", out JsonElement message) && message.TryGetProperty("content", out JsonElement content2))
                            token = content2.GetString();
                    }
                    return null;
                }
            }
            catch (JsonException) { return null; }
        }

        public override bool TestConnection(out string detail)
        {
            return Ping(ModelsUrl(), string.IsNullOrEmpty(ApiKey) ? null : ApiKey.Trim(), out detail);
        }

        public override string[] ListModels(out string error)
        {
            string body = GetJson(ModelsUrl(), string.IsNullOrEmpty(ApiKey) ? null : ApiKey.Trim(), out error);
            if (body == null) return new string[0];
            var names = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("data", out JsonElement data))
                        foreach (JsonElement model in data.EnumerateArray())
                            if (model.TryGetProperty("id", out JsonElement id)) names.Add(id.GetString());
                }
            }
            catch (JsonException) { error = "Provider sent something unreadable."; }
            return names.ToArray();
        }
    }

    internal static class ProviderRegistry
    {
        internal static string[] Ids = { "capri", "ollama", "openai" };
        internal static string[] Names = { "Capri Brain (Mainframe setup)", "Ollama (local server)", "OpenAI-compatible (LM Studio, OpenAI, ...)" };

        internal static IAiProvider Create(string id, Pet pet, Settings saved)
        {
            ProviderBase provider;
            if (id == "ollama") provider = new OllamaProvider();
            else if (id == "openai") provider = new OpenAiProvider();
            else provider = new CapriBrainProvider(pet);
            provider.Model = saved.CompanionModel;
            provider.Endpoint = saved.CompanionEndpoint;
            provider.ApiKey = saved.CompanionKey;
            provider.Temperature = saved.CompanionTemperature > 0 ? saved.CompanionTemperature : 0.7;
            provider.MaxTokens = saved.CompanionMaxTokens > 0 ? saved.CompanionMaxTokens : 2048;
            return provider;
        }

        internal static void Save(IAiProvider provider, string id, Settings saved)
        {
            saved.CompanionProvider = id;
            saved.CompanionModel = provider.Model;
            saved.CompanionEndpoint = provider.Endpoint;
            saved.CompanionKey = provider.ApiKey;
            saved.CompanionTemperature = provider.Temperature;
            saved.CompanionMaxTokens = provider.MaxTokens;
        }
    }
    internal sealed class Personality
    {
        internal string Id;
        internal string Name;
        internal string Prompt;
        internal double Temperature;
        internal Personality(string id, string name, string prompt, double temperature)
        {
            Id = id; Name = name; Prompt = prompt; Temperature = temperature;
        }
    }

    internal static class Personalities
    {
        internal static readonly Personality[] BuiltIns =
        {
            new Personality("capri", "Capri (default)",
                "You are Capri, a cheerful desktop companion who lives on the user's computer. Be warm, playful and helpful, answer in short friendly sentences, and use the language the user writes in. You can ask the user to confirm desktop actions with tool lines like [[TOOL open_app name=\"notepad\"]].",
                0.7),
            new Personality("friendly", "Friendly assistant",
                "You are a warm, helpful assistant. Be kind, clear and encouraging. Explain things in plain language.",
                0.6),
            new Personality("concise", "Concise",
                "You are a terse assistant. Answer in one or two short sentences, no filler, no lists unless asked.",
                0.3),
            new Personality("coder", "Coder",
                "You are a senior software engineer pairing with the user. Give working code first, brief reasoning after. Prefer simple, well-tested solutions.",
                0.4),
            new Personality("custom", "Custom", "", 0.7),
        };

        internal static Personality Find(string id, Settings saved)
        {
            foreach (Personality p in BuiltIns)
            {
                if (p.Id != id) continue;
                if (p.Id == "custom") return new Personality("custom", "Custom", saved.CompanionCustomPrompt ?? "", saved.CompanionTemperature > 0 ? saved.CompanionTemperature : 0.7);
                return p;
            }
            return BuiltIns[0];
        }

        internal static string BuildSystemPrompt(Personality personality, MemoryStore memory)
        {
            string prompt = personality.Prompt ?? "";
            if (memory != null && memory.Count > 0)
            {
                prompt += "\n\nWhat you remember about the user:";
                foreach (string fact in memory.Facts) prompt += "\n- " + fact;
                foreach (string pref in memory.Preferences) prompt += "\n- (likes) " + pref;
            }
            return prompt;
        }
    }
}
