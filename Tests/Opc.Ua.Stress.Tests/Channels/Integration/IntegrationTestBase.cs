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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

// CA2000: integration-test disposables are returned to callers or released by helper cleanup paths.
// CA2007: NUnit lifecycle methods are invoked by the framework.
// CA2016: cleanup intentionally ignores the test cancellation token so it can run after timeouts.
#pragma warning disable CA2000, CA2007, CA2016

namespace Opc.Ua.Stress.Tests.Channels.Integration
{
    /// <summary>
    /// Shared live-server infrastructure for channel-manager stress integration tests.
    /// </summary>
    [NonParallelizable]
    public abstract class IntegrationTestBase : ClientTestFramework
    {
        protected static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(30);
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

        protected async Task<List<ManagedSessionType>> ConnectManagedSessionsAsync(
            ConfiguredEndpoint endpoint,
            IClientChannelManager manager,
            string sessionNamePrefix,
            int count,
            CancellationToken ct)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var sessions = new List<ManagedSessionType>(count);
            try
            {
                for (int index = 0; index < count; index++)
                {
                    sessions.Add(await ConnectManagedSessionAsync(
                        endpoint,
                        manager,
                        FormattableString.Invariant($"{sessionNamePrefix}-{index}"),
                        ct).ConfigureAwait(false));
                }
            }
            catch
            {
                for (int index = sessions.Count - 1; index >= 0; index--)
                {
                    await CloseAndDisposeAsync(sessions[index]).ConfigureAwait(false);
                }

                throw;
            }

            return sessions;
        }

        protected static IManagedTransportChannel GetManagedChannel(
            ManagedSessionType session)
        {
            ITransportChannel channel = session.TransportChannel;
            Assert.That(
                channel,
                Is.AssignableTo<IManagedTransportChannel>(),
                "Session must be bound to a managed channel.");
            return (IManagedTransportChannel)channel;
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
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    break;
                }
            }

            return predicate();
        }

        protected static async Task<bool> WaitForConnectionStateAsync(
            ManagedSessionType session,
            ConnectionState expected,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (expected == ConnectionState.Connected && session.Connected)
            {
                return true;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
            {
                if (e.NewState == expected)
                {
                    completion.TrySetResult(true);
                }
            }

            session.ConnectionStateChanged += OnConnectionStateChanged;
            try
            {
                if (expected == ConnectionState.Connected && session.Connected)
                {
                    return true;
                }

                return await WaitForCompletionAsync(completion.Task, timeout, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                session.ConnectionStateChanged -= OnConnectionStateChanged;
            }
        }

        protected static Task<bool> WaitForChannelStateAsync(
            ClientChannelManager manager,
            ManagedChannelKey key,
            ChannelState expected,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return WaitForAsync(
                () => manager.GetChannelDiagnostics().Any(
                    diagnostic => diagnostic.Key.Equals(key) && diagnostic.State == expected),
                timeout,
                ct);
        }

        protected static async Task<bool> WaitForManagedChannelStateAsync(
            IManagedTransportChannel channel,
            ChannelState expected,
            TimeSpan timeout,
            CancellationToken ct)
        {
            if (channel.State == expected)
            {
                return true;
            }

            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnChannelStateChanged(IManagedTransportChannel sender, ChannelStateChange change)
            {
                if (change.NewState == expected)
                {
                    completion.TrySetResult(true);
                }
            }

            channel.StateChanged += OnChannelStateChanged;
            try
            {
                if (channel.State == expected)
                {
                    return true;
                }

                return await WaitForCompletionAsync(completion.Task, timeout, ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                channel.StateChanged -= OnChannelStateChanged;
            }
        }

        protected async Task RestartServerOnPreservedPortAsync(
            int port)
        {
            ReferenceServer = await ServerFixture.StartAsync(PkiRoot, port)
                .ConfigureAwait(false);
            ServerFixturePort = port;
        }

        protected async Task EnsureServerRunningAsync(
            int port)
        {
            if (ServerFixture.Server == null)
            {
                await RestartServerOnPreservedPortAsync(port)
                    .ConfigureAwait(false);
            }
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

        protected static ConnectionState GetConnectionState(ManagedSessionType session)
        {
            object stateMachine = s_stateMachineProperty.GetValue(session) ??
                throw new InvalidOperationException("ManagedSession.StateMachine was null.");
            object state = s_stateProperty.GetValue(stateMachine) ??
                throw new InvalidOperationException("ConnectionStateMachine.State was null.");
            return (ConnectionState)state;
        }

        protected static Session GetInnerSession(ManagedSessionType session)
        {
            object? value = s_innerSessionProperty.GetValue(session);
            return value as Session ??
                throw new InvalidOperationException("ManagedSession.InnerSession did not return a Session.");
        }

        private static async Task<bool> WaitForCompletionAsync(
            Task task,
            TimeSpan timeout,
            CancellationToken ct)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token,
                ct);

            try
            {
                await task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                return false;
            }
        }

        private static readonly PropertyInfo s_innerSessionProperty =
            typeof(ManagedSessionType).GetProperty(
                "InnerSession",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("ManagedSession.InnerSession reflection hook was not found.");

        private static readonly PropertyInfo s_stateMachineProperty =
            typeof(ManagedSessionType).GetProperty(
                "StateMachine",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException("ManagedSession.StateMachine reflection hook was not found.");

        private static readonly PropertyInfo s_stateProperty =
            s_stateMachineProperty.PropertyType.GetProperty("State") ??
            throw new InvalidOperationException("ConnectionStateMachine.State reflection hook was not found.");
    }
}
