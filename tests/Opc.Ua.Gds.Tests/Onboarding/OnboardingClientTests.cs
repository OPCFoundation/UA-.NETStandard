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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Gds.Client;
using Opc.Ua.Gds.Server.Onboarding;
using Opc.Ua.Onboarding;
using Opc.Ua.Tests;

namespace Opc.Ua.Gds.Tests.Onboarding
{
    /// <summary>
    /// Tests for <see cref="OnboardingClient"/>.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Onboarding")]
    public sealed class OnboardingClientTests
    {
        private static readonly NodeId kRegistrarId = new("Reg", 2);

        [Test]
        public void ConstructorRejectsNullSession()
        {
            Assert.Throws<ArgumentNullException>(
                () => new OnboardingClient(null!, kRegistrarId, CreateTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullRegistrarId()
        {
            Mock<ISession> session = CreateSessionMock(out _);
            Assert.Throws<ArgumentException>(
                () => new OnboardingClient(session.Object, NodeId.Null, CreateTelemetry()));
        }

        [Test]
        public void ConstructorRejectsNullTelemetry()
        {
            Mock<ISession> session = CreateSessionMock(out _);
            Assert.Throws<ArgumentNullException>(
                () => new OnboardingClient(session.Object, kRegistrarId, null!));
        }

        [Test]
        public async Task RegisterTicketsUsesGeneratedTypeMethodAndNativeTypes()
        {
            Mock<ISession> session = CreateSessionMock(out IServiceMessageContext messageContext);
            CallMethodRequest? observedRequest = null;
            ArrayOf<StatusCode> expected =
            [
                StatusCodes.Good,
                StatusCodes.BadEntryExists
            ];
            SetupCall(session, request =>
            {
                observedRequest = request;
                return Good(Variant.From(expected));
            });
            var client = new OnboardingClient(
                session.Object,
                kRegistrarId,
                CreateTelemetry());
            ArrayOf<ByteString> tickets =
            [
                new ByteString(new byte[] { 1, 2 }),
                new ByteString(new byte[] { 3, 4 })
            ];

            ArrayOf<StatusCode> statuses = await client
                .RegisterTicketsAsync(tickets)
                .ConfigureAwait(false);

            Assert.That(statuses, Is.EqualTo(expected));
            Assert.That(observedRequest, Is.Not.Null);
            NodeId expectedMethodId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.MethodIds.DeviceRegistrarAdminType_RegisterTickets,
                messageContext.NamespaceUris);
            Assert.That(observedRequest!.MethodId, Is.EqualTo(expectedMethodId));
            Assert.That(observedRequest.ObjectId, Is.EqualTo(kRegistrarId));
            Assert.That(
                observedRequest.InputArguments[0].TryGetValue(
                    out ArrayOf<ByteString> observedTickets),
                Is.True);
            Assert.That(observedTickets, Is.EqualTo(tickets));
            session.Verify(s => s.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(),
                It.IsAny<ArrayOf<BrowsePath>>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task RegisterTicketsFallbackUsesOnboardingBrowseNameNamespace()
        {
            Mock<ISession> session = CreateSessionMock(out IServiceMessageContext messageContext);
            var client = new OnboardingClient(
                session.Object,
                kRegistrarId,
                CreateTelemetry());
            NodeId typeMethodId = ExpandedNodeId.ToNodeId(
                Opc.Ua.Onboarding.MethodIds.DeviceRegistrarAdminType_RegisterTickets,
                messageContext.NamespaceUris);
            var instanceMethodId = new NodeId("Reg_Register", kRegistrarId.NamespaceIndex);
            var calls = new List<NodeId>();
            BrowsePath? observedPath = null;
            SetupCall(session, request =>
            {
                calls.Add(request.MethodId);
                return request.MethodId.Equals(typeMethodId)
                    ? BadMethodInvalid()
                    : Good(Variant.From(new StatusCode[] { StatusCodes.Good }.ToArrayOf()));
            });
            session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                    {
                        observedPath = paths[0];
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results =
                                [
                                    new BrowsePathResult
                                    {
                                        StatusCode = StatusCodes.Good,
                                        Targets =
                                        [
                                            new BrowsePathTarget
                                            {
                                                TargetId = new ExpandedNodeId(instanceMethodId),
                                                RemainingPathIndex = uint.MaxValue
                                            }
                                        ]
                                    }
                                ]
                            });
                    });
            ArrayOf<StatusCode> statuses = await client
                .RegisterTicketsAsync([new ByteString(new byte[] { 1 })])
                .ConfigureAwait(false);

            Assert.That(
                statuses,
                Is.EqualTo(new StatusCode[] { StatusCodes.Good }.ToArrayOf()));
            Assert.That(calls, Is.EqualTo(new[] { typeMethodId, instanceMethodId }));
            Assert.That(observedPath, Is.Not.Null);
            RelativePathElement element = observedPath!.RelativePath.Elements[0];
            Assert.That(
                element.TargetName.Name,
                Is.EqualTo(Opc.Ua.Onboarding.BrowseNames.RegisterTickets));
            Assert.That(
                element.TargetName.NamespaceIndex,
                Is.EqualTo((ushort)messageContext.NamespaceUris.GetIndex(
                    Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding)));
        }

        [Test]
        public async Task UnregisterTicketsReturnsStatusCodes()
        {
            Mock<ISession> session = CreateSessionMock(out _);
            SetupCall(
                session,
                _ => Good(Variant.From(
                    new StatusCode[] { StatusCodes.BadNotFound }.ToArrayOf())));
            var client = new OnboardingClient(
                session.Object,
                kRegistrarId,
                CreateTelemetry());

            ArrayOf<StatusCode> statuses = await client
                .UnregisterTicketsAsync([new ByteString(new byte[] { 0xAA })])
                .ConfigureAwait(false);

            Assert.That(
                statuses,
                Is.EqualTo(new StatusCode[] { StatusCodes.BadNotFound }.ToArrayOf()));
        }

        private static Mock<ISession> CreateSessionMock(
            out IServiceMessageContext messageContext)
        {
            ITelemetryContext telemetry = CreateTelemetry();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(
                Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            messageContext = context;

            var mock = new Mock<ISession>(MockBehavior.Strict);
            mock.SetupGet(s => s.MessageContext).Returns(context);
            mock.SetupGet(s => s.NamespaceUris).Returns(namespaceUris);
            return mock;
        }

        private static ITelemetryContext CreateTelemetry()
        {
            return NUnitTelemetryContext.Create();
        }

        private static void SetupCall(
            Mock<ISession> session,
            Func<CallMethodRequest, CallMethodResult> handler)
        {
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                        new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [handler(requests[0])]
                        }));
        }

