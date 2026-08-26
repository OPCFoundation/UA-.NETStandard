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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Runs one Vision inference and turns the detection it selects into exactly
    /// one piece of Robot Intent work on the same OPC UA session.
    /// </summary>
    /// <remarks>
    /// The manager is the cross-companion helper the <c>robotics_vision_pick</c>
    /// tool delegates to. It composes the Vision and Robot Intent client SDKs
    /// directly - it never calls another MCP tool - so the same orchestration is
    /// available to application code that does not host an MCP server. It is
    /// read-only towards command authority: it never requests or releases
    /// control, never waits for completion, never retries, and never cancels.
    /// Every refusal the server reports is returned verbatim.
    /// </remarks>
    public sealed class VisionGuidedRoboticsManager
    {
        /// <summary>
        /// Initializes the manager over the MCP session manager and the Robot
        /// Intent manager. This is the constructor the container resolves, and
        /// it is equally usable as a direct-construction fallback.
        /// </summary>
        /// <param name="sessionManager">
        /// The session manager that owns the named OPC UA sessions.
        /// </param>
        /// <param name="roboticsManager">
        /// The Robot Intent manager used to resolve the controller.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Any argument is <c>null</c>.
        /// </exception>
        public VisionGuidedRoboticsManager(
            OpcUaSessionManager sessionManager,
            RoboticsIntentManager roboticsManager)
        {
            m_sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            Robotics = roboticsManager ?? throw new ArgumentNullException(nameof(roboticsManager));
        }

        /// <summary>
        /// Gets the Robot Intent manager the controller is resolved through.
        /// </summary>
        public RoboticsIntentManager Robotics { get; }

        /// <summary>
        /// Creates a Vision client over the very same named session the Robot
        /// Intent clients are created on, so perception and actuation always
        /// observe one server through one session.
        /// </summary>
        /// <param name="sessionName">
        /// Session name to use; defaults to the only active session.
        /// </param>
        public VisionClient CreateVisionClient(string? sessionName = null)
        {
            ISession session = m_sessionManager.GetSessionOrThrow(sessionName);
            return new VisionClient(session, m_sessionManager.Telemetry);
        }

        /// <summary>
        /// Runs one Vision inference, selects one detection deterministically,
        /// and submits either a single Pick intent or a two-step Pick/Place
        /// mission exactly once.
        /// </summary>
        /// <param name="request">
        /// The vision-guided pick request.
        /// </param>
        /// <param name="ct">
        /// Cancels the operation.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="request"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// A request field is empty, out of range, non-finite, or conflicts
        /// with another field.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The Vision result could not be resolved, or no detection matched.
        /// </exception>
        public async Task<VisionGuidedPickResult> PickAsync(
            VisionGuidedPickRequest request,
            CancellationToken ct = default)
        {
            ValidateRequest(request);

            RoboticsResolutionContext context = await RoboticsResolutionContext.CreateAsync(
                Robotics, request.Controller, request.SessionName, ct).ConfigureAwait(false);
            VisionPickObservation observation = await ObserveAsync(request, ct).ConfigureAwait(false);
            return await SubmitAsync(request, context, observation, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the Vision half of a vision-guided pick on the same session:
        /// resolves the pipeline, runs one bounded one-shot inference, and reads
        /// the full detection snapshot when the bounded summary is incomplete.
        /// </summary>
        internal async Task<VisionPickObservation> ObserveAsync(
            VisionGuidedPickRequest request,
            CancellationToken ct)
        {
            VisionClient client = CreateVisionClient(request.SessionName);
            VisionNodeEntry entry = await client.ResolvePipelineAsync(request.Pipeline, ct)
                .ConfigureAwait(false);
            VisionPipelineClient pipeline = client.Pipeline(entry.NodeId);
            VisionInferenceService inference = client.Inference();

            VisionInferenceResult result = await inference.RunOneShotAsync(
                pipeline,
                entry.BrowseName.Name,
                VisionResultDetail.Summary,
                VisionExpectedResultKind.Detection,
                kMaxSummaryItems,
                ct).ConfigureAwait(false);

            return await ResolveObservationAsync(
                result,
                token => client.Result(result.ResultNodeId).ReadDetectionAsync(token),
                ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Projects one inference result into the detection set selection runs
        /// over, reading the full snapshot when the bounded summary truncated it.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static async Task<VisionPickObservation> ResolveObservationAsync(
            VisionInferenceResult result,
            Func<CancellationToken, Task<VisionDetectionResultSnapshot>> readFullSnapshot,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(readFullSnapshot);

            if (!result.Resolved || result.ResultNodeId.IsNull)
            {
                // All concatenated operands must remain interpolated for string.Create handler binding.
                // TODO: Remove when RCS1214 preserves interpolated-string-handler overload binding.
#pragma warning disable RCS1214
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"The Vision pipeline published result '{result.ResultId}' but the result NodeId " +
                    $"could not be resolved, so no detection can be selected."));
#pragma warning restore RCS1214
            }

            VisionDetectionSummary? summary = result.DetectionSummary;
            if (summary is null)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"The Vision result '{result.ResultId}' carries no detection summary."));
            }

            if (summary.TotalDetections <= summary.Items.Count)
            {
                return new VisionPickObservation(result, summary.Items, summary.TotalDetections, false);
            }

            VisionDetectionResultSnapshot snapshot = await readFullSnapshot(ct).ConfigureAwait(false);
            var items = new List<VisionDetectionItem>(snapshot.Detections.Count);
            for (int i = 0; i < snapshot.Detections.Count; i++)
            {
                VisionDetectionDataType detection = snapshot.Detections[i];
                items.Add(new VisionDetectionItem
                {
                    DetectionId = detection.DetectionId ?? string.Empty,
                    ClassLabel = detection.ClassLabel ?? string.Empty,
                    ClassId = detection.ClassId,
                    Confidence = detection.Confidence,
                    HasPose = detection.HasPose,
                    Pose = detection.HasPose ? detection.Pose : null
                });
            }

            return new VisionPickObservation(result, items.ToArrayOf(), items.Count, true);
        }

        /// <summary>
        /// Turns one observation into exactly one submission against an already
        /// resolved controller. This is the seam the tests drive with a fake
        /// controller client and a supplied observation.
        /// </summary>
        internal static async Task<VisionGuidedPickResult> SubmitAsync(
            VisionGuidedPickRequest request,
            RoboticsResolutionContext context,
            VisionPickObservation observation,
            CancellationToken ct)
        {
            ValidateRequest(request);
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(observation);

            (VisionDetectionItem selected, int matched) = SelectDetection(request, observation);
            VisionPickProvenance provenance = BuildProvenance(observation, selected, matched);
            string objectClass = string.IsNullOrEmpty(request.ObjectClass)
                ? selected.ClassLabel
                : request.ObjectClass;

            var pick = new PickIntentInput
            {
                Source = request.Source,
                Tool = request.Tool,
                ObjectClass = objectClass,
                IntentId = request.PickIntentId,
                Label = request.Label,
                BufferMode = request.BufferMode,
                BlockingMode = request.BlockingMode
            };

            if (string.IsNullOrEmpty(request.Destination))
            {
                IntentDataType intent = RoboticsIntentDtoConverter.ConvertPick(pick, context.Scope);
                IntentSubmissionResult submission = await context.Client
                    .TrySubmitIntentAsync(intent, ct).ConfigureAwait(false);
                return new VisionGuidedPickResult
                {
                    Kind = VisionPickSubmissionKind.Pick,
                    Provenance = provenance,
                    PickSubmission = new VisionPickIntentSubmission
                    {
                        Accepted = submission.Accepted,
                        IntentId = submission.IntentId,
                        Operation = submission.Operation.IsNull ? null : submission.Operation.ToString(),
                        Failure = submission.Failure,
                        Message = submission.Message.Text
                    }
                };
            }

            var place = new PlaceIntentInput
            {
                Destination = request.Destination,
                Tool = request.Tool,
                IntentId = request.PlaceIntentId,
                Label = request.Label,
                BufferMode = request.BufferMode,
                BlockingMode = request.BlockingMode
            };

            MissionStepInput[] steps =
            [
                new MissionStepInput
                {
                    StepId = kPickStepId,
                    Released = true,
                    Intent = new MissionIntentInput { Kind = IntentKind.Pick, Pick = pick }
                },
                new MissionStepInput
                {
                    StepId = kPlaceStepId,
                    Released = true,
                    Intent = new MissionIntentInput { Kind = IntentKind.Place, Place = place }
                }
            ];

            string missionId = string.IsNullOrEmpty(request.MissionId)
                ? kMissionIdPrefix + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
                : request.MissionId;
            uint missionUpdateId = request.MissionUpdateId ?? 1;
            MissionDataType mission = RoboticsMissionTools.BuildMission(
                missionId, missionUpdateId, steps, null, request.Label, context.Scope);

            MissionSubmissionResult missionResult = await context.Client
                .SubmitMissionAsync(mission, ct).ConfigureAwait(false);

            var result = new VisionGuidedPickResult
            {
                Kind = VisionPickSubmissionKind.Mission,
                Provenance = provenance,
                MissionSubmission = new VisionPickMissionSubmission
                {
                    Accepted = missionResult.Accepted,
                    // The server echoes the MissionId it accepted; an empty echo falls back to the
                    // submitted id exactly as the Robot Intent client SDK does, so the caller can
                    // always address the mission it just created.
                    MissionId = missionResult.MissionId.Length == 0 ? missionId : missionResult.MissionId,
                    MissionUpdateId = missionUpdateId,
                    Operation = missionResult.Operation.IsNull ? null : missionResult.Operation.ToString(),
                    Failure = missionResult.Failure,
                    Message = missionResult.Message.Text,
                    PickStepId = kPickStepId,
                    PlaceStepId = kPlaceStepId
                }
            };

            if (!missionResult.Accepted || missionResult.Operation.IsNull)
            {
                return result;
            }

            MissionSnapshot snapshot = await context.Client.Transport
                .ReadMissionSnapshotAsync(missionResult.Operation, ct).ConfigureAwait(false);
            result.Steps = MapSteps(snapshot);
            return result;
        }

        /// <summary>
        /// Filters the observed detections and selects one deterministically.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        internal static (VisionDetectionItem Selected, int Matched) SelectDetection(
            VisionGuidedPickRequest request,
            VisionPickObservation observation)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(observation);

            ArrayOf<VisionDetectionItem> detections = observation.Detections;
            VisionDetectionItem? best = null;
            int matched = 0;

            for (int i = 0; i < detections.Count; i++)
            {
                VisionDetectionItem candidate = detections[i];
                if (!Matches(request, candidate))
                {
                    continue;
                }

                matched++;
                if (best is null || IsBetter(candidate, best))
                {
                    best = candidate;
                }
            }

            if (best is null)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"No detection in Vision result '{observation.Result.ResultId}' matched the requested " +
                    $"filters (detectionId='{request.DetectionId ?? "<any>"}', " +
                    $"classLabel='{request.ClassLabel ?? "<any>"}', " +
                    $"minimumConfidence={FormatConfidence(request.MinimumConfidence)}). " +
                    $"The result carries {observation.TotalDetections} detection(s), " +
                    $"{detections.Count} of which were considered."));
            }

            return (best, matched);
        }

        /// <summary>
        /// Validates every request field explicitly before any server call.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        internal static void ValidateRequest(VisionGuidedPickRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            RequireText(request.Controller, "controller");
            RequireText(request.Pipeline, "pipeline");
            RequireText(request.Source, "source");
            RequireText(request.Tool, "tool");
            RejectBlank(request.Destination, "destination");
            RejectBlank(request.DetectionId, "detectionId");
            RejectBlank(request.ClassLabel, "classLabel");
            RejectBlank(request.ObjectClass, "objectClass");
            RejectBlank(request.PickIntentId, "pickIntentId");
            RejectBlank(request.PlaceIntentId, "placeIntentId");
            RejectBlank(request.Label, "label");
            RejectBlank(request.MissionId, "missionId");

            if (request.MinimumConfidence.HasValue)
            {
                double confidence = request.MinimumConfidence.Value;
                if (!double.IsFinite(confidence))
                {
                    throw new ArgumentException(
                        "'minimumConfidence' must be a finite number.", nameof(request));
                }

                if (confidence is < 0.0 or > 1.0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(request),
                        confidence,
                        "'minimumConfidence' must be between 0 and 1 inclusive.");
                }
            }

            if (!Enum.IsDefined(request.Selection))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.Selection, "Invalid 'selection' value.");
            }

            if (request.BufferMode.HasValue && !Enum.IsDefined(request.BufferMode.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.BufferMode.Value, "Invalid 'bufferMode' value.");
            }

            if (request.BlockingMode.HasValue && !Enum.IsDefined(request.BlockingMode.Value))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(request), request.BlockingMode.Value, "Invalid 'blockingMode' value.");
            }

            if (!string.IsNullOrEmpty(request.Destination))
            {
                return;
            }

            RejectWithoutDestination(request.PlaceIntentId, "placeIntentId");
            RejectWithoutDestination(request.MissionId, "missionId");
            if (request.MissionUpdateId.HasValue)
            {
                throw new ArgumentException(
                    "'missionUpdateId' requires 'destination'; without it a single Pick intent is submitted.",
                    nameof(request));
            }
        }

        private static bool Matches(VisionGuidedPickRequest request, VisionDetectionItem candidate)
        {
            if (!string.IsNullOrEmpty(request.DetectionId) &&
                !string.Equals(request.DetectionId, candidate.DetectionId, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(request.ClassLabel) &&
                !string.Equals(request.ClassLabel, candidate.ClassLabel, StringComparison.Ordinal))
            {
                return false;
            }

            return !request.MinimumConfidence.HasValue ||
                candidate.Confidence >= request.MinimumConfidence.Value;
        }

        private static bool IsBetter(VisionDetectionItem candidate, VisionDetectionItem best)
        {
            // double.CompareTo orders NaN below every number, so a detection whose
            // confidence the server did not report can never displace a real one.
            int comparison = candidate.Confidence.CompareTo(best.Confidence);
            if (comparison != 0)
            {
                return comparison > 0;
            }

            // Equal confidence: ordinal DetectionId decides, and an identical id keeps
            // the earlier item, which is the original result order.
            return string.CompareOrdinal(candidate.DetectionId, best.DetectionId) < 0;
        }

        private static VisionPickProvenance BuildProvenance(
            VisionPickObservation observation,
            VisionDetectionItem selected,
            int matched)
        {
            VisionInferenceResult result = observation.Result;
            return new VisionPickProvenance
            {
                ResultId = result.ResultId,
                ResultNodeId = result.ResultNodeId.IsNull ? string.Empty : result.ResultNodeId.ToString(),
                RequestedPipelineNodeId = result.RequestedPipelineNodeId.IsNull
                    ? string.Empty
                    : result.RequestedPipelineNodeId.ToString(),
                RequestedPipelineName = result.RequestedPipelineName,
                PipelineId = result.PipelineId.IsNull ? null : result.PipelineId.ToString(),
                SensorId = result.SensorId.IsNull ? null : result.SensorId.ToString(),
                ModelVersionUsed = result.ModelVersionUsed,
                CreationTime = result.CreationTime == default
                    ? null
                    : result.CreationTime.ToString("o", CultureInfo.InvariantCulture),
                FrameId = result.FrameId,
                TotalDetections = observation.TotalDetections,
                MatchedDetections = matched,
                FullResultRead = observation.FullResultRead,
                SelectedDetection = BuildDetection(selected)
            };
        }

        private static VisionPickDetection BuildDetection(VisionDetectionItem selected)
        {
            VisionPose3DDataType? pose = selected.HasPose ? selected.Pose : null;
            return new VisionPickDetection
            {
                DetectionId = selected.DetectionId,
                ClassLabel = selected.ClassLabel,
                ClassId = selected.ClassId,
                Confidence = selected.Confidence,
                HasPose = selected.HasPose,
                PoseFrameId = pose?.FrameId,
                PosePosition = (pose?.Position.ToArray()),
                PoseOrientation = (pose?.Orientation.ToArray())
            };
        }

        private static VisionPickMissionStep[] MapSteps(MissionSnapshot snapshot)
        {
            if (snapshot.Steps.Count == 0)
            {
                return [];
            }

            var steps = new VisionPickMissionStep[snapshot.Steps.Count];
            for (int i = 0; i < snapshot.Steps.Count; i++)
            {
                MissionStepOperation step = snapshot.Steps[i];
                steps[i] = new VisionPickMissionStep
                {
                    StepId = step.StepId,
                    IntentId = step.IntentId,
                    Operation = step.OperationNodeId.IsNull ? null : step.OperationNodeId.ToString(),
                    State = step.State
                };
            }

            return steps;
        }

        private static string FormatConfidence(double? confidence)
        {
            return confidence.HasValue
                ? confidence.Value.ToString("R", CultureInfo.InvariantCulture)
                : "<any>";
        }

        private static void RequireText(string? value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{parameterName}' is required."),
                    parameterName);
            }
        }

        private static void RejectBlank(string? value, string parameterName)
        {
            if (value != null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{parameterName}' must be omitted or non-empty."),
                    parameterName);
            }
        }

        private static void RejectWithoutDestination(string? value, string parameterName)
        {
            if (!string.IsNullOrEmpty(value))
            {
                // All concatenated operands must remain interpolated for string.Create handler binding.
                // TODO: Remove when RCS1214 preserves interpolated-string-handler overload binding.
#pragma warning disable RCS1214
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture,
                        $"'{parameterName}' requires 'destination'; without it a single Pick intent " +
                        $"is submitted."),
                    parameterName);
#pragma warning restore RCS1214
            }
        }

        private const int kMaxSummaryItems = 100;
        private const string kPickStepId = "pick";
        private const string kPlaceStepId = "place";
        private const string kMissionIdPrefix = "vision-pick-";

        private readonly OpcUaSessionManager m_sessionManager;
    }

    /// <summary>
    /// One bounded perception observation: the inference result, the detections
    /// selection considers, and whether the full snapshot had to be read.
    /// </summary>
    internal sealed record VisionPickObservation(
        VisionInferenceResult Result,
        ArrayOf<VisionDetectionItem> Detections,
        int TotalDetections,
        bool FullResultRead);
}
