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
using System.Text;
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
        private static readonly ArrayOf<string> s_expectedJsonTickets = ["{\"payload\":\"test\"}"];

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
            ArrayOf<string> tickets =
            [
                "first-ticket",
                "second-ticket"
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
                    out ArrayOf<string> observedTickets),
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
                .RegisterTicketsAsync(["ticket"])
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
                .UnregisterTicketsAsync(["ticket"])
                .ConfigureAwait(false);

            Assert.That(
                statuses,
                Is.EqualTo(new StatusCode[] { StatusCodes.BadNotFound }.ToArrayOf()));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyTicketOverloadsSendStrings(bool remove)
        {
            Mock<ISession> session = CreateSessionMock(out _);
            CallMethodRequest? observed = null;
            SetupCall(session, request =>
            {
                observed = request;
                return Good(Variant.From(new StatusCode[] { StatusCodes.Good }.ToArrayOf()));
            });
            var client = new OnboardingClient(session.Object, kRegistrarId, CreateTelemetry());
            ArrayOf<ByteString> tickets = [ByteString.From("{\"payload\":\"test\"}"u8)];

            ArrayOf<StatusCode> statuses = await LegacyTicketCall(client, remove)(
                tickets, CancellationToken.None).ConfigureAwait(false);

            Assert.That(statuses, Is.EqualTo(new StatusCode[] { StatusCodes.Good }.ToArrayOf()));
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.InputArguments[0].TryGetValue(out ArrayOf<string> sent), Is.True);
            Assert.That(sent, Is.EqualTo(s_expectedJsonTickets));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyTicketOverloadsPreserveNullAndEmptyArrays(bool remove)
        {
            Mock<ISession> session = CreateSessionMock(out _);
            var observed = new List<ArrayOf<string>>();
            SetupCall(session, request =>
            {
                Assert.That(request.InputArguments[0].TryGetValue(out ArrayOf<string> sent), Is.True);
                observed.Add(sent);
                return Good(Variant.From(ArrayOf<StatusCode>.Empty));
            });
            var client = new OnboardingClient(session.Object, kRegistrarId, CreateTelemetry());
            Func<ArrayOf<ByteString>, CancellationToken, ValueTask<ArrayOf<StatusCode>>> invoke =
                LegacyTicketCall(client, remove);

            ArrayOf<StatusCode> nullResult = await invoke(ArrayOf<ByteString>.Null, CancellationToken.None)
                .ConfigureAwait(false);
            ArrayOf<StatusCode> emptyResult = await invoke(ArrayOf<ByteString>.Empty, CancellationToken.None)
                .ConfigureAwait(false);

            Assert.That(observed, Has.Count.EqualTo(2));
            Assert.That(observed[0].IsNull, Is.True);
            Assert.That(observed[1].IsNull, Is.False);
            Assert.That(observed[1].Count, Is.Zero);
            Assert.That(nullResult.IsNull, Is.False);
            Assert.That(nullResult.Count, Is.Zero);
            Assert.That(emptyResult.IsNull, Is.False);
            Assert.That(emptyResult.Count, Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyTicketOverloadsPreserveNullElementsAndPerTicketResults(bool remove)
        {
            Mock<ISession> session = CreateSessionMock(out _);
            CallMethodRequest? observed = null;
            ArrayOf<StatusCode> expected =
            [
                StatusCodes.Good, StatusCodes.BadTicketInvalid, StatusCodes.BadTicketInvalid, StatusCodes.BadEntryExists
            ];
            SetupCall(session, request =>
            {
                observed = request;
                return Good(Variant.From(expected));
            });
            var client = new OnboardingClient(session.Object, kRegistrarId, CreateTelemetry());
            ArrayOf<ByteString> tickets =
            [
                ByteString.From("before"u8), default, ByteString.Empty, ByteString.From("after"u8)
            ];

            ArrayOf<StatusCode> statuses = await LegacyTicketCall(client, remove)(
                tickets, CancellationToken.None).ConfigureAwait(false);

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.InputArguments[0].TryGetValue(out ArrayOf<string> sent), Is.True);
            Assert.That(sent.IsNull, Is.False);
            Assert.That(sent.Count, Is.EqualTo(4));
            Assert.That(sent[0], Is.EqualTo("before"));
            Assert.That(sent[1], Is.Null);
            Assert.That(sent[2], Is.EqualTo(string.Empty));
            Assert.That(sent[3], Is.EqualTo("after"));
            Assert.That(statuses, Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LegacyTicketOverloadsRejectInvalidUtf8BeforeDispatch(bool remove)
        {
            Mock<ISession> session = CreateSessionMock(out _);
            var client = new OnboardingClient(session.Object, kRegistrarId, CreateTelemetry());
            session.Invocations.Clear();
            ArrayOf<ByteString> tickets = [ByteString.From(new byte[] { 0xC3, 0x28 })];
            Func<ArrayOf<ByteString>, CancellationToken, ValueTask<ArrayOf<StatusCode>>> invoke =
                LegacyTicketCall(client, remove);

            await Assert.ThrowsAsync<DecoderFallbackException>(async () =>
                await invoke(tickets, CancellationToken.None).ConfigureAwait(false));
            session.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Resolves the exact compatibility overload without disabling obsolete-use diagnostics for the tests.
        /// </summary>
        private static Func<ArrayOf<ByteString>, CancellationToken, ValueTask<ArrayOf<StatusCode>>> LegacyTicketCall(
            OnboardingClient client,
            bool remove)
        {
            string methodName = remove
                ? nameof(OnboardingClient.UnregisterTicketsAsync)
                : nameof(OnboardingClient.RegisterTicketsAsync);
            var method = typeof(OnboardingClient).GetMethod(methodName, s_legacyTicketSignature) ??
                throw new AssertionException("The legacy byte-ticket overload is missing.");
#if NETFRAMEWORK
            return (Func<ArrayOf<ByteString>, CancellationToken, ValueTask<ArrayOf<StatusCode>>>)
                method.CreateDelegate(
                    typeof(Func<ArrayOf<ByteString>, CancellationToken, ValueTask<ArrayOf<StatusCode>>>), client);
#else
            return method.CreateDelegate<
                Func<ArrayOf<ByteString>, CancellationToken, ValueTask<ArrayOf<StatusCode>>>>(client);
#endif
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

        /// <summary>The exact retained byte-ticket signature, separate from the corrected string API.</summary>
        private static readonly Type[] s_legacyTicketSignature =
            [typeof(ArrayOf<ByteString>), typeof(CancellationToken)];
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
            (SessionSystemContext context, DeviceRegistrarAdminState registrar) =
                CreateGeneratedRegistrar();
            var store = new MemoryTicketStore();
            var validator = new Mock<IOnboardingTicketValidator>(MockBehavior.Strict);
            validator.Setup(value => value.ValidateAsync(
                It.IsAny<string>(), OnboardingTicketSource.OutOfBand, It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ValidatedOnboardingTicket>(new ValidatedOnboardingTicket(
                    new DeviceIdentityTicketType
                    {
                        ManufacturerName = "Test Manufacturer",
                        ProductInstanceUri = "urn:test:device"
                    }, string.Empty)));
            registrar.BindToTicketStore(store, validator.Object);
            Mock<ISession> session = CreateSessionBridge(context, registrar);
            var client = new OnboardingClient(
                session.Object,
                registrar.NodeId,
                context.Telemetry);
            ArrayOf<string> tickets =
            [
                "ticket-one",
                "ticket-two"
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
                Is.EqualTo(Encoding.UTF8.GetBytes(tickets[1])));
        }

        private static (SessionSystemContext Context, DeviceRegistrarAdminState Registrar)
            CreateGeneratedRegistrar()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Gds.Namespaces.OpcUaGds);
            messageContext.NamespaceUris.GetIndexOrAppend(
                Opc.Ua.Onboarding.Namespaces.OpcUaOnboarding);
            var typeTable = new TypeTable(messageContext.NamespaceUris);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.String, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.DataTypeIds.EncodedTicket,
                Opc.Ua.DataTypeIds.String);
            var context = new SessionSystemContext(telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris,
                ServerUris = messageContext.ServerUris,
                EncodeableFactory = messageContext.Factory,
                TypeTable = typeTable,
                UserIdentity = new Opc.Ua.Server.RoleBasedIdentity(
                    new UserIdentity(),
                    [new Opc.Ua.Server.Role(
                        Opc.Ua.Onboarding.ObjectIds.WellKnownRole_RegistrarAdmin, "RegistrarAdmin")],
                    messageContext.NamespaceUris)
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
            SessionSystemContext context,
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
