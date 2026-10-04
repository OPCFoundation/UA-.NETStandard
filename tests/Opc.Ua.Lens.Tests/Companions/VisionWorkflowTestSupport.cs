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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;

namespace UaLens.Tests.Companions
{
    internal sealed class VisionWorkflowTestSupport
    {
        public VisionWorkflowTestSupport(int maxTargets = 128, int maxFields = 256, bool registerTypes = true)
        {
            Fixture = new CellProviderTestSession(Opc.Ua.Vision.Namespaces.Vision, maxTargets, maxFields);
            Sensor = new NodeId("sensor-7", Fixture.NamespaceIndex);
            Pipeline = new NodeId("pipeline-7", Fixture.NamespaceIndex);
            Media = new NodeId("media-7", Fixture.NamespaceIndex);
            Feedback = new NodeId("feedback-7", Fixture.NamespaceIndex);
            Stream = new NodeId("stream-7", Fixture.NamespaceIndex);
            Clip = new NodeId("clip-7", Fixture.NamespaceIndex);
            Fixture.Session.SetupGet(session => session.Connected).Returns(true);
            Fixture.Session.SetupGet(session => session.SessionId)
                .Returns(new NodeId("borrowed-session", Fixture.NamespaceIndex));
            Fixture.Session.SetupGet(session => session.Identity).Returns(new Mock<IUserIdentity>().Object);
            Fixture.Session.SetupGet(session => session.Endpoint).Returns(Endpoint);
            Clock.Setup(clock => clock.GetUtcNow()).Returns(() => UtcNow);
            Clock.Setup(clock => clock.CreateTimer(
                It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
                .Returns((TimerCallback callback, object? state, TimeSpan due, TimeSpan period) =>
                    Timers.Provider.CreateTimer(callback, state, due, period));
            Fixture.Session.Setup(session => session.BrowseAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                    It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ViewDescription _, uint _, ArrayOf<BrowseDescription> requests,
                    CancellationToken token) => Browse(requests, token));
            Fixture.Session.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                    new ValueTask<CallResponse>(CallAsync(requests, token)));
            SetType(Sensor, Opc.Ua.Vision.ObjectTypeIds.VisionSensorType);
            SetType(Pipeline, Opc.Ua.Vision.ObjectTypeIds.InferencePipelineType);
            SetType(Media, Opc.Ua.Vision.ObjectTypeIds.VisionMediaManagementType);
            SetType(Feedback, Opc.Ua.Vision.ObjectTypeIds.VisionFeedbackType);
            SetType(Stream, Opc.Ua.Vision.ObjectTypeIds.StreamEndpointType);
            SetType(Clip, Opc.Ua.Vision.ObjectTypeIds.ClipEndpointType);
            AddChild(Sensor, "Media", Media);
            AddChild(Pipeline, "Feedback", Feedback);
            var streamFolder = new NodeId("streams", Fixture.NamespaceIndex);
            var clipFolder = new NodeId("clips", Fixture.NamespaceIndex);
            AddChild(Media, "StreamEndpoints", streamFolder);
            AddChild(Media, "ClipEndpoints", clipFolder);
            AddChild(streamFolder, "Stream", Stream);
            AddChild(clipFolder, "Clip", Clip);
            Set(Sensor, "SensorId", Variant.From("sensor-identity-7"));
            Set(Sensor, "FrameId", Variant.From("camera-frame-7"));
            Set(Sensor, "RealityKind", Variant.From((int)VisionRealityKindEnum.Simulated));
            Set(Pipeline, "PipelineId", Variant.From("pipeline-identity-7"));
            Set(Pipeline, "Sensor", Variant.From(Sensor));
            Set(Pipeline, "Deployment", Variant.From(NodeId.Null));
            Set(Pipeline, "State", Variant.From((int)VisionEndpointStateEnum.Ready));
            Set(Pipeline, "Continuous", Variant.From(false));
            Set(Feedback, "MaxInlineFeedbackImageSize", Variant.From(1024u));
            foreach (NodeId endpoint in (ArrayOf<NodeId>)[Stream, Clip])
            {
                Set(endpoint, "EndpointId",
                    Variant.From(endpoint == Stream ? "stream-identity-7" : "clip-identity-7"));
                Set(endpoint, "State", Variant.From((int)VisionEndpointStateEnum.Ready));
                Set(endpoint, "SecureTransport", Variant.From(true));
                Set(endpoint, "EndpointUri", Variant.From("https://localhost/media/7"));
                Set(endpoint, "Authentication", Variant.From(0));
            }
            AddMethod(Media, "GetStreamEndpoint", 6044);
            AddMethod(Media, "ReleaseStreamEndpoint", 6047);
            AddMethod(Media, "ConfigureStreamEndpoint", 6049);
            AddMethod(Media, "SelectEndpoint", 6051);
            AddMethod(Media, "GetClip", 6053);
            AddMethod(Feedback, "SubmitDetections", 6157);
            AddMethod(Feedback, "SubmitInspectionResult", 6159);
            AddMethod(Feedback, "SubmitCorrection", 6161);
            AddMethod(Feedback, "SubmitImageReference", 6163);
            AddMethod(Pipeline, "RunInference", 6172);
            AddMethod(Pipeline, "StartContinuous", 6175);
            AddMethod(Pipeline, "Stop", 6176);
            if (registerTypes)
            {
                _ = new VisionClient(Fixture.Session.Object, Fixture.Context.Telemetry);
            }
        }

