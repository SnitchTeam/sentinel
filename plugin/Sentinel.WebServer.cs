using System;
using System.Net;
using System.Text;
using System.Text.Json;
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
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private int _currentPort;
        private bool _disabled;

        public bool IsRunning => _listener.IsListening;
        public bool IsDisabled => _disabled;
        public int CurrentPort => _currentPort;

        public SentinelWebServer(IHttpListenerWrapper listener, IRuntimeBridge? logger)
        {
            _listener = listener;
            _logger = logger;
        }

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

                if (method == "GET" && path == "/")
                {
                    WriteJson(context, 200, new { status = "ok", service = "Sentinel Web Panel" });
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"[Sentinel] Web server request error: {ex.Message}");
                try { context.Response.Close(); } catch { }
            }
        }

        private static void WriteJson(HttpListenerContext context, int statusCode, object data)
        {
            var json = JsonSerializer.Serialize(data);
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
            return new SentinelWebServer(new HttpListenerWrapper(), _runtimeBridge);
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
