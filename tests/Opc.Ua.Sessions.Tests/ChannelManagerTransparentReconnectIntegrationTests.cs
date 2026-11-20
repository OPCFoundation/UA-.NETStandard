/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

// CA2000: integration-test disposables are released by helper cleanup paths.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// Live-server tests for transparent channel-manager reconnect behavior.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("ChannelManager")]
    [Category("Reconnect")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ChannelManagerTransparentReconnectIntegrationTests
        : ChannelManagerIntegrationTestBase
    {
        private static readonly StatusCode[] s_reactivationSecurityFailureStatusCodes =
        [
            StatusCodes.BadApplicationSignatureInvalid,
            StatusCodes.BadSecurityChecksFailed,
            StatusCodes.BadIdentityChangeNotSupported,
            StatusCodes.BadSecureChannelIdInvalid
        ];

        [Test]
        [CancelAfter(120_000)]
        public async Task ChannelManagerReconnectDoesNotChurnOuterManagedSessionStateAsync(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 3
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionType? session = null;

            try
            {
                session = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ChannelManagerReconnectDoesNotChurnOuterManagedSessionStateAsync),
                    ct).ConfigureAwait(false);

                NodeId originalSessionId = session.SessionId;
                IManagedTransportChannel channel = GetManagedChannel(session);

                // The connect can return before the state machine dispatched the notification of
                // the initial Connecting -> Connected transition, so a handler attached now may
                // still receive it. Churn caused by the reconnect leaves Connected and is a
                // transition of its own.
                var outerTransitions = new ConcurrentQueue<(ConnectionState Previous, ConnectionState New)>();
                var channelStates = new ConcurrentQueue<ChannelState>();
                session.ConnectionStateChanged += (_, e) => outerTransitions.Enqueue((e.PreviousState, e.NewState));
                session.ChannelStateChanged += (_, e) => channelStates.Enqueue(e.NewState);

                await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

                Assert.That(
                    await WaitForAsync(
                        () => channelStates.Contains(ChannelState.Ready),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "ManagedSession.ChannelStateChanged should report the manager reconnect cycle.");

                (ConnectionState Previous, ConnectionState New)[] churn = [.. outerTransitions
                    .Where(transition => transition is not
                        (ConnectionState.Connecting, ConnectionState.Connected))];
                Assert.That(channelStates, Has.Member(ChannelState.TransportReconnecting));
                Assert.That(channelStates, Has.Member(ChannelState.TransportConnectedSessionReactivating));
                Assert.That(channelStates, Has.Member(ChannelState.Ready));
                Assert.That(churn, Is.Empty,
                    "The manager reconnect must not change the outer ManagedSession state.");
                Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));
                Assert.That(session.SessionId, Is.EqualTo(originalSessionId));

                await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(120_000)]
        [DependsOnTest(nameof(ChannelManagerReconnectDoesNotChurnOuterManagedSessionStateAsync), AllowFailure = true)]
        public async Task AnonymousSignChannelManagerReconnectRecreatesSessionAsync(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 3
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.Basic256Sha256)
                .ConfigureAwait(false);
            endpoint.Description.SecurityMode = MessageSecurityMode.Sign;
            ManagedSessionType? session = null;

            try
            {
                session = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(AnonymousSignChannelManagerReconnectRecreatesSessionAsync),
                    ct).ConfigureAwait(false);
                NodeId originalSessionId = session.SessionId;
                IManagedTransportChannel channel = GetManagedChannel(session);

                await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

                Assert.That(session.SessionId, Is.Not.EqualTo(originalSessionId));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [TestCaseSource(nameof(s_reactivationSecurityFailureStatusCodes))]
        [CancelAfter(120_000)]
        [DependsOnTest(nameof(AnonymousSignChannelManagerReconnectRecreatesSessionAsync), AllowFailure = true)]
        public async Task ReactivationSecurityFailureRecreatesSessionAsync(
            StatusCode statusCode,
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 3
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionType? session = null;
            IServiceResponseMutator? originalResponseMutator = ReferenceServer.ResponseMutator;
            var mockController = new MockResponseController();

            try
            {
                session = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ReactivationSecurityFailureRecreatesSessionAsync),
                    ct).ConfigureAwait(false);
                NodeId originalSessionId = session.SessionId;
                IManagedTransportChannel channel = GetManagedChannel(session);
                ReferenceServer.ResponseMutator = mockController;
                using IDisposable expectation = mockController
                    .ExpectNextResponse<ActivateSessionResponse>(
                        response => response.ResponseHeader.ServiceResult = statusCode);

                await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

                Assert.That(session.SessionId, Is.Not.EqualTo(originalSessionId));
                Assert.That(channel.State, Is.EqualTo(ChannelState.Ready));
                await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
            }
            finally
            {
                ReferenceServer.ResponseMutator = originalResponseMutator;
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [CancelAfter(120_000)]
        [DependsOnTest(nameof(ReactivationSecurityFailureRecreatesSessionAsync), AllowFailure = true)]
        public async Task ChannelManagerExhaustionEscalatesAndRecoversWhenServerReturns(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 1
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionType? session = null;
            int serverPort = ServerFixture.Port;
            bool serverStopped = false;

            try
            {
                session = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ChannelManagerExhaustionEscalatesAndRecoversWhenServerReturns),
                    ct).ConfigureAwait(false);

                session.KeepAliveInterval = 60_000;
                IManagedTransportChannel channel = GetManagedChannel(session);
                var channelStates = new ConcurrentQueue<ChannelState>();
                session.ChannelStateChanged += (_, e) => channelStates.Enqueue(e.NewState);

                await ServerFixture.StopAsync().ConfigureAwait(false);
                serverStopped = true;

                // The channel's background monitor races StopAsync; by the time
                // we call ReconnectAsync the lease's entry may already be Faulted
                // and SwapFaultedEntryAsync will attempt OpenInitial on a fresh
                // entry — which throws because the server is down. That is the
                // expected exhaustion path; swallow the transport-level
                // SocketException and let the assertions below verify the
                // Faulted state and the manager's eventual recovery.
                try
                {
                    await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                }
                catch (ServiceResultException sre) when (sre.InnerException is SocketException)
                {
                }

                Assert.That(
                    await WaitForAsync(
                        () => channelStates.Contains(ChannelState.Faulted),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The exhausted reconnect cycle should transition the entry to Faulted.");
                Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
                // GetDiagnostic intentionally omitted: when the swap's
                // OpenInitial fails on a down server the fresh entry's
                // DisposeAsync removes its record from the manager, leaving
                // no diagnostic for the key. The channel.State assertion
                // above already covers the observable Faulted contract.

                ReferenceServer = await ServerFixture.StartAsync(PkiRoot, serverPort).ConfigureAwait(false);
                ReferenceServer.TokenValidator = TokenValidator;
                serverStopped = false;

                Assert.That(
                    await ReconnectUntilReadyAsync(
                        manager,
                        channel,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The manager should recover after the restarted server accepts session services.");

                Assert.That(
                    await WaitForAsync(
                        () => channelStates.Contains(ChannelState.Ready),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The swapped entry should recover to Ready after the server returns.");
                // channel.State is a snapshot of the current state; poll
                // for Ready instead of asserting against the snapshot the
                // first WaitForAsync caught — on a slow Windows net48
                // runner the channel can flap Ready → Faulted → Ready
                // before the snapshot is taken, leaving the assertion
                // reading Faulted. The diagnostic snapshot below uses
                // the same lock-protected manager state so both polls
                // converge on the post-swap Ready entry.
                Assert.That(
                    await WaitForAsync(
                        () => channel.State == ChannelState.Ready,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Channel handle should observe Ready after the swap completes.");
                Assert.That(
                    await WaitForAsync(
                        () => GetDiagnostic(manager, channel.Key).State == ChannelState.Ready,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Manager diagnostics should observe Ready after the swap completes.");

                // The participant returned RequiresSessionRecreate from OnReconnectAsync
                // (the server lost the session id while down). The manager awaits
                // Session.RecreateAsync before reporting Ready; retain the retry loop
                // here for transient service startup lag after the server restart.
                Assert.That(
                    await WaitForAsync(
                        () => TryReadServerStatus(session),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Session should be recreated and able to read ServerStatus after the channel swap.");

                await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
            }
            finally
            {
                if (serverStopped)
                {
                    ReferenceServer = await ServerFixture.StartAsync(PkiRoot, serverPort)
                        .ConfigureAwait(false);
                    ReferenceServer.TokenValidator = TokenValidator;
                }

                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A swap onto a replacement channel entry (server down past the reconnect
        /// budget, then back) must keep encoding with the session's namespace table,
        /// server table and encodeable factory. A replacement entry with a fresh
        /// message context knows namespace 0 only: server structures are written
        /// with an empty type id (the write still reports Good) and read back as
        /// raw bodies.
        /// </summary>
        [Test]
        [CancelAfter(180_000)]
        [DependsOnTest(nameof(ChannelManagerExhaustionEscalatesAndRecoversWhenServerReturns), AllowFailure = true)]
        public async Task ChannelSwapKeepsSessionMessageContextAsync(CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager(
                new ExponentialBackoffChannelReconnectPolicy
                {
                    MinDelay = TimeSpan.Zero,
                    MaxDelay = TimeSpan.Zero,
                    MaxAttempts = 1
                });
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionType? session = null;
            int serverPort = ServerFixture.Port;
            bool serverStopped = false;

            try
            {
                session = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ChannelSwapKeepsSessionMessageContextAsync),
                    ct).ConfigureAwait(false);

                session.KeepAliveInterval = 60_000;
                IManagedTransportChannel channel = GetManagedChannel(session);
                var channelStates = new ConcurrentQueue<ChannelState>();
                session.ChannelStateChanged += (_, e) => channelStates.Enqueue(e.NewState);

                // Stands in for the complex type system: a server (ns > 0) structure
                // registered in the session's factory.
                string serverNamespace = Quickstarts.ReferenceServer.Namespaces.ReferenceServer;
                var encodingId = new ExpandedNodeId("SwapStructure_Encoding_DefaultBinary", serverNamespace);
                session.InnerSession.Factory.Builder.AddType(encodingId, typeof(Argument)).Commit();

                IServiceMessageContext contextBefore = session.InnerSession.TransportChannel.MessageContext;
                AssertSessionTablesUsedByChannel(session, "before the swap");
                await AssertStructureRoundTripAsync(session, encodingId, "before the swap", ct)
                    .ConfigureAwait(false);

                await ServerFixture.StopAsync().ConfigureAwait(false);
                serverStopped = true;
                try
                {
                    await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);
                }
                catch (SocketException)
                {
                }
                catch (ServiceResultException)
                {
                }
                Assert.That(
                    await WaitForAsync(
                        () => channelStates.Contains(ChannelState.Faulted),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The exhausted reconnect cycle should fault the entry.");

                ReferenceServer = await ServerFixture.StartAsync(PkiRoot, serverPort).ConfigureAwait(false);
                ReferenceServer.TokenValidator = TokenValidator;
                serverStopped = false;

                Assert.That(
                    await ReconnectUntilReadyAsync(manager, channel, DefaultWait, ct).ConfigureAwait(false),
                    Is.True,
                    "The lease should swap onto a replacement entry once the server is back.");
                Assert.That(
                    await WaitForAsync(() => TryReadServerStatus(session), DefaultWait, ct)
                        .ConfigureAwait(false),
                    Is.True,
                    "The session should serve reads after the swap.");
                Assert.That(
                    await WaitForStateAsync(session, ConnectionState.Connected, DefaultWait, ct)
                        .ConfigureAwait(false),
                    Is.True);

                Assert.That(
                    session.InnerSession.TransportChannel.MessageContext,
                    Is.Not.SameAs(contextBefore),
                    "Precondition: the lease must be on a replacement transport.");
                AssertSessionTablesUsedByChannel(session, "after the swap");
                await AssertStructureRoundTripAsync(session, encodingId, "after the swap", ct)
                    .ConfigureAwait(false);

                ISessionClient binding = await session.CreateBindingAsync(ct).ConfigureAwait(false);
                (binding as IDisposable)?.Dispose();
            }
            finally
            {
                if (serverStopped)
                {
                    ReferenceServer = await ServerFixture.StartAsync(PkiRoot, serverPort)
                        .ConfigureAwait(false);
                    ReferenceServer.TokenValidator = TokenValidator;
                }

                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        private static void AssertSessionTablesUsedByChannel(ManagedSessionType session, string when)
        {
            Session inner = session.InnerSession;
            IServiceMessageContext channelContext = inner.TransportChannel.MessageContext;
            Assert.Multiple(() =>
            {
                Assert.That(inner.NamespaceUris.Count, Is.GreaterThan(1), when);
                Assert.That(channelContext.NamespaceUris, Is.SameAs(inner.NamespaceUris),
                    $"The channel must encode with the session's namespace table {when}.");
                Assert.That(channelContext.ServerUris, Is.SameAs(inner.ServerUris),
                    $"The channel must encode with the session's server table {when}.");
                Assert.That(channelContext.Factory, Is.SameAs(inner.Factory),
                    $"The channel must decode with the session's encodeable factory {when}.");
            });
        }

        private static async Task AssertStructureRoundTripAsync(
            ManagedSessionType session,
            ExpandedNodeId encodingId,
            string when,
            CancellationToken ct)
        {
            ushort serverIndex = (ushort)session.InnerSession.NamespaceUris.GetIndex(
                Quickstarts.ReferenceServer.Namespaces.ReferenceServer);
            var variable = new NodeId("Scalar_Static_Variant", serverIndex);

            // Binary body of Argument { Name = "sw", DataType = i=0, ValueRank = -1 },
            // sent as a raw body under the absolute (nsu=) encoding id.
            var body = ByteString.From(
                2, 0, 0, 0, 0x73, 0x77, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0);
            var nodesToWrite = new WriteValue[]
            {
                new()
                {
                    NodeId = variable,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(new ExtensionObject(encodingId, body)))
                }
            };
            WriteResponse response = await session.WriteAsync(null, nodesToWrite, ct).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.EqualTo(StatusCodes.Good), when);

            DataValue value = await session.ReadValueAsync(variable, ct).ConfigureAwait(false);
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True, when);
            NodeId expectedTypeId = ExpandedNodeId.ToNodeId(encodingId, session.InnerSession.NamespaceUris);
            Assert.Multiple(() =>
            {
                Assert.That(
                    ExpandedNodeId.ToNodeId(extension.TypeId, session.InnerSession.NamespaceUris),
                    Is.EqualTo(expectedTypeId),
                    $"The structure type id must survive the round trip {when}.");
                Assert.That(extension.TryGetValue(out IEncodeable? decoded), Is.True,
                    $"The structure must decode with the session's factory {when}.");
                Assert.That(decoded, Is.TypeOf<Argument>(), when);
                Assert.That(((Argument?)decoded)?.Name, Is.EqualTo("sw"), when);
            });
        }

        private static bool TryReadServerStatus(ManagedSessionType session)
        {
            try
            {
                DataValue value = session
                    .ReadValueAsync(VariableIds.Server_ServerStatus_State, CancellationToken.None)
                    .GetAwaiter().GetResult();
                return !value.IsNull && StatusCode.IsGood(value.StatusCode);
            }
            catch (ServiceResultException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async Task<bool> ReconnectUntilReadyAsync(
            ClientChannelManager manager,
            IManagedTransportChannel channel,
            TimeSpan timeout,
            CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token,
                ct);

            while (!linkedCts.IsCancellationRequested)
            {
                try
                {
                    await manager.ReconnectAsync(channel, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                {
                    break;
                }
                catch (SocketException)
                {
                }
                catch (ServiceResultException sre) when (
                    sre.StatusCode == StatusCodes.BadServerHalted ||
                    sre.StatusCode == StatusCodes.BadSecureChannelClosed ||
                    sre.InnerException is SocketException)
                {
                }

                if (channel.State == ChannelState.Ready)
                {
                    return true;
                }

                try
                {
                    await Task.Delay(PollInterval, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                {
                    break;
                }
            }

            return channel.State == ChannelState.Ready;
        }
    }
}
