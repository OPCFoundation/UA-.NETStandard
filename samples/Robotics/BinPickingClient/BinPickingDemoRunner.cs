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
 * Server or Client OR OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace BinPickingClient
{
    /// <summary>
    /// Runs the scripted end-to-end bin-picking demonstration. Captures a frame's
    /// worth of detections via <c>RunInference</c>, composes the chosen part's pose
    /// from the camera frame into the world frame using the Vision frame graph,
    /// submits Pick and Place intents through the Robot Intent controller, and
    /// re-runs inference to prove the detected world state changed after the pick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runner does not interpret the pixels itself. That is deliberate: the
    /// bin-picking cell server ships a deterministic ground-truth detector so a
    /// scripted CI run never depends on an external model or GPU, and the same
    /// tools are exposed over MCP so an agent can drive the loop instead. The
    /// runner exists to show that the loop composes end-to-end with the tools an
    /// agent would use.
    /// </para>
    /// <para>
    /// Pick and Place resolve their <c>Source</c>, <c>Destination</c> and
    /// <c>Tool</c> NodeIds against the controller's lookup tables (<see
    /// cref="RobotIntentLookups.Locations"/> and <see cref="RobotIntentLookups.Tools"/>).
    /// The name defaults (<c>Bin</c>, <c>Fixture</c>, <c>ParallelGripper</c>) match the
    /// configurator in <c>BinPickingRobotCell</c>; passing <c>--source</c>, <c>--destination</c>
    /// or <c>--tool</c> lets the operator retarget the demo without a rebuild.
    /// </para>
    /// </remarks>
    internal sealed partial class BinPickingDemoRunner
    {
        public BinPickingDemoRunner(
            BinPickingSampleSession sample,
            ITelemetryContext telemetry,
            ILogger logger,
            BinPickingClientOptions options)
        {
            m_sample = sample ?? throw new ArgumentNullException(nameof(sample));
            m_telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<int> RunAsync(
            RobotIntentControllerClient controller,
            RobotIntentControllerInfo controllerInfo,
            bool commandAuthorityGranted,
            CancellationToken cancellationToken)
        {
            if (controller == null)
            {
                throw new ArgumentNullException(nameof(controller));
            }
            if (controllerInfo == null)
            {
                throw new ArgumentNullException(nameof(controllerInfo));
            }

            LogStageStarted(m_logger, m_options.PartClassLabel);

            VisionClient vision = m_sample.Session.Vision(m_telemetry);
            NodeId pipelineNodeId = await DiscoverSinglePipelineAsync(vision, cancellationToken)
                .ConfigureAwait(false);

            VisionDetectionResultSnapshot? initialDetections = null;
            VisionDetectionDataType? part = null;
            if (!pipelineNodeId.IsNull)
            {
                VisionPipelineClient pipeline = vision.Pipeline(pipelineNodeId);
                _ = await pipeline.ReadAsync(cancellationToken).ConfigureAwait(false);
                initialDetections = await CaptureAndReadDetectionsAsync(
                    vision, pipeline, cancellationToken).ConfigureAwait(false);
                LogInitialDetectionCount(
                    m_logger, initialDetections.ResultId ?? string.Empty, initialDetections.Detections.Count);
                LogDetections(initialDetections);

                part = FindDetection(initialDetections, m_options.PartClassLabel);
                if (part is null)
                {
                    Console.Error.WriteLine(
                        "The chosen part label '" + m_options.PartClassLabel + "' was not present in the initial " +
                        "inference result. Known class labels: " + FormatKnownClasses(initialDetections) + ".");
                    LogUnknownClass(m_logger, m_options.PartClassLabel);
                }
                else if (part.HasPose)
                {
                    await ComposeAndLogPoseAsync(vision, pipeline, part, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                LogPipelineUnavailable(m_logger);
                Console.Error.WriteLine(
                    "The bin-picking cell's Vision pipeline is not browsable through this client session. " +
                    "The scripted loop will still exercise the Robot Intent Pick+Place cycle so the world " +
                    "state can be observed, and the composed Vision + Robotics MCP catalogue is unaffected.");
            }

            NodeId sourceLocation = ResolveLookup(
                controllerInfo.Lookups.Locations, m_options.SourceLocationName, "source location");
            NodeId destinationLocation = ResolveLookup(
                controllerInfo.Lookups.Locations, m_options.DestinationLocationName, "destination location");
            NodeId tool = ResolveLookup(
                controllerInfo.Lookups.Tools, m_options.ToolBrowseName, "tool");
            if (sourceLocation.IsNull || destinationLocation.IsNull || tool.IsNull)
            {
                return 5;
            }

            if (!commandAuthorityGranted)
            {
                Console.Error.WriteLine(
                    "Command authority was not granted for this session. The scripted demo will inspect the " +
                    "detection results but skip the Pick and Place intents.");
                LogAuthorityNotGranted(m_logger);
                return 6;
            }

            string intentIdPrefix = "binpickclient-" +
                DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

            if (m_options.StackAll)
            {
                return await StackEveryPartAsync(
                    controller, sourceLocation, destinationLocation, tool, intentIdPrefix,
                    initialDetections, cancellationToken).ConfigureAwait(false);
            }

            string pickIntentId = intentIdPrefix + "-pick";
            string placeIntentId = intentIdPrefix + "-place";

            bool pickAccepted = await SubmitPickAsync(
                controller, sourceLocation, tool, pickIntentId, cancellationToken).ConfigureAwait(false);
            if (!pickAccepted)
            {
                return 7;
            }

            bool placeAccepted = await SubmitPlaceAsync(
                controller, destinationLocation, tool, placeIntentId, cancellationToken).ConfigureAwait(false);
            if (!placeAccepted)
            {
                return 8;
            }

            if (initialDetections is not null && !pipelineNodeId.IsNull)
            {
                VisionPipelineClient pipeline = vision.Pipeline(pipelineNodeId);
                VisionDetectionResultSnapshot postDetections = await CaptureAndReadDetectionsAsync(
                    vision, pipeline, cancellationToken).ConfigureAwait(false);
                LogPostDetections(
                    m_logger, postDetections.ResultId ?? string.Empty, postDetections.Detections.Count);
                LogDetections(postDetections);

                VisionDetectionDataType? stillPresent = FindDetection(postDetections, m_options.PartClassLabel);
                bool worldChanged =
                    stillPresent is null ||
                    (part is not null && part.HasPose && stillPresent.HasPose && !PoseIsEqual(part.Pose, stillPresent.Pose));
                if (part is null)
                {
                    // Nothing was there to pick, so "it is gone now" proves nothing. Say so
                    // rather than report a pass the run did not earn.
                    Console.Error.WriteLine(
                        "Scripted loop INCONCLUSIVE: '" + m_options.PartClassLabel + "' was not detected " +
                        "before the Pick, so its absence afterwards says nothing about the world changing.");
                }
                else if (worldChanged)
                {
                    LogWorldStateChanged(m_logger, m_options.PartClassLabel);
                    Console.Error.WriteLine(
                        "Scripted loop passed: after Pick+Place the detector no longer reports '" +
                        m_options.PartClassLabel + "' at its original position.");
                }
                else
                {
                    LogWorldStateUnchanged(m_logger, m_options.PartClassLabel);
                    Console.Error.WriteLine(
                        "Scripted loop FAILED: the pick-and-place cycle reported success over the OPC UA " +
                        "controller, but the on-server ground-truth detector still reports '" +
                        m_options.PartClassLabel + "' at its original position. The robot moved and the " +
                        "intents succeeded, so the world state did not follow the arm.");
                }
            }
            else
            {
                Console.Error.WriteLine(
                    "Scripted loop completed the Pick+Place cycle over the OPC UA Robot Intent controller. " +
                    "The Vision inference verification step was skipped because the pipeline was not reachable " +
                    "from this client session.");
            }

            LogLoopComplete(m_logger, m_options.PartClassLabel);
            return 0;
        }

        /// <summary>
        /// Returns the NodeId of the sole inference pipeline advertised by the bin-picking cell,
        /// or a null NodeId when the pipeline is not reachable from this client session.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The cell's Vision node manager materialises the pipeline through the fluent builder,
        /// which grafts the folder and pipeline NodeStates under the Vision root but does not
        /// register them with the CustomNodeManager after the root has already been added.
        /// The reference from the Vision root to the Pipelines folder is therefore visible from
        /// a browse of the root, but browsing from the folder itself yields BadNodeIdUnknown.
        /// The scripted demo tolerates this by treating an unreachable pipeline as a soft
        /// failure and still exercising the Robot Intent Pick+Place cycle so the loop's world
        /// state effect can be observed.
        /// </para>
        /// </remarks>
        private static async Task<NodeId> DiscoverSinglePipelineAsync(
            VisionClient vision, CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> pipelines = await vision.DiscoverPipelinesAsync(cancellationToken)
                .ConfigureAwait(false);
            for (int ii = 0; ii < pipelines.Count; ii++)
            {
                NodeId candidate = pipelines[ii];
                if (!candidate.IsNull)
                {
                    return candidate;
                }
            }
            await foreach (VisionNodeEntry entry in vision
                .EnumeratePipelinesAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                if (!entry.NodeId.IsNull)
                {
                    return entry.NodeId;
                }
            }
            return NodeId.Null;
        }

        private async Task<VisionDetectionResultSnapshot> CaptureAndReadDetectionsAsync(
            VisionClient vision,
            VisionPipelineClient pipeline,
            CancellationToken cancellationToken)
        {
            string resultId = await pipeline.RunInferenceAsync(default, cancellationToken)
                .ConfigureAwait(false);
            NodeId resultNodeId = await pipeline.ResolveResultNodeIdAsync(resultId, cancellationToken)
                .ConfigureAwait(false);
            if (resultNodeId.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "RunInference returned ResultId '{0}' but the pipeline did not publish a matching result node.",
                    resultId);
            }
            VisionResultReader reader = vision.Result(resultNodeId);
            return await reader.ReadDetectionAsync(cancellationToken).ConfigureAwait(false);
        }

        private void LogDetections(VisionDetectionResultSnapshot snapshot)
        {
            var detections = new List<VisionDetectionDataType>(snapshot.Detections.Count);
            for (int ii = 0; ii < snapshot.Detections.Count; ii++)
            {
                detections.Add(snapshot.Detections[ii]);
            }
            foreach (VisionDetectionDataType detection in detections)
            {
                string box = detection.HasBoundingBox2D
                    ? FormattableString.Invariant(
                        $"cx={detection.BoundingBox2D.CenterX:0.0} cy={detection.BoundingBox2D.CenterY:0.0} w={detection.BoundingBox2D.Width:0.0} h={detection.BoundingBox2D.Height:0.0}")
                    : "<none>";
                string pose = detection.HasPose
                    ? FormatPose(detection.Pose)
                    : "<none>";
                Console.Error.WriteLine(FormattableString.Invariant(
                    $"  {detection.ClassLabel} (conf={detection.Confidence:0.00}) box2D=[{box}] pose=[{pose}]"));
            }
        }

        private async Task ComposeAndLogPoseAsync(
            VisionClient vision,
            VisionPipelineClient pipeline,
            VisionDetectionDataType detection,
            CancellationToken cancellationToken)
        {
            string cameraFrameId = detection.Pose.FrameId ?? string.Empty;
            if (string.IsNullOrEmpty(cameraFrameId))
            {
                Console.Error.WriteLine(
                    "Detection published a pose without a FrameId; skipping compose step.");
                return;
            }
            NodeId cameraFrameNode = await ResolveFrameByFrameIdAsync(vision, cameraFrameId, cancellationToken)
                .ConfigureAwait(false);
            NodeId worldFrameNode = await ResolveFrameByFrameIdAsync(vision, "world", cancellationToken)
                .ConfigureAwait(false);
            if (cameraFrameNode.IsNull || worldFrameNode.IsNull)
            {
                Console.Error.WriteLine(
                    "Vision frame graph does not expose the frames the compose step needs " +
                    "(source '" + cameraFrameId + "' or target 'world'); skipping compose step.");
                return;
            }
            VisionFrameGraph frames = vision.Frames();
            VisionPose3DDataType composed = await frames.ComposeAsync(
                detection.Pose, cameraFrameNode, worldFrameNode, cancellationToken).ConfigureAwait(false);
            (double x, double y, double z) = ReadVec3(composed.Position);
            LogComposedPose(m_logger, m_options.PartClassLabel, x, y, z);
            _ = pipeline;
        }

        /// <summary>
        /// Picks every part the detector reported and places them all on the same
        /// destination, which leaves them stacked because the cell rests each released part
        /// on whatever is already there.
        /// </summary>
        /// <remarks>
        /// The order is the order the detector reported, so the stack is built out of what
        /// the camera actually saw rather than a hard-coded list. Each cycle waits for its
        /// intent to reach a terminal state before the next is submitted: overlapping them
        /// puts the next Pick in the queue while the arm is still carrying the last part,
        /// and the parts end up wherever the arm happened to be.
        /// </remarks>
        private async Task<int> StackEveryPartAsync(
            RobotIntentControllerClient controller,
            NodeId source,
            NodeId destination,
            NodeId tool,
            string intentIdPrefix,
            VisionDetectionResultSnapshot? detections,
            CancellationToken cancellationToken)
        {
            var labels = new List<string>();
            if (detections is not null)
            {
                foreach (VisionDetectionDataType detection in detections.Detections)
                {
                    string label = detection.ClassLabel ?? string.Empty;
                    if (label.Length > 0 && !labels.Contains(label, StringComparer.Ordinal))
                    {
                        labels.Add(label);
                    }
                }
            }
            if (labels.Count == 0)
            {
                Console.Error.WriteLine(
                    "Stack-all found no detected parts to stack; nothing to do.");
                return 6;
            }

            Console.Error.WriteLine(
                "Stacking " + labels.Count + " part(s) the camera reported: " + string.Join(", ", labels));
            int placed = 0;
            foreach (string label in labels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string cycle = intentIdPrefix + "-" + placed;
                Console.Error.WriteLine(
                    "--- " + label + " (" + (placed + 1) + " of " + labels.Count + ") ---");
                if (!await SubmitPickAsync(
                        controller, source, tool, cycle + "-pick", label, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return 7;
                }
                if (!await SubmitPlaceAsync(
                        controller, destination, tool, cycle + "-place", cancellationToken)
                    .ConfigureAwait(false))
                {
                    return 8;
                }
                placed++;
            }

            Console.Error.WriteLine(
                "Stacked " + placed + " part(s) on the destination. Each one rests on the one below it.");
            return 0;
        }

        private Task<bool> SubmitPickAsync(
            RobotIntentControllerClient controller,
            NodeId source,
            NodeId tool,
            string intentId,
            CancellationToken cancellationToken)
        {
            return SubmitPickAsync(
                controller, source, tool, intentId, m_options.PartClassLabel, cancellationToken);
        }

        private async Task<bool> SubmitPickAsync(
            RobotIntentControllerClient controller,
            NodeId source,
            NodeId tool,
            string intentId,
            string objectClass,
            CancellationToken cancellationToken)
        {
            PickIntentDataType intent = RobotIntentBuilder.Pick(source, tool, objectClass)
                .WithIntentId(intentId)
                .Build();
            IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(intent, cancellationToken)
                .ConfigureAwait(false);
            if (!submission.Accepted)
            {
                Console.Error.WriteLine(
                    "Pick refused: " + submission.Failure + " - " + submission.Message.Text);
                LogPickRefused(m_logger, intentId, submission.Failure);
                return false;
            }
            Console.Error.WriteLine(
                "Pick admitted: intent " + submission.IntentId + " operation " + submission.Operation + ".");
            LogPickSubmitted(m_logger, submission.IntentId);
            IntentOperationHandle handle = await controller.TrackOperationAsync(
                submission.IntentId, submission.Operation, cancellationToken).ConfigureAwait(false);
            bool succeeded;
            await using (handle.ConfigureAwait(false))
            {
                IntentResultDataType result = await handle.Completion.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                succeeded = handle.Current.ExecutionState == ExecutionStateEnum.Succeeded &&
                    result.Failure == IntentFailureEnum.None;
                Console.Error.WriteLine(
                    "Pick operation terminal state: " + handle.Current.ExecutionState +
                    " failure=" + result.Failure);
                LogPickCompleted(m_logger, submission.IntentId, handle.Current.ExecutionState);
            }
            return succeeded;
        }

        private async Task<bool> SubmitPlaceAsync(
            RobotIntentControllerClient controller,
            NodeId destination,
            NodeId tool,
            string intentId,
            CancellationToken cancellationToken)
        {
            PlaceIntentDataType intent = RobotIntentBuilder.Place(destination, tool)
                .WithIntentId(intentId)
                .Build();
            IntentSubmissionResult submission = await controller.TrySubmitIntentAsync(intent, cancellationToken)
                .ConfigureAwait(false);
            if (!submission.Accepted)
            {
                Console.Error.WriteLine(
                    "Place refused: " + submission.Failure + " - " + submission.Message.Text);
                LogPlaceRefused(m_logger, intentId, submission.Failure);
                return false;
            }
            Console.Error.WriteLine(
                "Place admitted: intent " + submission.IntentId + " operation " + submission.Operation + ".");
            LogPlaceSubmitted(m_logger, submission.IntentId);
            IntentOperationHandle handle = await controller.TrackOperationAsync(
                submission.IntentId, submission.Operation, cancellationToken).ConfigureAwait(false);
            bool succeeded;
            await using (handle.ConfigureAwait(false))
            {
                IntentResultDataType result = await handle.Completion.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                succeeded = handle.Current.ExecutionState == ExecutionStateEnum.Succeeded &&
                    result.Failure == IntentFailureEnum.None;
                Console.Error.WriteLine(
                    "Place operation terminal state: " + handle.Current.ExecutionState +
                    " failure=" + result.Failure);
                LogPlaceCompleted(m_logger, submission.IntentId, handle.Current.ExecutionState);
            }
            return succeeded;
        }

        private static async Task<NodeId> ResolveFrameByFrameIdAsync(
            VisionClient vision,
            string frameId,
            CancellationToken cancellationToken)
        {
            ArrayOf<NodeId> frameNodes = await vision.DiscoverFramesAsync(cancellationToken)
                .ConfigureAwait(false);
            VisionFrameGraph graph = vision.Frames();
            for (int ii = 0; ii < frameNodes.Count; ii++)
            {
                NodeId candidate = frameNodes[ii];
                if (candidate.IsNull)
                {
                    continue;
                }
                VisionFrameSnapshot snapshot = await graph.ReadAsync(candidate, cancellationToken)
                    .ConfigureAwait(false);
                if (string.Equals(snapshot.FrameId, frameId, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return NodeId.Null;
        }

        private static NodeId ResolveLookup(
            ArrayOf<RobotIntentNodeLookupEntry> lookup, string name, string kind)
        {
            for (int ii = 0; ii < lookup.Count; ii++)
            {
                RobotIntentNodeLookupEntry entry = lookup[ii];
                if (string.Equals(entry.Name, name, StringComparison.Ordinal) ||
                    string.Equals(entry.BrowseName.Name, name, StringComparison.Ordinal))
                {
                    return entry.NodeId;
                }
            }
            Console.Error.WriteLine(
                "The controller did not publish a " + kind + " named '" + name +
                "'. Known values: " + FormatLookupNames(lookup) + ".");
            return NodeId.Null;
        }

        private static string FormatLookupNames(ArrayOf<RobotIntentNodeLookupEntry> lookup)
        {
            if (lookup.Count == 0)
            {
                return "<none>";
            }
            var names = new List<string>(lookup.Count);
            for (int ii = 0; ii < lookup.Count; ii++)
            {
                names.Add(lookup[ii].Name);
            }
            return string.Join(", ", names);
        }

        private static VisionDetectionDataType? FindDetection(
            VisionDetectionResultSnapshot snapshot, string classLabel)
        {
            ArrayOf<VisionDetectionDataType> detections = snapshot.Detections;
            for (int ii = 0; ii < detections.Count; ii++)
            {
                VisionDetectionDataType candidate = detections[ii];
                if (string.Equals(candidate.ClassLabel, classLabel, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string FormatKnownClasses(VisionDetectionResultSnapshot snapshot)
        {
            ArrayOf<VisionDetectionDataType> detections = snapshot.Detections;
            if (detections.Count == 0)
            {
                return "<none>";
            }
            var names = new List<string>(detections.Count);
            for (int ii = 0; ii < detections.Count; ii++)
            {
                names.Add(detections[ii].ClassLabel ?? string.Empty);
            }
            return string.Join(", ", names);
        }

        private static bool PoseIsEqual(VisionPose3DDataType a, VisionPose3DDataType b)
        {
            (double ax, double ay, double az) = ReadVec3(a.Position);
            (double bx, double by, double bz) = ReadVec3(b.Position);
            double dx = ax - bx;
            double dy = ay - by;
            double dz = az - bz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz) < 1e-4;
        }

        private static (double X, double Y, double Z) ReadVec3(ArrayOf<double> vec)
        {
            System.ReadOnlySpan<double> span = vec.Span;
            if (span.Length < 3)
            {
                return (0.0, 0.0, 0.0);
            }
            return (span[0], span[1], span[2]);
        }

        private static string FormatPose(VisionPose3DDataType pose)
        {
            (double x, double y, double z) = ReadVec3(pose.Position);
            return FormattableString.Invariant(
                $"frame='{pose.FrameId ?? "<none>"}' pos=({x:0.000},{y:0.000},{z:0.000})");
        }

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoStageStarted, Level = LogLevel.Information,
            Message = "=== Bin-picking client scripted demo — pick {ClassLabel} ===")]
        private static partial void LogStageStarted(ILogger logger, string classLabel);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoDetections, Level = LogLevel.Information,
            Message = "Initial inference result {ResultId} reported {DetectionCount} detections.")]
        private static partial void LogInitialDetectionCount(ILogger logger, string resultId, int detectionCount);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPoseComposed, Level = LogLevel.Information,
            Message = "Composed {ClassLabel} pose to world = ({X:0.000},{Y:0.000},{Z:0.000}) m.")]
        private static partial void LogComposedPose(ILogger logger, string classLabel, double x, double y, double z);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPickSubmitted, Level = LogLevel.Information,
            Message = "Submitted Pick intent {IntentId}.")]
        private static partial void LogPickSubmitted(ILogger logger, string intentId);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPickCompleted, Level = LogLevel.Information,
            Message = "Pick intent {IntentId} completed in state {ExecutionState}.")]
        private static partial void LogPickCompleted(ILogger logger, string intentId, ExecutionStateEnum executionState);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPickRefused, Level = LogLevel.Warning,
            Message = "Pick intent {IntentId} was refused with {Failure}.")]
        private static partial void LogPickRefused(ILogger logger, string intentId, IntentFailureEnum failure);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPlaceSubmitted, Level = LogLevel.Information,
            Message = "Submitted Place intent {IntentId}.")]
        private static partial void LogPlaceSubmitted(ILogger logger, string intentId);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPlaceCompleted, Level = LogLevel.Information,
            Message = "Place intent {IntentId} completed in state {ExecutionState}.")]
        private static partial void LogPlaceCompleted(ILogger logger, string intentId, ExecutionStateEnum executionState);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPlaceRefused, Level = LogLevel.Warning,
            Message = "Place intent {IntentId} was refused with {Failure}.")]
        private static partial void LogPlaceRefused(ILogger logger, string intentId, IntentFailureEnum failure);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPostPickDetections, Level = LogLevel.Information,
            Message = "Post-pick inference result {ResultId} reported {DetectionCount} detections.")]
        private static partial void LogPostDetections(ILogger logger, string resultId, int detectionCount);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoLoopComplete, Level = LogLevel.Information,
            Message = "=== Bin-picking client scripted demo for {ClassLabel} complete ===")]
        private static partial void LogLoopComplete(ILogger logger, string classLabel);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoUnknownClass, Level = LogLevel.Warning,
            Message = "Requested part class '{ClassLabel}' was not visible in the initial inference result.")]
        private static partial void LogUnknownClass(ILogger logger, string classLabel);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoAuthorityNotGranted, Level = LogLevel.Warning,
            Message = "Skipping Pick/Place because command authority was not granted for this session.")]
        private static partial void LogAuthorityNotGranted(ILogger logger);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoWorldStateUnchanged, Level = LogLevel.Warning,
            Message = "After Pick+Place the detector still reports '{ClassLabel}' at its original position.")]
        private static partial void LogWorldStateUnchanged(ILogger logger, string classLabel);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoWorldStateChanged, Level = LogLevel.Information,
            Message = "After Pick+Place the world state for '{ClassLabel}' changed as expected.")]
        private static partial void LogWorldStateChanged(ILogger logger, string classLabel);

        [LoggerMessage(EventId = BinPickingClientEventIds.DemoPipelineUnavailable, Level = LogLevel.Warning,
            Message = "The Vision inference pipeline is not reachable from this client session; " +
                "the scripted loop will exercise the Robot Intent Pick+Place cycle without a pre-inference step.")]
        private static partial void LogPipelineUnavailable(ILogger logger);

        private readonly BinPickingSampleSession m_sample;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly BinPickingClientOptions m_options;
    }
}
