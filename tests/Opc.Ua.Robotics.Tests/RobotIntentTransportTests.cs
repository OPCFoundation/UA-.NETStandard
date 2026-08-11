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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;
using Opc.Ua.Tests;
using RiMethodIds = Opc.Ua.RobotIntent.MethodIds;
using RiNamespaces = Opc.Ua.RobotIntent.Namespaces;

namespace Opc.Ua.Robotics.Tests
{
    /// <summary>
    /// Pins the OPC UA wire contract used by <see cref="UaRobotIntentTransport"/>.
    /// </summary>
    [TestFixture]
    public class RobotIntentTransportTests
    {
        [Test]
        public async Task SubmitIntentMarshalsDeclaredObjectMethodAndIntentArgument()
        {
            using TransportHarness harness = TransportHarness.Create(SubmitAccepted("intent-1", OperationNode));
            WaitIntentDataType intent = WaitIntent();

            IntentSubmissionResult result = await harness.Transport.SubmitIntentAsync(intent);
            CallMethodRequest request = harness.SingleCall();
            IntentDataType decoded = DecodeStructure<IntentDataType>(request.InputArguments[0], harness.Context);

            Assert.Multiple(() =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(ControllerNode));
                Assert.That(request.MethodId, Is.EqualTo(harness.MethodNode(RiMethodIds.IntentControllerType_SubmitIntent)));
                Assert.That(request.InputArguments, Has.Count.EqualTo(1));
                Assert.That(decoded, Is.SameAs(intent));
                Assert.That(result.Accepted, Is.True);
                Assert.That(result.IntentId, Is.EqualTo("intent-1"));
                Assert.That(result.Operation, Is.EqualTo(OperationNode));
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.None));
                Assert.That(result.Message.Text, Is.EqualTo("accepted"));
            });
        }

        [Test]
        public async Task SubmitIntentGoodRefusalReturnsFailureInsteadOfThrowing()
        {
            using TransportHarness harness = TransportHarness.Create(SubmitRefused(IntentFailureEnum.ControlNotOwned));

            IntentSubmissionResult result = await harness.Transport.SubmitIntentAsync(WaitIntent());

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.IntentId, Is.Empty);
                Assert.That(result.Operation.IsNull, Is.True);
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(result.Message.Text, Is.EqualTo("refused"));
            });
        }

        [Test]
        public async Task SubmitIntentRefusalAcceptsNullVariantOutputs()
        {
            ArrayOf<Variant> outputs =
            [
                Variant.From(false),
                Variant.Null,
                Variant.Null,
                Variant.From(IntentFailureEnum.ControlNotOwned),
                Variant.Null
            ];
            using TransportHarness harness = TransportHarness.Create(outputs);

            IntentSubmissionResult result = await harness.Transport.SubmitIntentAsync(WaitIntent());

            Assert.Multiple(() =>
            {
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.IntentId, Is.Empty);
                Assert.That(result.Operation.IsNull, Is.True);
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.ControlNotOwned));
                Assert.That(result.Message, Is.EqualTo(LocalizedText.Null));
            });
        }


        [Test]
        public void SubmitIntentBadStatusCodeThrowsServiceFault()
        {
            using TransportHarness harness = TransportHarness.Create([], StatusCodes.BadUserAccessDenied);

            Assert.That(
                async () => await harness.Transport.SubmitIntentAsync(WaitIntent()),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadUserAccessDenied));
        }

        [Test]
        public void SubmitIntentWithTooFewOutputsThrowsClearServiceFault()
        {
            using TransportHarness harness = TransportHarness.Create([Variant.From(true)]);

            Assert.That(
                async () => await harness.Transport.SubmitIntentAsync(WaitIntent()),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadUnexpectedError)
                    .And.Message.Contains("returned fewer output arguments"));
        }

        [Test]
        public async Task SubmitIntentAcceptsInt32EnumOutputForFailure()
        {
            ArrayOf<Variant> outputs =
            [
                Variant.From(false),
                Variant.From(string.Empty),
                Variant.From(NodeId.Null),
                Variant.From((int)IntentFailureEnum.QueueFull),
                Variant.From(new LocalizedText("queue full"))
            ];
            using TransportHarness harness = TransportHarness.Create(outputs);

            IntentSubmissionResult result = await harness.Transport.SubmitIntentAsync(WaitIntent());

            Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.QueueFull));
        }

        [Test]
        public async Task RetryMarshalsIntentIdAndMapsDeclaredOutputPositions()
        {
            using TransportHarness harness = TransportHarness.Create(
                [
                    Variant.From(false),
                    Variant.From(OperationNode),
                    Variant.From(IntentFailureEnum.ParameterInvalid),
                    Variant.From(new LocalizedText("cannot retry"))
                ]);

            IntentSubmissionResult result = await harness.Transport.RetryAsync("intent-7");
            CallMethodRequest request = harness.SingleCall();

            Assert.Multiple(() =>
            {
                Assert.That(request.MethodId, Is.EqualTo(harness.MethodNode(RiMethodIds.IntentControllerType_Retry)));
                Assert.That(request.InputArguments, Has.Count.EqualTo(1));
                Assert.That(request.InputArguments[0].TryGetValue(out string intentId), Is.True);
                Assert.That(intentId, Is.EqualTo("intent-7"));
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.IntentId, Is.EqualTo("intent-7"));
                Assert.That(result.Operation, Is.EqualTo(OperationNode));
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.ParameterInvalid));
                Assert.That(result.Message.Text, Is.EqualTo("cannot retry"));
            });
        }

        [Test]
        public async Task SubmitMissionMarshalsMissionAndMapsDeclaredOutputPositions()
        {
            MissionDataType mission = new() { MissionId = "client-mission" };
            using TransportHarness harness = TransportHarness.Create(
                [
                    Variant.From(true),
                    Variant.From("server-mission"),
                    Variant.From(MissionNode),
                    Variant.From(IntentFailureEnum.None),
                    Variant.From(new LocalizedText("mission accepted"))
                ]);

            MissionSubmissionResult result = await harness.Transport.SubmitMissionAsync(mission);
            CallMethodRequest request = harness.SingleCall();
            MissionDataType decoded = DecodeStructure<MissionDataType>(request.InputArguments[0], harness.Context);

            Assert.Multiple(() =>
            {
                Assert.That(request.MethodId, Is.EqualTo(harness.MethodNode(RiMethodIds.IntentControllerType_SubmitMission)));
                Assert.That(request.InputArguments, Has.Count.EqualTo(1));
                Assert.That(decoded, Is.SameAs(mission));
                Assert.That(result.Accepted, Is.True);
                Assert.That(result.MissionId, Is.EqualTo("server-mission"));
                Assert.That(result.Operation, Is.EqualTo(MissionNode));
                Assert.That(result.Failure, Is.EqualTo(IntentFailureEnum.None));
                Assert.That(result.Message.Text, Is.EqualTo("mission accepted"));
            });
        }

        [Test]
        public async Task CommandMethodsMarshalArgumentsInNodeSetOrder()
        {
            MissionStepDataType step = new() { StepId = "step-1", SequenceId = 1 };
            ArrayOf<MissionStepDataType> steps = [step];
            using TransportHarness harness = TransportHarness.CreateQueued(
                [
                    CallResult([Variant.From(false)]),
                    CallResult([Variant.From(3u)]),
                    CallResult([Variant.From(true)]),
                    CallResult([Variant.From(false)]),
                    CallResult([Variant.From(MissionUpdateResultEnum.Accepted), Variant.From(new LocalizedText("updated"))]),
                    CallResult([Variant.From(true)])
                ]);

            await harness.Transport.CancelIntentAsync("intent-1", StopModeEnum.QuickStop);
            await harness.Transport.CancelAllAsync(StopModeEnum.OnPath);
            await harness.Transport.PauseAsync();
            await harness.Transport.ResumeAsync();
            await harness.Transport.UpdateMissionAsync("mission-1", 9, steps);
            await harness.Transport.CancelMissionAsync("mission-1", StopModeEnum.EndOfCycle);

            Assert.Multiple(() =>
            {
                AssertMethod(harness.Calls[0], harness.MethodNode(RiMethodIds.IntentControllerType_CancelIntent), 2);
                AssertStringArgument(harness.Calls[0], 0, "intent-1");
                AssertEnumArgument(harness.Calls[0], 1, StopModeEnum.QuickStop);
                AssertMethod(harness.Calls[1], harness.MethodNode(RiMethodIds.IntentControllerType_CancelAll), 1);
                AssertEnumArgument(harness.Calls[1], 0, StopModeEnum.OnPath);
                AssertMethod(harness.Calls[2], harness.MethodNode(RiMethodIds.IntentControllerType_Pause), 0);
                AssertMethod(harness.Calls[3], harness.MethodNode(RiMethodIds.IntentControllerType_Resume), 0);
                AssertMethod(harness.Calls[4], harness.MethodNode(RiMethodIds.IntentControllerType_UpdateMission), 3);
                AssertStringArgument(harness.Calls[4], 0, "mission-1");
                AssertUInt32Argument(harness.Calls[4], 1, 9);
                Assert.That(
                    harness.Calls[4].InputArguments[2].TryGetValue(
                        out ArrayOf<MissionStepDataType> decodedSteps,
                        harness.Context),
                    Is.True);
                Assert.That(decodedSteps, Has.Count.EqualTo(1));
                Assert.That(decodedSteps[0].StepId, Is.EqualTo("step-1"));
                AssertMethod(harness.Calls[5], harness.MethodNode(RiMethodIds.IntentControllerType_CancelMission), 2);
                AssertStringArgument(harness.Calls[5], 0, "mission-1");
                AssertEnumArgument(harness.Calls[5], 1, StopModeEnum.EndOfCycle);
            });
        }

        [Test]
        public async Task ControlAndChannelMethodsMarshalArgumentsInNodeSetOrder()
        {
            DateTimeUtc expiry = new(new DateTime(2026, 8, 5, 20, 0, 0, DateTimeKind.Utc));
            using TransportHarness harness = TransportHarness.CreateQueued(
                [
                    CallResult([Variant.From(true), Variant.From(new NodeId("owner", 2))]),
                    CallResult([]),
                    CallResult([
                        Variant.From(true),
                        Variant.From("opc.tcp://broker"),
                        Variant.From("payload"),
                        Variant.From(expiry),
                        Variant.From(new LocalizedText("leased"))
                    ]),
                    CallResult([Variant.From(true)])
                ]);

            CommandAuthorityOutcome control = await harness.Transport.RequestControlAsync();
            await harness.Transport.ReleaseControlAsync();
            RealTimeChannelOpenResult open = await harness.Transport.OpenRealTimeChannelAsync("rt-1", 5000);
            bool closed = await harness.Transport.CloseRealTimeChannelAsync("rt-1");

            Assert.Multiple(() =>
            {
                AssertMethod(harness.Calls[0], harness.MethodNode(RiMethodIds.IntentControllerType_RequestControl), 0);
                Assert.That(control.Granted, Is.True);
                Assert.That(control.CurrentOwner, Is.EqualTo(new NodeId("owner", 2)));
                AssertMethod(harness.Calls[1], harness.MethodNode(RiMethodIds.IntentControllerType_ReleaseControl), 0);
                AssertMethod(harness.Calls[2], harness.MethodNode(RiMethodIds.IntentControllerType_OpenRealTimeChannel), 2);
                AssertStringArgument(harness.Calls[2], 0, "rt-1");
                AssertDoubleArgument(harness.Calls[2], 1, 5000);
                Assert.That(open.Granted, Is.True);
                Assert.That(open.EndpointUrl, Is.EqualTo("opc.tcp://broker"));
                Assert.That(open.PayloadDescriptor, Is.EqualTo("payload"));
                Assert.That(open.LeaseExpiry, Is.EqualTo(expiry));
                Assert.That(open.Message.Text, Is.EqualTo("leased"));
                AssertMethod(harness.Calls[3], harness.MethodNode(RiMethodIds.IntentControllerType_CloseRealTimeChannel), 1);
                AssertStringArgument(harness.Calls[3], 0, "rt-1");
                Assert.That(closed, Is.True);
            });
        }

        [Test]
        public async Task ResolveChildBuildsRelativePathWithSessionRobotIntentNamespaceIndex()
        {
            using TransportHarness harness = TransportHarness.CreateForTranslate(
                new BrowsePathTarget { TargetId = new ExpandedNodeId(new NodeId("frames", RobotIntentNamespaceIndex)) });

            NodeId nodeId = await harness.Transport.ResolveChildAsync(ControllerNode, "Frames");
            BrowsePath path = harness.SingleBrowsePath();

            Assert.Multiple(() =>
            {
                Assert.That(nodeId, Is.EqualTo(new NodeId("frames", RobotIntentNamespaceIndex)));
                Assert.That(path.StartingNode, Is.EqualTo(ControllerNode));
                Assert.That(path.RelativePath.Elements, Has.Count.EqualTo(1));
                Assert.That(
                    path.RelativePath.Elements[0].ReferenceTypeId,
                    Is.EqualTo(global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences));
                Assert.That(path.RelativePath.Elements[0].IncludeSubtypes, Is.True);
                Assert.That(path.RelativePath.Elements[0].IsInverse, Is.False);
                Assert.That(path.RelativePath.Elements[0].TargetName.Name, Is.EqualTo("Frames"));
                Assert.That(path.RelativePath.Elements[0].TargetName.NamespaceIndex, Is.EqualTo(RobotIntentNamespaceIndex));
            });
        }

        [Test]
        public async Task BrowseControllersTranslatesServerRobotIntentControllersWithNonDefaultNamespaceIndex()
        {
            using TransportHarness harness = TransportHarness.CreateForTranslateSequence(
                new NodeId("server", 0),
                NodeId.Null);

            await harness.Transport.BrowseControllersAsync();

            Assert.Multiple(() =>
            {
                Assert.That(harness.BrowsePaths, Has.Count.EqualTo(2));
                Assert.That(harness.BrowsePaths[0].StartingNode, Is.EqualTo(global::Opc.Ua.ObjectIds.ObjectsFolder));
                Assert.That(harness.BrowsePaths[0].RelativePath.Elements[0].TargetName.Name, Is.EqualTo("Server"));
                Assert.That(harness.BrowsePaths[0].RelativePath.Elements[0].TargetName.NamespaceIndex, Is.Zero);
                Assert.That(harness.BrowsePaths[1].StartingNode, Is.EqualTo(new NodeId("server", 0)));
                Assert.That(harness.BrowsePaths[1].RelativePath.Elements, Has.Count.EqualTo(2));
                Assert.That(harness.BrowsePaths[1].RelativePath.Elements[0].TargetName.Name, Is.EqualTo("RobotIntent"));
                Assert.That(
                    harness.BrowsePaths[1].RelativePath.Elements[0].TargetName.NamespaceIndex,
                    Is.EqualTo(RobotIntentNamespaceIndex));
                Assert.That(harness.BrowsePaths[1].RelativePath.Elements[1].TargetName.Name, Is.EqualTo("Controllers"));
                Assert.That(
                    harness.BrowsePaths[1].RelativePath.Elements[1].TargetName.NamespaceIndex,
                    Is.EqualTo(RobotIntentNamespaceIndex));
            });
        }

        [Test]
        public void ConstructorAndRequiredArgumentsAreGuarded()
        {
            using TransportHarness harness = TransportHarness.Create(SubmitAccepted("intent-1", OperationNode));

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => new UaRobotIntentTransport(null!, ControllerNode, harness.Telemetry, harness.Streaming.Object),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("session"));
                Assert.That(
                    () => new UaRobotIntentTransport(harness.Session.Object, ControllerNode, null!, harness.Streaming.Object),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("telemetry"));
                Assert.That(
                    async () => await harness.Transport.SubmitIntentAsync(null!),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("intent"));
                Assert.That(
                    async () => await harness.Transport.SubmitMissionAsync(null!),
                    Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("mission"));
            });
        }

        private static ArrayOf<Variant> SubmitAccepted(string intentId, NodeId operation)
        {
            return
            [
                Variant.From(true),
                Variant.From(intentId),
                Variant.From(operation),
                Variant.From(IntentFailureEnum.None),
                Variant.From(new LocalizedText("accepted"))
            ];
        }

        private static ArrayOf<Variant> SubmitRefused(IntentFailureEnum failure)
        {
            return
            [
                Variant.From(false),
                Variant.From(string.Empty),
                Variant.From(NodeId.Null),
                Variant.From(failure),
                Variant.From(new LocalizedText("refused"))
            ];
        }

        private static CallMethodResult CallResult(ArrayOf<Variant> outputs, StatusCode statusCode = default)
        {
            return new CallMethodResult
            {
                StatusCode = statusCode == default(StatusCode) ? StatusCodes.Good : statusCode,
                OutputArguments = outputs
            };
        }

        private static WaitIntentDataType WaitIntent()
        {
            return new WaitIntentDataType { IntentId = "intent-client", Duration = 100 };
        }

        private static T DecodeStructure<T>(Variant variant, IServiceMessageContext context)
            where T : class, IEncodeable
        {
            T value = null!;
#pragma warning disable CS8600
            bool decoded = variant.TryGetValue(out value, context);
#pragma warning restore CS8600
            Assert.That(decoded, Is.True);
            return value!;
        }

        private static void AssertMethod(CallMethodRequest request, NodeId methodId, int inputArgumentCount)
        {
            Assert.That(request.ObjectId, Is.EqualTo(ControllerNode));
            Assert.That(request.MethodId, Is.EqualTo(methodId));
            Assert.That(request.InputArguments, Has.Count.EqualTo(inputArgumentCount));
        }

        private static void AssertStringArgument(CallMethodRequest request, int index, string expected)
        {
            Assert.That(request.InputArguments[index].TryGetValue(out string value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        private static void AssertUInt32Argument(CallMethodRequest request, int index, uint expected)
        {
            Assert.That(request.InputArguments[index].TryGetValue(out uint value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        private static void AssertDoubleArgument(CallMethodRequest request, int index, double expected)
        {
            Assert.That(request.InputArguments[index].TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        private static void AssertEnumArgument(CallMethodRequest request, int index, StopModeEnum expected)
        {
            Assert.That(request.InputArguments[index].TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo((int)expected));
        }

        private const ushort RobotIntentNamespaceIndex = 7;

        private static readonly NodeId ControllerNode = new("controller", 2);
        private static readonly NodeId OperationNode = new("operation", 2);
        private static readonly NodeId MissionNode = new("mission", 2);

        private sealed class TransportHarness : IDisposable
        {
            private TransportHarness()
            {
                Telemetry = NUnitTelemetryContext.Create(true);
                Context = ServiceMessageContext.Create(Telemetry);
                Context.NamespaceUris.Append("urn:unrelated");
                Context.NamespaceUris.Append("urn:another");
                while (Context.NamespaceUris.Count < RobotIntentNamespaceIndex)
                {
                    Context.NamespaceUris.Append("urn:filler:" + Context.NamespaceUris.Count);
                }
                Context.NamespaceUris.Append(RiNamespaces.RobotIntent);
                Context.Factory.Builder.AddOpcUaRobotIntent().Commit();

                Session.SetupGet(static s => s.MessageContext).Returns(Context);
                Session.SetupGet(static s => s.NamespaceUris).Returns(Context.NamespaceUris);
                Session.SetupGet(static s => s.Factory).Returns(Context.Factory);

                Transport = new UaRobotIntentTransport(
                    Session.Object,
                    ControllerNode,
                    Telemetry,
                    Streaming.Object);
            }

            public ServiceMessageContext Context { get; }

            public ITelemetryContext Telemetry { get; }

            public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

            public Mock<IStreamingSubscription> Streaming { get; } = new(MockBehavior.Strict);

            public UaRobotIntentTransport Transport { get; }

            public List<CallMethodRequest> Calls { get; } = new();

            public List<BrowsePath> BrowsePaths { get; } = new();

            public static TransportHarness Create(ArrayOf<Variant> outputs, StatusCode statusCode = default)
            {
                TransportHarness harness = new();
                harness.SetupCall(CallResult(outputs, statusCode));
                return harness;
            }

            public static TransportHarness CreateQueued(IReadOnlyList<CallMethodResult> results)
            {
                TransportHarness harness = new();
                int index = 0;
                harness.Session
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<CallMethodRequest>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                        (_, requests, _) => harness.Calls.Add(requests[0]))
                    .Returns(() =>
                    {
                        CallMethodResult result = results[index];
                        index++;
                        return new ValueTask<CallResponse>(CreateCallResponse(result));
                    });
                return harness;
            }

            public static TransportHarness CreateForTranslate(BrowsePathTarget target)
            {
                TransportHarness harness = new();
                harness.SetupTranslate(target);
                return harness;
            }

            public static TransportHarness CreateForTranslateSequence(params NodeId[] targets)
            {
                TransportHarness harness = new();
                int index = 0;
                harness.Session
                    .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<BrowsePath>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                        (_, paths, _) => harness.BrowsePaths.Add(paths[0]))
                    .Returns(() =>
                    {
                        NodeId target = targets[index];
                        index++;
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(target.IsNull
                            ? CreateTranslateMissResponse()
                            : CreateTranslateResponse(new BrowsePathTarget { TargetId = new ExpandedNodeId(target) }));
                    });
                return harness;
            }

            public void Dispose()
            {
            }

            public NodeId MethodNode(ExpandedNodeId methodId)
            {
                return ExpandedNodeId.ToNodeId(methodId, Context.NamespaceUris);
            }

            public CallMethodRequest SingleCall()
            {
                Assert.That(Calls, Has.Count.EqualTo(1));
                return Calls[0];
            }

            public BrowsePath SingleBrowsePath()
            {
                Assert.That(BrowsePaths, Has.Count.EqualTo(1));
                return BrowsePaths[0];
            }

            private static CallResponse CreateCallResponse(CallMethodResult result)
            {
                return new CallResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    Results = [result],
                    DiagnosticInfos = []
                };
            }

            private static TranslateBrowsePathsToNodeIdsResponse CreateTranslateResponse(BrowsePathTarget target)
            {
                return new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    Results =
                    [
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets = [target]
                        }
                    ],
                    DiagnosticInfos = []
                };
            }

            private static TranslateBrowsePathsToNodeIdsResponse CreateTranslateMissResponse()
            {
                return new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.Good },
                    Results =
                    [
                        new BrowsePathResult
                        {
                            StatusCode = StatusCodes.BadNoMatch,
                            Targets = []
                        }
                    ],
                    DiagnosticInfos = []
                };
            }

            private void SetupCall(CallMethodResult result)
            {
                Session
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<CallMethodRequest>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                        (_, requests, _) => Calls.Add(requests[0]))
                    .Returns(new ValueTask<CallResponse>(CreateCallResponse(result)));
            }

            private void SetupTranslate(BrowsePathTarget target)
            {
                Session
                    .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<BrowsePath>>(),
                        It.IsAny<CancellationToken>()))
                    .Callback<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                        (_, paths, _) => BrowsePaths.Add(paths[0]))
                    .Returns(new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(CreateTranslateResponse(target)));
            }
        }
    }
}
