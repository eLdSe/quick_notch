using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using System.Text.Json.Serialization;

namespace QuickNotchCs;

public sealed class ChatMessage
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user"; // "user" or "model"
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("time")] public DateTime Time { get; set; } = DateTime.Now;
    [JsonPropertyName("created_at")] public double CreatedAt { get; set; } = Clock.Now;
    [JsonIgnore] public object? CachedSegments { get; set; }
    [JsonIgnore] public string? LastParsedText { get; set; }
}

public static class AiService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    public static string GetActiveKey(AppSettings s) => s.AiProvider switch
    {
        "openai" => s.AiKeyOpenAi,
        "deepseek" => s.AiKeyDeepSeek,
        _ => s.AiKeyGemini
    };

    public static void SetActiveKey(AppSettings s, string key)
    {
        key = key.Trim();
        switch (s.AiProvider)
        {
            case "openai": s.AiKeyOpenAi = key; break;
            case "deepseek": s.AiKeyDeepSeek = key; break;
            default: s.AiKeyGemini = key; break;
        }
    }

    public static Task<(bool Success, string Text, string? Error)> AskAsync(string prompt, AppSettings s, IReadOnlyList<ChatMessage>? history = null, CancellationToken ct = default)
        => AskStreamAsync(prompt, s, null, history, ct);

    public static async Task<(bool Success, string Text, string? Error)> AskStreamAsync(
        string prompt,
        AppSettings s,
        Action<string>? onChunk,
        IReadOnlyList<ChatMessage>? history = null,
        CancellationToken ct = default)
    {
        string key = GetActiveKey(s);
        if (string.IsNullOrWhiteSpace(key))
        {
            return (false, "", Loc.T("Не указан API-ключ. Нажмите «Ввести ключ», чтобы настроить.",
                                  "No API key configured. Click 'Enter Key' to set up.",
                                  "API anahtarı yapılandırılmadı. Ayarlamak için 'Anahtar Gir'e tıklayın."));
        }

        try
        {
            string activeModel = s.GetEffectiveAiModel();
            return s.AiProvider switch
            {
                "openai" => await QueryOpenAiCompatibleStreamAsync("https://api.openai.com/v1/chat/completions", activeModel, key, prompt, onChunk, history, ct),
                "deepseek" => await QueryOpenAiCompatibleStreamAsync("https://api.deepseek.com/chat/completions", activeModel, key, prompt, onChunk, history, ct),
                _ => await QueryGeminiStreamAsync(activeModel, key, prompt, onChunk, history, ct)
            };
        }
        catch (TaskCanceledException)
        {
            return (false, "", Loc.T("Превышено время ожидания ответа.", "Request timed out.", "İstek zaman aşımına uğradı."));
        }
        catch (HttpRequestException ex)
        {
            return (false, "", $"{Loc.T("Ошибка сети", "Network error", "Ağ hatası")}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, "", $"{Loc.T("Ошибка", "Error", "Hata")}: {ex.Message}");
        }
    }

    private static string? _cachedGeminiModel;

    private static int GetModelScore(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("tts") || n.Contains("image") || n.Contains("robotics") ||
            n.Contains("embedding") || n.Contains("computer-use") || n.Contains("deep-research") ||
            n.Contains("argon") || n.Contains("aqa"))
            return -100;

        if (n.Contains("2.5") || n.Contains("2.0") || n.Contains("1.5")) return -100; // Deprecated by Google for new users

        if (n.Contains("3.5-flash-lite")) return 120; // Fastest, lowest latency, high quota
        if (n.Contains("3.5-flash")) return 115;
        if (n.Contains("3.1-flash-lite")) return 110;
        if (n.Contains("3.1-flash")) return 105;
        if (n.Contains("3-flash-preview")) return 95;
        if (n.Contains("flash-lite")) return 90;
        if (n.Contains("3.5")) return 85;
        if (n.Contains("3.8-flash")) return 40; // Google 3.8 preview often times out/503
        if (n.Contains("flash")) return 60;
        if (n.Contains("pro")) return 30;
        return 10;
    }

    private static async Task<string> ResolveGeminiModelAsync(string key, string preferred, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_cachedGeminiModel)) return _cachedGeminiModel;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            string listUrl = $"https://generativelanguage.googleapis.com/v1beta/models?key={key}";
            using var resp = await Http.GetAsync(listUrl, cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                string listJson = await resp.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(listJson);
                if (doc.RootElement.TryGetProperty("models", out var modelsArr))
                {
                    string? bestModel = null;
                    int bestScore = int.MinValue;

                    foreach (var m in modelsArr.EnumerateArray())
                    {
                        if (m.TryGetProperty("name", out var nameProp))
                        {
                            string full = nameProp.GetString() ?? "";
                            string clean = full.StartsWith("models/") ? full[7..] : full;

                            bool supportsGen = false;
                            if (m.TryGetProperty("supportedGenerationMethods", out var methods))
                            {
                                foreach (var method in methods.EnumerateArray())
                                {
                                    if (method.GetString() == "generateContent") { supportsGen = true; break; }
                                }
                            }

                            if (supportsGen)
                            {
                                int score = GetModelScore(clean);
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    bestModel = clean;
                                }
                            }
                        }
                    }

                    if (bestModel != null && bestScore > 0)
                    {
                        _cachedGeminiModel = bestModel;
                        return bestModel;
                    }
                }
            }
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(preferred) &&
            !preferred.Contains("1.5") && !preferred.Contains("2.0") && !preferred.Contains("2.5") && !preferred.Contains("3.8"))
        {
            return preferred;
        }

        return "gemini-3.5-flash-lite";
    }

    private static string? ExtractRecommendedModel(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"use\s+models/([a-zA-Z0-9\.\-_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim().TrimEnd('.', ',', ';', ' ', '\'');
        }
        return null;
    }

    private static async Task<(bool Success, string Text, string? Error)> AttemptGeminiStreamCall(string model, string key, string prompt, Action<string>? onChunk, IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        string cleanModel = model.StartsWith("models/") ? model[7..] : model;
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:streamGenerateContent?alt=sse&key={key}";

        var contentsList = new List<object>();
        if (history != null)
        {
            foreach (var m in history)
            {
                if (string.IsNullOrWhiteSpace(m.Text)) continue;
                string r = m.Role == "user" ? "user" : "model";
                contentsList.Add(new { role = r, parts = new[] { new { text = m.Text } } });
            }
        }
        contentsList.Add(new { role = "user", parts = new[] { new { text = prompt } } });

        var payload = new { contents = contentsList };

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(14));

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                string errJson = await response.Content.ReadAsStringAsync(cts.Token);
                string errMessage = ExtractErrorMessage(errJson) ?? $"HTTP {(int)response.StatusCode}";
                return (false, "", errMessage);
            }

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var sb = new StringBuilder();
            while (!reader.EndOfStream && !cts.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cts.Token);
                if (line == null) break;
                if (line.StartsWith("data: "))
                {
                    string data = line[6..].Trim();
                    if (data == "[DONE]") break;
                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                        {
                            var cand = candidates[0];
                            if (cand.TryGetProperty("content", out var content) &&
                                content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                            {
                                string? chunkText = parts[0].GetProperty("text").GetString();
                                if (!string.IsNullOrEmpty(chunkText))
                                {
                                    sb.Append(chunkText);
                                    onChunk?.Invoke(sb.ToString());
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            string full = sb.ToString().Trim();
            if (string.IsNullOrEmpty(full))
            {
                return (false, "", Loc.T("Пустой ответ от Gemini.", "Empty response from Gemini.", "Gemini'den boş yanıt."));
            }
            return (true, full, null);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static async Task<(bool Success, string Text, string? Error)> AttemptGeminiStandardCall(string model, string key, string prompt, IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        string cleanModel = model.StartsWith("models/") ? model[7..] : model;
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{cleanModel}:generateContent?key={key}";

        var contentsList = new List<object>();
        if (history != null)
        {
            foreach (var m in history)
            {
                if (string.IsNullOrWhiteSpace(m.Text)) continue;
                string r = m.Role == "user" ? "user" : "model";
                contentsList.Add(new { role = r, parts = new[] { new { text = m.Text } } });
            }
        }
        contentsList.Add(new { role = "user", parts = new[] { new { text = prompt } } });

        var payload = new { contents = contentsList };

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(12));

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(req, cts.Token);

            string body = await response.Content.ReadAsStringAsync(cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                string errMessage = ExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}";
                return (false, "", errMessage);
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
            {
                var cand = candidates[0];
                if (cand.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) && parts.GetArrayLength() > 0)
                {
                    string? text = parts[0].GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return (true, text.Trim(), null);
                    }
                }
            }

            return (false, "", Loc.T("Пустой ответ от Gemini.", "Empty response from Gemini.", "Gemini'den boş yanıt."));
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static async Task<(bool Success, string Text, string? Error)> QueryGeminiStreamAsync(string model, string key, string prompt, Action<string>? onChunk, IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        string activeModel = await ResolveGeminiModelAsync(key, model, ct);
        var initial = await AttemptGeminiStreamCall(activeModel, key, prompt, onChunk, history, ct);
        if (initial.Success) return initial;

        // Try standard non-streaming for the active model if streaming failed
        var stdAttempt = await AttemptGeminiStandardCall(activeModel, key, prompt, history, ct);
        if (stdAttempt.Success)
        {
            onChunk?.Invoke(stdAttempt.Text);
            return stdAttempt;
        }

        // 1. Check if Google explicitly recommended a replacement model
        string? suggested = ExtractRecommendedModel(initial.Error ?? stdAttempt.Error ?? "");
        if (!string.IsNullOrEmpty(suggested) && suggested != activeModel)
        {
            var retrySuggested = await AttemptGeminiStreamCall(suggested, key, prompt, onChunk, history, ct);
            if (retrySuggested.Success)
            {
                _cachedGeminiModel = suggested;
                return retrySuggested;
            }

            var retryStd = await AttemptGeminiStandardCall(suggested, key, prompt, history, ct);
            if (retryStd.Success)
            {
                _cachedGeminiModel = suggested;
                onChunk?.Invoke(retryStd.Text);
                return retryStd;
            }
        }

        // 2. High-speed multi-tier fallback
        string[] fallbacks = { "gemini-3.5-flash-lite", "gemini-3.5-flash", "gemini-3.1-flash-lite", "gemini-3-flash-preview" };
        foreach (var fb in fallbacks)
        {
            if (fb == activeModel || fb == suggested) continue;
            var fbRes = await AttemptGeminiStreamCall(fb, key, prompt, onChunk, history, ct);
            if (fbRes.Success)
            {
                _cachedGeminiModel = fb;
                return fbRes;
            }

            var fbStd = await AttemptGeminiStandardCall(fb, key, prompt, history, ct);
            if (fbStd.Success)
            {
                _cachedGeminiModel = fb;
                onChunk?.Invoke(fbStd.Text);
                return fbStd;
            }
        }

        return (false, "", $"Gemini error: {initial.Error}");
    }

    private static async Task<(bool Success, string Text, string? Error)> QueryOpenAiCompatibleStreamAsync(string endpoint, string model, string key, string prompt, Action<string>? onChunk, IReadOnlyList<ChatMessage>? history, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Headers.Add("Authorization", $"Bearer {key}");

        var messagesList = new List<object>();
        if (history != null)
        {
            foreach (var m in history)
            {
                if (string.IsNullOrWhiteSpace(m.Text)) continue;
                string r = m.Role == "user" ? "user" : "assistant";
                messagesList.Add(new { role = r, content = m.Text });
            }
        }
        messagesList.Add(new { role = "user", content = prompt });

        var payload = new
        {
            model,
            messages = messagesList,
            stream = true
        };

        try
        {
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var response = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                string errJson = await response.Content.ReadAsStringAsync(ct);
                string errMessage = ExtractErrorMessage(errJson) ?? $"HTTP {(int)response.StatusCode}";
                return (false, "", $"API error: {errMessage}");
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var sb = new StringBuilder();
            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (line.StartsWith("data: "))
                {
                    string data = line[6..].Trim();
                    if (data == "[DONE]") break;
                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var cand = choices[0];
                            if (cand.TryGetProperty("delta", out var delta) &&
                                delta.TryGetProperty("content", out var cElem))
                            {
                                string? chunkText = cElem.GetString();
                                if (!string.IsNullOrEmpty(chunkText))
                                {
                                    sb.Append(chunkText);
                                    onChunk?.Invoke(sb.ToString());
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            string full = sb.ToString().Trim();
            if (string.IsNullOrEmpty(full))
            {
                return (false, "", Loc.T("Пустой ответ от API.", "Empty response from API.", "API'den boş yanıt."));
            }
            return (true, full, null);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static string? ExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.TryGetProperty("message", out var m)) return m.GetString();
            }
        }
        catch { }
        return null;
    }
}

