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
    /// Off-server perception path. Combined
    /// <see cref="IVisionInferenceProvider"/> and
    /// <see cref="IVisionFeedbackSink"/> that publishes results the
    /// Server itself did not compute — the "agent-as-VLM" story of
    /// clause 8.2, where inference runs outside the Server and the
    /// results arrive over §9 feedback (<c>SubmitDetections</c>,
    /// <c>SubmitCorrection</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registered on the pipeline when the run selects
    /// <c>--inferenceLocation EdgeOffServer</c>; the on-server ground
    /// truth is used otherwise. Only one path is active at a time so
    /// the pipeline's advertised inference-location facet
    /// (<c>VIS-Inference-OnServer</c> vs
    /// <c>VIS-Inference-OffServer</c>, derived from the pipeline
    /// builder's <c>onServer</c> flag) says honestly which path is in
    /// force.
    /// </para>
    /// <para>
    /// <see cref="RunInferenceAsync"/> refuses with
    /// <see cref="StatusCodes.BadNotSupported"/> in this mode: the
    /// point of the off-server path is that the Server does not have
    /// a local model. Results arrive through the sink methods; the
    /// message that comes back with the refusal spells that out for
    /// the agent so it is not silent.
    /// </para>
    /// <para>
    /// Submissions are validated, not trusted: a language model can
    /// hallucinate a class label that is not a part in this cell, a
    /// confidence outside 0..1, a bounding box outside the image, or
    /// so many detections it cannot be a plausible bin. The provider
    /// refuses the submission with
    /// <see cref="StatusCodes.BadInvalidArgument"/> and a
    /// <see cref="LocalizedText"/> that says WHY — the same message
    /// the agent's tool sees — rather than silently trimming the
    /// bad input into something that looks acceptable. §9 says the
    /// Server refuses purposes it does not permit; malformed content
    /// belongs to the same "refuse-with-a-reason" surface.
    /// </para>
    /// <para>
    /// Every result carries the two provenance signals the address
    /// space models:
    /// <list type="bullet">
    ///   <item><see cref="DetectionResultState.ModelVersionUsed"/> is
    ///   <c>agent-off-server-1</c> (or the exact model tag when a
    ///   real REST VLM answered), so <see cref="ModelVersion"/> for
    ///   the ground-truth path and this one are distinct enough for a
    ///   reader to tell them apart on that field alone.</item>
    ///   <item><see cref="DetectionResultState.ExplanationUri"/> is
    ///   <see cref="ExplanationUri"/>, a URN that names this sink and
    ///   the submission's purpose so a consumer can trace back what
    ///   produced the value.</item>
    /// </list>
    /// A correction publishes a fresh <c>DetectionResultType</c> whose
    /// <c>ExplanationUri</c> encodes the corrected result's id — this
    /// is the specification's learning path (a failed pick becomes a
    /// labelled sample) and it is wired even if nothing consumes it
    /// yet.
    /// </para>
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1812",
        Justification = "Instantiated by the DI container via AddSingleton.")]
    internal sealed partial class BinPickingAgentInferenceProvider
        : IVisionInferenceProvider, IVisionFeedbackSink
    {
        public BinPickingAgentInferenceProvider(
            ILogger<BinPickingAgentInferenceProvider> logger)
        {
            m_logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// True when the provider has been bound to a pipeline. Consumed
        /// by the proof hosted service to know when it is safe to run.
        /// </summary>
        public bool IsAttached => m_target != null;

        /// <summary>
        /// The bound pipeline's node id, or <see cref="NodeId.Null"/>
        /// before <see cref="Attach"/>.
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
        /// Camera-in-world pose the pipeline was configured with. The
        /// proof service composes a submitted camera-frame pose to
        /// world through this so it does not have to walk the address
        /// space.
        /// </summary>
        public VisionPose3DDataType CameraInWorldPose =>
            m_target?.CameraInWorld ?? new VisionPose3DDataType();

        /// <summary>
        /// The camera frame the pipeline is calibrated against —
        /// exposed so the proof can label the frame of a synthesised
        /// pose the way the on-server provider labels its own.
        /// </summary>
        public string CameraFrameId => m_target?.CameraFrameId ?? string.Empty;

        /// <summary>
        /// Camera intrinsics width in pixels, exposed so the proof can
        /// generate an in-bounds bounding box without duplicating the
        /// configuration.
        /// </summary>
        public double ImageWidth => m_target?.ImageWidth ?? 0.0;

        /// <summary>
        /// Camera intrinsics height in pixels.
        /// </summary>
        public double ImageHeight => m_target?.ImageHeight ?? 0.0;

        /// <summary>
        /// Looks up a previously-published result by its identifier.
        /// The correction path calls this to confirm the target
        /// result actually exists before publishing the correction.
        /// </summary>
        public bool TryGetResult(string resultId, out DetectionResultState state)
        {
            return m_results.TryGetValue(resultId, out state!);
        }

        /// <summary>
        /// The identifier of the most recent detection result
        /// published through <see cref="SubmitDetectionsAsync"/>. Empty
        /// when no submission has been accepted yet. Corrections do
        /// not update this — the proof service relies on it to find
        /// the id it just published so it can inspect the addressed
        /// result and drive a correction against it.
        /// </summary>
        public string LastPublishedResultId => m_lastPublishedResultId;

        /// <summary>
        /// Called from the Vision configurator once the pipeline node
        /// and its Results folder are available.
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
                    "BinPickingAgentInferenceProvider has already been attached to a pipeline.");
            }
            m_logger.AgentSinkAttached(
                target.PipelineNodeId.IsNull ? string.Empty : target.PipelineNodeId.ToString());
        }

        /// <inheritdoc/>
        public ValueTask<VisionInferenceRunResult> RunInferenceAsync(
            VisionInferenceRunRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Off-server perception has no server-side computation to perform. Rather than
            // silently return an empty result the agent might mistake for "nothing found",
            // spell out that submissions arrive through the Feedback object. The message is
            // the one an MCP tool surfaces to the calling model.
            var result = new VisionInferenceRunResult(
                new ServiceResult(
                    StatusCodes.BadNotSupported,
                    LocalizedText.From(
                        "This pipeline is configured for off-server perception (InferenceLocation=" +
                        "EdgeOffServer). Publish results by calling SubmitDetections on the " +
                        "pipeline's Feedback object.")),
                string.Empty);
            return ValueTask.FromResult(result);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> StartContinuousAsync(
            NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Continuous inference is driven by the agent's cadence in this mode; there is no
            // Server-side clock to start. Refusing preserves the invariant "InferenceLocation
            // says honestly which path is in force" — a Good response would suggest the Server
            // was polling something it is not.
            return ValueTask.FromResult(new ServiceResult(
                StatusCodes.BadNotSupported,
                LocalizedText.From(
                    "Continuous inference is not supported when InferenceLocation=EdgeOffServer. " +
                    "The off-server agent drives its own cadence and publishes results through " +
                    "SubmitDetections.")));
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> StopAsync(
            NodeId pipeline, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> SubmitDetectionsAsync(
            VisionSubmitDetectionsRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            BinPickingInferenceTarget target = RequireTarget();
            if (!request.Pipeline.Equals(target.PipelineNodeId))
            {
                return Refuse(
                    "SubmitDetections",
                    StatusCodes.BadNodeIdUnknown,
                    "The pipeline node id does not match the attached off-server pipeline.");
            }
            // Part 9.5 pairs the array with the flag. SceneIsEmpty is how the agent
            // reports an emptied bin - the terminating condition of the pick loop -
            // so an empty array is accepted exactly when it is set. The dispatcher
            // checks this too; the check is kept so the provider is correct when
            // driven directly.
            ServiceResult? refusal = ValidateDetections(
                target, request.Detections, allowEmpty: request.SceneIsEmpty);
            if (refusal != null)
            {
                return refusal;
            }
            if (!IsKnownPurpose(request.Purpose))
            {
                return Refuse(
                    "SubmitDetections",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Purpose '{request.Purpose}' is not a defined VisionFeedbackPurposeEnum value."));
            }
            string modelTag = ResolveModelTag(request.FrameReference);
            string modelVersion = FormattableString.Invariant($"agent-off-server:{modelTag}");
            string explanation = FormattableString.Invariant(
                $"{ExplanationUri}?purpose={request.Purpose}&source={Uri.EscapeDataString(modelTag)}");
            string resultId = "det-agent-" + Guid.NewGuid().ToString("N");
            DateTimeUtc timestamp = DateTimeUtc.From(DateTime.UtcNow);
            try
            {
                await PublishAsync(
                    target,
                    resultId,
                    timestamp,
                    request.Detections,
                    modelVersion,
                    explanation,
                    request.FrameReference,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                // The frame maths raises BadInvalidArgument for a zero-norm quaternion rather
                // than substituting identity. Surface it as a clean refusal so the agent sees
                // the same shape of refusal it would for any other invalid input.
                return Refuse("SubmitDetections", ex.StatusCode, ex.Message);
            }
            m_logger.AgentDetectionsPublished(
                resultId, request.Detections.Count, request.Purpose, modelTag);
            m_lastPublishedResultId = resultId;
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> SubmitInspectionResultAsync(
            VisionSubmitInspectionResultRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            // The bin-picking cell is a detection cell, not an inspection cell. A model
            // submitting an inspection is confused about what pipeline this is, so refuse
            // rather than accept-and-silently-discard.
            return ValueTask.FromResult(new ServiceResult(
                StatusCodes.BadNotSupported,
                LocalizedText.From(
                    "This pipeline exposes DetectionResultType results only. Use " +
                    "SubmitDetections or SubmitCorrection with corrected detections.")));
        }

        /// <inheritdoc/>
        public async ValueTask<ServiceResult> SubmitCorrectionAsync(
            VisionSubmitCorrectionRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            BinPickingInferenceTarget target = RequireTarget();
            if (!request.Pipeline.Equals(target.PipelineNodeId))
            {
                return Refuse(
                    "SubmitCorrection",
                    StatusCodes.BadNodeIdUnknown,
                    "The pipeline node id does not match the attached off-server pipeline.");
            }
            if (string.IsNullOrEmpty(request.ResultId))
            {
                return Refuse(
                    "SubmitCorrection",
                    StatusCodes.BadInvalidArgument,
                    "ResultId is required and must name the result being corrected.");
            }
            if (!m_results.ContainsKey(request.ResultId))
            {
                return Refuse(
                    "SubmitCorrection",
                    StatusCodes.BadNodeIdUnknown,
                    FormattableString.Invariant(
                        $"ResultId '{request.ResultId}' does not name a result on this pipeline."));
            }
            if (request.CorrectedCharacteristics.Count > 0)
            {
                return Refuse(
                    "SubmitCorrection",
                    StatusCodes.BadNotSupported,
                    "This pipeline publishes DetectionResultType only; corrections must carry " +
                    "CorrectedDetections and not CorrectedCharacteristics.");
            }
            // Part 9.5 asks for at most one non-empty corrected array, and both empty
            // is the false-positive retraction when RetractAll says so. The dispatcher
            // checks this too; the check is kept so the provider is correct when
            // driven directly.
            ServiceResult? refusal = ValidateDetections(
                target, request.CorrectedDetections, allowEmpty: request.RetractAll);
            if (refusal != null)
            {
                return refusal;
            }
            if (!IsKnownPurpose(request.Purpose))
            {
                return Refuse(
                    "SubmitCorrection",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Purpose '{request.Purpose}' is not a defined VisionFeedbackPurposeEnum value."));
            }
            string reason = request.Reason.IsNull
                ? string.Empty
                : request.Reason.Text ?? string.Empty;
            string correctionResultId = "det-agent-correction-" + Guid.NewGuid().ToString("N");
            DateTimeUtc timestamp = DateTimeUtc.From(DateTime.UtcNow);
            string modelVersion = "agent-off-server:correction";
            string explanation = FormattableString.Invariant(
                $"{ExplanationUri}?purpose={request.Purpose}&corrects={Uri.EscapeDataString(request.ResultId)}");
            try
            {
                await PublishAsync(
                    target,
                    correctionResultId,
                    timestamp,
                    request.CorrectedDetections,
                    modelVersion,
                    explanation,
                    frameReference: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                return Refuse("SubmitCorrection", ex.StatusCode, ex.Message);
            }
            m_logger.AgentCorrectionPublished(
                correctionResultId,
                request.ResultId,
                request.CorrectedDetections.Count,
                reason);
            return ServiceResult.Good;
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> SubmitImageReferenceAsync(
            VisionSubmitImageReferenceRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            cancellationToken.ThrowIfCancellationRequested();
            // A pure image submission without detections has no home on the pipeline's
            // Results folder — the address space stores images alongside a result, never on
            // their own. Accept the call so the address space stays consistent with §9.5,
            // record the pointer, and let a consumer that cares walk it out of band.
            m_logger.AgentImageReference(
                request.Image?.Uri ?? string.Empty,
                request.ResultId ?? string.Empty,
                request.Purpose);
            return ValueTask.FromResult(ServiceResult.Good);
        }

        private BinPickingInferenceTarget RequireTarget()
        {
            BinPickingInferenceTarget? target = m_target;
            return target ?? throw new InvalidOperationException(
                "BinPickingAgentInferenceProvider has not been attached to a pipeline.");
        }

        private ServiceResult? ValidateDetections(
            BinPickingInferenceTarget target,
            ArrayOf<VisionDetectionDataType> detections,
            bool allowEmpty)
        {
            if (!allowEmpty && detections.Count == 0)
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    "At least one detection is required.");
            }
            if (detections.Count > MaxDetectionsPerSubmission)
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"{detections.Count} detections exceeds the plausible ceiling of {MaxDetectionsPerSubmission} for this bin (five parts \u00d7 three)."));
            }
            for (int ii = 0; ii < detections.Count; ii++)
            {
                VisionDetectionDataType detection = detections[ii];
                if (string.IsNullOrEmpty(detection.ClassLabel))
                {
                    return Refuse(
                        "Validate",
                        StatusCodes.BadInvalidArgument,
                        FormattableString.Invariant(
                            $"Detection {ii} has no ClassLabel."));
                }
                if (BinPickingPartsCatalog.TryGet(detection.ClassLabel) == null)
                {
                    return Refuse(
                        "Validate",
                        StatusCodes.BadInvalidArgument,
                        FormattableString.Invariant(
                            $"Detection {ii} class '{detection.ClassLabel}' is not a part in this cell. Known classes: RedCube, GreenCylinder, BlueSphere, YellowSlab, OrangeBrick."));
                }
                double confidence = detection.Confidence;
                if (double.IsNaN(confidence) ||
                    confidence < 0.0 || confidence > 1.0)
                {
                    return Refuse(
                        "Validate",
                        StatusCodes.BadInvalidArgument,
                        FormattableString.Invariant(
                            $"Detection {ii} confidence {confidence:0.###} is outside [0, 1]."));
                }
                if (detection.HasBoundingBox2D)
                {
                    ServiceResult? boxRefusal = ValidateBoundingBox2D(target, ii, detection.BoundingBox2D);
                    if (boxRefusal != null)
                    {
                        return boxRefusal;
                    }
                }
                if (detection.HasPose)
                {
                    ServiceResult? poseRefusal = ValidatePose(ii, detection.Pose);
                    if (poseRefusal != null)
                    {
                        return poseRefusal;
                    }
                }
            }
            return null;
        }

        private ServiceResult? ValidateBoundingBox2D(
            BinPickingInferenceTarget target,
            int index,
            VisionBoundingBox2DDataType box)
        {
            if (double.IsNaN(box.CenterX) || double.IsNaN(box.CenterY)
                || double.IsNaN(box.Width) || double.IsNaN(box.Height))
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} BoundingBox2D contains NaN."));
            }
            if (box.Width <= 0.0 || box.Height <= 0.0)
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} BoundingBox2D has non-positive extents (w={box.Width:0.##}, h={box.Height:0.##})."));
            }
            double halfW = box.Width * 0.5;
            double halfH = box.Height * 0.5;
            double minU = box.CenterX - halfW;
            double maxU = box.CenterX + halfW;
            double minV = box.CenterY - halfH;
            double maxV = box.CenterY + halfH;
            if (maxU <= 0.0 || minU >= target.ImageWidth
                || maxV <= 0.0 || minV >= target.ImageHeight)
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} BoundingBox2D (cx={box.CenterX:0.#}, cy={box.CenterY:0.#}, w={box.Width:0.#}, h={box.Height:0.#}) lies entirely outside the {target.ImageWidth:0}x{target.ImageHeight:0} image."));
            }
            return null;
        }

        private ServiceResult? ValidatePose(int index, VisionPose3DDataType pose)
        {
            System.ReadOnlySpan<double> orientation = pose.Orientation.Span;
            if (orientation.Length < 4)
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} Pose.Orientation must carry four components (x, y, z, w) per \u00a75.12."));
            }
            double normSq = orientation[0] * orientation[0]
                + orientation[1] * orientation[1]
                + orientation[2] * orientation[2]
                + orientation[3] * orientation[3];
            if (normSq <= 0.0)
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} Pose.Orientation has zero norm and does not describe a rotation."));
            }
            System.ReadOnlySpan<double> position = pose.Position.Span;
            if (position.Length < 3)
            {
                return Refuse(
                    "Validate",
                    StatusCodes.BadInvalidArgument,
                    FormattableString.Invariant(
                        $"Detection {index} Pose.Position must carry three components."));
            }
            return null;
        }

        private static bool IsKnownPurpose(VisionFeedbackPurposeEnum purpose)
        {
            return purpose switch
            {
                VisionFeedbackPurposeEnum.Overlay => true,
                VisionFeedbackPurposeEnum.Reconciliation => true,
                VisionFeedbackPurposeEnum.GroundTruthLabel => true,
                VisionFeedbackPurposeEnum.Trigger => true,
                _ => false
            };
        }

        private static string ResolveModelTag(VisionImageReferenceDataType? frameReference)
        {
            // A submission's frame reference carries an optional model tag in its URI when
            // an MCP tool includes one; keep the wire simple and just report the URI or a
            // placeholder. Nothing here consumes it, but it flows to ModelVersionUsed so a
            // consumer can distinguish a hand-driven submission from a real VLM one.
            if (frameReference == null || string.IsNullOrEmpty(frameReference.Uri))
            {
                return "unspecified";
            }
            return frameReference.Uri;
        }

        private async Task PublishAsync(
            BinPickingInferenceTarget target,
            string resultId,
            DateTimeUtc timestamp,
            ArrayOf<VisionDetectionDataType> detections,
            string modelVersion,
            string explanationUri,
            VisionImageReferenceDataType? frameReference,
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
            state.CreateOrReplaceSensor(context, null!).Value = target.SensorNodeId;
            state.CreateOrReplacePipeline(context, null!).Value = target.PipelineNodeId;
            state.CreateOrReplaceModelVersionUsed(context, null!).Value = modelVersion;
            state.CreateOrReplaceConfidence(context, null!).Value = ComputeAggregateConfidence(detections);
            state.CreateOrReplaceExplanationUri(context, null!).Value = explanationUri;
            BaseDataVariableState<VisionImageReferenceDataType> frame =
                state.CreateOrReplaceFrame(context, null!);
            frame.Value = frameReference ?? new VisionImageReferenceDataType
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

        private static double ComputeAggregateConfidence(
            ArrayOf<VisionDetectionDataType> detections)
        {
            if (detections.Count == 0)
            {
                return 0.0;
            }
            double sum = 0.0;
            for (int ii = 0; ii < detections.Count; ii++)
            {
                sum += detections[ii].Confidence;
            }
            return sum / detections.Count;
        }

        private ServiceResult Refuse(string operation, StatusCode code, string message)
        {
            m_logger.AgentRefused(operation, code.Code, message);
            return new ServiceResult(code, LocalizedText.From(message));
        }

        private const string ExplanationUri = "urn:opcfoundation:BinPickingCell:vision:agent-off-server";
        private const int MaxDetectionsPerSubmission = 15;

        private readonly ILogger<BinPickingAgentInferenceProvider> m_logger;
        private readonly ConcurrentDictionary<string, DetectionResultState> m_results
            = new(StringComparer.Ordinal);
        private BinPickingInferenceTarget? m_target;
        private string m_lastPublishedResultId = string.Empty;
    }

    internal static partial class BinPickingAgentInferenceProviderLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.Agent + 1,
            Level = LogLevel.Information,
            Message = "Bin-picking agent-driven off-server perception attached to pipeline {PipelineNodeId}.")]
        public static partial void AgentSinkAttached(
            this ILogger<BinPickingAgentInferenceProvider> logger,
            string pipelineNodeId);

        [LoggerMessage(EventId = BinPickingCellEventIds.Agent + 2,
            Level = LogLevel.Information,
            Message = "Agent SubmitDetections published result {ResultId} " +
                "({DetectionCount} detections, purpose={Purpose}, source={ModelTag}).")]
        public static partial void AgentDetectionsPublished(
            this ILogger<BinPickingAgentInferenceProvider> logger,
            string resultId, int detectionCount, VisionFeedbackPurposeEnum purpose, string modelTag);

        [LoggerMessage(EventId = BinPickingCellEventIds.Agent + 3,
            Level = LogLevel.Information,
            Message = "Agent SubmitCorrection published result {CorrectionResultId} " +
                "correcting {OriginalResultId} ({DetectionCount} detections, reason='{Reason}').")]
        public static partial void AgentCorrectionPublished(
            this ILogger<BinPickingAgentInferenceProvider> logger,
            string correctionResultId, string originalResultId,
            int detectionCount, string reason);

        [LoggerMessage(EventId = BinPickingCellEventIds.Agent + 4,
            Level = LogLevel.Information,
            Message = "Agent SubmitImageReference: uri={Uri} resultId={ResultId} purpose={Purpose}.")]
        public static partial void AgentImageReference(
            this ILogger<BinPickingAgentInferenceProvider> logger,
            string uri, string resultId, VisionFeedbackPurposeEnum purpose);

        [LoggerMessage(EventId = BinPickingCellEventIds.Agent + 5,
            Level = LogLevel.Warning,
            Message = "Agent {Operation} refused with code 0x{StatusCode:X8}: {Reason}.")]
        public static partial void AgentRefused(
            this ILogger<BinPickingAgentInferenceProvider> logger,
            string operation, uint statusCode, string reason);
    }
}
