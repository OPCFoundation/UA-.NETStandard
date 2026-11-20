// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// Deterministic on-server inference provider used by the loop
    /// tests. Derives detections from the shared <see cref="TestBinWorld"/>
    /// — no GPU required. The reported pose is expressed in the
    /// <c>camera_eih</c> frame the sensor is calibrated against, so a
    /// client composing pose from camera → world lands on the part's
    /// known world position.
    /// </summary>
    internal sealed class TestGroundTruthInferenceProvider : IVisionInferenceProvider, IDisposable
    {
        public TestGroundTruthInferenceProvider(TestBinWorld world)
        {
            m_world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public bool IsAttached => m_target != null;

        public NodeId PipelineNodeId => m_target?.PipelineNodeId ?? NodeId.Null;

        public NodeId SensorNodeId => m_target?.SensorNodeId ?? NodeId.Null;

        public NodeId DeploymentNodeId => m_target?.DeploymentNodeId ?? NodeId.Null;

        /// <summary>
        /// Called from the test-cell configurator once the pipeline
        /// exists and its Results folder is created.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public void Attach(TestInferenceTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (Interlocked.CompareExchange(ref m_target, target, null) != null)
            {
                throw new InvalidOperationException(
                    "TestGroundTruthInferenceProvider has already been attached.");
            }
        }

        public bool TryGetResult(string resultId, out DetectionResultState state)
        {
            return m_results.TryGetValue(resultId, out state!);
        }

        public async ValueTask<VisionInferenceRunResult> RunInferenceAsync(
            VisionInferenceRunRequest request,
            CancellationToken cancellationToken)
        {
            TestInferenceTarget target = RequireTarget();
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<TestPartSnapshot> parts = m_world.Snapshot();
            var detections = new List<VisionDetectionDataType>(parts.Count);
            for (int ii = 0; ii < parts.Count; ii++)
            {
                TestPartSnapshot snapshot = parts[ii];
                if (snapshot.Location != TestPartLocation.InBin)
                {
                    continue;
                }
                (double xc, double yc, double zc) = target.WorldToCamera(
                    snapshot.WorldX, snapshot.WorldY, snapshot.WorldZ);
                var poseInCamera = new VisionPose3DDataType
                {
                    FrameId = target.CameraFrameId,
                    Position = new[] { xc, yc, zc }.ToArrayOf(),
                    Orientation = s_identityOrientation.ToArrayOf(),
                    Covariance = []
                };
                var box3D = new VisionBoundingBox3DDataType
                {
                    Center = poseInCamera,
                    Size = new[]
                    {
                        snapshot.Part.Size[0],
                        snapshot.Part.Size[1],
                        snapshot.Part.Size[2]
                    }.ToArrayOf()
                };
                double halfW = snapshot.Part.Size[0] * 0.5;
                double halfH = snapshot.Part.Size[1] * 0.5;
                double centerU = 300.0 + (snapshot.Part.ClassId * 40.0);
                double centerV = 200.0 + (snapshot.Part.ClassId * 30.0);
                var box2D = new VisionBoundingBox2DDataType
                {
                    CenterX = centerU,
                    CenterY = centerV,
                    Width = halfW * 4000.0,
                    Height = halfH * 4000.0,
                    Rotation = 0.0
                };
                detections.Add(new VisionDetectionDataType
                {
                    DetectionId = FormattableString.Invariant(
                        $"det-{snapshot.Part.ClassLabel}-{snapshot.Part.ClassId}"),
                    ClassLabel = snapshot.Part.ClassLabel,
                    ClassId = snapshot.Part.ClassId,
                    Confidence = 0.98,
                    HasBoundingBox2D = true,
                    BoundingBox2D = box2D,
                    HasBoundingBox3D = true,
                    BoundingBox3D = box3D,
                    HasPose = true,
                    Pose = poseInCamera,
                    TrackId = snapshot.Part.ClassLabel
                });
            }
            string resultId = "det-" + Guid.NewGuid().ToString("N");
            DateTimeUtc timestamp = request.Timestamp.IsNull
                ? DateTimeUtc.From(DateTime.UtcNow)
                : request.Timestamp;
            await PublishAsync(
                target, resultId, timestamp, request, detections.ToArray().ToArrayOf(), cancellationToken)
                .ConfigureAwait(false);
            return new VisionInferenceRunResult(ServiceResult.Good, resultId);
        }

        public ValueTask<ServiceResult> StartContinuousAsync(
            NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TestInferenceTarget target = RequireTarget();
            if (pipeline.IsNull || !pipeline.Equals(target.PipelineNodeId))
            {
                return new ValueTask<ServiceResult>(new ServiceResult(
                    StatusCodes.BadNodeIdUnknown,
                    LocalizedText.From("Pipeline id does not match the attached test pipeline.")));
            }
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public ValueTask<ServiceResult> StopAsync(
            NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        public void Dispose()
        {
        }

        private TestInferenceTarget RequireTarget()
        {
            return m_target ??
                throw new InvalidOperationException(
                    "TestGroundTruthInferenceProvider has not been attached.");
        }

        private async Task PublishAsync(
            TestInferenceTarget target,
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
            state.ReferenceTypeId = ReferenceTypeIds.Organizes;
            state.ResultId?.Value = resultId;
            state.CreationTime?.Value = timestamp;
            state.CreateOrReplaceSensor(context, null).Value = request.Sensor;
            state.CreateOrReplacePipeline(context, null).Value = request.Pipeline;
            state.CreateOrReplaceModelVersionUsed(context, null).Value = ModelVersion;
            state.CreateOrReplaceConfidence(context, null).Value = 0.98;
            state.CreateOrReplaceExplanationUri(context, null).Value = ExplanationUri;
            BaseDataVariableState<VisionImageReferenceDataType> frame =
                state.CreateOrReplaceFrame(context, null);
            frame.Value = new VisionImageReferenceDataType
            {
                Uri = FormattableString.Invariant(
                    $"opcua-inline://test-cell/frames/{resultId}"),
                Digest = ByteString.Empty,
                DigestAlgorithm = string.Empty,
                Format = VisionClipFormatEnum.Png,
                PixelFormat = "Mono8",
                Width = 640u,
                Height = 480u,
                SizeBytes = 0u,
                Timestamp = timestamp
            };
            state.Detections?.Value = detections;
            state.AddFrameId(context, NodeId.Null);
            state.FrameId?.Value = target.CameraFrameId;
            state.NodeId = context.RequireNodeIdFactory().New(context, state);
            context.AssignInstanceChildNodeIds(state, state.NodeId);
            target.ResultsFolder.AddChild(state);
            await target.NodeManager.AddPredefinedNodeAsync(state, cancellationToken).ConfigureAwait(false);
            m_results[resultId] = state;
        }

        private const string ModelVersion = "test-groundtruth-1";
        private const string ExplanationUri = "urn:opcfoundation:tests:vision:groundtruth";
        private static readonly double[] s_identityOrientation = [0.0, 0.0, 0.0, 1.0];

        private readonly TestBinWorld m_world;
        private readonly ConcurrentDictionary<string, DetectionResultState> m_results = new(StringComparer.Ordinal);
        private TestInferenceTarget? m_target;
    }
}
