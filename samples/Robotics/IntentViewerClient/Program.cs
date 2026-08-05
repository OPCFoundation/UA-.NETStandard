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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.OpenUsd.Client;
using Opc.Ua.Robotics.Client;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace IntentViewerClient
{
    internal sealed record IntentViewerOptions
    {
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62840/MinimalIntentRobotServer";

        public bool Insecure { get; init; }

        public bool View { get; init; }

        public string? Renderer { get; init; }

        public string? FetchAssetsDirectory { get; init; }

        public int Seconds { get; init; }

        public UsdViewPickMode PickMode { get; init; } = UsdViewPickMode.Auto;

        public bool Mission { get; init; }

        public string CommandPrimPath { get; init; } = "/World/IntentCommand";

        public static IntentViewerOptions Parse(string[] args)
        {
            return new IntentViewerOptions
            {
                ServerUrl = GetOption(args, "--server") ?? "opc.tcp://localhost:62840/MinimalIntentRobotServer",
                Insecure = HasFlag(args, "--insecure"),
                View = HasFlag(args, "--view"),
                Renderer = GetOption(args, "--renderer"),
                FetchAssetsDirectory = GetOption(args, "--fetch-assets"),
                Seconds = int.TryParse(
                    GetOption(args, "--seconds"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                    ? seconds
                    : 0,
                PickMode = Enum.TryParse(GetOption(args, "--pick-mode"), ignoreCase: true, out UsdViewPickMode pickMode)
                    ? pickMode
                    : UsdViewPickMode.Auto,
                Mission = HasFlag(args, "--mission"),
                CommandPrimPath = GetOption(args, "--command-prim") ?? "/World/IntentCommand"
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
    }

    internal sealed record TargetLocation(string PrimPath, NodeId LocationNodeId, string Name);

    internal static partial class Program
    {
        [STAThread]
        public static async Task<int> Main(string[] args)
        {
            var options = IntentViewerOptions.Parse(args);
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

                CommandAuthorityLease? authority = null;
                bool commandAllowed = false;
                try
                {
                    authority = await controller.RequestAuthorityAsync(lifetime.Token).ConfigureAwait(false);
                    if (authority.Granted)
                    {
                        Console.WriteLine("Command authority: granted for this session.");
                        commandAllowed = true;
                    }
                    else
                    {
                        Console.WriteLine(
                            $"Command authority: held by {authority.CurrentOwner}; submissions may be refused.");
                    }
                }
                catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUserAccessDenied)
                {
                    Console.WriteLine(
                        "Command authority request was denied: the connecting identity lacks the " +
                        "Operator role required by command Methods. Continuing in read-only mode so " +
                        "discovery, capabilities, facets and target mappings remain visible.");
                }

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
                            Console.WriteLine("Mission demo skipped because command authority was not granted.");
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

                    var processor = new PickProcessor(controller, sample.Session, targets, logger, commandAllowed);
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
                            Console.WriteLine($"Viewport unavailable; falling back to headless mode. {unavailable}");
                        }
                        await RunHeadlessAsync(
                            sample.Session, outPath, targets, processor, lifetime.Token).ConfigureAwait(false);
                    }
                }

                if (authority is not null)
                {
                    await authority.DisposeAsync().ConfigureAwait(false);
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

        private static void PrintCapabilities(RobotIntentControllerInfo info)
        {
            Console.WriteLine($"Controller: {info.BrowseName} ({info.NodeId})");
            Console.WriteLine($"Axis count: {info.AxisCount}; queue depth: {info.MaxQueueDepth}");
            Console.WriteLine("Facets:");
            Console.WriteLine($"  Base: {info.Facets.Base}");
            Console.WriteLine($"  Trajectories: {info.Facets.Trajectories}");
            Console.WriteLine($"  Missions: {info.Facets.Missions}");
            Console.WriteLine($"  Mission branching: {info.Facets.MissionBranching}");
            Console.WriteLine($"  Force control: {info.Facets.ForceControl}");
            Console.WriteLine($"  Real-time channels: {info.Facets.RealTimeChannels}");
            Console.WriteLine($"  Buffer-mode rule conformant: {info.Facets.EveryCapabilitySupportsAborting}");
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
                    Console.WriteLine(
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
                    Console.WriteLine("Server did not advertise served OpenUSD assets.");
                    return;
                }
                WriteStageUsda(cacheDir, fetched);
                Console.WriteLine($"Fetched {fetched.Count} OpenUSD asset(s) into {cacheDir}.");
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
            string stagePath = Path.Combine(cacheDir, "stage.usda");
            if (!File.Exists(stagePath))
            {
                Console.WriteLine("No fetched stage.usda exists; viewport will open the live override layer only.");
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
            Console.WriteLine("Opening OpenUSD viewport. Click a target puck to submit a Robot Intent.");
            viewHost.RunViewport(
                viewOptions,
                async (sink, ct) => await StreamOpenUsdAsync(session, sink, ct).ConfigureAwait(false),
                cancellationToken);
            await Task.CompletedTask.ConfigureAwait(false);
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
            Console.WriteLine("Headless target menu:");
            for (int ii = 0; ii < targets.Count; ii++)
            {
                Console.WriteLine($"  {ii + 1}. {targets[ii].Name} ({targets[ii].PrimPath})");
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
            Console.Write(allowExit ? "Choose target number, or press Enter to exit: " : "Choose target number: ");
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
                Console.WriteLine("No valid selection supplied; exiting headless mode.");
                return null;
            }
            Console.WriteLine("No valid selection supplied; using the first published target for headless automation.");
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
                await connector.StartAsync(cancellationToken).ConfigureAwait(false);
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
                Console.WriteLine("Mission demo skipped: fewer than two target locations were published.");
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
            Console.WriteLine(result.Accepted
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
