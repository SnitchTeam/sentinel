using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Oxide.Plugins
{
    public interface IHttpListenerWrapper
    {
        bool IsListening { get; }
        void AddPrefix(string prefix);
        void Start();
        void Stop();
        void ClearPrefixes();
        Task<HttpListenerContext> GetContextAsync();
    }

    public class HttpListenerWrapper : IHttpListenerWrapper
    {
        private readonly HttpListener _listener = new HttpListener();

        public bool IsListening => _listener.IsListening;

        public void AddPrefix(string prefix) => _listener.Prefixes.Add(prefix);

        public void Start() => _listener.Start();

        public void Stop() => _listener.Stop();

        public void ClearPrefixes() => _listener.Prefixes.Clear();

        public Task<HttpListenerContext> GetContextAsync() => _listener.GetContextAsync();
    }

    public class SentinelWebServer
    {
        private readonly IHttpListenerWrapper _listener;
        private readonly IRuntimeBridge? _logger;
        private readonly SentinelWebAuth _auth;
        private readonly SentinelRateLimiter _rateLimiter;
        private ISentinelWebApi? _api;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private int _currentPort;
        private bool _disabled;

        public bool IsRunning => _listener.IsListening;
        public bool IsDisabled => _disabled;
        public int CurrentPort => _currentPort;
        public SentinelWebAuth Auth => _auth;
        public SentinelRateLimiter RateLimiter => _rateLimiter;

        public SentinelWebServer(IHttpListenerWrapper listener, IRuntimeBridge? logger, string authToken = "", int rateLimitPerMinute = 60, int rateLimitWindowSeconds = 60)
        {
            _listener = listener;
            _logger = logger;
            _auth = new SentinelWebAuth(authToken);
            _rateLimiter = new SentinelRateLimiter(rateLimitPerMinute, rateLimitWindowSeconds);
        }

        public void SetApi(ISentinelWebApi api) => _api = api;

        public virtual void Start(int port)
        {
            if (_disabled)
            {
                _logger?.LogInfo("[Sentinel] Web server is disabled; skipping start.");
                return;
            }

            Stop();

            var prefix = $"http://127.0.0.1:{port}/";
            try
            {
                _listener.AddPrefix(prefix);
                _listener.Start();
            }
            catch (HttpListenerException ex)
            {
                try { _listener.ClearPrefixes(); } catch { /* ignore */ }
                var isPortConflict = ex.ErrorCode == 48
                    || ex.Message.Contains("in use", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("conflicts with an existing registration", StringComparison.OrdinalIgnoreCase);

                if (isPortConflict)
                {
                    _logger?.LogError($"[Sentinel] Web panel port {port} is in use. Web panel disabled.");
                }
                else
                {
                    _logger?.LogError($"[Sentinel] Failed to start web server on port {port}: {ex.Message}");
                }

                _disabled = true;
                return;
            }
            catch (Exception ex)
            {
                try { _listener.ClearPrefixes(); } catch { /* ignore */ }
                _logger?.LogError($"[Sentinel] Failed to start web server on port {port}: {ex.Message}");
                _disabled = true;
                return;
            }

            _currentPort = port;
            _disabled = false;
            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoop(_cts.Token));

            _logger?.LogInfo($"[Sentinel] Web server started on port {port}.");
        }

        public virtual void Stop()
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { /* ignore */ }
            try { _listener.ClearPrefixes(); } catch { /* ignore */ }
            try { _listenerTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* ignore */ }
            _listenerTask = null;
            _cts = null;
            _currentPort = 0;
        }

        public virtual void RestartIfPortChanged(int newPort)
        {
            if (_disabled && newPort != _currentPort)
            {
                _disabled = false;
            }

            if (_currentPort == newPort && IsRunning)
            {
                return;
            }

            Stop();
            Start(newPort);
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var context = await _listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequest(context), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[Sentinel] Web server accept error: {ex.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath ?? "/";
                var method = context.Request.HttpMethod;
                var clientIp = context.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";

                // 1. Rate limiting (applied before auth to prevent brute force)
                if (!_rateLimiter.IsAllowed(clientIp, out var retryAfter))
                {
                    context.Response.StatusCode = 429;
                    context.Response.Headers["Retry-After"] = retryAfter.ToString();
                    context.Response.Close();
                    return;
                }

                // 2. Authentication — required for all requests
                if (!_auth.IsAuthenticated(context.Request))
                {
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    return;
                }

                if (method == "GET" && path == "/")
                {
                    WriteJson(context, 200, new { status = "ok", service = "Sentinel Web Panel" });
                    return;
                }

                if (_api == null)
                {
                    context.Response.StatusCode = 503;
                    context.Response.Close();
                    return;
                }

                try
                {
                    RouteApi(context, method, path);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"[Sentinel] API route error: {ex.Message}");
                    WriteJson(context, 500, new { error = "Internal server error" });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Sentinel] Web server request error: {ex.Message}");
                try { context.Response.Close(); } catch { }
            }
        }

        private void RouteApi(HttpListenerContext context, string method, string path)
        {
#pragma warning disable CS8602
            var query = context.Request.Url?.Query ?? "";
            var queryParams = ParseQueryString(query);

            // ─── Players ───
            if (method == "GET" && path == "/api/players")
            {
                var players = _api.ApiGetOnlinePlayers();
                WriteJson(context, 200, players);
                return;
            }

            if (method == "POST" && path.StartsWith("/api/players/"))
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[0] == "api" && parts[1] == "players")
                {
                    var steamId = parts[2];
                    var action = parts.Length > 3 ? parts[3] : "";
                    var body = ReadJsonBody(context)!;
                    var reason = GetString(body, "reason");
                    var duration = GetInt(body, "durationMinutes");

                    var result = _api.ExecutePlayerAction(steamId, action, reason, duration);
                    if (result.NotFound)
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                    }
                    else if (result.Success)
                    {
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                    }
                    else
                    {
                        WriteJson(context, 400, new { error = result.Error });
                    }
                    return;
                }
            }

            // ─── Bans ───
            if (method == "GET" && path == "/api/bans")
            {
                var page = ParseInt(queryParams, "page", 1);
                var limit = ParseInt(queryParams, "limit", 50);
                if (limit > 200) limit = 200;
                if (limit < 1) limit = 1;
                var bans = _api.GetBans(page, limit);
                WriteJson(context, 200, bans);
                return;
            }

            if (method == "POST" && path == "/api/bans")
            {
                var body = ReadJsonBody(context)!;
                var steamId = GetString(body, "steamId") ?? "";
                var name = GetString(body, "name");
                var reason = GetString(body, "reason") ?? "No reason given";
                var duration = GetInt(body, "durationMinutes");

                if (string.IsNullOrEmpty(steamId))
                {
                    WriteJson(context, 400, new { error = "steamId is required" });
                    return;
                }

                var ban = _api.CreateBan(steamId, name, reason, duration);
                if (ban != null)
                {
                    WriteJson(context, 201, ban);
                }
                else
                {
                    WriteJson(context, 500, new { error = "Failed to create ban" });
                }
                return;
            }

            if (method == "DELETE" && path.StartsWith("/api/bans/"))
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3 && parts[0] == "api" && parts[1] == "bans" && long.TryParse(parts[2], out var banId))
                {
                    if (_api.RevokeBan(banId))
                    {
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                    }
                    return;
                }
            }

            // ─── Actions ───
            if (method == "GET" && path == "/api/actions")
            {
                var type = queryParams.TryGetValue("type", out var t) ? t : null;
                long? since = null;
                if (queryParams.TryGetValue("since", out var sinceStr) && !string.IsNullOrEmpty(sinceStr))
                {
                    if (long.TryParse(sinceStr, out var sinceVal))
                        since = sinceVal;
                    else if (DateTimeOffset.TryParse(sinceStr, out var sinceDt))
                        since = sinceDt.ToUnixTimeSeconds();
                }
                var page = ParseInt(queryParams, "page", 1);
                var limit = ParseInt(queryParams, "limit", 50);
                if (limit > 200) limit = 200;
                if (limit < 1) limit = 1;

                var actions = _api.GetActions(type, since, page, limit);
                WriteJson(context, 200, actions);
                return;
            }

            // ─── AI ───
            if (method == "GET" && path == "/api/ai/log")
            {
                var page = ParseInt(queryParams, "page", 1);
                var limit = ParseInt(queryParams, "limit", 50);
                if (limit > 200) limit = 200;
                if (limit < 1) limit = 1;
                var log = _api.GetAiLog(page, limit);
                WriteJson(context, 200, log);
                return;
            }

            if (method == "POST" && path == "/api/ai/feedback")
            {
                var body = ReadJsonBody(context)!;
                var id = GetLong(body, "id") ?? 0;
                var verdict = GetString(body, "verdict") ?? "";

                if (id <= 0 || string.IsNullOrEmpty(verdict))
                {
                    WriteJson(context, 400, new { error = "id and verdict are required" });
                    return;
                }

                if (_api.RecordAiFeedback(id, verdict))
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
                return;
            }

            if (method == "GET" && path == "/api/ai/config")
            {
                WriteJson(context, 200, _api.GetAiConfig());
                return;
            }

            if (method == "POST" && path == "/api/ai/query")
            {
                var body = ReadJsonBody(context)!;
                var queryStr = GetString(body, "query") ?? "";
                if (string.IsNullOrEmpty(queryStr))
                {
                    WriteJson(context, 400, new { error = "query is required" });
                    return;
                }
                var result = _api.QueryAi(queryStr);
                WriteJson(context, 200, result);
                return;
            }

            // ─── Config ───
            if (method == "GET" && path == "/api/config")
            {
                WriteJson(context, 200, _api.GetConfig());
                return;
            }

            if (method == "POST" && path == "/api/config")
            {
                var bodyJson = ReadRawBody(context);
                var result = _api.UpdateConfig(bodyJson);
                if (result.Success)
                {
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                }
                else
                {
                    WriteJson(context, 400, new { error = result.Error });
                }
                return;
            }

            // ─── Permissions ───
            if (method == "GET" && path == "/api/perms")
            {
                WriteJson(context, 200, _api.GetPermissionGroups());
                return;
            }

            if (method == "POST" && path == "/api/perms/groups")
            {
                var body = ReadJsonBody(context)!;
                var name = GetString(body, "name") ?? "";
                var title = GetString(body, "title") ?? name;
                var parent = GetString(body, "parent");

                if (string.IsNullOrEmpty(name))
                {
                    WriteJson(context, 400, new { error = "name is required" });
                    return;
                }

                var (success, id, error) = _api.CreatePermissionGroup(name, title, parent);
                if (success)
                {
                    WriteJson(context, 201, new { id, name });
                }
                else
                {
                    WriteJson(context, 400, new { error });
                }
                return;
            }

            if (method == "PUT" && path.StartsWith("/api/perms/groups/"))
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[0] == "api" && parts[1] == "perms" && parts[2] == "groups" && int.TryParse(parts[3], out var groupId))
                {
                    var body = ReadJsonBody(context)!;
                    var title = GetString(body, "title");
                    var parent = GetString(body, "parent");
                    List<string>? perms = null;
                    if (body.TryGetValue("permissions", out var permEl) && permEl != null)
                    {
                        try
                        {
                            var raw = permEl is JsonElement je ? je.GetRawText() : permEl.ToString()!;
                            perms = JsonSerializer.Deserialize<List<string>>(raw);
                        }
                        catch { }
                    }

                    if (_api.UpdatePermissionGroup(groupId, title, parent, perms))
                    {
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                    }
                    return;
                }
            }

            if (method == "DELETE" && path.StartsWith("/api/perms/groups/"))
            {
                var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4 && parts[0] == "api" && parts[1] == "perms" && parts[2] == "groups" && int.TryParse(parts[3], out var delGroupId))
                {
                    if (_api.DeletePermissionGroup(delGroupId))
                    {
                        context.Response.StatusCode = 204;
                        context.Response.Close();
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                    }
                    return;
                }
            }

            // ─── Baselines ───
            if (method == "GET" && path == "/api/baselines")
            {
                WriteJson(context, 200, _api.GetBaselines());
                return;
            }

            if (method == "POST" && path == "/api/baselines/recalculate")
            {
                var jobId = _api.TriggerBaselineRecalculation();
                context.Response.StatusCode = 202;
                context.Response.Headers["Location"] = $"/api/baselines/jobs/{jobId}";
                context.Response.Close();
                return;
            }

            // ─── Stats ───
            if (method == "GET" && path == "/api/stats")
            {
                var days = ParseInt(queryParams, "days", 7);
                if (days < 1 || days > 30)
                {
                    WriteJson(context, 400, new { error = "days must be between 1 and 30" });
                    return;
                }
                WriteJson(context, 200, _api.GetStats(days));
                return;
            }

            // Fallback
            context.Response.StatusCode = 404;
            context.Response.Close();
