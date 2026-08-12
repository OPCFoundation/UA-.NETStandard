/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if INTENT_VIEWER_MCP
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#endif
using Microsoft.Extensions.Logging;
#if INTENT_VIEWER_MCP
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.Server;
#endif
using Opc.Ua;
using Opc.Ua.Client;
#if INTENT_VIEWER_MCP
using Opc.Ua.Mcp;
#endif
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.Robotics.Client;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace IntentViewerClient
{
    internal sealed record IntentViewerOptions
    {
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62840/IntentEnabledRobot";

        public bool Insecure { get; init; }

        public bool View { get; init; }

        public string? Renderer { get; init; }

        public string? FetchAssetsDirectory { get; init; }

        public int Seconds { get; init; }

        public UsdViewPickMode PickMode { get; init; } = UsdViewPickMode.Auto;

        public bool Mission { get; init; }

        public bool Mcp { get; init; }

        public string CommandPrimPath { get; init; } = "/World/IntentCommand";

        public string? Transport { get; init; }

        public int Port { get; init; } = 5100;

        public static IntentViewerOptions Parse(string[] args)
        {
            return new IntentViewerOptions
            {
                ServerUrl = GetOption(args, "--server") ?? "opc.tcp://localhost:62840/IntentEnabledRobot",
                Insecure = HasFlag(args, "--insecure"),
                View = HasFlag(args, "--view"),
                Renderer = GetOption(args, "--renderer"),
                FetchAssetsDirectory = GetOption(args, "--fetch-assets"),
                Seconds = int.TryParse(
                    GetOption(args, "--seconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                    ? seconds
                    : 0,
                PickMode = TryParsePickMode(GetOption(args, "--pick-mode"), out UsdViewPickMode pickMode)
                    ? pickMode
                    : UsdViewPickMode.Auto,
                Mission = HasFlag(args, "--mission"),
                Mcp = HasFlag(args, "--mcp"),
                CommandPrimPath = GetOption(args, "--command-prim") ?? "/World/IntentCommand",
                Transport = GetOption(args, "--transport"),
                Port = int.TryParse(
                    GetOption(args, "--port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port)
                    ? port
                    : 5100
            };
        }

        private static string? GetOption(string[] args, string name)
        {
            for (int ii = 0; ii < args.Length - 1; ii++)
            {
                if (string.Equals(args[ii], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[ii + 1];
                }
            }
            return null;
        }

        private static bool HasFlag(string[] args, string name)
        {
            return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        }

        internal static bool TryParsePickMode(string? value, out UsdViewPickMode pickMode)
        {
            return Enum.TryParse(value, ignoreCase: true, out pickMode) &&
                pickMode is UsdViewPickMode.Auto or UsdViewPickMode.Renderer or UsdViewPickMode.CommandPrim;
        }

        internal IntentViewerMcpTransportSelection SelectMcpTransport()
        {
            if (Transport is not null &&
                TryParseMcpTransport(Transport, out IntentViewerMcpTransport requestedTransport))
            {
                if (View && requestedTransport == IntentViewerMcpTransport.Stdio)
                {
                    return new IntentViewerMcpTransportSelection(
                        requestedTransport,
                        true,
                        "WARNING: --transport stdio was explicitly requested with --view. " +
                        "MCP stdio uses stdout for protocol frames and the in-process viewer may share that stream; " +
                        "protocol corruption is possible.");
                }

                return new IntentViewerMcpTransportSelection(
                    requestedTransport,
                    true,
                    $"Using explicitly requested MCP transport '{requestedTransport.ToOptionValue()}'.");
            }

            if (View)
            {
                return new IntentViewerMcpTransportSelection(
                    IntentViewerMcpTransport.Http,
                    false,
                    "Using MCP transport 'http' because --view is enabled. MCP stdio frames use stdout, " +
                    "which cannot safely coexist with the in-process OpenUSD viewer.");
            }

            return new IntentViewerMcpTransportSelection(
                IntentViewerMcpTransport.Stdio,
                false,
                "Using default MCP transport 'stdio'.");
        }

        internal static bool TryParseMcpTransport(string? value, out IntentViewerMcpTransport transport)
        {
            if (string.Equals(value, "stdio", StringComparison.OrdinalIgnoreCase))
            {
                transport = IntentViewerMcpTransport.Stdio;
                return true;
            }

            if (string.Equals(value, "http", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "sse", StringComparison.OrdinalIgnoreCase))
            {
                transport = IntentViewerMcpTransport.Http;
                return true;
            }

            transport = IntentViewerMcpTransport.Stdio;
            return false;
        }
    }

    internal sealed record TargetLocation(string PrimPath, NodeId LocationNodeId, string Name);

    internal enum IntentViewerMcpTransport
    {
        Stdio,

        Http
    }

    internal sealed record IntentViewerMcpTransportSelection(
        IntentViewerMcpTransport Transport,
        bool Explicit,
        string Message);

    internal static class IntentViewerMcpTransportExtensions
    {
        public static string ToOptionValue(this IntentViewerMcpTransport transport)
        {
            return transport switch
            {
                IntentViewerMcpTransport.Stdio => "stdio",
                IntentViewerMcpTransport.Http => "http",
                _ => throw new ArgumentOutOfRangeException(nameof(transport), transport, "Unknown MCP transport.")
            };
        }
    }

    internal static partial class Program
    {
        [STAThread]
        public static async Task<int> Main(string[] args)
        {
            var options = IntentViewerOptions.Parse(args);
            if (options.Transport is not null &&
                !IntentViewerOptions.TryParseMcpTransport(options.Transport, out _))
            {
                Console.Error.WriteLine(
                    $"Unknown MCP transport '{options.Transport}'. Valid transports: stdio, http, sse.");
                return 2;
            }

            IntentViewerMcpTransportSelection mcpTransport = options.SelectMcpTransport();
            if (options.Mcp)
            {
#if INTENT_VIEWER_MCP
                Console.Error.WriteLine(mcpTransport.Message);
#else
                Console.Error.WriteLine(
                    "MCP hosting is unavailable for this target framework. Run the sample without --mcp, " +
                    "or use the net8.0, net9.0, or net10.0 target framework for MCP hosting.");
                return 2;
#endif
            }

            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
                builder.SetMinimumLevel(LogLevel.Information));
            ILogger logger = loggerFactory.CreateLogger("IntentViewerClient");
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
            using CancellationTokenSource lifetime = options.Seconds > 0
                ? new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds))
                : new CancellationTokenSource();

            SampleSession sample = await SampleSession.ConnectAsync(options, telemetry, lifetime.Token)
                .ConfigureAwait(false);
            await using (sample.ConfigureAwait(false))
            {
                LogConnected(logger, options.ServerUrl);

                RobotIntentClient intentClient = sample.Session.RobotIntent(telemetry, sample.Streaming);
                ArrayOf<RobotIntentNodeLookupEntry> controllers =
                    await intentClient.DiscoverControllersAsync(lifetime.Token).ConfigureAwait(false);
                if (controllers.Count == 0)
                {
                    Console.Error.WriteLine(
                        "No Robot Intent controllers were advertised at the conformant " +
                        "Server/RobotIntent/Controllers path.");
                    return 2;
                }

                RobotIntentControllerClient controller = intentClient.Controller(controllers[0].NodeId);
                RobotIntentControllerInfo controllerInfo = await controller
                    .ReadAsync(lifetime.Token).ConfigureAwait(false);
                PrintCapabilities(controllerInfo);

                bool commandAllowed = false;
                CommandAuthorityLease? authority = null;
#if INTENT_VIEWER_MCP
                IHost? mcpHost = null;
#endif
                try
                {
                    authority = await controller.RequestAuthorityAsync(lifetime.Token).ConfigureAwait(false);
                    if (authority.Granted)
                    {
                        Console.Error.WriteLine("Command authority: granted for this session.");
                        commandAllowed = true;
                    }
                    else
                    {
                        Console.Error.WriteLine(
                            $"Command authority: held by {authority.CurrentOwner}; submissions may be refused.");
                    }
                }
                catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Console.Error.WriteLine(
                        "Command authority request was denied: the connecting identity lacks the " +
                        "Operator role required by command Methods. Continuing in read-only mode so " +
                        "discovery, capabilities, facets and target mappings remain visible.");
                }

                try
                {
#if INTENT_VIEWER_MCP
                    if (options.Mcp)
                    {
                        mcpHost = await StartMcpHostAsync(
                            mcpTransport, options, sample.Session, lifetime.Token).ConfigureAwait(false);
                    }
#endif

                    var connector = new OpenUsdConnector(sample.Session, new MockUsdSink(), enableCommands: false);
                    await using (connector.ConfigureAwait(false))
                    {
                        IReadOnlyList<TargetLocation> targets = await DiscoverTargetLocationsAsync(
                            connector, controllerInfo, lifetime.Token).ConfigureAwait(false);
                        if (targets.Count == 0)
                        {
                            Console.Error.WriteLine(
                                "No target prim to LocationType mapping was published by OpenUSD bindings.");
                            return 3;
                        }

                        if (options.Mission)
                        {
                            if (commandAllowed)
                            {
                                await SubmitSmallMissionAsync(controller, targets, sample.Session, lifetime.Token)
                                    .ConfigureAwait(false);
                            }
                            else
                            {
                                Console.Error.WriteLine("Mission demo skipped because command authority was not granted.");
                            }
                        }

                        string outPath = PrepareLiveLayerPath(options);
                        if (!string.IsNullOrEmpty(options.FetchAssetsDirectory))
                        {
                            await FetchAssetsAsync(
                                sample.Session, options.FetchAssetsDirectory!, lifetime.Token).ConfigureAwait(false);
                        }

                        targets = await FilterTargetsWithReadablePoseAsync(sample.Session, targets, lifetime.Token)
                            .ConfigureAwait(false);
                        if (targets.Count == 0)
                        {
                            Console.Error.WriteLine(
                                "All published target locations had Pose values that could not be decoded; " +
                                "no commandable targets remain.");
                            return 4;
                        }

                        using var processor = new PickProcessor(
                            controller, sample.Session, targets, logger, commandAllowed);
                        string unavailable = string.Empty;
                        if (options.View && UsdViewHostLoader.TryLoad(out IUsdViewHost? viewHost, out unavailable))
                        {
                            await RunViewportAsync(
                                options, sample.Session, outPath, viewHost!, processor, telemetry, lifetime.Token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            if (options.View)
                            {
                                Console.Error.WriteLine(
                                    $"Viewport unavailable; falling back to headless mode. {unavailable}");
                            }
                            if (options.Mcp && mcpTransport.Transport == IntentViewerMcpTransport.Stdio)
                            {
                                Console.Error.WriteLine(
                                    "Headless keyboard control is disabled while MCP stdio is active because " +
                                    "both would read from standard input. Use --transport http for the headless " +
                                    "menu, or drive the robot through the MCP client.");
#if INTENT_VIEWER_MCP
                                await WaitForMcpServerAsync(mcpHost!, lifetime.Token).ConfigureAwait(false);
#endif
                            }
                            else
                            {
                                await RunHeadlessAsync(
                                    sample.Session, outPath, targets, processor, lifetime.Token).ConfigureAwait(false);
                            }
                        }
                    }
                }
                finally
                {
#if INTENT_VIEWER_MCP
                    if (mcpHost is not null)
                    {
                        await StopMcpHostAsync(mcpHost).ConfigureAwait(false);
                    }
#endif

                    if (authority is not null)
                    {
                        await authority.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            return 0;
        }

        internal static Task<Pose3DDataType> ReadLocationPoseForProcessorAsync(
            ISession session,
            NodeId locationNodeId,
            CancellationToken cancellationToken)
        {
            return ReadLocationPoseAsync(session, locationNodeId, cancellationToken);
        }

#if INTENT_VIEWER_MCP
        private static async Task<IHost> StartMcpHostAsync(
            IntentViewerMcpTransportSelection transport,
            IntentViewerOptions options,
            ISession session,
            CancellationToken cancellationToken)
        {
            IHost host = transport.Transport == IntentViewerMcpTransport.Stdio
                ? BuildStdioMcpHost()
                : BuildHttpMcpHost(options.Port);

            OpcUaSessionManager sessionManager = host.Services.GetRequiredService<OpcUaSessionManager>();
            await sessionManager.RegisterExistingSessionAsync(
                "intent-viewer",
                session,
                "Anonymous",
                cancellationToken).ConfigureAwait(false);

            await host.StartAsync(cancellationToken).ConfigureAwait(false);

            if (transport.Transport == IntentViewerMcpTransport.Http)
            {
                Console.Error.WriteLine(
                    $"MCP server is listening on http://localhost:{options.Port}/mcp with Robotics tools.");
            }
            else
            {
                Console.Error.WriteLine("MCP server is listening on stdio with Robotics tools.");
            }

            return host;
        }

        private static IHost BuildStdioMcpHost()
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureMcpLogging(builder.Logging, useStdioTransport: true);
            ConfigureMcpServices(builder.Services);

            IMcpServerBuilder mcpServerBuilder = builder.Services
                .AddMcpServer()
                .WithStdioServerTransport();
            ConfigureRoboticsMcpTools(mcpServerBuilder);
            return builder.Build();
        }

        private static WebApplication BuildHttpMcpHost(int port)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            ConfigureMcpLogging(builder.Logging, useStdioTransport: false);
            ConfigureMcpServices(builder.Services);

            IMcpServerBuilder mcpServerBuilder = builder.Services
                .AddMcpServer()
                .WithHttpTransport();
            ConfigureRoboticsMcpTools(mcpServerBuilder);

            WebApplication app = builder.Build();
            app.MapMcp("/mcp");
            app.Urls.Add($"http://localhost:{port}");
            return app;
        }

        private static void ConfigureMcpServices(IServiceCollection services)
        {
            services.AddOpcUaMcpCore(new OpcUaMcpOptions { ToolProfile = McpToolProfile.Robotics });
            services.AddOpcUaMcpRobotics();
        }

        private static void ConfigureRoboticsMcpTools(IMcpServerBuilder mcpServerBuilder)
        {
            mcpServerBuilder
                .WithOpcUaMcpFilters()
                .WithOpcUaRoboticsTools(McpToolProfile.Robotics);
        }

        private static void ConfigureMcpLogging(ILoggingBuilder logging, bool useStdioTransport)
        {
            logging.ClearProviders();
            logging.SetMinimumLevel(LogLevel.Information);
            logging.AddSimpleConsole(options =>
            {
                options.UseUtcTimestamp = true;
                options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
            });
            logging.Services.Configure<ConsoleLoggerOptions>(o =>
                o.LogToStandardErrorThreshold = useStdioTransport ? LogLevel.Trace : LogLevel.Error);
        }

        private static async Task WaitForMcpServerAsync(IHost host, CancellationToken cancellationToken)
        {
            try
            {
                await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private static async Task StopMcpHostAsync(IHost host)
        {
            try
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                host.Dispose();
            }
        }
#endif

        private static void PrintCapabilities(RobotIntentControllerInfo info)
        {
            Console.Error.WriteLine($"Controller: {info.BrowseName} ({info.NodeId})");
            Console.Error.WriteLine($"Axis count: {info.AxisCount}; queue depth: {info.MaxQueueDepth}");
            Console.Error.WriteLine("Facets:");
            Console.Error.WriteLine($"  Base: {info.Facets.Base}");
            Console.Error.WriteLine($"  Trajectories: {info.Facets.Trajectories}");
            Console.Error.WriteLine($"  Missions: {info.Facets.Missions}");
            Console.Error.WriteLine($"  Mission branching: {info.Facets.MissionBranching}");
            Console.Error.WriteLine($"  Force control: {info.Facets.ForceControl}");
            Console.Error.WriteLine($"  Real-time channels: {info.Facets.RealTimeChannels}");
            Console.Error.WriteLine($"  Buffer-mode rule conformant: {info.Facets.EveryCapabilitySupportsAborting}");
        }

        private static async Task<IReadOnlyList<TargetLocation>> DiscoverTargetLocationsAsync(
            OpenUsdConnector connector,
            RobotIntentControllerInfo controllerInfo,
            CancellationToken cancellationToken)
        {
            List<OpenUsdConnector.RepresentationInfo> representations = await connector
                .DiscoverAllRepresentationsAsync(cancellationToken).ConfigureAwait(false);
            var locationLookup = new Dictionary<NodeId, RobotIntentNodeLookupEntry>();
            foreach (RobotIntentNodeLookupEntry entry in controllerInfo.Lookups.Locations)
            {
                locationLookup[entry.NodeId] = entry;
            }
            var targets = new List<TargetLocation>();
            foreach (OpenUsdConnector.BindingInfo binding in representations.SelectMany(rep => rep.Bindings))
            {
                if (string.IsNullOrWhiteSpace(binding.PrimPath))
                {
                    continue;
                }
                if (!string.Equals(binding.SourceSemanticId, "RobotIntent.Location", StringComparison.Ordinal) &&
                    !string.Equals(binding.PropertyName, "inputs:robotIntentLocation", StringComparison.Ordinal))
                {
                    continue;
                }
                NodeId nodeId = binding.SourceNodeId;
                if (nodeId.IsNull && !string.IsNullOrWhiteSpace(binding.SourceSemanticId))
                {
                    try
                    {
                        nodeId = NodeId.Parse(binding.SourceSemanticId);
                    }
                    catch (Exception) when (binding.SourceSemanticId is not null)
                    {
                        continue;
                    }
                }
                if (nodeId.IsNull)
                {
                    continue;
                }
                if (targets.Any(t => string.Equals(t.PrimPath, binding.PrimPath, StringComparison.Ordinal)))
                {
                    continue;
                }
                string name = locationLookup.TryGetValue(nodeId, out RobotIntentNodeLookupEntry? entry)
                    ? entry.Name
                    : binding.PrimPath!.Split('/').LastOrDefault(static s => s.Length > 0) ?? binding.PrimPath!;
                targets.Add(new TargetLocation(binding.PrimPath!, nodeId, name));
            }
            return [.. targets.OrderBy(t => t.Name, StringComparer.Ordinal)];
        }

        private static async Task<IReadOnlyList<TargetLocation>> FilterTargetsWithReadablePoseAsync(
            ISession session,
            IReadOnlyList<TargetLocation> targets,
            CancellationToken cancellationToken)
        {
            var readable = new List<TargetLocation>();
            foreach (TargetLocation target in targets)
            {
                try
                {
                    _ = await ReadLocationPoseAsync(
                        session, target.LocationNodeId, cancellationToken).ConfigureAwait(false);
                    readable.Add(target);
                }
                catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUnexpectedError)
                {
                    Console.Error.WriteLine(
                        $"Location {target.Name} ({target.LocationNodeId}) published a Pose value this " +
                        "client could not decode; it will be omitted from the command menu.");
                }
            }
            return readable;
        }

        private static string PrepareLiveLayerPath(IntentViewerOptions options)
        {
            string root = options.FetchAssetsDirectory ?? Path.Combine(GetPrivateStateRoot(), "intent-viewer");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "live.usda");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "#usda 1.0\n(\n    doc = \"OPC UA -> OpenUSD live override layer\"\n)\n");
            }
            return path;
        }

        private static async Task FetchAssetsAsync(
            ISession session,
            string cacheDir,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(cacheDir);
            var fetcher = new OpenUsdConnector(session, new MockUsdSink(), enableCommands: false);
            await using (fetcher.ConfigureAwait(false))
            {
                List<OpenUsdConnector.FetchedAsset> fetched =
                    await fetcher.FetchServedAssetsAsync(cacheDir, cancellationToken)
                    .ConfigureAwait(false);
                if (fetched.Count == 0)
                {
                    Console.Error.WriteLine("Server did not advertise served OpenUSD assets.");
                    return;
                }
                WriteStageUsda(cacheDir, fetched);
                Console.Error.WriteLine($"Fetched {fetched.Count} OpenUSD asset(s) into {cacheDir}.");
            }
        }

        private static async Task RunViewportAsync(
            IntentViewerOptions options,
            ISession session,
            string liveLayerPath,
            IUsdViewHost viewHost,
            PickProcessor processor,
            ITelemetryContext telemetry,
            CancellationToken cancellationToken)
        {
            string cacheDir = options.FetchAssetsDirectory
                ?? Path.GetDirectoryName(liveLayerPath)
                ?? AppContext.BaseDirectory;

            // The viewport needs the served geometry: without it only the live override layer
            // composes, which carries transforms but no geometry and renders as an empty scene.
            await FetchAssetsAsync(session, cacheDir, cancellationToken).ConfigureAwait(false);

            string stagePath = Path.Combine(cacheDir, "stage.usda");
            if (!File.Exists(stagePath))
            {
                Console.Error.WriteLine("No fetched stage.usda exists; viewport will open the live override layer only.");
                stagePath = liveLayerPath;
            }

            var viewOptions = new UsdViewOptions
            {
                StagePath = stagePath,
                Renderer = options.Renderer,
                CameraPath = "/World/Camera",
                Title = "OPC UA Robot Intent Viewer",
                Telemetry = telemetry,
                PickMode = options.PickMode,
                CommandPrimPath = options.CommandPrimPath,
                PrimPicked = processor.ProcessPickAsync
            };
            Console.Error.WriteLine("Opening OpenUSD viewport. Click a target puck to submit a Robot Intent.");
            await RunViewportOnStaThreadAsync(viewHost, viewOptions, session, cancellationToken)
                .ConfigureAwait(false);
        }

        private static Task<bool> RunViewportOnStaThreadAsync(
            IUsdViewHost viewHost,
            UsdViewOptions viewOptions,
            ISession session,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var uiThread = new Thread(() =>
            {
                try
                {
                    viewHost.RunViewport(
                        viewOptions,
                        async (sink, ct) => await StreamOpenUsdAsync(session, sink, ct).ConfigureAwait(false),
                        cancellationToken);
                    completion.TrySetResult(true);
                }
#pragma warning disable CA1031 // Surfaced to the asynchronous caller.
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    completion.TrySetException(exception);
                }
            })
            {
                IsBackground = false,
                Name = "Intent OpenUSD viewport"
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uiThread.SetApartmentState(ApartmentState.STA);
            }
            uiThread.Start();
            return completion.Task;
        }

        private static async Task RunHeadlessAsync(
            ISession session,
            string liveLayerPath,
            IReadOnlyList<TargetLocation> targets,
            PickProcessor processor,
            CancellationToken cancellationToken)
        {
            _ = session;
            _ = liveLayerPath;
            Console.Error.WriteLine("Headless target menu:");
            for (int ii = 0; ii < targets.Count; ii++)
            {
                Console.Error.WriteLine($"  {ii + 1}. {targets[ii].Name} ({targets[ii].PrimPath})");
            }
            bool processed = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                int? index = await ReadTargetIndexAsync(targets.Count, processed, cancellationToken)
                    .ConfigureAwait(false);
                if (!index.HasValue)
                {
                    return;
                }
                processed = true;
                await processor.ProcessPickAsync(targets[index.Value].PrimPath, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        private static async Task<int?> ReadTargetIndexAsync(
            int count,
            bool allowExit,
            CancellationToken cancellationToken)
        {
            Console.Error.Write(allowExit ? "Choose target number, or press Enter to exit: " : "Choose target number: ");
            string? line = await Task.Run(Console.In.ReadLine, cancellationToken).ConfigureAwait(false);
            if (line is null || (allowExit && string.IsNullOrWhiteSpace(line)))
            {
                return null;
            }
            if (int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out int selected) &&
                selected >= 1 &&
                selected <= count)
            {
                return selected - 1;
            }
            if (allowExit)
            {
                Console.Error.WriteLine("No valid selection supplied; exiting headless mode.");
                return null;
            }
            Console.Error.WriteLine("No valid selection supplied; using the first published target for headless automation.");
            return 0;
        }

        private static async Task StreamOpenUsdAsync(
            ISession session,
            IUsdSink sink,
            CancellationToken cancellationToken)
        {
            var connector = new OpenUsdConnector(session, sink, enableCommands: false);
            await using (connector.ConfigureAwait(false))
            {
                await StreamConnectorUntilCancelledAsync(
                    connector.StartAsync,
                    connector.StopAsync,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task StreamConnectorUntilCancelledAsync(
            Func<CancellationToken, Task> startAsync,
            Func<CancellationToken, Task> stopAsync,
            CancellationToken cancellationToken)
        {
            await startAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Viewport shutdown is the expected end of the live stream.
            }
            finally
            {
                await stopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        private static async Task SubmitSmallMissionAsync(
            RobotIntentControllerClient controller,
            IReadOnlyList<TargetLocation> targets,
            ISession session,
            CancellationToken cancellationToken)
        {
            if (targets.Count < 2)
            {
                Console.Error.WriteLine("Mission demo skipped: fewer than two target locations were published.");
                return;
            }
            Pose3DDataType first = await ReadLocationPoseAsync(session, targets[0].LocationNodeId, cancellationToken)
                .ConfigureAwait(false);
            Pose3DDataType second = await ReadLocationPoseAsync(session, targets[1].LocationNodeId, cancellationToken)
                .ConfigureAwait(false);
            MissionDataType mission = RobotIntentBuilder.Mission("viewer-mission")
                .ReleasedStep("base", RobotIntentBuilder.LinearMove(first, 0.25).Build())
                .HorizonStep("horizon", RobotIntentBuilder.LinearMove(second, 0.25).Build())
                .Build();
            MissionSubmissionResult result = await controller.SubmitMissionAsync(mission, cancellationToken)
                .ConfigureAwait(false);
            Console.Error.WriteLine(result.Accepted
                ? $"Mission accepted: {result.MissionId} operation {result.Operation}."
                : $"Mission refused: {result.Failure} {result.Message.Text}");
        }

        private static async Task<Pose3DDataType> ReadLocationPoseAsync(
            ISession session,
            NodeId locationNodeId,
            CancellationToken cancellationToken)
        {
            NodeId poseNodeId = await BrowseChildAsync(
                session, locationNodeId, "Pose", cancellationToken).ConfigureAwait(false);
            DataValue value = await session.ReadValueAsync(poseNodeId, cancellationToken).ConfigureAwait(false);
            Pose3DDataType pose = null!;
            if (value.WrappedValue.TryGetValue(out pose!, session.MessageContext) && pose is not null)
            {
                return pose;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadUnexpectedError, "Location Pose did not contain Pose3DDataType.");
        }

        private static async ValueTask<NodeId> BrowseChildAsync(
            ISession session,
            NodeId parent,
            string browseName,
            CancellationToken cancellationToken)
        {
            ArrayOf<ReferenceDescription> references =
                await BrowseReferencesAsync(session, parent, cancellationToken).ConfigureAwait(false);
            foreach (ReferenceDescription reference in references)
            {
                if (string.Equals(reference.BrowseName.Name, browseName, StringComparison.Ordinal))
                {
                    return ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNodeIdUnknown, "Child {0} was not found on {1}.", browseName, parent);
        }

        private static async Task<ArrayOf<ReferenceDescription>> BrowseReferencesAsync(
            ISession session,
            NodeId parent,
            CancellationToken cancellationToken)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> _) = await session
                .ManagedBrowseAsync(
                    null,
                    null,
                    [parent],
                    0,
                    BrowseDirection.Forward,
                    global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                    includeSubtypes: true,
                    0,
                    cancellationToken).ConfigureAwait(false);
            if (results.Count > 0)
            {
                return results[0];
            }
            return [];
        }

        private static void WriteStageUsda(string cacheDir, List<OpenUsdConnector.FetchedAsset> fetched)
        {
            OpenUsdConnector.FetchedAsset? root = fetched.Find(asset => asset.Kind == OpenUsdAssetKind.RootLayer);
            string rootName = root != null ? Path.GetFileName(root.LocalPath) : "base.usda";
            var builder = new StringBuilder();
            builder.Append("#usda 1.0\n(\n");
            builder.Append(
                "    doc = \"Self-contained OpenUSD stage: server-delivered base layers + live override.\"\n");
            builder.Append("    subLayers = [\n        @./live.usda@,\n        @./")
                .Append(rootName).Append("@\n    ]\n");
            builder.Append(")\n");
            File.WriteAllText(Path.Combine(cacheDir, "stage.usda"), builder.ToString());
            string livePath = Path.Combine(cacheDir, "live.usda");
            if (!File.Exists(livePath))
            {
                File.WriteAllText(livePath, "#usda 1.0\n(\n    doc = \"OPC UA -> OpenUSD live bindings\"\n)\n");
            }
        }

        private static string GetPrivateStateRoot()
        {
            string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = AppContext.BaseDirectory;
            }
            string root = Path.Combine(baseDirectory, "OPC Foundation", "IntentViewerClient");
            Directory.CreateDirectory(root);
            return root;
        }

        [LoggerMessage(EventId = IntentViewerClientEventIds.Connected, Level = LogLevel.Information,
            Message = "Connected to {ServerUrl}.")]
        private static partial void LogConnected(ILogger logger, string serverUrl);
    }
}
