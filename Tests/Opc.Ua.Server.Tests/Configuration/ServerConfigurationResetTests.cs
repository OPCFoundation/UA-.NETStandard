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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests the OPC 10000-12 §7.10.13 <c>ResetToServerDefaults</c> deferred
    /// shutdown sequencing: the Method returns its response before the reset
    /// runs, the server advertises the pending shutdown, the injected provider
    /// is invoked after the grace period, and a racing server shutdown cancels
    /// the reset. Each test uses its own dedicated server so the advertised
    /// <c>ServerState</c> = Shutdown never disturbs another test.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [NonParallelizable]
    [Parallelizable(ParallelScope.None)]
    public class ServerConfigurationResetTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        [Test]
        public async Task ResetReturnsResponseThenAdvertisesShutdownAndInvokesProviderAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                IServerInternal serverInternal = server.CurrentInstance;
                var provider = new FakeResetProvider();
                ConfigurationNodeManager manager = await CreateManagerAsync(
                    fixture, serverInternal,
                    new ServerConfigurationOptions
                    {
                        ResetProvider = provider,
                        ResetShutdownDelay = TimeSpan.Zero
                    }).ConfigureAwait(false);

                var node = manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
                MethodState reset = node!.ResetToServerDefaults!;

                ServiceResult result = await reset.OnCallMethod2Async!(
                    CreateAdminContextForSession(new NodeId(1, 1)),
                    reset,
                    node.NodeId,
                    ArrayOf<Variant>.Empty,
                    new List<Variant>(),
                    CancellationToken.None).ConfigureAwait(false);

                // §7.10.13: the response is returned first.
                Assert.That(ServiceResult.IsGood(result), Is.True);

                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await provider.Entered.WaitAsync(timeout.Token).ConfigureAwait(false);
                }

                // The reset only runs after the response, and the server has
                // advertised the pending shutdown by the time it runs.
                Assert.That(serverInternal.CurrentState, Is.EqualTo(ServerState.Shutdown));

                using (var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await manager.DrainPendingResetAsync(drainTimeout.Token).ConfigureAwait(false);
                }
                Assert.That(provider.InvocationCount, Is.EqualTo(1));

                manager.Dispose();
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ResetCancelledByServerShutdownDoesNotInvokeProviderAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                IServerInternal serverInternal = server.CurrentInstance;
                var provider = new FakeResetProvider();
                ConfigurationNodeManager manager = await CreateManagerAsync(
                    fixture, serverInternal,
                    new ServerConfigurationOptions
                    {
                        ResetProvider = provider,
                        // A long grace period so that, unless shutdown cancels
                        // it, the provider would only run much later.
                        ResetShutdownDelay = TimeSpan.FromSeconds(30)
                    }).ConfigureAwait(false);

                var node = manager.FindPredefinedNode<ServerConfigurationState>(ObjectIds.ServerConfiguration);
                MethodState reset = node!.ResetToServerDefaults!;

                ServiceResult result = await reset.OnCallMethod2Async!(
                    CreateAdminContextForSession(new NodeId(2, 1)),
                    reset,
                    node.NodeId,
                    ArrayOf<Variant>.Empty,
                    new List<Variant>(),
                    CancellationToken.None).ConfigureAwait(false);
                Assert.That(ServiceResult.IsGood(result), Is.True);

                // Disposing the manager signals the shutdown token; the deferred
                // reset must abandon its grace wait without invoking the provider.
                manager.Dispose();

                using (var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    await manager.DrainPendingResetAsync(drainTimeout.Token).ConfigureAwait(false);
                }

                Assert.That(provider.InvocationCount, Is.Zero,
                    "a reset cancelled during its grace period must not run the provider");
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        private static async Task<ConfigurationNodeManager> CreateManagerAsync(
            ServerFixture<StandardServer> fixture,
            IServerInternal serverInternal,
            ServerConfigurationOptions options)
        {
            var coordinator = new PushConfigurationTransactionCoordinator(serverInternal.Telemetry);
            var manager = new ConfigurationNodeManager(
                serverInternal,
                fixture.Config,
                serverInternal.Telemetry.CreateLogger<ConfigurationNodeManager>(),
                timeProvider: null,
                coordinator,
                pendingKeyStore: null,
                keyGenerator: null,
                trustListEffectHandler: null,
                serverConfigurationOptions: options);

            IDictionary<NodeId, IList<IReference>> externalReferences =
                new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None).ConfigureAwait(false);
            manager.CreateServerConfiguration(serverInternal.DefaultSystemContext, fixture.Config);
            return manager;
        }

        private static SessionSystemContext CreateAdminContextForSession(NodeId sessionId)
        {
            var identity = new Mock<IUserIdentity>();
            identity.Setup(i => i.TokenType).Returns(UserTokenType.UserName);
            identity.Setup(i => i.DisplayName).Returns(nameof(UserTokenType.UserName));
            identity.Setup(i => i.GrantedRoleIds).Returns(ArrayOf.Wrapped(ObjectIds.WellKnownRole_SecurityAdmin));

            var session = new Mock<ISession>();
            session.Setup(s => s.Id).Returns(sessionId);
            session.Setup(s => s.EffectiveIdentity).Returns(identity.Object);
            session.Setup(s => s.PreferredLocales).Returns([]);

            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channelContext = new SecureChannelContext("test", endpoint, RequestEncoding.Binary);
            var operationContext = new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.Call,
                RequestLifetime.None,
                session.Object);
            return new SessionSystemContext(operationContext, s_telemetry)
            {
                NamespaceUris = new NamespaceTable(),
                ServerUris = new StringTable()
            };
        }

        private sealed class FakeResetProvider : IServerConfigurationResetProvider
        {
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public int InvocationCount { get; private set; }

            public Task Entered => m_entered.Task;

            public ValueTask ResetToServerDefaultsAsync(CancellationToken cancellationToken = default)
            {
                InvocationCount++;
                m_entered.TrySetResult(true);
                return default;
            }
        }
    }
}