#pragma warning restore CS8602
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query[1..];
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2)
                {
                    dict[kv[0]] = Uri.UnescapeDataString(kv[1]);
                }
                else if (kv.Length == 1)
                {
                    dict[kv[0]] = "";
                }
            }
            return dict;
        }

        private static int ParseInt(Dictionary<string, string> query, string key, int defaultValue)
        {
            if (query.TryGetValue(key, out var val) && int.TryParse(val, out var result))
                return result;
            return defaultValue;
        }

        private static string? GetString(Dictionary<string, object?> body, string key)
        {
            if (!body.TryGetValue(key, out var val) || val == null) return null;
            if (val is JsonElement je) return je.GetString();
            return val.ToString();
        }

        private static int? GetInt(Dictionary<string, object?> body, string key)
        {
            if (!body.TryGetValue(key, out var val) || val == null) return null;
            if (val is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n)) return n;
                if (je.ValueKind == JsonValueKind.String && int.TryParse(je.GetString(), out var sn)) return sn;
                return null;
            }
            return Convert.ToInt32(val);
        }

        private static long? GetLong(Dictionary<string, object?> body, string key)
        {
            if (!body.TryGetValue(key, out var val) || val == null) return null;
            if (val is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var n)) return n;
                if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var sn)) return sn;
                return null;
            }
            return Convert.ToInt64(val);
        }

        private static Dictionary<string, object?> ReadJsonBody(HttpListenerContext context)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
                var json = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, object?>();
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, object?>();
            }
            catch
            {
                return new Dictionary<string, object?>();
            }
        }

        private static string ReadRawBody(HttpListenerContext context)
        {
            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static void WriteJson(HttpListenerContext context, int statusCode, object data)
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
        }
    }

    public partial class Sentinel
    {
        private SentinelWebServer? _webServer;

        public virtual void InitializeWebServer()
        {
            var config = PluginConfig?.WebPanel;
            if (config == null || !config.Enabled)
            {
                _runtimeBridge?.LogInfo("[Sentinel] Web panel is disabled in config.");
                return;
            }

            _webServer = CreateWebServer();
            StartWebServer(config.Port);
        }

        public virtual SentinelWebServer CreateWebServer()
        {
            var config = PluginConfig?.WebPanel;
            var server = new SentinelWebServer(
                new HttpListenerWrapper(),
                _runtimeBridge,
                config?.AuthToken ?? "",
                config?.RateLimitPerMinute ?? 60);
            server.SetApi(this);
            return server;
        }

        public virtual void StartWebServer(int port)
        {
            if (_webServer == null) return;
            _webServer.Start(port);
        }

        public virtual void StopWebServer()
        {
            _webServer?.Stop();
            _webServer = null;
        }

        public virtual void ReloadWebServer()
        {
            var config = PluginConfig?.WebPanel;
            if (config == null)
            {
                StopWebServer();
                return;
            }

            if (!config.Enabled)
            {
                StopWebServer();
                _runtimeBridge?.LogInfo("[Sentinel] Web panel disabled via config reload.");
                return;
            }

            if (_webServer == null)
            {
                _webServer = CreateWebServer();
            }

            _webServer.RestartIfPortChanged(config.Port);
        }
    }
}
