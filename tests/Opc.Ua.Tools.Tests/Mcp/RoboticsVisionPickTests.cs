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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Server;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Mcp.Tools;
using Opc.Ua.RobotIntent;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;

namespace Opc.Ua.Tools.Tests.Mcp
{
    /// <summary>
    /// Tests the cross-companion <c>robotics_vision_pick</c> helper: deterministic
    /// detection selection, Pick and Pick/Place mission construction, authoritative
    /// refusal propagation, exact provenance, the absence of any command-authority,
    /// retry, wait, or cancel side effect, and the published MCP contract.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class RoboticsVisionPickTests
    {
        [Test]
        public async Task HighestConfidenceDetectionIsSelectedAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickResult result = await SubmitAsync(
                transport,
                PickRequest(),
                Observation(
                    Detection("d-1", "cube", 1, 0.40),
                    Detection("d-2", "cube", 1, 0.91),
                    Detection("d-3", "cube", 1, 0.72))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Provenance.SelectedDetection.DetectionId, Is.EqualTo("d-2"));
                Assert.That(result.Provenance.SelectedDetection.Confidence, Is.EqualTo(0.91));
                Assert.That(result.Provenance.MatchedDetections, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task EqualConfidenceIsBrokenByOrdinalDetectionIdAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickResult result = await SubmitAsync(
                transport,
                PickRequest(),
                Observation(
                    Detection("d-b", "cube", 1, 0.80),
                    Detection("d-a", "cube", 1, 0.80),
                    Detection("d-c", "cube", 1, 0.80))).ConfigureAwait(false);

            Assert.That(result.Provenance.SelectedDetection.DetectionId, Is.EqualTo("d-a"));
        }

        [Test]
        public async Task IdenticalIdAndConfidenceKeepsTheOriginalOrderAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickResult result = await SubmitAsync(
                transport,
                PickRequest(),
                Observation(
                    Detection("d-same", "first", 1, 0.5),
                    Detection("d-same", "second", 2, 0.5))).ConfigureAwait(false);

            Assert.That(result.Provenance.SelectedDetection.ClassLabel, Is.EqualTo("first"));
        }

        [Test]
        public async Task FiltersAreAppliedBeforeSelectionAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickRequest request = PickRequest();
            request.ClassLabel = "cube";
            request.MinimumConfidence = 0.5;

