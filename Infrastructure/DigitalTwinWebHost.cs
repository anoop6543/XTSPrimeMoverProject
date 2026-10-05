using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using XTSPrimeMoverProject.Services;
using XTSPrimeMoverProject.Services.DigitalTwin3D;

namespace XTSPrimeMoverProject.Infrastructure
{
    /// <summary>
    /// Hosts the Three.js digital twin (Web/twin) in WebView2 and streams live TwinFrames to it.
    /// The page is served from the output folder through a virtual host, so the HMI works offline.
    /// </summary>
    public sealed class DigitalTwinWebHost
    {
        private const string VirtualHost = "twin.local";
        private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(66); // ≈15 Hz; the page interpolates to 60 fps

        private readonly WebView2 _view;
        private readonly IMachineGatewayService _gateway;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private bool _initializing;
        private bool _pageReady;

        public DigitalTwinWebHost(WebView2 view, IMachineGatewayService gateway)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _view.Loaded += async (_, _) => await InitializeAsync();
        }

        public event EventHandler<string>? StatusChanged;

        public bool IsReady => _pageReady && _view.CoreWebView2 != null;

        public async Task InitializeAsync()
        {
            if (_initializing || _view.CoreWebView2 != null)
            {
                return;
            }

            _initializing = true;
            try
            {
                string root = Path.Combine(AppContext.BaseDirectory, "Web", "twin");
                if (!File.Exists(Path.Combine(root, "index.html")))
                {
                    StatusChanged?.Invoke(this, $"Digital twin assets not found in {root}.");
                    return;
                }

                string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XTSPrimeMover", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await _view.EnsureCoreWebView2Async(environment);

                var core = _view.CoreWebView2!;
                core.SetVirtualHostNameToFolderMapping(VirtualHost, root, CoreWebView2HostResourceAccessKind.Allow);
                core.Settings.AreDevToolsEnabled = Debugger.IsAttached;
                core.Settings.IsStatusBarEnabled = false;
                core.WebMessageReceived += OnWebMessageReceived;
                core.NavigationStarting += (_, _) => _pageReady = false;
                core.Navigate($"https://{VirtualHost}/index.html");
                StatusChanged?.Invoke(this, "3D digital twin loading…");
            }
            catch (WebView2RuntimeNotFoundException)
            {
                StatusChanged?.Invoke(this, "Microsoft Edge WebView2 Runtime is not installed – use the 'Native 3D (WPF)' tab, or install the runtime from Microsoft.");
            }
            catch (Exception ex)
            {
                ErrorHandlingService.Instance.ReportException(ErrorCategory.Configuration, "DigitalTwinWebHost.Initialize", ex);
                StatusChanged?.Invoke(this, $"3D digital twin unavailable: {ex.Message}");
            }
            finally
            {
                _initializing = false;
            }
        }

        /// <summary>Call on the UI thread after engine ticks; throttled internally.</summary>
        public void PushFrame()
        {
            if (!IsReady || _clock.Elapsed < FrameInterval)
            {
                return;
            }

            _clock.Restart();
            try
            {
                string json = TwinFrameBuilder.ToJson(TwinFrameBuilder.Build(_gateway));
                _view.CoreWebView2.PostWebMessageAsJson(json);
            }
            catch (Exception ex)
            {
                ErrorHandlingService.Instance.ReportException(ErrorCategory.ViewModel, "DigitalTwinWebHost.PushFrame", ex, wasRecovered: true);
            }
        }

        public void SetCamera(string preset) => Send(new { type = "camera", preset });

        public void StartTour(string name) => Send(new { type = "tour", name });

        private void Send(object message)
        {
            if (IsReady)
            {
                _view.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));
            }
        }

        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ready")
                {
                    _pageReady = true;
                    _clock.Restart();
                    StatusChanged?.Invoke(this, "Live: 3D digital twin connected to the line runtime.");
                    PushFrameNow();
                }
            }
            catch (JsonException)
            {
                // ignore malformed messages from the page
            }
        }

        private void PushFrameNow()
        {
            _clock.Restart();
            _view.CoreWebView2?.PostWebMessageAsJson(TwinFrameBuilder.ToJson(TwinFrameBuilder.Build(_gateway)));
        }
    }
}