        private static CallMethodResult Good(params Variant[] outputs)
        {
            return new CallMethodResult
            {
                StatusCode = StatusCodes.Good,
                OutputArguments = outputs
            };
        }

        private static CallMethodResult BadMethodInvalid()
        {
            return new CallMethodResult
            {
                StatusCode = StatusCodes.BadMethodInvalid
            };
        }
    }

    /// <summary>
    /// Exercises the generated Part 21 model, client proxy fallback, method
    /// validation and ticket-store bindings as one in-process call chain.
    /// </summary>
    [TestFixture]
    [Category("Onboarding")]
    public sealed class OnboardingEndToEndTests
    {
        [Test]
        public async Task GeneratedRegistrarAndClientRoundTripTickets()
        {
            (SystemContext context, DeviceRegistrarAdminState registrar) =
                CreateGeneratedRegistrar();
            var store = new MemoryTicketStore();
            registrar.BindToTicketStore(store);
            Mock<ISession> session = CreateSessionBridge(context, registrar);
            var client = new OnboardingClient(
                session.Object,
                registrar.NodeId,
                context.Telemetry);
            ArrayOf<ByteString> tickets =
            [
                new ByteString("ticket-one"u8.ToArray()),
                new ByteString("ticket-two"u8.ToArray())
            ];

            ArrayOf<StatusCode> registered = await client
                .RegisterTicketsAsync(tickets)
                .ConfigureAwait(false);
            ArrayOf<StatusCode> removed = await client
                .UnregisterTicketsAsync([tickets[0]])
                .ConfigureAwait(false);
            ArrayOf<StatusCode> removedAgain = await client
                .UnregisterTicketsAsync([tickets[0]])
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    registered,
                    Is.EqualTo(new StatusCode[]
                    {
                        StatusCodes.Good,
                        StatusCodes.Good
                    }.ToArrayOf()));
                Assert.That(
                    removed,
                    Is.EqualTo(new StatusCode[] { StatusCodes.Good }.ToArrayOf()));
                Assert.That(
                    removedAgain,
                    Is.EqualTo(new StatusCode[] { StatusCodes.BadNotFound }.ToArrayOf()));
            });

            var remaining = new List<TicketRecord>();
            await foreach (TicketRecord record in store.ListAsync().ConfigureAwait(false))
            {
                remaining.Add(record);
            }
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(
                remaining[0].EncodedTicket,
                Is.EqualTo(tickets[1].ToArray()));
        }

        private static (SystemContext Context, DeviceRegistrarAdminState Registrar)
            CreateGeneratedRegistrar()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Gds.Namespaces.OpcUaGds);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var typeTable = new TypeTable(messageContext.NamespaceUris);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.ByteString, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.DataTypeIds.EncodedTicket,
                Opc.Ua.DataTypeIds.ByteString);
            var context = new SystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory,
                TypeTable = typeTable
            };
            NodeStateCollection nodes = new NodeStateCollection()
                .AddOpcUaGds(context)
                .AddOpcUaOnboarding(context);
            Assert.That(
                nodes.Any(node => node.NodeId.Equals(ExpandedNodeId.ToNodeId(
                    Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType,
                    context.NamespaceUris))),
                Is.True,
                "The Onboarding model's GDS datatype dependency must be loaded.");
            DeviceRegistrarState root = nodes.OfType<DeviceRegistrarState>().Single();
            return (context, root.Administration ??
                throw new InvalidOperationException(
                    "The generated DeviceRegistrar has no Administration child."));
        }

        private static Mock<ISession> CreateSessionBridge(
            SystemContext context,
            DeviceRegistrarAdminState registrar)
        {
            var session = new Mock<ISession>(MockBehavior.Strict);
            ServiceMessageContext messageContext = ServiceMessageContext.Create(context.Telemetry);
            messageContext.NamespaceUris = context.NamespaceUris;
            messageContext.ServerUris = context.ServerUris;
            session.SetupGet(s => s.MessageContext).Returns(messageContext);
            session.SetupGet(s => s.NamespaceUris).Returns(context.NamespaceUris);
            session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                        new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            ResolvePaths(context, registrar, paths)));
            session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, ct) => InvokeMethodsAsync(
                        context,
                        registrar,
                        requests,
                        ct));
            return session;
        }

        private static TranslateBrowsePathsToNodeIdsResponse ResolvePaths(
            ISystemContext context,
            DeviceRegistrarAdminState registrar,
            ArrayOf<BrowsePath> paths)
        {
            var results = new BrowsePathResult[paths.Count];
            for (int i = 0; i < paths.Count; i++)
            {
                BrowsePath path = paths[i];
                NodeState? current = path.StartingNode.Equals(registrar.NodeId)
                    ? registrar
                    : null;
                foreach (RelativePathElement element in path.RelativePath.Elements)
                {
                    current = current?.FindChild(context, element.TargetName);
                    if (current == null)
                    {
                        break;
                    }
                }
                results[i] = current == null
                    ? new BrowsePathResult
                    {
                        StatusCode = StatusCodes.BadNoMatch,
                        Targets = []
                    }
                    : new BrowsePathResult
                    {
                        StatusCode = StatusCodes.Good,
                        Targets =
                        [
                            new BrowsePathTarget
                            {
                                TargetId = new ExpandedNodeId(current.NodeId),
                                RemainingPathIndex = uint.MaxValue
                            }
                        ]
                    };
            }
            return new TranslateBrowsePathsToNodeIdsResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = results
            };
        }

        private static async ValueTask<CallResponse> InvokeMethodsAsync(
            ISystemContext context,
            DeviceRegistrarAdminState registrar,
            ArrayOf<CallMethodRequest> requests,
            CancellationToken cancellationToken)
        {
            var results = new CallMethodResult[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                CallMethodRequest request = requests[i];
                MethodState? method = FindInstanceMethod(registrar, request.MethodId);
                if (method == null)
                {
                    results[i] = new CallMethodResult
                    {
                        StatusCode = StatusCodes.BadMethodInvalid,
                        InputArgumentResults = [],
                        OutputArguments = []
                    };
                    continue;
                }

                var argumentErrors = new List<ServiceResult>();
                var outputs = new List<Variant>();
                ServiceResult result = await method.CallAsync(
                        context,
                        request.ObjectId,
                        request.InputArguments,
                        argumentErrors,
                        outputs,
                        cancellationToken)
                    .ConfigureAwait(false);
                results[i] = new CallMethodResult
                {
                    StatusCode = result.StatusCode,
                    InputArgumentResults = argumentErrors
                        .Select(error => error.StatusCode)
                        .ToArrayOf(),
                    OutputArguments = outputs.ToArrayOf()
                };
            }
            return new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = results
            };
        }

        private static MethodState? FindInstanceMethod(
            DeviceRegistrarAdminState registrar,
            NodeId methodId)
        {
            if (registrar.RegisterTickets?.NodeId.Equals(methodId) == true)
            {
                return registrar.RegisterTickets;
            }
            if (registrar.UnregisterTickets?.NodeId.Equals(methodId) == true)
            {
                return registrar.UnregisterTickets;
            }
            return null;
        }
    }
}