            VisionGuidedPickResult result = await SubmitAsync(
                transport,
                request,
                Observation(
                    Detection("d-1", "sphere", 1, 0.99),
                    Detection("d-2", "cube", 2, 0.40),
                    Detection("d-3", "cube", 2, 0.55),
                    Detection("d-4", "cube", 2, 0.60))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Provenance.SelectedDetection.DetectionId, Is.EqualTo("d-4"));
                Assert.That(result.Provenance.MatchedDetections, Is.EqualTo(2));
                Assert.That(result.Provenance.TotalDetections, Is.EqualTo(4));
            });
        }

        [Test]
        public async Task ExactDetectionIdFilterSelectsThatDetectionAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickRequest request = PickRequest();
            request.DetectionId = "d-2";

            VisionGuidedPickResult result = await SubmitAsync(
                transport,
                request,
                Observation(
                    Detection("d-1", "cube", 1, 0.99),
                    Detection("d-2", "cube", 1, 0.10))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Provenance.SelectedDetection.DetectionId, Is.EqualTo("d-2"));
                Assert.That(result.Provenance.MatchedDetections, Is.EqualTo(1));
            });
        }

        [Test]
        public void NoMatchFailsWithTheFilterCriteriaAndCounts()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickRequest request = PickRequest();
            request.ClassLabel = "wedge";
            request.MinimumConfidence = 0.75;

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => SubmitAsync(
                    transport,
                    request,
                    Observation(Detection("d-1", "cube", 1, 0.9))))!;

            Assert.Multiple(() =>
            {
                Assert.That(exception.Message, Does.Contain("wedge"));
                Assert.That(exception.Message, Does.Contain("0.75"));
                Assert.That(exception.Message, Does.Contain("1 detection(s)"));
                Assert.That(transport.SubmitIntentCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task BoundedSummaryIsUsedWhenItCarriesEveryDetectionAsync()
        {
            bool readFull = false;
            VisionInferenceResult result = InferenceResult(
                total: 3,
                Detection("d-1", "cube", 1, 0.1),
                Detection("d-2", "cube", 1, 0.2),
                Detection("d-3", "cube", 1, 0.3));

            VisionPickObservation observation = await VisionGuidedRoboticsManager.ResolveObservationAsync(
                result,
                _ =>
                {
                    readFull = true;
                    return Task.FromResult(DetectionSnapshot());
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(readFull, Is.False);
                Assert.That(observation.FullResultRead, Is.False);
                Assert.That(observation.Detections.Count, Is.EqualTo(3));
                Assert.That(observation.TotalDetections, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task TruncatedSummaryReadsTheFullDetectionSnapshotAsync()
        {
            VisionDetectionItem[] bounded = new VisionDetectionItem[100];
            for (int i = 0; i < bounded.Length; i++)
            {
                bounded[i] = Detection(
                    string.Create(CultureInfo.InvariantCulture, $"d-{i:D3}"), "cube", 1, 0.10);
            }

            // The winning detection sits beyond the bounded window, so selection is
            // only correct when the full snapshot is read.
            var detections = new List<VisionDetectionDataType>(150);
            for (int i = 0; i < 150; i++)
            {
                detections.Add(new VisionDetectionDataType
                {
                    DetectionId = string.Create(CultureInfo.InvariantCulture, $"d-{i:D3}"),
                    ClassLabel = "cube",
                    ClassId = 1,
                    Confidence = i == 149 ? 0.99 : 0.10,
                    HasPose = false
                });
            }

            VisionPickObservation observation = await VisionGuidedRoboticsManager.ResolveObservationAsync(
                InferenceResult(total: 150, bounded),
                _ => Task.FromResult(DetectionSnapshot(detections)),
                CancellationToken.None).ConfigureAwait(false);

            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickResult result = await SubmitAsync(transport, PickRequest(), observation)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(observation.FullResultRead, Is.True);
                Assert.That(observation.Detections.Count, Is.EqualTo(150));
                Assert.That(result.Provenance.FullResultRead, Is.True);
                Assert.That(result.Provenance.TotalDetections, Is.EqualTo(150));
                Assert.That(result.Provenance.SelectedDetection.DetectionId, Is.EqualTo("d-149"));
            });
        }

        [Test]
        public void UnresolvedResultIsAnExplicitFailure()
        {
            var unresolved = new VisionInferenceResult
            {
                ResultId = "res-1",
                RequestedPipelineNodeId = new NodeId("Pipelines/BinPicking", 3),
                Resolved = false
            };

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                () => VisionGuidedRoboticsManager.ResolveObservationAsync(
                    unresolved,
                    _ => Task.FromResult(DetectionSnapshot()),
                    CancellationToken.None))!;

            Assert.That(exception.Message, Does.Contain("res-1"));
        }

        [Test]
        public async Task PickOnlySubmissionResolvesNamesAndDefaultsTheObjectClassAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickRequest request = PickRequest();
            request.PickIntentId = "pick-1";
            request.Label = "bin picking";
            request.BufferMode = BufferModeEnum.Buffered;
            request.BlockingMode = BlockingModeEnum.Single;

            VisionGuidedPickResult result = await SubmitAsync(
                transport, request, Observation(Detection("d-1", "cube", 7, 0.9))).ConfigureAwait(false);

            var pick = transport.SubmittedIntent as PickIntentDataType;

            Assert.Multiple(() =>
            {
                Assert.That(result.Kind, Is.EqualTo(VisionPickSubmissionKind.Pick));
                Assert.That(result.MissionSubmission, Is.Null);
                Assert.That(result.PickSubmission, Is.Not.Null);
                Assert.That(result.Steps, Is.Empty);
                Assert.That(pick, Is.Not.Null);
                Assert.That(pick!.Source, Is.EqualTo(kSourceNodeId));
                Assert.That(pick.Tool, Is.EqualTo(kToolNodeId));
                Assert.That(pick.ObjectClass, Is.EqualTo("cube"));
                Assert.That(pick.IntentId, Is.EqualTo("pick-1"));
                Assert.That(pick.Label.Text, Is.EqualTo("bin picking"));
                Assert.That(pick.BufferMode, Is.EqualTo(BufferModeEnum.Buffered));
                Assert.That(pick.BlockingMode, Is.EqualTo(BlockingModeEnum.Single));
            });
        }

        [Test]
        public async Task ObjectClassOverrideReplacesTheSelectedClassAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickRequest request = PickRequest();
            request.ObjectClass = "override-class";

            _ = await SubmitAsync(transport, request, Observation(Detection("d-1", "cube", 1, 0.9)))
                .ConfigureAwait(false);

            Assert.That(
                (transport.SubmittedIntent as PickIntentDataType)!.ObjectClass,
                Is.EqualTo("override-class"));
        }

        [Test]
        public async Task PickSubmissionNeverTouchesAuthorityRetryWaitOrCancelAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };

            _ = await SubmitAsync(transport, PickRequest(), Observation(Detection("d-1", "cube", 1, 0.9)))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.SubmitIntentCallCount, Is.EqualTo(1));
                Assert.That(transport.RequestControlCallCount, Is.Zero);
                Assert.That(transport.ReleaseControlCallCount, Is.Zero);
                Assert.That(transport.RetryCallCount, Is.Zero);
                Assert.That(transport.CancelIntentCallCount, Is.Zero);
                Assert.That(transport.CancelAllCallCount, Is.Zero);
                Assert.That(transport.CancelMissionCallCount, Is.Zero);
                Assert.That(transport.SubscribeCallCount, Is.Zero);
                Assert.That(transport.ReadOperationSnapshotCallCount, Is.Zero);
                Assert.That(transport.SubmitMissionCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task ControllerInfoIsReadExactlyOncePerCallAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            var controller = new RobotIntentControllerClient(transport);
            RoboticsResolutionContext context = await RoboticsResolutionContext
                .CreateAsync(controller, CancellationToken.None).ConfigureAwait(false);

            _ = await VisionGuidedRoboticsManager.SubmitAsync(
                PickRequest(),
                context,
                Observation(Detection("d-1", "cube", 1, 0.9)),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(transport.ReadControllerCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RefusedPickSubmissionSurvivesIntactAsync()
        {
            var transport = new CapturingRobotIntentTransport
            {
                ControllerInfo = ScopedControllerInfo(),
                SubmissionResult = new IntentSubmissionResult
                {
                    Accepted = false,
                    IntentId = "pick-1",
                    Failure = IntentFailureEnum.ControlNotOwned,
                    Message = new LocalizedText("operator owns command authority")
                }
            };

            VisionGuidedPickResult result = await SubmitAsync(
                transport, PickRequest(), Observation(Detection("d-1", "cube", 1, 0.9)))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.PickSubmission!.Accepted, Is.False);
                Assert.That(result.PickSubmission.IntentId, Is.EqualTo("pick-1"));
                Assert.That(result.PickSubmission.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(
                    result.PickSubmission.Message, Is.EqualTo("operator owns command authority"));
                Assert.That(transport.SubmitIntentCallCount, Is.EqualTo(1));
                Assert.That(transport.RequestControlCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task DestinationSubmitsTwoReleasedMissionStepsAsync()
        {
            var missionNode = new NodeId("Missions/m-1", 2);
            var transport = new CapturingRobotIntentTransport
            {
                ControllerInfo = ScopedControllerInfo(),
                MissionResult = new MissionSubmissionResult
                {
                    Accepted = true,
                    MissionId = "m-1",
                    Operation = missionNode
                },
                MissionSnapshot = new MissionSnapshot
                {
                    MissionId = "m-1",
                    Steps =
                    [
                        new MissionStepOperation
                        {
                            StepId = "pick",
                            IntentId = "i-pick",
                            OperationNodeId = new NodeId("Operations/1", 2),
                            State = ExecutionStateEnum.Executing
                        },
                        new MissionStepOperation
                        {
                            StepId = "place",
                            IntentId = "i-place",
                            State = ExecutionStateEnum.Accepted
                        }
                    ]
                }
            };

            VisionGuidedPickRequest request = PickRequest();
            request.Destination = "Tray";
            request.MissionId = "m-1";
            request.MissionUpdateId = 7;
            request.PlaceIntentId = "place-1";

            VisionGuidedPickResult result = await SubmitAsync(
                transport, request, Observation(Detection("d-1", "cube", 1, 0.9))).ConfigureAwait(false);

            MissionDataType mission = transport.SubmittedMission!;
            var pick = mission.Steps[0].Intent as PickIntentDataType;
            var place = mission.Steps[1].Intent as PlaceIntentDataType;

            Assert.Multiple(() =>
            {
                Assert.That(result.Kind, Is.EqualTo(VisionPickSubmissionKind.Mission));
                Assert.That(result.PickSubmission, Is.Null);
                Assert.That(result.MissionSubmission!.Accepted, Is.True);
                Assert.That(result.MissionSubmission.MissionId, Is.EqualTo("m-1"));
                Assert.That(result.MissionSubmission.MissionUpdateId, Is.EqualTo(7));
                Assert.That(result.MissionSubmission.Operation, Is.EqualTo(missionNode.ToString()));
                Assert.That(mission.MissionId, Is.EqualTo("m-1"));
                Assert.That(mission.MissionUpdateId, Is.EqualTo(7));
                Assert.That(mission.Steps.Count, Is.EqualTo(2));
                Assert.That(mission.Steps[0].StepId, Is.EqualTo("pick"));
                Assert.That(mission.Steps[0].Released, Is.True);
                Assert.That(mission.Steps[1].StepId, Is.EqualTo("place"));
                Assert.That(mission.Steps[1].Released, Is.True);
                Assert.That(pick!.Source, Is.EqualTo(kSourceNodeId));
                Assert.That(pick.ObjectClass, Is.EqualTo("cube"));
                Assert.That(place!.Destination, Is.EqualTo(kDestinationNodeId));
                Assert.That(place.Tool, Is.EqualTo(kToolNodeId));
                Assert.That(place.IntentId, Is.EqualTo("place-1"));
                Assert.That(transport.SubmitMissionCallCount, Is.EqualTo(1));
                Assert.That(transport.SubmitIntentCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task AcceptedMissionMapsStepsFromOneSnapshotReadAsync()
        {
            var transport = new CapturingRobotIntentTransport
            {
                ControllerInfo = ScopedControllerInfo(),
                MissionResult = new MissionSubmissionResult
                {
                    Accepted = true,
                    MissionId = "m-1",
                    Operation = new NodeId("Missions/m-1", 2)
                },
                MissionSnapshot = new MissionSnapshot
                {
                    Steps =
                    [
                        new MissionStepOperation
                        {
                            StepId = "pick",
                            IntentId = "i-pick",
                            OperationNodeId = new NodeId("Operations/1", 2),
                            State = ExecutionStateEnum.Executing
                        },
                        new MissionStepOperation
                        {
                            StepId = "place",
                            IntentId = "i-place",
                            State = ExecutionStateEnum.Accepted
                        }
                    ]
                }
            };

            VisionGuidedPickRequest request = PickRequest();
            request.Destination = "Tray";

            VisionGuidedPickResult result = await SubmitAsync(
                transport, request, Observation(Detection("d-1", "cube", 1, 0.9))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.ReadMissionSnapshotCallCount, Is.EqualTo(1));
                Assert.That(result.Steps, Has.Length.EqualTo(2));
                Assert.That(result.Steps[0].StepId, Is.EqualTo("pick"));
                Assert.That(result.Steps[0].IntentId, Is.EqualTo("i-pick"));
                Assert.That(result.Steps[0].Operation, Is.EqualTo("ns=2;s=Operations/1"));
                Assert.That(result.Steps[0].State, Is.EqualTo(ExecutionStateEnum.Executing));
                Assert.That(result.Steps[1].Operation, Is.Null);
                Assert.That(transport.SubscribeCallCount, Is.Zero);
                Assert.That(transport.CancelMissionCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task RefusedMissionSurfacesUnchangedAndReadsNoSnapshotAsync()
        {
            var transport = new CapturingRobotIntentTransport
            {
                ControllerInfo = ScopedControllerInfo(),
                MissionResult = new MissionSubmissionResult
                {
                    Accepted = false,
                    Failure = IntentFailureEnum.CapabilityNotSupported,
                    Message = new LocalizedText("missions are not supported")
                }
            };

            VisionGuidedPickRequest request = PickRequest();
            request.Destination = "Tray";
            request.MissionId = "m-9";

            VisionGuidedPickResult result = await SubmitAsync(
                transport, request, Observation(Detection("d-1", "cube", 1, 0.9))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.MissionSubmission!.Accepted, Is.False);
                Assert.That(
                    result.MissionSubmission.Failure,
                    Is.EqualTo(IntentFailureEnum.CapabilityNotSupported));
                Assert.That(
                    result.MissionSubmission.Message, Is.EqualTo("missions are not supported"));
                Assert.That(result.MissionSubmission.MissionId, Is.EqualTo("m-9"));
                Assert.That(result.Steps, Is.Empty);
                Assert.That(transport.ReadMissionSnapshotCallCount, Is.Zero);
                Assert.That(transport.SubmitMissionCallCount, Is.EqualTo(1));
                Assert.That(transport.RequestControlCallCount, Is.Zero);
            });
        }

        [Test]
        public async Task GeneratedMissionIdIsUsedWhenNoneIsSuppliedAsync()
        {
            var transport = new CapturingRobotIntentTransport
            {
                ControllerInfo = ScopedControllerInfo(),
                MissionResult = new MissionSubmissionResult { Accepted = false }
            };

            VisionGuidedPickRequest request = PickRequest();
            request.Destination = "Tray";

            VisionGuidedPickResult result = await SubmitAsync(
                transport, request, Observation(Detection("d-1", "cube", 1, 0.9))).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(transport.SubmittedMission!.MissionId, Does.StartWith("vision-pick-"));
                Assert.That(transport.SubmittedMission.MissionUpdateId, Is.EqualTo(1));
                Assert.That(
                    result.MissionSubmission!.MissionId,
                    Is.EqualTo(transport.SubmittedMission.MissionId));
            });
        }

        [Test]
        public async Task ProvenanceReportsTheExactVisionResultAndSelectedPoseAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            var pose = new VisionPose3DDataType
            {
                FrameId = "camera",
                Position = kPosePosition.ToArrayOf(),
                Orientation = kPoseOrientation.ToArrayOf(),
                Covariance = new double[36].ToArrayOf()
            };
            VisionDetectionItem selected = Detection("d-2", "cube", 7, 0.91) with
            {
                HasPose = true,
                Pose = pose
            };

            VisionGuidedPickResult result = await SubmitAsync(
                transport,
                PickRequest(),
                Observation(Detection("d-1", "cube", 7, 0.10), selected)).ConfigureAwait(false);

            VisionPickProvenance provenance = result.Provenance;

            Assert.Multiple(() =>
            {
                Assert.That(provenance.ResultId, Is.EqualTo("res-1"));
                Assert.That(provenance.ResultNodeId, Is.EqualTo("ns=3;s=Results/res-1"));
                Assert.That(
                    provenance.RequestedPipelineNodeId, Is.EqualTo("ns=3;s=Pipelines/BinPicking"));
                Assert.That(provenance.RequestedPipelineName, Is.EqualTo("BinPickingPipeline"));
                Assert.That(provenance.PipelineId, Is.EqualTo("ns=3;s=Pipelines/BinPicking"));
                Assert.That(provenance.SensorId, Is.EqualTo("ns=3;s=Sensors/Cam1"));
                Assert.That(provenance.ModelVersionUsed, Is.EqualTo("model-1.2.3"));
                Assert.That(provenance.CreationTime, Is.Not.Null);
                Assert.That(provenance.FrameId, Is.EqualTo("camera"));
                Assert.That(provenance.SelectedDetection.DetectionId, Is.EqualTo("d-2"));
                Assert.That(provenance.SelectedDetection.ClassLabel, Is.EqualTo("cube"));
                Assert.That(provenance.SelectedDetection.ClassId, Is.EqualTo(7));
                Assert.That(provenance.SelectedDetection.Confidence, Is.EqualTo(0.91));
                Assert.That(provenance.SelectedDetection.HasPose, Is.True);
                Assert.That(provenance.SelectedDetection.PoseFrameId, Is.EqualTo("camera"));
                Assert.That(
                    provenance.SelectedDetection.PosePosition, Is.EqualTo(kPosePosition));
                Assert.That(
                    provenance.SelectedDetection.PoseOrientation, Is.EqualTo(kPoseOrientation));
            });
        }

        [Test]
        public async Task ProvenanceLeavesThePoseNullWhenTheDetectionHasNoneAsync()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };

            VisionGuidedPickResult result = await SubmitAsync(
                transport, PickRequest(), Observation(Detection("d-1", "cube", 1, 0.9)))
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Provenance.SelectedDetection.HasPose, Is.False);
                Assert.That(result.Provenance.SelectedDetection.PosePosition, Is.Null);
                Assert.That(result.Provenance.SelectedDetection.PoseOrientation, Is.Null);
                Assert.That(result.Provenance.SelectedDetection.PoseFrameId, Is.Null);
            });
        }

        [Test]
        public void UnknownLocationNameIsRejectedBeforeSubmission()
        {
            var transport = new CapturingRobotIntentTransport { ControllerInfo = ScopedControllerInfo() };
            VisionGuidedPickRequest request = PickRequest();
            request.Source = "NoSuchBin";

            Assert.Multiple(() =>
            {
                Assert.ThrowsAsync<ArgumentException>(
                    () => SubmitAsync(
                        transport, request, Observation(Detection("d-1", "cube", 1, 0.9))));
                Assert.That(transport.SubmitIntentCallCount, Is.Zero);
            });
        }

        [TestCaseSource(nameof(InvalidRequests))]
        public void InvalidRequestsAreRejectedExplicitly(VisionGuidedPickRequest request)
        {
            Assert.That(
                () => VisionGuidedRoboticsManager.ValidateRequest(request),
                Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void ValidateRequestAcceptsTheMinimalRequest()
        {
            Assert.That(() => VisionGuidedRoboticsManager.ValidateRequest(PickRequest()), Throws.Nothing);
        }

        [Test]
        public void AddOpcUaMcpRoboticsRegistersTheVisionGuidedManager()
        {
            var services = new ServiceCollection();

            services.AddOpcUaMcpRobotics();

            Assert.That(
                services.Any(d => d.ServiceType == typeof(VisionGuidedRoboticsManager)), Is.True);
        }

        [Test]
        public async Task ManagerResolvesFromTheContainerAsASingletonAsync()
        {
            await using ServiceProvider provider = BuildRoboticsProvider();

            var first = provider.GetRequiredService<VisionGuidedRoboticsManager>();
            var second = provider.GetRequiredService<VisionGuidedRoboticsManager>();

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.SameAs(second));
                Assert.That(
                    first.Robotics, Is.SameAs(provider.GetRequiredService<RoboticsIntentManager>()));
            });
        }

        [Test]
        public void ManagerSupportsDirectConstruction()
        {
            OpcUaSessionManager sessionManager = McpTestEnvironment.SessionManager;
            var robotics = new RoboticsIntentManager(sessionManager);

            var manager = new VisionGuidedRoboticsManager(sessionManager, robotics);

            Assert.Multiple(() =>
            {
                Assert.That(manager.Robotics, Is.SameAs(robotics));
                Assert.That(
                    () => new VisionGuidedRoboticsManager(null!, robotics),
                    Throws.InstanceOf<ArgumentNullException>());
                Assert.That(
                    () => new VisionGuidedRoboticsManager(sessionManager, null!),
                    Throws.InstanceOf<ArgumentNullException>());
            });
        }

        [Test]
        public void VisionClientIsCreatedOnTheSameNamedSessionAsTheController()
        {
            OpcUaSessionManager sessionManager = McpTestEnvironment.SessionManager;
            var manager = new VisionGuidedRoboticsManager(
                sessionManager, new RoboticsIntentManager(sessionManager));
            ISession session = sessionManager.GetSessionOrThrow(McpTestEnvironment.SessionName);

            VisionClient client = manager.CreateVisionClient(McpTestEnvironment.SessionName);

            Assert.Multiple(() =>
            {
                Assert.That(client.Session, Is.SameAs(session));
                Assert.That(client.Telemetry, Is.SameAs(sessionManager.Telemetry));
                Assert.That(manager.CreateVisionClient().Session, Is.SameAs(session));
            });
        }

        [Test]
        public void VisionPickToolIsRegisteredForRoboticsAndFullProfiles()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    ResolveToolNames(McpToolProfile.Robotics), Does.Contain("robotics_vision_pick"));
                Assert.That(
                    ResolveToolNames(McpToolProfile.Full), Does.Contain("robotics_vision_pick"));
                Assert.That(
                    ResolveToolNames(McpToolProfile.Vision), Does.Not.Contain("robotics_vision_pick"));
            });
        }

        [Test]
        public void RoboticsProfilePublishesExactlyFortyTwoTools()
        {
            Assert.That(ResolveToolNames(McpToolProfile.Robotics), Has.Count.EqualTo(42));
        }

        [Test]
        public void ComposedVisionAndRoboticsProfilePublishesSixtyFourTools()
        {
            var services = new ServiceCollection();
            var toolProfiles = new McpToolProfileSet(
                new[] { McpToolProfile.Vision, McpToolProfile.Robotics });
            services.AddMcpServer()
                .WithOpcUaCoreTools(toolProfiles)
                .WithOpcUaRoboticsTools(toolProfiles)
                .WithOpcUaVisionTools(toolProfiles);

            using ServiceProvider provider = services.BuildServiceProvider();
            HashSet<string> tools = provider
                .GetServices<McpServerTool>()
                .Select(tool => tool.ProtocolTool.Name)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Multiple(() =>
            {
                Assert.That(tools, Has.Count.EqualTo(64));
                Assert.That(tools, Does.Contain("robotics_vision_pick"));
                Assert.That(tools, Does.Contain("vision_run_inference"));
            });
        }

        [Test]
        public void SchemaExposesTheNestedTypedRequestAndItsRequiredMembers()
        {
            JsonElement schema = Schema();
            JsonElement request = Path(schema, "properties", "request");

            Assert.Multiple(() =>
            {
                Assert.That(Required(schema), Is.EquivalentTo(kTopLevelRequired));
                Assert.That(Types(request), Does.Contain("object"));
                Assert.That(
                    Required(request),
                    Is.EquivalentTo(kRequestRequired));
            });
        }

        [Test]
        public void SchemaExposesTheSelectionPolicyEnum()
        {
            JsonElement selection = Path(Schema(), "properties", "request", "properties", "selection");

            Assert.Multiple(() =>
            {
                Assert.That(Types(selection), Does.Contain("string"));
                Assert.That(Enums(selection), Is.EqualTo(kSelectionPolicies));
            });
        }

        [Test]
        public void SchemaExposesTheConfidenceRange()
        {
            JsonElement confidence = Path(
                Schema(), "properties", "request", "properties", "minimumConfidence");

            Assert.Multiple(() =>
            {
                Assert.That(Types(confidence), Does.Contain("number"));
                Assert.That(confidence.GetProperty("minimum").GetDouble(), Is.Zero);
                Assert.That(confidence.GetProperty("maximum").GetDouble(), Is.EqualTo(1.0));
            });
        }

        [Test]
        public void SchemaExposesTheIntentModeEnums()
        {
            JsonElement request = Path(Schema(), "properties", "request");

            Assert.Multiple(() =>
            {
                Assert.That(
                    Enums(Path(request, "properties", "bufferMode")),
                    Does.Contain("Buffered").And.Contain("Aborting"));
                Assert.That(
                    Enums(Path(request, "properties", "blockingMode")),
                    Does.Contain("Single"));
            });
        }

        [Test]
        public void SchemaDoesNotExposeTheInjectedManager()
        {
            Assert.That(
                Path(Schema(), "properties").TryGetProperty("manager", out _), Is.False);
        }

        private const string kControllerName = "Controller1";

        private static readonly NodeId kSourceNodeId = new("Locations/Bin", 2);

        private static readonly NodeId kDestinationNodeId = new("Locations/Tray", 2);

        private static readonly NodeId kToolNodeId = new("Tools/Gripper", 2);

        private static readonly double[] kPosePosition = [0.1, 0.2, 0.3];

        private static readonly double[] kPoseOrientation = [0.0, 0.0, 0.0, 1.0];

        private static readonly string[] kTopLevelRequired = ["request"];

        private static readonly string[] kRequestRequired = ["controller", "pipeline", "source", "tool"];

        private static readonly string[] kSelectionPolicies = ["HighestConfidence"];

        private static IEnumerable<TestCaseData> InvalidRequests()
        {
            yield return new TestCaseData(Mutate(r => r.Controller = "  ")).SetName("BlankController");
            yield return new TestCaseData(Mutate(r => r.Pipeline = string.Empty)).SetName("EmptyPipeline");
            yield return new TestCaseData(Mutate(r => r.Source = " ")).SetName("BlankSource");
            yield return new TestCaseData(Mutate(r => r.Tool = string.Empty)).SetName("EmptyTool");
            yield return new TestCaseData(Mutate(r => r.Destination = "   ")).SetName("BlankDestination");
            yield return new TestCaseData(Mutate(r => r.DetectionId = " ")).SetName("BlankDetectionId");
            yield return new TestCaseData(Mutate(r => r.ClassLabel = " ")).SetName("BlankClassLabel");
            yield return new TestCaseData(Mutate(r => r.ObjectClass = " ")).SetName("BlankObjectClass");
            yield return new TestCaseData(Mutate(r => r.Label = " ")).SetName("BlankLabel");
            yield return new TestCaseData(Mutate(r => r.PickIntentId = " ")).SetName("BlankPickIntentId");
            yield return new TestCaseData(Mutate(r => r.MinimumConfidence = double.NaN))
                .SetName("NonFiniteConfidence");
            yield return new TestCaseData(Mutate(r => r.MinimumConfidence = double.PositiveInfinity))
                .SetName("InfiniteConfidence");
            yield return new TestCaseData(Mutate(r => r.MinimumConfidence = -0.1))
                .SetName("NegativeConfidence");
            yield return new TestCaseData(Mutate(r => r.MinimumConfidence = 1.1))
                .SetName("ConfidenceAboveOne");
            yield return new TestCaseData(Mutate(r => r.Selection = (VisionPickSelectionPolicy)42))
                .SetName("UnknownSelectionPolicy");
            yield return new TestCaseData(Mutate(r => r.BufferMode = (BufferModeEnum)999))
                .SetName("UnknownBufferMode");
            yield return new TestCaseData(Mutate(r => r.BlockingMode = (BlockingModeEnum)999))
                .SetName("UnknownBlockingMode");
            yield return new TestCaseData(Mutate(r => r.PlaceIntentId = "place-1"))
                .SetName("PlaceIntentIdWithoutDestination");
            yield return new TestCaseData(Mutate(r => r.MissionId = "m-1"))
                .SetName("MissionIdWithoutDestination");
            yield return new TestCaseData(Mutate(r => r.MissionUpdateId = 3))
                .SetName("MissionUpdateIdWithoutDestination");
        }

        private static VisionGuidedPickRequest Mutate(Action<VisionGuidedPickRequest> mutate)
        {
            VisionGuidedPickRequest request = PickRequest();
            mutate(request);
            return request;
        }

        private static VisionGuidedPickRequest PickRequest()
        {
            return new VisionGuidedPickRequest
            {
                Controller = kControllerName,
                Pipeline = "BinPickingPipeline",
                Source = "Bin",
                Tool = "Gripper"
            };
        }

        private static RobotIntentControllerInfo ScopedControllerInfo()
        {
            return new RobotIntentControllerInfo
            {
                NodeId = new NodeId("Controllers/Controller1", 2),
                BrowseName = new QualifiedName(kControllerName, 2),
                Lookups = new RobotIntentLookups
                {
                    Locations =
                    [
                        new RobotIntentNodeLookupEntry(
                            kSourceNodeId, new QualifiedName("Bin", 2), "Bin"),
                        new RobotIntentNodeLookupEntry(
                            kDestinationNodeId, new QualifiedName("Tray", 2), "Tray")
                    ],
                    Tools =
                    [
                        new RobotIntentNodeLookupEntry(
                            kToolNodeId, new QualifiedName("Gripper", 2), "Gripper")
                    ]
                }
            };
        }

        private static VisionDetectionItem Detection(
            string detectionId, string classLabel, uint classId, double confidence)
        {
            return new VisionDetectionItem
            {
                DetectionId = detectionId,
                ClassLabel = classLabel,
                ClassId = classId,
                Confidence = confidence,
                HasPose = false
            };
        }

        private static VisionInferenceResult InferenceResult(
            int total, params VisionDetectionItem[] items)
        {
            return new VisionInferenceResult
            {
                ResultId = "res-1",
                ResultNodeId = new NodeId("Results/res-1", 3),
                Resolved = true,
                RequestedPipelineNodeId = new NodeId("Pipelines/BinPicking", 3),
                RequestedPipelineName = "BinPickingPipeline",
                PipelineId = new NodeId("Pipelines/BinPicking", 3),
                SensorId = new NodeId("Sensors/Cam1", 3),
                ModelVersionUsed = "model-1.2.3",
                CreationTime = DateTimeUtc.Now,
                FrameId = "camera",
                ResultKind = VisionResultKind.Detection,
                DetectionSummary = new VisionDetectionSummary
                {
                    CreationTime = DateTimeUtc.Now,
                    ModelVersionUsed = "model-1.2.3",
                    FrameId = "camera",
                    TotalDetections = total,
                    Items = items.ToArrayOf()
                }
            };
        }

        private static VisionPickObservation Observation(params VisionDetectionItem[] items)
        {
            return new VisionPickObservation(
                InferenceResult(items.Length, items), items.ToArrayOf(), items.Length, false);
        }

        private static VisionDetectionResultSnapshot DetectionSnapshot(
            List<VisionDetectionDataType>? detections = null)
        {
            return new VisionDetectionResultSnapshot
            {
                NodeId = new NodeId("Results/res-1", 3),
                ResultId = "res-1",
                Detections = (detections ?? []).ToArrayOf()
            };
        }

        private static async Task<VisionGuidedPickResult> SubmitAsync(
            CapturingRobotIntentTransport transport,
            VisionGuidedPickRequest request,
            VisionPickObservation observation)
        {
            var controller = new RobotIntentControllerClient(transport);
            RoboticsResolutionContext context = await RoboticsResolutionContext
                .CreateAsync(controller, CancellationToken.None).ConfigureAwait(false);
            return await VisionGuidedRoboticsManager.SubmitAsync(
                request, context, observation, CancellationToken.None).ConfigureAwait(false);
        }

        private static ServiceProvider BuildRoboticsProvider()
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpRobotics();
            return services.BuildServiceProvider();
        }

        private static IReadOnlyList<McpServerTool> ResolveTools(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpRobotics();
            services.AddOpcUaMcpVision();
            services.AddMcpServer()
                .WithOpcUaRoboticsTools(profile)
                .WithOpcUaVisionTools(profile);

            using ServiceProvider provider = services.BuildServiceProvider();
            return [.. provider.GetServices<McpServerTool>()];
        }

        private static HashSet<string> ResolveToolNames(McpToolProfile profile)
        {
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore();
            services.AddOpcUaMcpRobotics();
            services.AddMcpServer().WithOpcUaRoboticsTools(profile);

            using ServiceProvider provider = services.BuildServiceProvider();
            return [.. provider.GetServices<McpServerTool>().Select(t => t.ProtocolTool.Name)];
        }

        private static JsonElement Schema()
        {
            McpServerTool tool = ResolveTools(McpToolProfile.Robotics)
                .FirstOrDefault(t => string.Equals(
                    t.ProtocolTool.Name, "robotics_vision_pick", StringComparison.Ordinal))
                ?? throw new AssertionException("Tool 'robotics_vision_pick' is not registered.");
            return tool.ProtocolTool.InputSchema;
        }

        private static JsonElement Path(JsonElement element, params string[] segments)
        {
            JsonElement current = element;
            foreach (string segment in segments)
            {
                Assert.That(current.TryGetProperty(segment, out JsonElement next), Is.True,
                    $"schema is missing '{segment}' in [{string.Join('.', segments)}]");
                current = next;
            }
            return current;
        }

        private static string[] Required(JsonElement schema)
        {
            if (!schema.TryGetProperty("required", out JsonElement required))
            {
                return [];
            }
            return [.. required.EnumerateArray().Select(e => e.GetString()!)];
        }

        private static string[] Types(JsonElement element)
        {
            JsonElement type = element.GetProperty("type");
            if (type.ValueKind == JsonValueKind.String)
            {
                return [type.GetString()!];
            }
            return [.. type.EnumerateArray().Select(e => e.GetString()!)];
        }

        private static string[] Enums(JsonElement element)
        {
            return [.. element.GetProperty("enum")
                .EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)];
        }

        /// <summary>
        /// A Robot Intent transport double that captures what the vision-guided
        /// helper submitted and counts every call it must never make.
        /// </summary>
        private sealed class CapturingRobotIntentTransport : IRobotIntentTransport
        {
            public event RobotIntentReconnectHandler? Reconnected
            {
                add { }
                remove { }
            }

            public ILogger Logger { get; } = NullLogger.Instance;

            public NodeId ControllerId { get; } = new("Controllers/Controller1", 2);

            public NamespaceTable NamespaceUris { get; } = new();

            public IServiceMessageContext MessageContext { get; } = new ServiceMessageContext(
                DefaultTelemetry.Create(static _ => { }),
                EncodeableFactory.Create());

            public RobotIntentControllerInfo ControllerInfo { get; init; } = new();

            public RobotIntentControllerState ControllerState { get; init; } = new();

            public IntentSubmissionResult SubmissionResult { get; init; } =
                new() { Accepted = true, IntentId = "i-1" };

            public MissionSubmissionResult MissionResult { get; init; } = new() { Accepted = true };

            public IntentOperationSnapshot OperationSnapshot { get; init; } = new();

            public MissionSnapshot MissionSnapshot { get; init; } = new();

            public IntentDataType? SubmittedIntent { get; private set; }

            public MissionDataType? SubmittedMission { get; private set; }

            public int ReadControllerCallCount { get; private set; }

            public int ReadMissionSnapshotCallCount { get; private set; }

            public int ReadOperationSnapshotCallCount { get; private set; }

            public int SubmitIntentCallCount { get; private set; }

            public int SubmitMissionCallCount { get; private set; }

            public int RequestControlCallCount { get; private set; }

            public int ReleaseControlCallCount { get; private set; }

            public int RetryCallCount { get; private set; }

            public int CancelIntentCallCount { get; private set; }

            public int CancelAllCallCount { get; private set; }

            public int CancelMissionCallCount { get; private set; }

            public int SubscribeCallCount { get; private set; }

            public ValueTask<ArrayOf<RobotIntentNodeLookupEntry>> BrowseControllersAsync(
                CancellationToken ct = default)
            {
                return ValueTask.FromResult<ArrayOf<RobotIntentNodeLookupEntry>>(
                    [new RobotIntentNodeLookupEntry(
                        ControllerId, new QualifiedName(kControllerName, 2), kControllerName)]);
            }

            public ValueTask<RobotIntentControllerInfo> ReadControllerAsync(CancellationToken ct = default)
            {
                ReadControllerCallCount++;
                return ValueTask.FromResult(ControllerInfo);
            }

            public ValueTask<RobotIntentControllerState> ReadControllerStateAsync(
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(ControllerState);
            }

            public ValueTask<ArrayOf<IntentOperationSnapshot>> ListOperationsAsync(
                CancellationToken ct = default)
            {
                return ValueTask.FromResult<ArrayOf<IntentOperationSnapshot>>([OperationSnapshot]);
            }

            public ValueTask<ArrayOf<MissionSnapshot>> ListMissionsAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult<ArrayOf<MissionSnapshot>>([MissionSnapshot]);
            }

            public ValueTask<IntentSubmissionResult> SubmitIntentAsync(
                IntentDataType intent,
                CancellationToken ct = default)
            {
                SubmitIntentCallCount++;
                SubmittedIntent = intent;
                return ValueTask.FromResult(SubmissionResult);
            }

            public ValueTask<IntentCommandOutcome> CancelIntentAsync(
                string intentId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                CancelIntentCallCount++;
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<uint> CancelAllAsync(StopModeEnum stopMode, CancellationToken ct = default)
            {
                CancelAllCallCount++;
                return ValueTask.FromResult<uint>(0);
            }

            public ValueTask<IntentCommandOutcome> PauseAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<IntentCommandOutcome> ResumeAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<IntentSubmissionResult> RetryAsync(
                string intentId, CancellationToken ct = default)
            {
                RetryCallCount++;
                return ValueTask.FromResult(SubmissionResult);
            }

            public ValueTask<MissionSubmissionResult> SubmitMissionAsync(
                MissionDataType mission,
                CancellationToken ct = default)
            {
                SubmitMissionCallCount++;
                SubmittedMission = mission;
                return ValueTask.FromResult(MissionResult);
            }

            public ValueTask<MissionUpdateOutcome> UpdateMissionAsync(
                string missionId,
                uint missionUpdateId,
                ArrayOf<MissionStepDataType> steps,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(
                    new MissionUpdateOutcome(MissionUpdateResultEnum.Accepted, LocalizedText.Null));
            }

            public ValueTask<IntentCommandOutcome> CancelMissionAsync(
                string missionId,
                StopModeEnum stopMode,
                CancellationToken ct = default)
            {
                CancelMissionCallCount++;
                return ValueTask.FromResult(new IntentCommandOutcome(true));
            }

            public ValueTask<CommandAuthorityOutcome> RequestControlAsync(CancellationToken ct = default)
            {
                RequestControlCallCount++;
                return ValueTask.FromResult(new CommandAuthorityOutcome(true, NodeId.Null));
            }

            public ValueTask ReleaseControlAsync(CancellationToken ct = default)
            {
                ReleaseControlCallCount++;
                return ValueTask.CompletedTask;
            }

            public ValueTask<RealTimeChannelOpenResult> OpenRealTimeChannelAsync(
                string channelId,
                double requestedLease,
                CancellationToken ct = default)
            {
                return ValueTask.FromResult(new RealTimeChannelOpenResult { Granted = true });
            }

            public ValueTask<bool> CloseRealTimeChannelAsync(
                string channelId, CancellationToken ct = default)
            {
                return ValueTask.FromResult(true);
            }

            public ValueTask<NodeId> ResolveChildAsync(
                NodeId root, string browseName, CancellationToken ct = default)
            {
                return ValueTask.FromResult(new NodeId(browseName, 2));
            }

            public ValueTask<IntentOperationSnapshot> ReadOperationSnapshotAsync(
                NodeId operation,
                CancellationToken ct = default)
            {
                ReadOperationSnapshotCallCount++;
                return ValueTask.FromResult(OperationSnapshot);
            }

            public ValueTask<MissionSnapshot> ReadMissionSnapshotAsync(
                NodeId mission,
                CancellationToken ct = default)
            {
                ReadMissionSnapshotCallCount++;
                return ValueTask.FromResult(MissionSnapshot);
            }

            public ValueTask<NodeId> ReadControlOwnerAsync(CancellationToken ct = default)
            {
                return ValueTask.FromResult(NodeId.Null);
            }

            public async IAsyncEnumerable<RobotIntentDataChange> SubscribeDataChangesAsync(
                ArrayOf<NodeId> nodeIds,
                [EnumeratorCancellation] CancellationToken ct = default)
            {
                SubscribeCallCount++;
                await Task.CompletedTask.ConfigureAwait(false);
                yield break;
            }
        }
    }
}
#endif
