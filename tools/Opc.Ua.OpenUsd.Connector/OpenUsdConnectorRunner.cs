/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsdScene.Conversion;
using Opc.Ua.OpenUsdScene.Scene;

namespace Opc.Ua.OpenUsd.Connector
{
    /// <summary>
    /// Runs the generic <see cref="OpenUsdConnector"/>: connects to a running OPC UA
    /// server (e.g. MinimalRobotServer or PumpDeviceIntegrationServer), discovers the
    /// OpenUSD representation and bindings via <c>Server/OpenUSD/Representations</c>, and
    /// streams live values into a <see cref="UsdFileSink"/> (an override <c>live.usda</c>).
    /// With <c>--view</c> it additionally opens a viewport on the composed stage and fans
    /// the same values into it, so the twin animates on screen in the same process.
    /// Invoked as <c>Opc.Ua.OpenUsd.Connector [--server &lt;url&gt;] [--out &lt;live.usda&gt;]
    /// [--seconds N] [--view] [--camera &lt;primPath&gt;]
    /// [--renderer &lt;Auto|Storm|D3D12|Vulkan&gt;]</c>. Absent <c>--camera</c>, the
    /// viewport opens on the first camera the served stage authors.
    /// </summary>
    public static class OpenUsdConnectorRunner
    {
        public static async Task<int> RunAsync(string[] args)
        {
            string server = GetOption(args, "--server")
                ?? "opc.tcp://localhost:62542/PumpDeviceIntegrationServer";
            string outPath = GetOption(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "live.usda");
            int seconds = int.TryParse(GetOption(args, "--seconds"), out int s) ? s : 0;

            // Opens a viewport on the composed stage and streams the same values into it.
            // The renderer lives in the optional Opc.Ua.OpenUsd.Connector.Viewer assembly,
            // so the connector itself stays free of Avalonia and the native USD payload.
            bool view = HasFlag(args, "--view");
            string? renderer = GetOption(args, "--renderer");
            string? stageOption = GetOption(args, "--stage");
            string? pluginPath = GetOption(args, "--plugins");
            string? cameraPath = GetOption(args, "--camera");

            // §5.15 asset content delivery (OU-AssetDelivery): when set, the connector
            // downloads the server's served USD layer closure into this cache directory
            // (verifying each digest) and writes a self-contained stage.usda there, so a
            // viewer renders the twin with no external asset resolver. live.usda is
            // written into the same directory.
            string? cacheDir = GetOption(args, "--fetch-assets");
            if (view && string.IsNullOrEmpty(cacheDir) && string.IsNullOrEmpty(stageOption))
            {
                // Rendering needs a resolvable asset closure. Without an explicit stage the
                // connector fetches one rather than opening an empty viewport.
                cacheDir = Path.Combine(GetPrivateStateRoot(), "stage");
            }
            if (!string.IsNullOrEmpty(cacheDir))
            {
                Directory.CreateDirectory(cacheDir!);
                outPath = Path.Combine(cacheDir!, "live.usda");
            }

            // Secure by default (spec §9: an authenticated, integrity-protected endpoint
            // with server-certificate trust is required). The --insecure flag opts into
            // an unsecured endpoint and blanket certificate acceptance, which is only
            // appropriate for a localhost demo with self-signed certificates.
            bool insecure = HasFlag(args, "--insecure");

            // Command bindings (UsdToUaCommand) are opt-in and disabled by default
            // (fail-closed). --enable-commands lets the connector actuate the single
            // controllable command binding; --command-value <double> supplies the
            // setpoint to write once at start (demo).
            bool enableCommands = HasFlag(args, "--enable-commands");
            string? commandValueOpt = GetOption(args, "--command-value");

            ITelemetryContext telemetry = DefaultTelemetry.Create(b => b.SetMinimumLevel(LogLevel.Warning));

            string pkiRoot = Path.Combine(GetPrivateStateRoot(), Path.GetRandomFileName());
            var config = new ApplicationConfiguration(telemetry)
            {
                ApplicationName = "Opc.Ua.OpenUsd.Connector",
                ApplicationUri = "urn:localhost:OPCFoundation:Opc.Ua.OpenUsd.Connector",
                ApplicationType = ApplicationType.Client,
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "own"),
                        SubjectName = "CN=Opc.Ua.OpenUsd.Connector, O=OPC Foundation"
                    },
                    TrustedIssuerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "issuer")
                    },
                    TrustedPeerCertificates = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "trusted")
                    },
                    RejectedCertificateStore = new CertificateTrustList
                    {
                        StoreType = CertificateStoreType.Directory,
                        StorePath = Path.Combine(pkiRoot, "rejected")
                    },
                    AutoAcceptUntrustedCertificates = insecure
                },
                TransportQuotas = new TransportQuotas { MaxMessageSize = 4 * 1024 * 1024 },
                ClientConfiguration = new ClientConfiguration(),
                ServerConfiguration = new ServerConfiguration()
            };
            await config.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);

            var appInstance = new Opc.Ua.Configuration.ApplicationInstance(config, telemetry);
            await appInstance.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false);
            await appInstance.DisposeAsync().ConfigureAwait(false);
            config.CertificateManager ??= CertificateManagerFactory.Create(config.SecurityConfiguration, telemetry);
            if (insecure)
            {
                // Demo-only: accept any server certificate.
                config.CertificateManager.AcceptError = static (cert, err) => true;
                Console.WriteLine("WARNING: --insecure: using an unsecured endpoint and accepting any server certificate.");
            }

            Console.WriteLine($"Connecting to {server} ...");
            EndpointDescription? endpointDescription = null;
            for (int attempt = 0; attempt < 40 && endpointDescription == null; attempt++)
            {
                try
                {
                    endpointDescription = await CoreClientUtils.SelectEndpointAsync(
                        config, server, useSecurity: !insecure, telemetry, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
            if (endpointDescription == null)
            {
                Console.Error.WriteLine("ERROR: could not reach the server endpoint. Is the server running?");
                return 2;
            }

            var endpoint = new ConfiguredEndpoint(null, endpointDescription, EndpointConfiguration.Create(config));
            var sessionFactory = new DefaultSessionFactory(telemetry);
            ISession session = await sessionFactory.CreateAsync(
                config, endpoint, updateBeforeConnect: false,
                sessionName: "Opc.Ua.OpenUsd.Connector", sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default, ct: CancellationToken.None).ConfigureAwait(false);

            // Cross-server composition (spec 5.14) is opt-in. A component binding can
            // name another server's endpoint, and honouring that means the connector
            // opens an outbound session to a URL the server chose. That is a trust
            // decision, so the library is fail-closed - no factory, no federation -
            // and --federate is what supplies the factory. The same --insecure
            // posture governs certificate acceptance for those sessions.
            OpenUsdConnectorOptions? connectorOptions = null;
            if (HasFlag(args, "--federate"))
            {
                connectorOptions = new OpenUsdConnectorOptions
                {
                    EnableCommands = enableCommands,
                    RemoteSessionFactory = (endpointUrl, ct) =>
                        OpenRemoteSessionAsync(config, sessionFactory, telemetry, endpointUrl, insecure, ct),
                };
                Console.WriteLine("--federate: composing components hosted on other servers.");
            }

            var fileSink = new UsdFileSink(outPath);
            string? stagePath = stageOption;
            IUsdViewHost? viewHost = null;
            if (view && !UsdViewHostLoader.TryLoad(out viewHost, out string unavailable))
            {
                Console.Error.WriteLine($"ERROR: the view option is unavailable. {unavailable}");
                await CloseAsync(session, config).ConfigureAwait(false);
                return 3;
            }

            // The asset closure is fetched before anything renders, because the viewport
            // needs a stage it can resolve. Fetching does not use a sink, so a throwaway
            // connector keeps the live sink entirely out of that path.
            if (!string.IsNullOrEmpty(cacheDir))
            {
                var fetcher = new OpenUsdConnector(session, new MockUsdSink(), enableCommands: false);
                try
                {
                    List<OpenUsdConnector.FetchedAsset> fetched =
                        await fetcher.FetchServedAssetsAsync(cacheDir!, CancellationToken.None).ConfigureAwait(false);
                    if (fetched.Count > 0)
                    {
                        WriteStageUsda(cacheDir!, fetched);
                        stagePath ??= Path.Combine(cacheDir!, "stage.usda");
                        cameraPath ??= FindStageCamera(fetched);
                        Console.WriteLine(
                            $"Fetched {fetched.Count} server-delivered USD layer(s) into {cacheDir}; " +
                            "wrote a self-contained stage.usda.");
                        if (cameraPath != null)
                        {
                            Console.WriteLine($"Opening on the stage camera {cameraPath}.");
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            "Server does not advertise served assets (OU-AssetDelivery); using the external base asset.");
                    }
                }
                finally
                {
                    await fetcher.DisposeAsync().ConfigureAwait(false);
                }
            }

            int exit = view
                ? await RunViewportAsync(
                    viewHost!, stagePath, renderer, pluginPath, cameraPath, session, fileSink,
                    enableCommands, commandValueOpt, seconds, outPath, connectorOptions, telemetry)
                    .ConfigureAwait(false)
                : await RunHeadlessAsync(
                    session, fileSink, enableCommands, commandValueOpt, seconds, outPath,
                    connectorOptions, telemetry)
                    .ConfigureAwait(false);

            await CloseAsync(session, config).ConfigureAwait(false);
            Console.WriteLine($"Stopped. Final override layer: {outPath}");
            return exit;
        }

        /// <summary>
        /// Streams into the override layer only, until Ctrl+C or the requested duration.
        /// </summary>
        private static async Task<int> RunHeadlessAsync(
            ISession session,
            IUsdSink sink,
            bool enableCommands,
            string? commandValueOpt,
            int seconds,
            string outPath,
            OpenUsdConnectorOptions? connectorOptions,
            ITelemetryContext telemetry)
        {
            var connector = connectorOptions != null
                ? new OpenUsdConnector(session, sink, connectorOptions, telemetry)
                : new OpenUsdConnector(session, sink, enableCommands);
            try
            {
                await connector.StartAsync(CancellationToken.None).ConfigureAwait(false);
                Console.WriteLine($"Streaming live OPC UA values into {outPath}. Press Ctrl+C to stop.");
                await IssueCommandIfRequestedAsync(
                    connector, enableCommands, commandValueOpt, CancellationToken.None)
                    .ConfigureAwait(false);

                using var stop = new SemaphoreSlim(0, 1);
                ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stop.Release(); };
                Console.CancelKeyPress += handler;
                try
                {
                    if (seconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
                    }
                    else
                    {
                        await stop.WaitAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    Console.CancelKeyPress -= handler;
                }

                await connector.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await connector.DisposeAsync().ConfigureAwait(false);
            }
            return 0;
        }

        /// <summary>
        /// Opens the viewport on a dedicated UI thread and streams into both the override
        /// layer and the rendered stage. Completes when the window closes.
        /// </summary>
        /// <remarks>
        /// The viewport owns whichever thread runs it, and on Windows that thread must be
        /// single-threaded-apartment. Running it on a thread of its own rather than on the
        /// process main thread keeps this method genuinely asynchronous, so no caller ever
        /// blocks waiting on the window.
        /// </remarks>
        private static Task<int> RunViewportAsync(
            IUsdViewHost host,
            string? stagePath,
            string? renderer,
            string? pluginPath,
            string? cameraPath,
            ISession session,
            IUsdSink fileSink,
            bool enableCommands,
            string? commandValueOpt,
            int seconds,
            string outPath,
            OpenUsdConnectorOptions? connectorOptions,
            ITelemetryContext telemetry)
        {
            if (string.IsNullOrEmpty(stagePath) || !File.Exists(stagePath))
            {
                Console.Error.WriteLine(
                    "ERROR: there is no stage to render. The server does not deliver its USD " +
                    "assets, so pass --stage with a locally composed stage instead.");
                return Task.FromResult(4);
            }

            var options = new UsdViewOptions
            {
                StagePath = stagePath!,
                PluginPath = pluginPath,
                Renderer = renderer,
                CameraPath = cameraPath,
                Title = $"OPC UA - OpenUSD Connector - {Path.GetFileName(stagePath)}"
            };

            var completion = new TaskCompletionSource<int>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var uiThread = new Thread(() =>
            {
                using var lifetime = new CancellationTokenSource();
                if (seconds > 0)
                {
                    lifetime.CancelAfter(TimeSpan.FromSeconds(seconds));
                }
                try
                {
                    host.RunViewport(
                        options,
                        (stageSink, cancellationToken) => StreamAsync(
                            session, fileSink, stageSink, enableCommands,
                            commandValueOpt, outPath, connectorOptions, telemetry, cancellationToken),
                        lifetime.Token);
                    completion.TrySetResult(0);
                }
#pragma warning disable CA1031 // Surfaced to the caller through the completion source.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    Console.Error.WriteLine($"ERROR: the viewport failed: {exception.Message}");
                    completion.TrySetResult(5);
                }
            })
            {
                IsBackground = false,
                Name = "OpenUSD viewport"
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uiThread.SetApartmentState(ApartmentState.STA);
            }
            uiThread.Start();
            return completion.Task;
        }

        /// <summary>
        /// Runs the connector against both sinks until the viewport shuts down.
        /// </summary>
        private static async Task StreamAsync(
            ISession session,
            IUsdSink fileSink,
            IUsdSink stageSink,
            bool enableCommands,
            string? commandValueOpt,
            string outPath,
            OpenUsdConnectorOptions? connectorOptions,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            var sink = new CompositeUsdSink(fileSink, stageSink);
            var connector = connectorOptions != null
                ? new OpenUsdConnector(session, sink, connectorOptions, telemetry)
                : new OpenUsdConnector(session, sink, enableCommands);
            try
            {
                await connector.StartAsync(cancellationToken).ConfigureAwait(false);
                Console.WriteLine(
                    $"Streaming live OPC UA values into {outPath} and the viewport. " +
                    "Close the window to stop.");
                await IssueCommandIfRequestedAsync(
                    connector, enableCommands, commandValueOpt, cancellationToken)
                    .ConfigureAwait(false);
                await WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
                // Shutdown runs after the token has already fired, so stopping cleanly
                // must not itself be cancelled.
                await connector.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await connector.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async Task WaitForShutdownAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The window closed or the duration elapsed; both are ordinary shutdowns.
            }
        }

        private static async Task IssueCommandIfRequestedAsync(
            OpenUsdConnector connector,
            bool enableCommands,
            string? commandValueOpt,
            CancellationToken cancellationToken)
        {
            if (!enableCommands || commandValueOpt == null
                || !double.TryParse(commandValueOpt, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double commandValue))
            {
                return;
            }
            bool ok = await connector.IssueCommandAsync(commandValue, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(ok
                ? $"Command issued: setpoint <- {commandValue}."
                : "Command binding not found or write rejected.");
        }

        private static async Task CloseAsync(ISession session, ApplicationConfiguration config)
        {
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            await session.DisposeAsync().ConfigureAwait(false);
            (config.CertificateManager as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Opens a session to a server named by a cross-server component binding.
        /// </summary>
        /// <param name="config">The connector's application configuration.</param>
        /// <param name="sessionFactory">Factory used for the primary session too.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="endpointUrl">Endpoint the component binding names.</param>
        /// <param name="insecure">Whether to accept an unsecured endpoint.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>A session on the subordinate server.</returns>
        /// <remarks>
        /// Reuses the connector's own security posture rather than relaxing it: a
        /// federated session is negotiated exactly the way the primary one is, so
        /// composing a subordinate server cannot silently downgrade security. The
        /// connector owns these sessions and closes them on disposal.
        /// </remarks>
        private static async Task<ISession> OpenRemoteSessionAsync(
            ApplicationConfiguration config,
            DefaultSessionFactory sessionFactory,
            ITelemetryContext telemetry,
            string endpointUrl,
            bool insecure,
            CancellationToken ct)
        {
            EndpointDescription? description = await CoreClientUtils.SelectEndpointAsync(
                config, endpointUrl, useSecurity: !insecure, telemetry, ct).ConfigureAwait(false);
            if (description == null)
            {
                throw new InvalidOperationException(
                    $"No endpoint could be selected for federated server '{endpointUrl}'.");
            }
            var endpoint = new ConfiguredEndpoint(
                null, description, EndpointConfiguration.Create(config));
            return await sessionFactory.CreateAsync(
                config,
                endpoint,
                updateBeforeConnect: false,
                sessionName: "Opc.Ua.OpenUsd.Connector (federated)",
                sessionTimeout: 60000,
                identity: new UserIdentity(new AnonymousIdentityToken()),
                preferredLocales: default,
                ct: ct).ConfigureAwait(false);
        }

        // Writes a self-contained stage.usda that composes the connector's live override
        // layer over the server-delivered root layer (both now local in the cache dir).
        /// <summary>
        /// The prim path of the camera a served stage wants a viewer to open on.
        /// </summary>
        /// <remarks>
        /// A stage that authors a camera has an opinion about how it should first be seen,
        /// and it is a better one than framing the bounds: the eye lands wherever the
        /// geometry happens to extend, which for an enclosed scene means inside it. The
        /// first camera in the served root layer wins, so a stage orders its establishing
        /// shot first; <c>--camera</c> overrides this outright.
        /// </remarks>
        /// <param name="fetched">The layers fetched from the server.</param>
        /// <returns>The camera prim path, or <c>null</c> when the stage authors none.</returns>
        private static string? FindStageCamera(List<OpenUsdConnector.FetchedAsset> fetched)
        {
            OpenUsdConnector.FetchedAsset? root =
                fetched.Find(a => a.Kind == OpenUsdAssetKind.RootLayer);
            if (root == null || !File.Exists(root.LocalPath))
            {
                return null;
            }

            try
            {
                UsdStage stage = UsdaReader.ParseFile(
                    root.LocalPath, applyExampleOverlays: false);
                foreach (UsdPrim prim in stage.AllPrims())
                {
                    if (string.Equals(prim.TypeName, "Camera", StringComparison.Ordinal))
                    {
                        return prim.Path;
                    }
                }
            }
            catch (IOException)
            {
                // A stage we cannot read is not a reason to refuse to open one: fall back
                // to letting the host frame the bounds itself.
            }
            catch (FormatException)
            {
            }
            return null;
        }

        private static void WriteStageUsda(string cacheDir, List<OpenUsdConnector.FetchedAsset> fetched)
        {
            OpenUsdConnector.FetchedAsset? root = fetched.Find(a => a.Kind == OpenUsdAssetKind.RootLayer);
            string rootName = root != null ? Path.GetFileName(root.LocalPath) : "base.usda";
            var sb = new StringBuilder();
            sb.Append("#usda 1.0\n(\n");
            sb.Append("    doc = \"Self-contained OpenUSD stage: server-delivered base layers + the live OPC UA override.\"\n");
            sb.Append("    subLayers = [\n        @./live.usda@,\n        @./").Append(rootName).Append("@\n    ]\n");
            sb.Append(")\n");
            File.WriteAllText(Path.Combine(cacheDir, "stage.usda"), sb.ToString());

            // The override layer is only written once the first values arrive, so seed an
            // empty one now. Without it a viewer that opens the stage first reports the
            // sublayer as missing before the connector has had anything to say.
            string livePath = Path.Combine(cacheDir, "live.usda");
            if (!File.Exists(livePath))
            {
                File.WriteAllText(
                    livePath,
                    "#usda 1.0\n(\n    doc = \"OPC UA -> OpenUSD live bindings (override layer)\"\n)\n");
            }
        }

        /// <summary>
        /// Returns the per-user directory the connector keeps its asset cache
        /// and PKI stores in, creating it if needed.
        /// </summary>
        /// <remarks>
        /// Deliberately not under <see cref="Path.GetTempPath"/>: on POSIX that
        /// is the shared, world-writable <c>/tmp</c>, so a fixed sub-path there
        /// can be pre-created by another local user as a symlink. Everything the
        /// connector writes into the asset cache is server-supplied content at
        /// server-supplied relative paths, and the PKI root holds the client's
        /// own private key plus its trusted-issuer store - redirecting either
        /// would be serious. LocalApplicationData is per-user on every supported
        /// platform.
        /// </remarks>
        /// <returns>The private state directory.</returns>
        private static string GetPrivateStateRoot()
        {
            string baseDirectory = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                // A headless POSIX account may have neither XDG_DATA_HOME nor
                // HOME; fall back to a directory beside the executable rather
                // than to a shared temp path.
                baseDirectory = AppContext.BaseDirectory;
            }
            string root = Path.Combine(baseDirectory, "Opc.Ua.OpenUsd.Connector");
            Directory.CreateDirectory(root);
            return root;
        }

        private static string? GetOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
