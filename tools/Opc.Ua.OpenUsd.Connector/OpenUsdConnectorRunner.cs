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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.OpenUsd.Scene.Conversion;
using Opc.Ua.OpenUsd.Scene;

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
        internal sealed class ConnectorRunOptions
        {
            public string Server { get; private set; } = string.Empty;

            public string OutPath { get; private set; } = string.Empty;

            public int Seconds { get; private set; }

            public bool View { get; private set; }

            public string? Renderer { get; private set; }

            public string? StagePath { get; private set; }

            public string? PluginPath { get; private set; }

            public string? CameraPath { get; private set; }

            public bool PrintPickCommands { get; private set; }

            public string? CommandPrimPath { get; private set; }

            public UsdViewPickMode PickMode { get; private set; }

            public string? FetchAssetsPath { get; private set; }

            public bool Insecure { get; private set; }

            public bool EnableCommands { get; private set; }

            public string? CommandValue { get; private set; }

            public static bool TryParse(
                string[] args,
                string currentDirectory,
                out ConnectorRunOptions options)
            {
                options = new ConnectorRunOptions
                {
                    Server = GetOption(args, "--server") ??
                        "opc.tcp://localhost:62542/PumpDeviceIntegrationServer",
                    OutPath = GetOption(args, "--out") ?? Path.Combine(currentDirectory, "live.usda"),
                    Seconds = int.TryParse(GetOption(args, "--seconds"), out int seconds) ? seconds : 0,
                    View = HasFlag(args, "--view"),
                    Renderer = GetOption(args, "--renderer"),
                    StagePath = GetOption(args, "--stage"),
                    PluginPath = GetOption(args, "--plugins"),
                    CameraPath = GetOption(args, "--camera"),
                    PrintPickCommands = HasFlag(args, "--pick-command"),
                    CommandPrimPath = GetOptionalOption(args, "--pick-command"),
                    FetchAssetsPath = GetOption(args, "--fetch-assets"),
                    Insecure = HasFlag(args, "--insecure"),
                    EnableCommands = HasFlag(args, "--enable-commands"),
                    CommandValue = GetOption(args, "--command-value")
                };
                string? pickMode = GetOption(args, "--pick-mode");
                if (!TryParsePickMode(pickMode, out UsdViewPickMode parsedPickMode))
                {
                    return false;
                }
                options.PickMode = parsedPickMode;
                return true;
            }

            private static bool TryParsePickMode(string? pickMode, out UsdViewPickMode parsedPickMode)
            {
                parsedPickMode = UsdViewPickMode.Auto;
                if (string.IsNullOrWhiteSpace(pickMode))
                {
                    return true;
                }

                string normalized = pickMode.Replace("-", string.Empty, StringComparison.Ordinal);
                if (!Enum.TryParse(normalized, ignoreCase: true, out parsedPickMode))
                {
                    return false;
                }
#if NET5_0_OR_GREATER
                return Enum.IsDefined(parsedPickMode);
#else
                // Enum.IsDefined<T>(T) is .NET 5+; .NET Framework only has the non-generic overload.
                return Enum.IsDefined(typeof(UsdViewPickMode), parsedPickMode);
#endif
            }
        }

        // Excluded because this opens a live OPC UA Session against a running server and the parser decisions are tested.
        [ExcludeFromCodeCoverage]
        public static async Task<int> RunAsync(string[] args)
        {
            if (!ConnectorRunOptions.TryParse(args, Environment.CurrentDirectory, out ConnectorRunOptions options))
            {
                Console.Error.WriteLine("ERROR: --pick-mode must be Auto, Renderer, or CommandPrim.");
                return 1;
            }

            string server = options.Server;
            string outPath = options.OutPath;
            int seconds = options.Seconds;
            bool view = options.View;
            string? renderer = options.Renderer;
            string? stageOption = options.StagePath;
            string? pluginPath = options.PluginPath;
            string? cameraPath = options.CameraPath;
            bool printPickCommands = options.PrintPickCommands;
            string? commandPrimPath = options.CommandPrimPath;

            // §5.15 asset content delivery (OU-AssetDelivery): when set, the connector
            // downloads the server's served USD layer closure into this cache directory
            // (verifying each digest) and writes a self-contained stage.usda there, so a
            // viewer renders the twin with no external asset resolver. live.usda is
            // written into the same directory.
            string? cacheDir = options.FetchAssetsPath;
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
            bool insecure = options.Insecure;

            // Command bindings (UsdToUaCommand) are opt-in and disabled by default
            // (fail-closed). --enable-commands lets the connector actuate the single
            // controllable command binding; --command-value <double> supplies the
            // setpoint to write once at start (demo).
            bool enableCommands = options.EnableCommands;
            string? commandValueOpt = options.CommandValue;

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
                Console.WriteLine(
                    "WARNING: --insecure: using an unsecured endpoint and accepting any server certificate.");
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

                    // A federated stage is only half fetched at this point. Composition
                    // has authored reference arcs onto prims owned by other servers, but
                    // the layers those arcs resolve against live on those servers, so
                    // without this the viewport shows the primary server's shell with
                    // empty placeholders where every subordinate's machines should be -
                    // live values arriving onto prims that have no geometry behind them.
                    if (connectorOptions?.RemoteSessionFactory != null)
                    {
                        fetched.AddRange(await FetchFederatedAssetsAsync(
                            fetcher, config, sessionFactory, telemetry, cacheDir!, insecure)
                            .ConfigureAwait(false));
                    }

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
                            "Server does not advertise served assets (OU-AssetDelivery); " +
                            "using the external base asset.");
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
                    enableCommands, commandValueOpt, printPickCommands, commandPrimPath, options.PickMode,
                    seconds, outPath, connectorOptions, telemetry)
                    .ConfigureAwait(false)
                : await RunHeadlessAsync(
                    session, fileSink, enableCommands, commandValueOpt, seconds, outPath,
                    connectorOptions, telemetry)
                    .ConfigureAwait(false);

            await CloseAsync(session, config).ConfigureAwait(false);
            Console.WriteLine($"Stopped. Final override layer: {outPath}");
            return exit;
        }

        internal static Task PrintPickedPrimAsync(
            string primPath,
            CancellationToken cancellationToken)
        {
            return PrintPickedPrimAsync(primPath, Console.Out, cancellationToken);
        }

        internal static Task PrintPickedPrimAsync(
            string primPath,
            TextWriter output,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.WriteLine($"Picked prim: {primPath}");
            return Task.CompletedTask;
        }

        internal static UsdViewOptions CreateViewOptions(
            string stagePath,
            string? renderer,
            string? pluginPath,
            string? cameraPath,
            bool printPickCommands,
            string? commandPrimPath,
            UsdViewPickMode pickMode,
            ITelemetryContext? telemetry = null,
            TextWriter? pickOutput = null)
        {
            var options = new UsdViewOptions
            {
                StagePath = stagePath,
                PluginPath = pluginPath,
                Renderer = renderer,
                CameraPath = cameraPath,
                Title = $"OPC UA - OpenUSD Connector - {Path.GetFileName(stagePath)}",
                Telemetry = telemetry,
                PrimPicked = printPickCommands
                    ? (primPath, cancellationToken) => PrintPickedPrimAsync(
                        primPath, pickOutput ?? Console.Out, cancellationToken)
                    : null,
                PickMode = pickMode
            };
            if (!string.IsNullOrWhiteSpace(commandPrimPath))
            {
                options.CommandPrimPath = commandPrimPath!;
            }
            return options;
        }

        internal static async Task WaitForShutdownAsync(CancellationToken cancellationToken)
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

        internal static bool TryParseCommandValue(
            bool enableCommands,
            string? commandValueOpt,
            out double commandValue)
        {
            commandValue = 0;
            return enableCommands &&
                commandValueOpt != null &&
                double.TryParse(
                    commandValueOpt,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out commandValue);
        }

        // Writes a self-contained stage.usda that composes the connector's live override
        // layer over the server-delivered root layer (both now local in the cache dir).
        internal static void WriteStageUsda(string cacheDir, List<OpenUsdConnector.FetchedAsset> fetched)
        {
            OpenUsdConnector.FetchedAsset? root = fetched.Find(a => a.Kind == OpenUsdAssetKind.RootLayer);
            string rootName = root != null ? Path.GetFileName(root.LocalPath) : "base.usda";
            var sb = new StringBuilder();
            sb.Append("#usda 1.0\n(\n");
            sb.Append("    doc = \"Self-contained OpenUSD stage: server-delivered base layers " +
                "+ the live OPC UA override.\"\n");
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
        internal static string GetPrivateStateRoot()
        {
            return GetPrivateStateRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }

        internal static string GetPrivateStateRoot(string? baseDirectory)
        {
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

        /// <summary>
        /// Streams into the override layer only, until Ctrl+C or the requested duration.
        /// </summary>
        // Excluded because this starts a live connector session and waits for console cancellation or elapsed duration.
        [ExcludeFromCodeCoverage]
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
                ConsoleCancelEventHandler handler = (_, e) =>
                {
                    e.Cancel = true;
                    stop.Release();
                };
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
        // Excluded because this runs the Avalonia event loop on an STA UI thread and requires the native OpenUSD payload.
        [ExcludeFromCodeCoverage]
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
            bool printPickCommands,
            string? commandPrimPath,
            UsdViewPickMode pickMode,
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

            UsdViewOptions options = CreateViewOptions(
                stagePath!, renderer, pluginPath, cameraPath, printPickCommands, commandPrimPath, pickMode, telemetry);

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
        // Excluded because this starts a live connector against two sinks and then waits for viewport shutdown.
        [ExcludeFromCodeCoverage]
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

        // Excluded because issuing the command requires a live connector binding; command-value parsing is tested.
        [ExcludeFromCodeCoverage]
        private static async Task IssueCommandIfRequestedAsync(
            OpenUsdConnector connector,
            bool enableCommands,
            string? commandValueOpt,
            CancellationToken cancellationToken)
        {
            if (!TryParseCommandValue(enableCommands, commandValueOpt, out double commandValue))
            {
                return;
            }
            bool ok = await connector.IssueCommandAsync(commandValue, cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine(ok
                ? $"Command issued: setpoint <- {commandValue}."
                : "Command binding not found or write rejected.");
        }

        // Excluded because this closes and disposes a live OPC UA Session plus its certificate manager.
        [ExcludeFromCodeCoverage]
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

        /// <summary>
        /// Fetches the asset closure of every server named by a cross-server component.
        /// </summary>
        /// <param name="primary">Connector on the primary session, used for discovery.</param>
        /// <param name="config">Application configuration for the outbound sessions.</param>
        /// <param name="sessionFactory">Factory used to open the outbound sessions.</param>
        /// <param name="telemetry">Telemetry context.</param>
        /// <param name="cacheDir">Directory every layer is fetched into.</param>
        /// <param name="insecure">Whether to accept an unsecured endpoint.</param>
        /// <returns>The layers fetched from the subordinate servers.</returns>
        /// <remarks>
        /// Everything lands in the same cache directory on purpose: a component's
        /// <c>ComponentAssetReference</c> is a plain relative identifier such as
        /// <c>@pump.usda@</c>, so it only resolves if the subordinate's layer sits
        /// beside the primary server's. Best-effort per server, for the same reason
        /// federation itself is: one unreachable subordinate costs its own geometry,
        /// not the whole scene.
        /// </remarks>
        private static async Task<List<OpenUsdConnector.FetchedAsset>> FetchFederatedAssetsAsync(
            OpenUsdConnector primary,
            ApplicationConfiguration config,
            DefaultSessionFactory sessionFactory,
            ITelemetryContext telemetry,
            string cacheDir,
            bool insecure)
        {
            var result = new List<OpenUsdConnector.FetchedAsset>();
            var endpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (OpenUsdConnector.RepresentationInfo rep in
                await primary.DiscoverAllRepresentationsAsync(CancellationToken.None).ConfigureAwait(false))
            {
                foreach (OpenUsdConnector.ComponentInfo component in rep.Components)
                {
                    if (component.Enabled && !string.IsNullOrEmpty(component.ComponentEndpointUrl))
                    {
                        endpoints.Add(component.ComponentEndpointUrl!);
                    }
                }
            }

            foreach (string endpointUrl in endpoints)
            {
                ISession? remote = null;
                OpenUsdConnector? remoteFetcher = null;
                try
                {
                    remote = await OpenRemoteSessionAsync(
                        config, sessionFactory, telemetry, endpointUrl, insecure, CancellationToken.None)
                        .ConfigureAwait(false);
                    remoteFetcher = new OpenUsdConnector(remote, new MockUsdSink(), enableCommands: false);
                    List<OpenUsdConnector.FetchedAsset> got = await remoteFetcher
                        .FetchServedAssetsAsync(cacheDir, CancellationToken.None)
                        .ConfigureAwait(false);
                    result.AddRange(got);
                    Console.WriteLine(
                        $"Fetched {got.Count} layer(s) from the federated server {endpointUrl}.");
                }
#pragma warning disable CA1031 // One unreachable subordinate must not fail the stage.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    // TODO: narrow once RemoteSessionFactory documents what it may throw.
                    Console.Error.WriteLine(
                        $"WARNING: could not fetch assets from {endpointUrl}: {ex.Message}");
                }
                finally
                {
                    if (remoteFetcher != null)
                    {
                        await remoteFetcher.DisposeAsync().ConfigureAwait(false);
                    }
                    if (remote != null)
                    {
                        await remote.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }

            return result;
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

        private static string? GetOptionalOption(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) &&
                    !args[i + 1].StartsWith("--", StringComparison.Ordinal))
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