        public CellProviderTestSession Fixture { get; }
        public CompanionContext Context => Fixture.Context;
        public Mock<ISession> Session => Fixture.Session;
        public Mock<TimeProvider> Clock { get; } = new(MockBehavior.Strict);
        public Samples.SampleTestClock Timers { get; } = new();
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 15, 6, 0, 0, TimeSpan.Zero);
        public NodeId Sensor { get; }
        public NodeId Pipeline { get; }
        public NodeId Media { get; }
        public NodeId Feedback { get; }
        public NodeId Stream { get; }
        public NodeId Clip { get; }
        public CompanionTarget SensorTarget => new("vision", Sensor, "Simulated camera", "VisionSensor");
        public CompanionTarget PipelineTarget => new("vision", Pipeline, "Simulated pipeline", "InferencePipeline");
        public ArrayOf<CallMethodRequest> Calls => [.. m_calls];
        public ArrayOf<CancellationToken> CallTokens => [.. m_callTokens];
        public Func<CallMethodRequest, CancellationToken, ValueTask<CallMethodResult>>? OnCall { get; set; }
        public StatusCode BrowseStatus { get; set; }

        public EndpointDescription Endpoint { get; } = new()
        {
            EndpointUrl = "opc.tcp://localhost:4840/VisionWorkflowTests",
            SecurityMode = MessageSecurityMode.SignAndEncrypt,
            SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
            ServerCertificate = ByteString.From(1, 3, 5, 7),
            Server = new ApplicationDescription { ApplicationUri = "urn:vision-workflow:unit-test" }
        };

        public CompanionTarget Target(string operation)
        {
            return operation is "get-clip" or "probe-stream" or "configure-stream" or "select-endpoints"
                ? SensorTarget : PipelineTarget;
        }

        public VisionTaskDefinition Definition(string operation)
        {
            return VisionTaskDefinition.Find(Target(operation), operation);
        }

        public VisionWorkflowTask CreateTask(
            string operation, ArrayOf<CompanionValue> inputs = default, ByteString metadata = default)
        {
            CompanionTarget target = Target(operation);
            var binding = new VisionWorkflowBinding(
                Sensor, target == SensorTarget ? Media : Pipeline, NodeId.Null, "observed-identity-7",
                metadata.IsNull ? ByteString.From(1, 2, 3) : metadata, VisionEndpointStateEnum.Ready, false, null);
            return new VisionWorkflowTask(Context, target, Definition(operation).Operation, binding,
                inputs.IsNull ? Inputs(operation) : inputs, Clock.Object);
        }

