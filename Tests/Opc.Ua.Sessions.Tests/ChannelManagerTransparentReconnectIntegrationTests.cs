/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
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
        [Test]
        [Order(100)]
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

                IManagedTransportChannel channel = GetManagedChannel(session);
                var outerStates = new ConcurrentQueue<ConnectionState>();
                var channelStates = new ConcurrentQueue<ChannelState>();
                session.ConnectionStateChanged += (_, e) => outerStates.Enqueue(e.NewState);
                session.ChannelStateChanged += (_, e) => channelStates.Enqueue(e.NewState);

                await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

                Assert.That(
                    await WaitForAsync(
                        () => channelStates.Contains(ChannelState.Ready),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "ManagedSession.ChannelStateChanged should report the manager reconnect cycle.");

                Assert.That(channelStates, Has.Member(ChannelState.TransportReconnecting));
                Assert.That(channelStates, Has.Member(ChannelState.TransportConnectedSessionReactivating));
                Assert.That(channelStates, Has.Member(ChannelState.Ready));
                Assert.That(outerStates, Has.No.Member(ConnectionState.Reconnecting));
                Assert.That(outerStates, Has.No.Member(ConnectionState.Connected));
                Assert.That(session.StateMachine.State, Is.EqualTo(ConnectionState.Connected));

                await AssertReadServerStatusAsync(session, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(120_000)]
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

                await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

                Assert.That(
                    await WaitForAsync(
                        () => channelStates.Contains(ChannelState.Faulted),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The exhausted reconnect cycle should transition the entry to Faulted.");
                Assert.That(channel.State, Is.EqualTo(ChannelState.Faulted));
                Assert.That(GetDiagnostic(manager, channel.Key).State, Is.EqualTo(ChannelState.Faulted));

                ReferenceServer = await ServerFixture.StartAsync(PkiRoot, serverPort).ConfigureAwait(false);
                ReferenceServer.TokenValidator = TokenValidator;
                serverStopped = false;

                await manager.ReconnectAsync(channel, ct).ConfigureAwait(false);

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
                // (the server lost the session id while down). The manager dispatches
                // Session.RecreateAsync fire-and-forget; poll the read until the
                // recreate has installed a fresh server-side session id. On a slow
                // CI runner (macOS) the fire-and-forget can lose the race against
                // an immediate Read.
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
                    ReferenceServer = await ServerFixture.StartAsync(PkiRoot, serverPort).ConfigureAwait(false);
                    ReferenceServer.TokenValidator = TokenValidator;
                }

                await CloseAndDisposeAsync(session).ConfigureAwait(false);
            }
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
    }
}
