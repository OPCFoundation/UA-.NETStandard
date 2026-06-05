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

// CA2000: integration-test disposables are released by helper cleanup paths.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

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
        [Ignore("Depends on channel-manager reset support after a Faulted entry. " +
            "The current manager keeps a faulted leased entry and subsequent outer retries " +
            "cannot acquire a fresh cycle without additional production plumbing.")]
        public void ChannelManagerExhaustionEscalatesAndRecoversWhenServerReturns()
        {
        }
    }
}
