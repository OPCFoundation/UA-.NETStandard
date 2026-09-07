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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
#if BINPICKING_CLIENT_MCP
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#endif
using Microsoft.Extensions.Logging;
#if BINPICKING_CLIENT_MCP
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.Server;
#endif
using Opc.Ua;
using Opc.Ua.Client;
#if BINPICKING_CLIENT_MCP
using Opc.Ua.Mcp;
#endif
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.Robotics.Client;
using Opc.Ua.Robotics.Client.Intent;

namespace BinPickingClient
{
    /// <summary>
    /// Bin-picking client entry point. Connects to the bin-picking cell server, exposes
    /// the composed Vision + Robotics MCP catalogue for an external agent, optionally
    /// opens the OpenUSD viewport so a human can watch the arm move, and can run the
    /// scripted pick-and-place demonstration end to end.
    /// </summary>
    internal static partial class Program
    {
        [STAThread]
        public static async Task<int> Main(string[] args)
        {
            BinPickingClientOptions options = BinPickingClientOptions.Parse(args);
            if (options.Transport is not null &&
                !BinPickingClientOptions.TryParseMcpTransport(options.Transport, out _))
            {
                Console.Error.WriteLine(
                    $"Unknown MCP transport '{options.Transport}'. Valid transports: stdio, http, sse.");
                return 2;
            }

            BinPickingClientMcpTransportSelection mcpTransport = options.SelectMcpTransport();
            if (options.Mcp)
            {
#if BINPICKING_CLIENT_MCP
                Console.Error.WriteLine(mcpTransport.Message);
#else
                Console.Error.WriteLine(
                    "MCP hosting is unavailable for this target framework. Run the sample without --mcp, " +
                    "or use the net8.0, net9.0, or net10.0 target framework for MCP hosting.");
                return 2;
#endif
            }

            // A console provider, or the sample's own logging goes nowhere: the OpenUSD
            // connector reports what it bound and any target it had to leave unresolved,
            // and a live stream that silently binds nothing looks exactly like one that
            // works. Errors go to stderr so stdout stays clean for MCP stdio transport.
            LogLevel minimumLevel = options.Verbose ? LogLevel.Debug : LogLevel.Information;
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
                .SetMinimumLevel(minimumLevel)
                .AddFilter(level => level >= minimumLevel)
                .AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace));
            ILogger logger = loggerFactory.CreateLogger("BinPickingClient");
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder
                .SetMinimumLevel(minimumLevel)
                .AddFilter(level => level >= minimumLevel)
                .AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace));
            using CancellationTokenSource lifetime = options.Seconds > 0
                ? new CancellationTokenSource(TimeSpan.FromSeconds(options.Seconds))
                : new CancellationTokenSource();

            BinPickingSampleSession sample = await BinPickingSampleSession.ConnectAsync(
                options, telemetry, lifetime.Token).ConfigureAwait(false);
            await using (sample.ConfigureAwait(false))
            {
                LogConnected(logger, options.ServerUrl);

                RobotIntentClient intentClient = sample.Session.RobotIntent(telemetry, sample.Streaming);
                ArrayOf<RobotIntentNodeLookupEntry> controllers =
                    await intentClient.DiscoverControllersAsync(lifetime.Token).ConfigureAwait(false);
                if (controllers.Count == 0)
                {
                    Console.Error.WriteLine(
                        "No Robot Intent controllers were advertised at the conformant Server/RobotIntent/Controllers path.");
                    return 2;
                }
                RobotIntentControllerClient controller = intentClient.Controller(controllers[0].NodeId);
                RobotIntentControllerInfo controllerInfo = await controller
                    .ReadAsync(lifetime.Token).ConfigureAwait(false);
                string controllerName = string.IsNullOrEmpty(controllerInfo.BrowseName.Name)
                    ? controllers[0].BrowseName.Name ?? "(unnamed)"
                    : controllerInfo.BrowseName.Name;
                Console.Error.WriteLine(
                    $"Controller: {controllerName} ({controllerInfo.NodeId})");

                bool commandGranted = false;
                CommandAuthorityLease? authority = null;
#if BINPICKING_CLIENT_MCP
                IHost? mcpHost = null;
#endif

                // Only take command authority when this process is going to command the
                // robot: the scripted demo, or an MCP host driving the cell through this
                // session. A viewer is an observer, and taking an exclusive lease just to
                // watch locks out the very agent the sample exists to serve - an agent on
                // its own MCP session gets every intent refused while a window is open.
                bool willCommand = options.Demo || options.Mcp;
                if (willCommand)
                {
                    try
                    {
                        authority = await controller.RequestAuthorityAsync(lifetime.Token).ConfigureAwait(false);
                        if (authority.Granted)
                        {
                            Console.Error.WriteLine("Command authority: granted for this session.");
                            commandGranted = true;
                        }
                        else
                        {
                            Console.Error.WriteLine(
                                $"Command authority: held by {authority.CurrentOwner}; submissions may be refused.");
                        }
                    }
                    catch (ServiceResultException exception)
                        when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
                    {
                        Console.Error.WriteLine(
                            "Command authority request was denied: the connecting identity lacks the Operator role. " +
                            "Continuing in read-only mode so vision inference and MCP tool discovery remain visible.");
                    }
                }
                else
                {
                    Console.Error.WriteLine(
                        "Command authority: not requested - this session only observes, so another client " +
                        "or an agent can command the cell while the viewport is open.");
                }

                int exitCode = 0;
                try
                {
#if BINPICKING_CLIENT_MCP
                    if (options.Mcp)
                    {
                        mcpHost = await StartMcpHostAsync(
                            mcpTransport, options, sample.Session, logger, lifetime.Token).ConfigureAwait(false);
                    }
#endif

                    if (options.Demo && options.View)
                    {
                        // Order matters: the scripted loop is over in seconds, so running it
                        // before the viewport opens leaves nothing to watch - the arm has already
                        // parked by the time the window appears. Open the viewport, wait for the
                        // live stream to be subscribed, and only then command the robot.
                        var streamReady = new TaskCompletionSource<bool>(
                            TaskCreationOptions.RunContinuationsAsynchronously);
                        Task<bool> viewport = RunViewportIfAvailableAsync(
                            sample, options, telemetry, streamReady, lifetime.Token);
                        await WaitForLiveStreamAsync(streamReady, viewport, lifetime.Token).ConfigureAwait(false);
                        var runner = new BinPickingDemoRunner(sample, telemetry, logger, options);
                        exitCode = await runner.RunAsync(
                            controller, controllerInfo, commandGranted, lifetime.Token).ConfigureAwait(false);
                        Console.Error.WriteLine(
                            "Scripted loop finished; the viewport stays open so the cell can still be " +
                            "inspected. Close the window to exit.");
                        _ = await viewport.ConfigureAwait(false);
                    }
                    else if (options.Demo)
                    {
                        var runner = new BinPickingDemoRunner(sample, telemetry, logger, options);
                        exitCode = await runner.RunAsync(
                            controller, controllerInfo, commandGranted, lifetime.Token).ConfigureAwait(false);
                    }
                    else if (options.View)
                    {
                        bool closedByUser = await RunViewportIfAvailableAsync(
                            sample, options, telemetry, null, lifetime.Token).ConfigureAwait(false);
#if BINPICKING_CLIENT_MCP
                        if (!closedByUser && options.Mcp && !options.Demo)
                        {
                            // The viewer never opened, or it failed. An agent is still driving the
                            // cell over MCP, so keep serving instead of exiting underneath it.
                            Console.Error.WriteLine(
                                "MCP server still running without a viewport; connect an MCP client to drive " +
                                "the cell. Press Ctrl+C to exit.");
                            await WaitForMcpServerAsync(mcpHost!, lifetime.Token).ConfigureAwait(false);
                        }
#endif
                    }
                    else if (options.Mcp && !options.Demo)
                    {
#if BINPICKING_CLIENT_MCP
                        Console.Error.WriteLine(
                            "MCP server running; connect an MCP client to drive the cell. Press Ctrl+C to exit.");
                        await WaitForMcpServerAsync(mcpHost!, lifetime.Token).ConfigureAwait(false);
#endif
                    }
                    else if (!options.Mcp && !options.Demo && !options.View)
                    {
                        Console.Error.WriteLine(
                            "Nothing to do: no --mcp, --demo, or --view supplied. Connected and read the controller " +
                            "capabilities to prove the session is healthy; exiting.");
                    }
                }
                finally
                {
#if BINPICKING_CLIENT_MCP
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

                return exitCode;
            }
        }

        /// <summary>
        /// Waits until the live OpenUSD stream is subscribed, so a caller can command motion
        /// that the viewport will actually show. Gives up if the viewport ends first (it is
        /// optional and may be unavailable) or after a short grace period, because a demo that
        /// cannot be watched is still better than one that never runs.
        /// </summary>
        private static async Task WaitForLiveStreamAsync(
            TaskCompletionSource<bool> streamReady,
            Task<bool> viewport,
            CancellationToken cancellationToken)
        {
            Task completed = await Task.WhenAny(
                streamReady.Task,
                viewport,
                Task.Delay(TimeSpan.FromSeconds(30), cancellationToken)).ConfigureAwait(false);
            if (completed == streamReady.Task)
            {
                // Let the first subscription values land before commanding, so the opening
                // frame shows the cell at rest rather than mid-motion.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task<bool> RunViewportIfAvailableAsync(
            BinPickingSampleSession sample,
            BinPickingClientOptions options,
            ITelemetryContext telemetry,
            TaskCompletionSource<bool>? streamReady,
            CancellationToken cancellationToken)
        {
            if (!UsdViewHostLoader.TryLoad(out IUsdViewHost? viewHost, out string reason))
            {
                Console.Error.WriteLine(
                    "Viewport unavailable; the sample continues without a viewer. " + reason);
                streamReady?.TrySetResult(false);
                return false;
            }

            string liveLayerPath = PrepareLiveLayerPath(options);
            string cacheDir = options.FetchAssetsDirectory
                ?? Path.GetDirectoryName(liveLayerPath)
                ?? AppContext.BaseDirectory;

            string stagePath = Path.Combine(cacheDir, "stage.usda");

            // The viewport needs the served geometry: without it only the live override layer
            // composes, which carries transforms but no geometry and renders as an empty scene.
            await FetchAssetsAsync(sample.Session, cacheDir, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(stagePath))
            {
                Console.Error.WriteLine(
                    "No fetched stage.usda exists; viewport will open the live override layer only.");
                stagePath = liveLayerPath;
            }

            var viewOptions = new UsdViewOptions
            {
                StagePath = stagePath,
                Renderer = options.Renderer,

                // Defaults to the stage's fixed observer camera, which shows the cell
                // working; --camera auto hands framing back to the viewer. Do not point
                // this at /World/Robot/.../Camera by default - that is the eye-in-hand
                // sensor on the flange, so it shows what the tool sees, not the cell.
                CameraPath = options.CameraPath,
                Title = "OPC UA Bin-picking Viewer",
                Telemetry = telemetry
            };
            Console.Error.WriteLine("Opening OpenUSD viewport for the bin-picking cell.");
            Console.Error.WriteLine(options.CameraPath is { Length: > 0 } cameraPath
                ? $"Opening on the stage camera {cameraPath}."
                : "No stage camera requested; the viewport frames the scene itself.");
            try
            {
                await RunViewportOnStaThreadAsync(
                    viewHost!, viewOptions, sample.Session, streamReady, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }
#pragma warning disable CA1031 // The viewer is a third-party UI; no exception from it should end the session.
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                // The viewport is optional. A failure inside the renderer or its window code must
                // not take down an MCP session an agent is driving, so report it and carry on.
                Console.Error.WriteLine(
                    "The OpenUSD viewport ended with an error; the sample continues without a viewer. " +
                    exception.Message);
                return false;
            }
        }

        private static Task<bool> RunViewportOnStaThreadAsync(
            IUsdViewHost viewHost,
            UsdViewOptions viewOptions,
            ISession session,
            TaskCompletionSource<bool>? streamReady,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var uiThread = new Thread(() =>
            {
                try
                {
                    viewHost.RunViewport(
                        viewOptions,
                        async (sink, ct) => await StreamOpenUsdAsync(
                                session, sink, viewOptions.Telemetry, streamReady, ct)
                            .ConfigureAwait(false),
                        cancellationToken);
                    completion.TrySetResult(true);
                }
#pragma warning disable CA1031
                catch (Exception exception)
#pragma warning restore CA1031
                {
                    completion.TrySetException(exception);
                }
            })
            {
                IsBackground = false,
                Name = "Bin-picking OpenUSD viewport"
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                uiThread.SetApartmentState(ApartmentState.STA);
            }
            uiThread.Start();
            return completion.Task;
        }

        private static async Task StreamOpenUsdAsync(
            ISession session,
            IUsdSink sink,
            ITelemetryContext? telemetry,
            TaskCompletionSource<bool>? streamReady,
            CancellationToken cancellationToken)
        {
            // Thread telemetry in: without it the connector logs to NullLogger, so a live
            // stream that binds nothing, or that leaves every target unresolved, looks
            // exactly like one that is working.
            var connector = new OpenUsdConnector(
                session, sink, new OpenUsdConnectorOptions { EnableCommands = false }, telemetry);
            await using (connector.ConfigureAwait(false))
            {
                await connector.StartAsync(cancellationToken).ConfigureAwait(false);
                Console.Error.WriteLine("Live OpenUSD stream started; the viewport now follows the cell.");
                streamReady?.TrySetResult(true);
                try
                {
                    await PumpWhileViewportIsOpenAsync(session, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Viewport shutdown ends the live stream.
                }
                finally
                {
                    Console.Error.WriteLine("Live OpenUSD stream stopping.");
                    await connector.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Keeps the client doing work for as long as the viewport is open.
        /// </summary>
        /// <remarks>
        /// This used to be a single infinite delay, which is the natural thing to write:
        /// the connector's subscriptions push updates, so the host has nothing else to do.
        /// It also stops the viewport updating. Measured against this cell, an idle host
        /// applies no stage updates at all while a busy one applies them normally
        /// (openusd-dotnet issue 17), so a viewer that only watches shows a frozen scene
        /// while the robot is demonstrably moving - which is exactly the case when an
        /// agent, rather than this client, is driving the cell. Reading the controller on
        /// a timer keeps the host doing something between updates. The read is a real
        /// one so this is a poll rather than a spin, and it is cheap next to rendering.
        /// </remarks>
        private static async Task PumpWhileViewportIsOpenAsync(
            ISession session,
            CancellationToken cancellationToken)
        {
            var serverStatus = new ReadValueId
            {
                NodeId = global::Opc.Ua.VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value
            };
            ArrayOf<ReadValueId> nodesToRead = [serverStatus];
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    _ = await session.ReadAsync(
                        null, 0, TimestampsToReturn.Neither, nodesToRead, cancellationToken)
                        .ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A read failure must not close a viewport the user is watching.
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
                {
                    // A dropped read is the reconnect handler's business, not the viewer's.
                }
                await Task.Delay(ViewportPumpInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task FetchAssetsAsync(
            ISession session, string cacheDir, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(cacheDir);
            var fetcher = new OpenUsdConnector(session, new MockUsdSink(), enableCommands: false);
            await using (fetcher.ConfigureAwait(false))
            {
                System.Collections.Generic.List<OpenUsdConnector.FetchedAsset> fetched =
                    await fetcher.FetchServedAssetsAsync(cacheDir, cancellationToken).ConfigureAwait(false);
                if (fetched.Count == 0)
                {
                    Console.Error.WriteLine(
                        "Server did not advertise served OpenUSD assets; viewport will use the live override layer only.");
                    return;
                }
                Console.Error.WriteLine(
                    $"Fetched {fetched.Count} OpenUSD asset(s) into {cacheDir}.");
            }
        }

        private static string PrepareLiveLayerPath(BinPickingClientOptions options)
        {
            string root = options.FetchAssetsDirectory ?? Path.Combine(GetPrivateStateRoot(), "bin-picking");
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "live.usda");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "#usda 1.0\n(\n    doc = \"OPC UA -> OpenUSD live override layer\"\n)\n");
            }
            return path;
        }

        private static string GetPrivateStateRoot()
        {
            string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDirectory))
            {
                baseDirectory = AppContext.BaseDirectory;
            }
            string root = Path.Combine(baseDirectory, "OPC Foundation", "BinPickingClient");
            Directory.CreateDirectory(root);
            return root;
        }

#if BINPICKING_CLIENT_MCP
        private static async Task<IHost> StartMcpHostAsync(
            BinPickingClientMcpTransportSelection transport,
            BinPickingClientOptions options,
            ISession session,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            IHost host = transport.Transport == BinPickingClientMcpTransport.Stdio
                ? BuildStdioMcpHost()
                : BuildHttpMcpHost(options.Port);

            OpcUaSessionManager sessionManager = host.Services.GetRequiredService<OpcUaSessionManager>();
            await sessionManager.RegisterExistingSessionAsync(
                "bin-picking", session, "Anonymous", cancellationToken).ConfigureAwait(false);

            int toolCount = host.Services.GetServices<McpServerTool>().Count();
            LogMcpCatalogueSize(logger, toolCount);
            Console.Error.WriteLine(
                $"MCP catalogue exposes {toolCount} tools (Vision + Robotics + Connection).");

            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            string transportName = transport.Transport.ToOptionValue();
            LogMcpHostStarted(logger, transportName);
            if (transport.Transport == BinPickingClientMcpTransport.Http)
            {
                Console.Error.WriteLine(
                    $"MCP server is listening on http://localhost:{options.Port}/mcp with Vision + Robotics tools.");
            }
            else
            {
                Console.Error.WriteLine("MCP server is listening on stdio with Vision + Robotics tools.");
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
            ConfigureVisionAndRoboticsTools(mcpServerBuilder);
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
            ConfigureVisionAndRoboticsTools(mcpServerBuilder);

            WebApplication app = builder.Build();
            app.MapMcp("/mcp");
            app.Urls.Add($"http://localhost:{port}");
            return app;
        }

        private static void ConfigureMcpServices(IServiceCollection services)
        {
            McpToolProfileSet profiles = new McpToolProfileSet(McpToolProfile.Vision).With(McpToolProfile.Robotics);
            services.AddOpcUaMcpCore(new OpcUaMcpOptions { ToolProfiles = profiles });
            services.AddOpcUaMcpVision();
            services.AddOpcUaMcpRobotics();
        }

        private static void ConfigureVisionAndRoboticsTools(IMcpServerBuilder mcpServerBuilder)
        {
            McpToolProfileSet profiles = new McpToolProfileSet(McpToolProfile.Vision).With(McpToolProfile.Robotics);
            mcpServerBuilder
                .WithOpcUaMcpFilters()
                .WithOpcUaCoreTools(profiles)
                .WithOpcUaVisionTools(profiles)
                .WithOpcUaRoboticsTools(profiles);
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
                // Ctrl+C or --seconds is the expected way for the host to stop.
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

        [LoggerMessage(EventId = BinPickingClientEventIds.Connected, Level = LogLevel.Information,
            Message = "Connected to bin-picking cell at {ServerUrl}.")]
        private static partial void LogConnected(ILogger logger, string serverUrl);

#if BINPICKING_CLIENT_MCP
        [LoggerMessage(EventId = BinPickingClientEventIds.McpCatalogueSize, Level = LogLevel.Information,
            Message = "MCP catalogue registered with {ToolCount} tools.")]
        private static partial void LogMcpCatalogueSize(ILogger logger, int toolCount);

        [LoggerMessage(EventId = BinPickingClientEventIds.McpHostStarted, Level = LogLevel.Information,
            Message = "MCP host started with transport {Transport}.")]
        private static partial void LogMcpHostStarted(ILogger logger, string transport);
#endif

        /// <summary>
        /// How often a watching client reads the server while the viewport is open. Fast
        /// enough that the host is never idle for long, slow enough to stay a poll.
        /// </summary>
        private static readonly TimeSpan ViewportPumpInterval = TimeSpan.FromMilliseconds(50);
    }
}