        public ArrayOf<CompanionValue> Inputs(string operation)
        {
            return operation switch
            {
                "get-clip" =>
                [
                    new("endpoint", Variant.From(Clip)),
                    new("resultId", Variant.From("result-7")),
                    new("timestamp", Variant.From(new DateTimeUtc(UtcNow))),
                    new("format", Variant.From((int)VisionClipFormatEnum.Jpeg)),
                    new("requestInline", Variant.From(true))
                ],
                "probe-stream" =>
                [
                    new("endpoint", Variant.From(Stream)),
                    new("profile", Variant.From("reviewed-profile")),
                    new("protocol", Variant.From((int)VisionStreamProtocolEnum.Rtsp))
                ],
                "configure-stream" =>
                [
                    new("endpoint", Variant.From(Stream)),
                    new("codec", Variant.From((int)VisionVideoCodecEnum.H264)),
                    new("width", Variant.From(640u)),
                    new("height", Variant.From(480u)),
                    new("frameRate", Variant.From(29.5)),
                    new("bitrate", Variant.From(8_000_000u))
                ],
                "select-endpoints" => [new("stream", Variant.From(Stream)), new("clip", Variant.From(Clip))],
                "run-inference" => [new("timestamp", Variant.From(new DateTimeUtc(UtcNow)))],
                "run-continuous-window" => [new("seconds", Variant.From(3u))],
                "submit-detections" =>
                [
                    new("purpose", Variant.From((int)VisionFeedbackPurposeEnum.GroundTruthLabel)),
                    new("detections", Variant.FromStructure([Detection()])),
                    new("image", Variant.From(ExtensionObject.Null)),
                    new("inlineImage", Variant.From(ByteString.Empty)),
                    new("sceneIsEmpty", Variant.From(false))
                ],
                "submit-inspection" =>
                [
                    new("resultId", Variant.From("result-7")),
                    new("evaluation", Variant.From((int)VisionResultEvaluationEnum.Ok)),
                    new("characteristics", Variant.FromStructure([Characteristic()]))
                ],
                "submit-correction" =>
                [
                    new("resultId", Variant.From("result-7")),
                    new("purpose", Variant.From((int)VisionFeedbackPurposeEnum.GroundTruthLabel)),
                    new("detections", Variant.FromStructure([Detection()])),
                    new("characteristics", Variant.FromStructure(ArrayOf<VisionCharacteristicDataType>.Empty)),
                    new("reason", Variant.From(new LocalizedText("en", "Verified against the selected frame."))),
                    new("inlineImage", Variant.From(ByteString.Empty)),
                    new("retractAll", Variant.From(false))
                ],
                "submit-image-reference" =>
                [
                    new("purpose", Variant.From((int)VisionFeedbackPurposeEnum.Overlay)),
                    new("image", Variant.FromStructure(Image())),
                    new("resultId", Variant.From("result-7"))
                ],
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }

        public void Validate(string operation, ArrayOf<CompanionValue> values)
        {
            VisionWorkflowValues.Validate(Context, Definition(operation).Operation, values);
        }

        public void Advance(TimeSpan elapsed)
        {
            UtcNow += elapsed;
            Timers.Advance(elapsed);
        }

        public Mock<IVisionExecutionPolicy> AllowExecution()
        {
            var policy = new Mock<IVisionExecutionPolicy>(MockBehavior.Strict);
            policy.Setup(value => value.AuthorizeAsync(
                Context, It.IsAny<VisionWorkflowTask>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);
            return policy;
        }

        public NodeId AddResult(string id = "result-7", VisionResultKind kind = VisionResultKind.Detection)
        {
            if (!Fixture.Children.TryGetValue((Pipeline, "Results"), out NodeId folder))
            {
                folder = new NodeId("results", Fixture.NamespaceIndex);
                AddChild(Pipeline, "Results", folder);
            }
            var node = new NodeId("published-" + id, Fixture.NamespaceIndex);
            ExpandedNodeId type = kind switch
            {
                VisionResultKind.Detection => Opc.Ua.Vision.ObjectTypeIds.DetectionResultType,
                VisionResultKind.Inspection => Opc.Ua.Vision.ObjectTypeIds.InspectionResultType,
                VisionResultKind.Segmentation => Opc.Ua.Vision.ObjectTypeIds.SegmentationResultType,
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            SetType(node, type);
            var local = ExpandedNodeId.ToNodeId(type, Fixture.NamespaceUris);
            Fixture.Browses[local] =
            [
                new ReferenceDescription
                {
                    NodeId = ExpandedNodeId.ToNodeId(
                        Opc.Ua.Vision.ObjectTypeIds.VisionResultType, Fixture.NamespaceUris),
                    NodeClass = NodeClass.ObjectType,
                    IsForward = false,
                    ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasSubtype
                }
            ];
            AddChild(folder, "DifferentBrowseName-" + id, node);
            Set(node, "ResultId", Variant.From(id));
            Set(node, "CreationTime", Variant.From(new DateTimeUtc(UtcNow)));
            Set(node, "Sensor", Variant.From(Sensor));
            Set(node, "Pipeline", Variant.From(Pipeline));
            Set(node, "FrameId", Variant.From("camera-frame-7"));
            Set(node, "Frame", Variant.FromStructure(Image()));
            Set(node, "ModelVersionUsed", Variant.From("fixture-model-v7"));
            switch (kind)
            {
                case VisionResultKind.Detection:
                    Set(node, "Detections", Variant.FromStructure([Detection()]));
                    break;
                case VisionResultKind.Inspection:
                    Set(node, "Evaluation", Variant.From((int)VisionResultEvaluationEnum.Ok));
                    Set(node, "Characteristics",
                        Variant.FromStructure([Characteristic()]));
                    break;
                case VisionResultKind.Segmentation:
                    Set(node, "LabelClasses", Variant.From(["background", "part"]));
                    Set(node, "Mask", Variant.FromStructure(Image()));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            return node;
        }

        public NodeId AddAiDeployment(
            Opc.Ua.AI.InferenceLocationEnum location = Opc.Ua.AI.InferenceLocationEnum.OnServer,
            string? endpoint = null, bool egress = false)
        {
            ushort aiNamespace = Fixture.NamespaceUris.GetIndexOrAppend(Opc.Ua.AI.Namespaces.AI);
            var node = new NodeId("ai-deployment", Fixture.NamespaceIndex);
            var model = new NodeId("ai-model", Fixture.NamespaceIndex);
            SetType(node, Opc.Ua.AI.ObjectTypeIds.DeploymentType);
            Set(Pipeline, "Deployment", Variant.From(node));
            Set(node, "DeploymentId", Variant.From("ai-deployment-7"));
            Set(node, "InferenceLocation", Variant.From((int)location));
            Set(node, "State", Variant.From((int)Opc.Ua.AI.DeploymentStateEnum.Ready));
            Set(node, "DataJurisdiction", Variant.From("fixture-local"));
            Set(node, "EgressPermitted", Variant.From(egress));
            Set(node, "MaxInlinePayloadSize", Variant.From(4096u));
            if (endpoint is not null)
            {
                Set(node, "EndpointUri", Variant.From(endpoint));
            }
            ArrayOf<ReferenceDescription> existing = Fixture.Browses[node];
            Fixture.Browses[node] =
            [
                .. existing,
                new ReferenceDescription
                {
                    NodeId = model,
                    NodeClass = NodeClass.Object,
                    IsForward = true,
                    ReferenceTypeId = new NodeId(Opc.Ua.AI.ReferenceTypes.UsesModel, aiNamespace)
                }
            ];
            return node;
        }

        public void Set(NodeId parent, string name, Variant value, StatusCode status = default)
        {
            if (!Fixture.Children.TryGetValue((parent, name), out NodeId node))
            {
                Fixture.AddValue(parent, name, value);
                node = Fixture.Children[(parent, name)];
            }
            Fixture.Values[(node, Attributes.Value)] = new DataValue(value).WithStatus(status);
        }

        public void SetType(NodeId node, ExpandedNodeId type)
        {
            m_types[node] = ExpandedNodeId.ToNodeId(type, Fixture.NamespaceUris);
            Fixture.Values[(node, Attributes.NodeClass)] = new DataValue(Variant.From((int)NodeClass.Object));
        }

        public void AddChild(NodeId parent, string name, NodeId child)
        {
            Fixture.Children[(parent, name)] = child;
            ArrayOf<ReferenceDescription> existing = Fixture.Browses.TryGetValue(
                parent, out ArrayOf<ReferenceDescription> values) ? values : [];
            Fixture.Browses[parent] =
            [
                .. existing,
                Fixture.Reference(child, name, m_types.TryGetValue(child, out NodeId type) ? type : NodeId.Null)
            ];
        }

        public void VerifyBorrowedSession()
        {
            Session.Verify(session => session.WriteAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            Session.Verify(session => session.CloseAsync(
                It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
            Session.Verify(session => session.Dispose(), Times.Never);
        }

        public static ArrayOf<CompanionValue> Replace(ArrayOf<CompanionValue> inputs, string name, Variant value)
        {
            return inputs.ConvertAll(input => input.Name == name ? input with { Value = value } : input);
        }

        public static VisionDetectionDataType Detection(string id = "detection-7")
        {
            return new VisionDetectionDataType
            {
                DetectionId = id,
                ClassLabel = "component",
                Confidence = 0.875,
                HasBoundingBox2D = true,
                BoundingBox2D = new VisionBoundingBox2DDataType
                {
                    CenterX = 30.5,
                    CenterY = 40.25,
                    Width = 10,
                    Height = 20,
                    Rotation = 0
                }
            };
        }

        public static VisionCharacteristicDataType Characteristic(string id = "length-7")
        {
            return new VisionCharacteristicDataType
            {
                CharacteristicId = id,
                Name = "Length",
                Nominal = 10,
                Actual = 10.25,
                Deviation = 0.25,
                Uncertainty = 0.01,
                LowerTolerance = -0.5,
                UpperTolerance = 0.5
            };
        }

        public static VisionPose3DDataType Pose()
        {
            return new VisionPose3DDataType
            {
                FrameId = "camera-frame-7",
                Position = [1, 2, 3],
                Orientation = [0, 0, 0, 1],
                Covariance = []
            };
        }

        public static VisionImageReferenceDataType Image()
        {
            return new VisionImageReferenceDataType
            {
                Uri = "https://localhost/frames/frame-7",
                Timestamp = new DateTimeUtc(new DateTimeOffset(2026, 9, 15, 6, 0, 0, TimeSpan.Zero)),
                Format = VisionClipFormatEnum.Jpeg,
                Width = 640,
                Height = 480,
                SizeBytes = 4,
                DigestAlgorithm = "SHA-256",
                Digest = ByteString.From(SHA256.HashData([1, 2, 3, 4]))
            };
        }

        private void AddMethod(NodeId parent, string name, uint identifier)
        {
            var node = new NodeId(identifier, Fixture.NamespaceIndex);
            Fixture.Children[(parent, name)] = node;
            Fixture.Values[(node, Attributes.Executable)] = new DataValue(Variant.From(true));
            Fixture.Values[(node, Attributes.UserExecutable)] = new DataValue(Variant.From(true));
        }

        private ValueTask<BrowseResponse> Browse(ArrayOf<BrowseDescription> requests, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var results = new List<BrowseResult>();
            foreach (BrowseDescription request in requests)
            {
                var references = new List<ReferenceDescription>();
                if (request.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasTypeDefinition)
                {
                    if (request.BrowseDirection == BrowseDirection.Forward &&
                        m_types.TryGetValue(request.NodeId, out NodeId type))
                    {
                        references.Add(new ReferenceDescription
                        {
                            NodeId = type,
                            NodeClass = NodeClass.ObjectType,
                            IsForward = true,
                            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasTypeDefinition
                        });
                    }
                }
                else if (Fixture.Browses.TryGetValue(request.NodeId, out ArrayOf<ReferenceDescription> existing))
                {
                    foreach (ReferenceDescription reference in existing)
                    {
                        bool direction = request.BrowseDirection == BrowseDirection.Both ||
                            reference.IsForward == (request.BrowseDirection == BrowseDirection.Forward);
                        bool type = request.ReferenceTypeId.IsNull ||
                            request.ReferenceTypeId == reference.ReferenceTypeId ||
                            (request.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HierarchicalReferences &&
                                (reference.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.Organizes ||
                                    reference.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasProperty ||
                                    reference.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HasComponent));
                        if (direction &&
                            type &&
                            (request.NodeClassMask == 0 ||
                                (request.NodeClassMask & (uint)reference.NodeClass) != 0))
                        {
                            references.Add(reference);
                        }
                    }
                }
                results.Add(new BrowseResult { StatusCode = BrowseStatus, References = [.. references] });
            }
            return ValueTask.FromResult(new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [.. results],
                DiagnosticInfos = []
            });
        }

        private async Task<CallResponse> CallAsync(ArrayOf<CallMethodRequest> requests, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Assert.That(requests.Count, Is.EqualTo(1), "Each workflow dispatch is one explicit method request.");
            CallMethodRequest request = requests[0];
            m_calls.Add(request);
            m_callTokens.Add(token);
            CallMethodResult result = await (OnCall ??
                throw new InvalidOperationException("No mutation response was configured."))(request, token)
                .ConfigureAwait(false);
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [result],
                DiagnosticInfos = []
            };
        }

        private readonly Dictionary<NodeId, NodeId> m_types = [];
        private readonly List<CallMethodRequest> m_calls = [];
        private readonly List<CancellationToken> m_callTokens = [];
    }
}
