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
using System.Collections.Generic;
using System.Linq;
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
    /// Shared live-server infrastructure for channel-manager integration tests.
    /// </summary>
    [NonParallelizable]
    public abstract class ChannelManagerIntegrationTestBase : ClientTestFramework
    {
        /// <summary>
        /// Upper bound for WaitForAsync polls (predicates return as soon as they
        /// are satisfied, so a larger budget never slows a healthy run). Sized to
        /// tolerate a server stop + restart + transparent reconnect on a heavily
        /// loaded CI agent: the macOS runners exceeded the previous 30s ceiling on
        /// the post-restart recovery step. Stays well within the per-test
        /// [CancelAfter(120_000..180_000)] budgets since only one poll per test is
        /// genuinely slow.
        /// </summary>
        protected static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(60);
        protected static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            SupportsExternalServerUrl = false;
            SingleSession = false;
            return OneTimeSetUpCoreAsync(securityNone: true);
        }

        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        protected ClientChannelManager CreateChannelManager(
            IChannelReconnectPolicy? reconnectPolicy = null)
        {
            return new ClientChannelManager(
                ClientFixture.Config,
                Telemetry,
                reconnectPolicy: reconnectPolicy);
        }

        protected Task<ConfiguredEndpoint> GetEndpointAsync(
            string securityPolicy,
            Uri? serverUrl = null)
        {
            return ClientFixture.GetEndpointAsync(
                serverUrl ?? ServerUrl,
                securityPolicy);
        }

        protected Task<ManagedSessionType> ConnectManagedSessionAsync(
            ConfiguredEndpoint endpoint,
            IClientChannelManager manager,
            string sessionName,
            CancellationToken ct)
        {
            return new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(endpoint)
                .WithChannelManager(manager)
                .WithSessionName(sessionName)
                .WithSessionTimeout(TimeSpan.FromSeconds(60))
                .WithReconnectPolicy(p => p with
                {
                    Strategy = BackoffStrategy.Constant,
                    InitialDelay = TimeSpan.FromMilliseconds(100),
                    MaxDelay = TimeSpan.FromMilliseconds(500),
                    MaxRetries = 0,
                    JitterFactor = 0.0
                })
                .ConnectAsync(ct);
        }

        protected static IManagedTransportChannel GetManagedChannel(
            ManagedSessionType session)
        {
            IManagedTransportChannel? channel = session.InnerSession.ManagedChannel;
            Assert.That(channel, Is.Not.Null, "Session must be bound to a managed channel.");
            return channel!;
        }

        protected static ManagedChannelDiagnostic GetDiagnostic(
            ClientChannelManager manager,
            ManagedChannelKey key)
        {
            IReadOnlyList<ManagedChannelDiagnostic> diagnostics = manager.GetChannelDiagnostics();
            return diagnostics.Single(d => d.Key.Equals(key));
        }

        protected static async Task AssertReadServerStatusAsync(
            ManagedSessionType session,
            CancellationToken ct)
        {
            DataValue value = await session
                .ReadValueAsync(VariableIds.Server_ServerStatus_State, ct)
                .ConfigureAwait(false);

            Assert.That(value.IsNull, Is.False);
            Assert.That(
                StatusCode.IsGood(value.StatusCode),
                Is.True,
                $"ServerStatus read returned {value.StatusCode}.");
        }

        protected static async Task<bool> WaitForAsync(
            Func<bool> predicate,
            TimeSpan timeout,
            CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token,
                ct);

            while (!linkedCts.IsCancellationRequested)
            {
                if (predicate())
                {
                    return true;
                }

                try
                {
                    await Task.Delay(PollInterval, linkedCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return predicate();
        }

        protected static async Task<bool> WaitForStateAsync(
            ManagedSessionType session,
            ConnectionState expected,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return await WaitForAsync(
                () => session.StateMachine.State == expected,
                timeout,
                ct).ConfigureAwait(false);
        }

        protected static async Task CloseAndDisposeAsync(
            ManagedSessionType? session)
        {
            if (session == null)
            {
                return;
            }

            try
            {
                await session.CloseAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup after transport-fault tests.
            }

            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup after transport-fault tests.
            }
        }
    }

    /// <summary>
    /// Live-server tests for channel-manager sharing and lease lifetime semantics.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("ManagedSession")]
    [Category("ChannelManager")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public sealed class ChannelManagerSharingIntegrationTests
        : ChannelManagerIntegrationTestBase
    {
        [Test]
        [Order(100)]
        [CancelAfter(120_000)]
        public async Task TwoManagedSessionsShareOneManagedChannelAsync(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager();
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionType? first = null;
            ManagedSessionType? second = null;

            try
            {
                first = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(TwoManagedSessionsShareOneManagedChannelAsync) + "1",
                    ct).ConfigureAwait(false);
                second = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(TwoManagedSessionsShareOneManagedChannelAsync) + "2",
                    ct).ConfigureAwait(false);

                IManagedTransportChannel firstChannel = GetManagedChannel(first);
                IManagedTransportChannel secondChannel = GetManagedChannel(second);

                Assert.That(firstChannel.Key, Is.EqualTo(secondChannel.Key));

                ManagedChannelDiagnostic diagnostic = GetDiagnostic(
                    manager,
                    firstChannel.Key);
                Assert.That(diagnostic.Refcount, Is.EqualTo(2));
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(2));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));
            }
            finally
            {
                await CloseAndDisposeAsync(second).ConfigureAwait(false);
                await CloseAndDisposeAsync(first).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(200)]
        [CancelAfter(120_000)]
        public async Task ReleasingOneSessionKeepsSharedChannelReadyAsync(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager();
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionType? first = null;
            ManagedSessionType? second = null;

            try
            {
                first = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ReleasingOneSessionKeepsSharedChannelReadyAsync) + "1",
                    ct).ConfigureAwait(false);
                second = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ReleasingOneSessionKeepsSharedChannelReadyAsync) + "2",
                    ct).ConfigureAwait(false);

                ManagedChannelKey key = GetManagedChannel(first).Key;
                await CloseAndDisposeAsync(first).ConfigureAwait(false);
                first = null;

                Assert.That(
                    await WaitForAsync(
                        () => GetDiagnostic(manager, key).Refcount == 1,
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "Refcount should drop to one after releasing one session.");

                ManagedChannelDiagnostic diagnostic = GetDiagnostic(manager, key);
                Assert.That(diagnostic.ParticipantCount, Is.EqualTo(1));
                Assert.That(diagnostic.State, Is.EqualTo(ChannelState.Ready));

                await AssertReadServerStatusAsync(second, ct).ConfigureAwait(false);
            }
            finally
            {
                await CloseAndDisposeAsync(second).ConfigureAwait(false);
                await CloseAndDisposeAsync(first).ConfigureAwait(false);
            }
        }

        [Test]
        [Order(300)]
        [CancelAfter(120_000)]
        public async Task ReleasingAllSessionsRemovesSharedChannelEntryAsync(
            CancellationToken ct)
        {
            await using ClientChannelManager manager = CreateChannelManager();
            ConfiguredEndpoint endpoint = await GetEndpointAsync(SecurityPolicies.None)
                .ConfigureAwait(false);
            ManagedSessionType? first = null;
            ManagedSessionType? second = null;

            try
            {
                first = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ReleasingAllSessionsRemovesSharedChannelEntryAsync) + "1",
                    ct).ConfigureAwait(false);
                second = await ConnectManagedSessionAsync(
                    endpoint,
                    manager,
                    nameof(ReleasingAllSessionsRemovesSharedChannelEntryAsync) + "2",
                    ct).ConfigureAwait(false);

                ManagedChannelKey key = GetManagedChannel(first).Key;
                Assert.That(GetDiagnostic(manager, key).Refcount, Is.EqualTo(2));

                await CloseAndDisposeAsync(second).ConfigureAwait(false);
                second = null;
                await CloseAndDisposeAsync(first).ConfigureAwait(false);
                first = null;

                Assert.That(
                    await WaitForAsync(
                        () => manager.GetChannelDiagnostics().All(d => !d.Key.Equals(key)),
                        DefaultWait,
                        ct).ConfigureAwait(false),
                    Is.True,
                    "The shared channel entry should be removed after the last lease is released.");
            }
            finally
            {
                await CloseAndDisposeAsync(second).ConfigureAwait(false);
                await CloseAndDisposeAsync(first).ConfigureAwait(false);
            }
        }
    }
}
