using System.Diagnostics;
using System.Text.Json;
using Matraca.Core;
using Matraca.Mac.Platform.Interop;

namespace Matraca.Mac.Platform.Web;

internal static class MacWebViewSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length is < 3 or > 4
            || (args.Length == 4
                && (!int.TryParse(args[3], out int duration)
                    || duration is < 1000 or > 120000)))
        {
            Logger.Error(
                "Uso: --webview-smoke <raiz-assets> <resultado-json> [duracao-ms: 1000..120000].");
            return 2;
        }

        string assetRoot = args[1];
        string resultPath = args[2];
        int durationMilliseconds = args.Length == 4 ? int.Parse(args[3]) : 5000;

        try
        {
            Frameworks.EnsureLoaded();
            ObjCClasses.Warm();
            using var pool = AutoreleasePool.New();
            var application = MacApplication.Shared();
            application.ConfigureAsAccessory();
            MainThread.Initialize();

            var messages = new List<string>();
            bool bridgeReady = false;
            bool bridgeRoundTrip = false;
            bool cssLoaded = false;
            bool securityProbeBlocked = false;
            bool placeholderResponseReceived = false;
            using var host = new MacWebViewHost(
                assetRoot,
                "Matraca WebKit Smoke",
                entryPath: "smoke.html");
            host.MessageReceived += message =>
            {
                messages.Add(message);
                using JsonDocument document = JsonDocument.Parse(message);
                JsonElement root = document.RootElement;
                string? messageType = root.TryGetProperty("type", out JsonElement type)
                    ? type.GetString()
                    : null;
                if (messageType == "bridge.ready")
                {
                    bridgeReady = true;
                    host.PostJson("{\"version\":1,\"type\":\"smoke.native\",\"payload\":{}}");
                }
                else if (messageType == "smoke.pong"
                    && root.TryGetProperty("payload", out JsonElement payload))
                {
                    bridgeRoundTrip = true;
                    cssLoaded = payload.TryGetProperty("cssLoaded", out JsonElement css)
                        && css.GetBoolean();
                    securityProbeBlocked = payload.TryGetProperty(
                        "securityProbeBlocked",
                        out JsonElement security)
                        && security.GetBoolean();
                }
                else if (messageType == "request"
                    && root.TryGetProperty("id", out JsonElement requestId)
                    && requestId.ValueKind == JsonValueKind.String)
                {
                    host.PostJson(JsonSerializer.Serialize(new
                    {
                        version = 1,
                        id = requestId.GetString(),
                        type = "response",
                        ok = false,
                        error = new { code = "not_implemented" },
                    }));
                }
                else if (messageType == "smoke.requestRejected"
                    && root.TryGetProperty("payload", out JsonElement rejection))
                {
                    placeholderResponseReceived = rejection.TryGetProperty(
                        "code",
                        out JsonElement code)
                        && code.GetString() == "not_implemented";
                }
            };
            host.ShowExplicitly();

            IntPtr runLoop = ObjC.Send(ObjCClasses.NSRunLoop, ObjCSelectors.CurrentRunLoop);
            var elapsed = Stopwatch.StartNew();
            while (elapsed.ElapsedMilliseconds < durationMilliseconds)
            {
                IntPtr until = ObjC.SendDouble(
                    ObjCClasses.NSDate,
                    ObjCSelectors.DateWithTimeIntervalSinceNow,
                    0.05);
                ObjC.SendVoid(runLoop, ObjCSelectors.RunUntilDate, until);
            }

            host.Hide();
            bool uiReady = false;
            bool uiDataReady = false;
            bool microphoneReady = false;
            int pageCount = 0;
            int spectrumBandCount = 0;
            int settingsTabCount = 0;
            int settingsControlCount = 0;
            int deviceCount = 0;
            int modelCount = 0;
            bool modelsValid = false;
            using var appHost = new MacWebViewHost(
                assetRoot,
                "Matraca UI Smoke",
                entryPath: "#microphone");
            using var bridge = new MacWebBridge(app: null);
            bridge.MessageProduced += message => MainThread.Post(() => appHost.PostJson(message));
            appHost.MessageReceived += message =>
            {
                using JsonDocument document = JsonDocument.Parse(message);
                JsonElement root = document.RootElement;
                if (!root.TryGetProperty("type", out JsonElement type)) return;
                if (type.GetString() == "request")
                {
                    string? response = bridge.HandleAsync(message).GetAwaiter().GetResult();
                    if (response != null) appHost.PostJson(response);
                }
                else if (type.GetString() == "ui.ready"
                    && root.TryGetProperty("payload", out JsonElement readyPayload))
                {
                    uiReady = true;
                    pageCount = readyPayload.GetProperty("pageCount").GetInt32();
                    spectrumBandCount = readyPayload.GetProperty("spectrumBandCount").GetInt32();
                    settingsTabCount = readyPayload.GetProperty("settingsTabCount").GetInt32();
                    settingsControlCount = readyPayload.GetProperty("settingsControlCount").GetInt32();
                }
                else if (type.GetString() == "ui.dataReady"
                    && root.TryGetProperty("payload", out JsonElement dataPayload))
                {
                    uiDataReady = true;
                    deviceCount = dataPayload.GetProperty("deviceCount").GetInt32();
                    modelCount = dataPayload.GetProperty("modelCount").GetInt32();
                    modelsValid = dataPayload.GetProperty("modelsValid").GetBoolean();
                }
                else if (type.GetString() == "ui.micReady"
                    && root.TryGetProperty("payload", out JsonElement microphonePayload))
                {
                    microphoneReady = microphonePayload.GetProperty("hasFiniteLevel").GetBoolean()
                        && microphonePayload.GetProperty("bandCount").GetInt32() == 48;
                }
            };
            var uiElapsed = Stopwatch.StartNew();
            while ((!uiReady || !uiDataReady || !microphoneReady)
                && uiElapsed.ElapsedMilliseconds < 7000)
            {
                IntPtr until = ObjC.SendDouble(
                    ObjCClasses.NSDate,
                    ObjCSelectors.DateWithTimeIntervalSinceNow,
                    0.05);
                ObjC.SendVoid(runLoop, ObjCSelectors.RunUntilDate, until);
            }

            bool hudReady = false;
            using var hudHost = new MacWebViewHost(
                assetRoot,
                "Matraca HUD Smoke",
                width: 430,
                height: 92,
                entryPath: "hud.html",
                nonActivatingOverlay: true);
            hudHost.MessageReceived += message =>
            {
                using JsonDocument document = JsonDocument.Parse(message);
                if (document.RootElement.TryGetProperty("type", out JsonElement type)
                    && type.GetString() == "ui.hudReady")
                    hudReady = true;
            };
            hudHost.ShowExplicitly();
            var hudElapsed = Stopwatch.StartNew();
            while (!hudReady && hudElapsed.ElapsedMilliseconds < 2000)
            {
                IntPtr until = ObjC.SendDouble(
                    ObjCClasses.NSDate,
                    ObjCSelectors.DateWithTimeIntervalSinceNow,
                    0.05);
                ObjC.SendVoid(runLoop, ObjCSelectors.RunUntilDate, until);
            }
            bool hudNonActivating = !hudHost.CanBecomeKeyWindow
                && !hudHost.CanBecomeMainWindow
                && hudHost.IgnoresMouseEvents
                && application.ActivationPolicy == 1;
            hudHost.Hide();

            bool success = bridgeReady
                && bridgeRoundTrip
                && cssLoaded
                && securityProbeBlocked
                && placeholderResponseReceived
                && host.ServedAssetCount >= 4
                && host.BlockedAssetCount >= 1
                && host.BlockedNavigationCount >= 1
                && uiReady
                && uiDataReady
                && microphoneReady
                && pageCount == 5
                && spectrumBandCount == 48
                && settingsTabCount == 5
                && settingsControlCount >= 26
                && deviceCount > 0
                && modelCount == ModelDownloader.Catalog.Count()
                && modelsValid
                && hudReady
                && hudNonActivating
                && hudHost.ServedAssetCount >= 4
                && appHost.ServedAssetCount >= 4
                && appHost.LastAssetError == null;
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success,
                bridgeReady,
                bridgeRoundTrip,
                cssLoaded,
                securityProbeBlocked,
                placeholderResponseReceived,
                host.ServedAssetCount,
                host.BlockedAssetCount,
                host.BlockedNavigationCount,
                host.LastAssetError,
                uiReady,
                uiDataReady,
                microphoneReady,
                pageCount,
                spectrumBandCount,
                settingsTabCount,
                settingsControlCount,
                deviceCount,
                modelCount,
                modelsValid,
                hudReady,
                hudNonActivating,
                hudServedAssetCount = hudHost.ServedAssetCount,
                uiServedAssetCount = appHost.ServedAssetCount,
                uiAssetError = appHost.LastAssetError,
                receivedMessages = messages,
                durationMilliseconds,
                manualValidationRequired = true,
            }));
            return success ? 0 : 1;
        }
        catch (Exception exception)
        {
            Exception rootCause = exception.GetBaseException();
            Logger.Error("Smoke do WebKit falhou", rootCause);
            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                success = false,
                error = rootCause.GetType().Name,
                rootCause.Message,
            }));
            return 1;
        }
    }
}
