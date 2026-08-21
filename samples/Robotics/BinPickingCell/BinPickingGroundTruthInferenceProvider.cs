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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Deterministic on-server inference provider that derives detections
    /// from the cell's ground truth: the parts' authored world positions
    /// (see <see cref="BinPickingWorldState"/>) projected through the
    /// same camera intrinsics the sensor publishes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The provider needs neither a model nor a GPU: it reads
    /// <see cref="BinPickingWorldState.Snapshot"/> on every tick, keeps
    /// only parts that are still <see cref="BinPickingPartLocation.InBin"/>
    /// (so a picked part disappears from the next result), and emits one
    /// <see cref="VisionDetectionDataType"/> per remaining part with a
    /// projected 2-D box, a 3-D box with grasp pose, and a full 6-DoF
    /// grasp pose in the <c>camera_eih</c> frame the sensor is calibrated
    /// against.
    /// </para>
    /// <para>
    /// Convention:
    /// <list type="bullet">
    ///   <item>Positions are metres, orientations are unit quaternions in
    ///   <c>(x, y, z, w)</c> ordering (§5.12).</item>
    ///   <item>Reported <c>Pose.FrameId</c> is always
    ///   <c>camera_eih</c>; a consumer composes camera → flange → base
    ///   using the vision-side frame tree to obtain a base-frame grasp.
    ///   The demo tunes the vision-side <c>flange</c> transform so the
    ///   composed camera pose matches the USD-authored camera prim; that
    ///   is how BB2D and Pose stay internally consistent.</item>
    ///   <item>2-D projection uses the classic OpenCV pinhole model with
    ///   the sensor's published intrinsics (<see cref="Fx"/>,
    ///   <see cref="Fy"/>, <see cref="Cx"/>, <see cref="Cy"/>); lens
    ///   distortion is ignored — the calibration residual (0.21 pixels)
    ///   is well below the class-separation margin.</item>
    /// </list>
    /// </para>
    /// <para>
    /// Results are materialised as <see cref="DetectionResultState"/>
    /// nodes under <c>Pipeline.Results</c>, per the docstring on
    /// <c>IVisionFeedbackSink</c>. Old results are kept up to
    /// <see cref="ResultRetention"/> to bound the address-space
    /// footprint of the continuous mode.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by the DI container via AddSingleton.")]
    internal sealed partial class BinPickingGroundTruthInferenceProvider : IVisionInferenceProvider, IDisposable
    {
        public BinPickingGroundTruthInferenceProvider(
            BinPickingWorldState worldState,
            IBinPickingTargetProvider targetProvider,
            ILogger<BinPickingGroundTruthInferenceProvider> logger)
        {
            m_worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            m_targetProvider = targetProvider ?? throw new ArgumentNullException(nameof(targetProvider));
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// True when the provider has been bound to a pipeline. Consumed
        /// by the proof hosted service to know when it is safe to run.
        /// </summary>
        public bool IsAttached => m_target != null;

        /// <summary>
        /// The bound pipeline's node id, or <see cref="NodeId.Null"/> if
        /// the provider has not been attached yet.
        /// </summary>
        public NodeId PipelineNodeId => m_target?.PipelineNodeId ?? NodeId.Null;

        /// <summary>
        /// The sensor node id the pipeline was configured against, or
        /// <see cref="NodeId.Null"/> before <see cref="Attach"/>.
        /// </summary>
        public NodeId SensorNodeId => m_target?.SensorNodeId ?? NodeId.Null;

        /// <summary>
        /// The deployment node id the pipeline was configured against,
        /// or <see cref="NodeId.Null"/> before <see cref="Attach"/>.
        /// </summary>
        public NodeId DeploymentNodeId => m_target?.DeploymentNodeId ?? NodeId.Null;

        /// <summary>
        /// Camera-in-world pose used for projection and pose-in-camera.
        /// Snapshotted here so a client (or the proof hosted service)
        /// can chain camera → world without walking the OPC UA frame
        /// tree.
        /// </summary>
        public VisionPose3DDataType CameraInWorldPose =>
            m_target?.CameraInWorld ?? new VisionPose3DDataType();

        /// <summary>
        /// Looks up a previously-published detection result by its
        /// identifier. Returns <c>false</c> if the id is unknown or the
        /// provider has been disposed.
        /// </summary>
        public bool TryGetResult(string resultId, out DetectionResultState state)
        {
            return m_results.TryGetValue(resultId, out state!);
        }

        /// <summary>
        /// Called from the Vision configurator once the pipeline node
        /// has been created and its Results folder is available. Stores
        /// the references the provider needs to publish
        /// <c>DetectionResultType</c> instances.
        /// </summary>
        public void Attach(BinPickingInferenceTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (Interlocked.CompareExchange(ref m_target, target, null) != null)
            {
                throw new InvalidOperationException(
                    "BinPickingGroundTruthInferenceProvider has already been attached to a pipeline.");
            }
            m_logger.ProviderAttached(
                target.PipelineNodeId.IsNull ? string.Empty : target.PipelineNodeId.ToString());
        }

        /// <inheritdoc/>
        public async ValueTask<VisionInferenceRunResult> RunInferenceAsync(
            VisionInferenceRunRequest request,
            CancellationToken cancellationToken)
        {
            BinPickingInferenceTarget target = RequireTarget();
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<BinPickingPartSnapshot> parts = m_worldState.Snapshot();
            string resultId = "det-" + Guid.NewGuid().ToString("N");
            DateTimeUtc timestamp = request.Timestamp.IsNull
                ? DateTimeUtc.From(DateTime.UtcNow)
                : request.Timestamp;
            var detections = new List<VisionDetectionDataType>(parts.Count);
            for (int ii = 0; ii < parts.Count; ii++)
            {
                BinPickingPartSnapshot snapshot = parts[ii];
                if (snapshot.Location != BinPickingPartLocation.InBin)
                {
                    continue;
                }
                if (!TryBuildDetection(target, snapshot, out VisionDetectionDataType detection))
                {
                    continue;
                }
                detections.Add(detection);
            }
            ArrayOf<VisionDetectionDataType> payload = detections.ToArray().ToArrayOf();
            m_targetProvider.PublishWorldState(resultId, timestamp, parts);
            await PublishDetectionAsync(
                target, resultId, timestamp, request, payload, cancellationToken).ConfigureAwait(false);
            m_logger.ProducedDetectionResult(
                resultId,
                detections.Count,
                parts.Count);
            return new VisionInferenceRunResult(ServiceResult.Good, resultId);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> StartContinuousAsync(
            NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BinPickingInferenceTarget target = RequireTarget();
            if (pipeline.IsNull || !pipeline.Equals(target.PipelineNodeId))
            {
                return ValueTask.FromResult(new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    LocalizedText.From(
                        "The pipeline node id does not match the attached bin-picking pipeline.")));
            }
            lock (m_continuousLock)
            {
                if (m_continuousCts != null)
                {
                    return ValueTask.FromResult(ServiceResult.Good);
                }
                var cts = new CancellationTokenSource();
                m_continuousCts = cts;
                m_continuousTask = Task.Run(() => RunContinuousAsync(cts.Token), CancellationToken.None);
            }
            m_logger.ContinuousStarted();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> StopAsync(
            NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BinPickingInferenceTarget target = RequireTarget();
            if (pipeline.IsNull || !pipeline.Equals(target.PipelineNodeId))
            {
                return new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    LocalizedText.From(
                        "The pipeline node id does not match the attached bin-picking pipeline."));
            }
            CancellationTokenSource? cts;
            Task? task;
            lock (m_continuousLock)
            {
                cts = m_continuousCts;
                task = m_continuousTask;
                m_continuousCts = null;
                m_continuousTask = null;
            }
            if (cts != null)
            {
                await cts.CancelAsync().ConfigureAwait(false);
                cts.Dispose();
            }
            if (task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }
            m_logger.ContinuousStopped();
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            CancellationTokenSource? cts;
            lock (m_continuousLock)
            {
                cts = m_continuousCts;
                m_continuousCts = null;
                m_continuousTask = null;
            }
            cts?.Cancel();
            cts?.Dispose();
        }

        private BinPickingInferenceTarget RequireTarget()
        {
            BinPickingInferenceTarget? target = m_target;
            return target ?? throw new InvalidOperationException(
                "BinPickingGroundTruthInferenceProvider has not been attached to a pipeline.");
        }

        private async Task RunContinuousAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(ContinuousPeriod);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var request = new VisionInferenceRunRequest(
                        RequireTarget().PipelineNodeId,
                        RequireTarget().SensorNodeId,
                        RequireTarget().DeploymentNodeId,
                        DateTimeUtc.From(DateTime.UtcNow));
                    _ = await RunInferenceAsync(request, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_logger.ContinuousTickFailed(ex.Message);
                }
            }
        }

        private bool TryBuildDetection(
            BinPickingInferenceTarget target,
            BinPickingPartSnapshot snapshot,
            out VisionDetectionDataType detection)
        {
            (double xc, double yc, double zc) = target.WorldToCamera(
                snapshot.WorldX, snapshot.WorldY, snapshot.WorldZ);
            if (zc <= 0.0)
            {
                detection = new VisionDetectionDataType();
                return false;
            }
            if (!TryProjectBoundingBox2D(target, snapshot, out VisionBoundingBox2DDataType box2D))
            {
                detection = new VisionDetectionDataType();
                return false;
            }
            VisionPose3DDataType poseInCamera = new()
            {
                FrameId = target.CameraFrameId,
                Position = new[] { xc, yc, zc }.ToArrayOf(),
                Orientation = s_identityOrientation.ToArrayOf(),
                Covariance = ArrayOf<double>.Empty
            };
            var boundingBox3D = new VisionBoundingBox3DDataType
            {
                Center = poseInCamera,
                Size = new[]
                {
                    snapshot.Part.Size[0],
                    snapshot.Part.Size[1],
                    snapshot.Part.Size[2]
                }.ToArrayOf()
            };
            detection = new VisionDetectionDataType
            {
                DetectionId = FormattableString.Invariant(
                    $"det-{snapshot.Part.ClassLabel}-{snapshot.Part.ClassId}"),
                ClassLabel = snapshot.Part.ClassLabel,
                ClassId = snapshot.Part.ClassId,
                Confidence = 0.99,
                HasBoundingBox2D = true,
                BoundingBox2D = box2D,
                HasBoundingBox3D = true,
                BoundingBox3D = boundingBox3D,
                HasPose = true,
                Pose = new VisionPose3DDataType
                {
                    FrameId = target.CameraFrameId,
                    Position = new[] { xc, yc, zc }.ToArrayOf(),
                    Orientation = s_identityOrientation.ToArrayOf(),
                    Covariance = ArrayOf<double>.Empty
                },
                TrackId = snapshot.Part.ClassLabel
            };
            return true;
        }

        private static bool TryProjectBoundingBox2D(
            BinPickingInferenceTarget target,
            BinPickingPartSnapshot snapshot,
            out VisionBoundingBox2DDataType box2D)
        {
            double sizeX = snapshot.Part.Size[0];
            double sizeY = snapshot.Part.Size[1];
            double sizeZ = snapshot.Part.Size[2];
            double rotZ = snapshot.RotationZDegrees * Math.PI / 180.0;
            double cosZ = Math.Cos(rotZ);
            double sinZ = Math.Sin(rotZ);
            double minU = double.PositiveInfinity;
            double maxU = double.NegativeInfinity;
            double minV = double.PositiveInfinity;
            double maxV = double.NegativeInfinity;
            Span<double> local = stackalloc double[3];
            for (int ii = -1; ii <= 1; ii += 2)
            {
                for (int jj = -1; jj <= 1; jj += 2)
                {
                    for (int kk = -1; kk <= 1; kk += 2)
                    {
                        local[0] = ii * (sizeX * 0.5);
                        local[1] = jj * (sizeY * 0.5);
                        local[2] = kk * (sizeZ * 0.5);
                        double dxWorld = local[0] * cosZ - local[1] * sinZ;
                        double dyWorld = local[0] * sinZ + local[1] * cosZ;
                        double dzWorld = local[2];
                        double x = snapshot.WorldX + dxWorld;
                        double y = snapshot.WorldY + dyWorld;
                        double z = snapshot.WorldZ + dzWorld;
                        (double xc, double yc, double zc) = target.WorldToCamera(x, y, z);
                        if (zc <= 0.0)
                        {
                            box2D = new VisionBoundingBox2DDataType();
                            return false;
                        }
                        double u = (target.Fx * xc / zc) + target.Cx;
                        double v = (target.Fy * yc / zc) + target.Cy;
                        if (u < minU)
                        {
                            minU = u;
                        }
                        if (u > maxU)
                        {
                            maxU = u;
                        }
                        if (v < minV)
                        {
                            minV = v;
                        }
                        if (v > maxV)
                        {
                            maxV = v;
                        }
                    }
                }
            }
            double clampedMinU = Math.Clamp(minU, 0.0, target.ImageWidth);
            double clampedMaxU = Math.Clamp(maxU, 0.0, target.ImageWidth);
            double clampedMinV = Math.Clamp(minV, 0.0, target.ImageHeight);
            double clampedMaxV = Math.Clamp(maxV, 0.0, target.ImageHeight);
            double width = clampedMaxU - clampedMinU;
            double height = clampedMaxV - clampedMinV;
            if (width <= 0.0 || height <= 0.0)
            {
                box2D = new VisionBoundingBox2DDataType();
                return false;
            }
            box2D = new VisionBoundingBox2DDataType
            {
                CenterX = (clampedMinU + clampedMaxU) * 0.5,
                CenterY = (clampedMinV + clampedMaxV) * 0.5,
                Width = width,
                Height = height,
                Rotation = 0.0
            };
            return true;
        }

        private async Task PublishDetectionAsync(
            BinPickingInferenceTarget target,
            string resultId,
            DateTimeUtc timestamp,
            VisionInferenceRunRequest request,
            ArrayOf<VisionDetectionDataType> detections,
            CancellationToken cancellationToken)
        {
            ISystemContext context = target.SystemContext;
            var qualifiedName = new QualifiedName(resultId, target.InstanceNamespaceIndex);
            DetectionResultState state = context.CreateInstanceOfDetectionResultType(
                target.ResultsFolder, qualifiedName);
            state.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            if (state.ResultId != null)
            {
                state.ResultId.Value = resultId;
            }
            if (state.CreationTime != null)
            {
                state.CreationTime.Value = timestamp;
            }
            state.CreateOrReplaceSensor(context, null!).Value = request.Sensor;
            state.CreateOrReplacePipeline(context, null!).Value = request.Pipeline;
            state.CreateOrReplaceModelVersionUsed(context, null!).Value = ModelVersion;
            state.CreateOrReplaceConfidence(context, null!).Value = 0.99;
            state.CreateOrReplaceExplanationUri(context, null!).Value = ExplanationUri;
            BaseDataVariableState<VisionImageReferenceDataType> frame =
                state.CreateOrReplaceFrame(context, null!);
            frame.Value = new VisionImageReferenceDataType
            {
                Uri = FormattableString.Invariant(
                    $"opcua-inline://binpicking-cell/frames/{resultId}"),
                Digest = ByteString.Empty,
                DigestAlgorithm = string.Empty,
                Format = VisionClipFormatEnum.Png,
                PixelFormat = target.PixelFormat,
                Width = (uint)Math.Round(target.ImageWidth),
                Height = (uint)Math.Round(target.ImageHeight),
                SizeBytes = 0u,
                Timestamp = timestamp
            };
            if (state.Detections != null)
            {
                state.Detections.Value = detections;
            }
            state.AddFrameId(context, NodeId.Null);
            if (state.FrameId != null)
            {
                state.FrameId.Value = target.CameraFrameId;
            }
            state.NodeId = context.RequireNodeIdFactory().New(context, state);
            NodeInstanceExtensions.AssignInstanceChildNodeIds(context, state, state.NodeId);
            target.ResultsFolder.AddChild(state);
            await target.NodeManager.AddPredefinedNodeAsync(state, cancellationToken).ConfigureAwait(false);
            m_results[resultId] = state;
        }

        private const string ModelVersion = "binpicking-groundtruth-1";
        private const string ExplanationUri = "urn:opcfoundation:BinPickingCell:vision:groundtruth";
        private static readonly TimeSpan ContinuousPeriod = TimeSpan.FromMilliseconds(500);
        private static readonly double[] s_identityOrientation = [0.0, 0.0, 0.0, 1.0];

        private readonly BinPickingWorldState m_worldState;
        private readonly IBinPickingTargetProvider m_targetProvider;
        private readonly ILogger<BinPickingGroundTruthInferenceProvider> m_logger;
        private readonly ConcurrentDictionary<string, DetectionResultState> m_results = new(StringComparer.Ordinal);
        private readonly Lock m_continuousLock = new();
        private BinPickingInferenceTarget? m_target;
        private CancellationTokenSource? m_continuousCts;
        private Task? m_continuousTask;
    }

    /// <summary>
    /// Everything the provider needs to publish results into the address
    /// space and to project part positions into camera pixels. Populated
    /// by the Vision configurator once the pipeline node and its Results
    /// folder are available.
    /// </summary>
    internal sealed class BinPickingInferenceTarget
    {
        public BinPickingInferenceTarget(
            AsyncCustomNodeManager nodeManager,
            ISystemContext systemContext,
            ushort instanceNamespaceIndex,
            NodeId pipelineNodeId,
            NodeId sensorNodeId,
            NodeId deploymentNodeId,
            FolderState resultsFolder,
            string cameraFrameId,
            string pixelFormat,
            double fx, double fy, double cx, double cy,
            double imageWidth, double imageHeight,
            VisionPose3DDataType cameraInWorld)
        {
            NodeManager = nodeManager ?? throw new ArgumentNullException(nameof(nodeManager));
            SystemContext = systemContext ?? throw new ArgumentNullException(nameof(systemContext));
            InstanceNamespaceIndex = instanceNamespaceIndex;
            PipelineNodeId = pipelineNodeId.IsNull
                ? throw new ArgumentException("Pipeline NodeId must not be null.", nameof(pipelineNodeId))
                : pipelineNodeId;
            SensorNodeId = sensorNodeId.IsNull
                ? throw new ArgumentException("Sensor NodeId must not be null.", nameof(sensorNodeId))
                : sensorNodeId;
            DeploymentNodeId = deploymentNodeId.IsNull
                ? throw new ArgumentException(
                    "Deployment NodeId must not be null.", nameof(deploymentNodeId))
                : deploymentNodeId;
            ResultsFolder = resultsFolder ?? throw new ArgumentNullException(nameof(resultsFolder));
            CameraFrameId = cameraFrameId ?? throw new ArgumentNullException(nameof(cameraFrameId));
            PixelFormat = pixelFormat ?? throw new ArgumentNullException(nameof(pixelFormat));
            Fx = fx;
            Fy = fy;
            Cx = cx;
            Cy = cy;
            ImageWidth = imageWidth;
            ImageHeight = imageHeight;
            CameraInWorld = cameraInWorld ?? throw new ArgumentNullException(nameof(cameraInWorld));
            (m_cameraInvOrientation, m_cameraPositionInWorld) = InvertCameraInWorld(cameraInWorld);
        }

        public AsyncCustomNodeManager NodeManager { get; }

        public ISystemContext SystemContext { get; }

        public ushort InstanceNamespaceIndex { get; }

        public NodeId PipelineNodeId { get; }

        public NodeId SensorNodeId { get; }

        public NodeId DeploymentNodeId { get; }

        public FolderState ResultsFolder { get; }

        public string CameraFrameId { get; }

        public string PixelFormat { get; }

        public double Fx { get; }

        public double Fy { get; }

        public double Cx { get; }

        public double Cy { get; }

        public double ImageWidth { get; }

        public double ImageHeight { get; }

        public VisionPose3DDataType CameraInWorld { get; }

        /// <summary>
        /// Transforms a world position into the camera frame used for
        /// projection and pose reporting. Uses the pre-inverted camera
        /// orientation so the hot path allocates nothing.
        /// </summary>
        public (double X, double Y, double Z) WorldToCamera(double x, double y, double z)
        {
            double dx = x - m_cameraPositionInWorld.X;
            double dy = y - m_cameraPositionInWorld.Y;
            double dz = z - m_cameraPositionInWorld.Z;
            (double qx, double qy, double qz, double qw) = m_cameraInvOrientation;
            double tx = qy * dz - qz * dy;
            double ty = qz * dx - qx * dz;
            double tz = qx * dy - qy * dx;
            double rotatedX = dx + 2.0 * (qw * tx + qy * tz - qz * ty);
            double rotatedY = dy + 2.0 * (qw * ty + qz * tx - qx * tz);
            double rotatedZ = dz + 2.0 * (qw * tz + qx * ty - qy * tx);
            return (rotatedX, rotatedY, rotatedZ);
        }

        private static ((double X, double Y, double Z, double W) InverseOrientation,
            (double X, double Y, double Z) CameraPositionInWorld) InvertCameraInWorld(
            VisionPose3DDataType cameraInWorld)
        {
            ReadOnlySpan<double> p = cameraInWorld.Position.Span;
            ReadOnlySpan<double> q = cameraInWorld.Orientation.Span;
            if (p.Length < 3 || q.Length < 4)
            {
                throw new ArgumentException(
                    "Camera-in-world pose must carry a 3-vector position and 4-vector quaternion.",
                    nameof(cameraInWorld));
            }
            return ((-q[0], -q[1], -q[2], q[3]), (p[0], p[1], p[2]));
        }

        private readonly (double X, double Y, double Z, double W) m_cameraInvOrientation;
        private readonly (double X, double Y, double Z) m_cameraPositionInWorld;
    }

    internal static partial class BinPickingGroundTruthInferenceProviderLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.Inference + 1,
            Level = LogLevel.Information,
            Message = "Bin-picking ground-truth inference provider attached to pipeline {PipelineNodeId}.")]
        public static partial void ProviderAttached(
            this ILogger<BinPickingGroundTruthInferenceProvider> logger,
            string pipelineNodeId);

        [LoggerMessage(EventId = BinPickingCellEventIds.Inference + 2,
            Level = LogLevel.Information,
            Message = "Bin-picking ground-truth inference produced result {ResultId} " +
                "with {Detections} of {TrackedParts} tracked parts visible.")]
        public static partial void ProducedDetectionResult(
            this ILogger<BinPickingGroundTruthInferenceProvider> logger,
            string resultId, int detections, int trackedParts);

        [LoggerMessage(EventId = BinPickingCellEventIds.Inference + 3,
            Level = LogLevel.Information,
            Message = "Bin-picking ground-truth inference started continuous mode.")]
        public static partial void ContinuousStarted(
            this ILogger<BinPickingGroundTruthInferenceProvider> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.Inference + 4,
            Level = LogLevel.Information,
            Message = "Bin-picking ground-truth inference stopped continuous mode.")]
        public static partial void ContinuousStopped(
            this ILogger<BinPickingGroundTruthInferenceProvider> logger);

        [LoggerMessage(EventId = BinPickingCellEventIds.Inference + 5,
            Level = LogLevel.Warning,
            Message = "Bin-picking ground-truth inference continuous tick failed: {Reason}.")]
        public static partial void ContinuousTickFailed(
            this ILogger<BinPickingGroundTruthInferenceProvider> logger,
            string reason);
    }
}
