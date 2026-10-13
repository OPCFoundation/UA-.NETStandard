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
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.RuntimeNodeSet
{
    /// <summary>
    /// End-to-end lifecycle test for the WoT Connectivity V2 registry hosted on a
    /// real running <see cref="ReferenceServer"/>. It registers a Thing
    /// Description, materializes it as a shadow-reloadable runtime projection,
    /// creates a real subscription and monitored item on the projected value,
    /// registers a compatible new version, refreshes into a new generation, and
    /// verifies that new Read/Browse observe the new generation while the existing
    /// monitored item is kept alive on the retained generation until the
    /// subscription is deleted and the retired projection is cleaned up.
    /// </summary>
    public sealed partial class WotRegistryLifecycleTests
    {
        /// <summary>
        /// Verifies that initial and runtime managers at running transition receive readiness once.
        /// </summary>
        /// <param name="synchronousInitialFactory">The synchronous-initial-factory flag for this test case.</param>
        /// <param name="synchronousRuntimeFactory">The synchronous-runtime-factory flag for this test case.</param>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task InitialAndRuntimeManagersAtRunningTransitionReceiveReadinessOnceAsync(
            bool synchronousInitialFactory, bool synchronousRuntimeFactory)
        {
            await CloseActiveSessionAsync().ConfigureAwait(false);
            await m_fixture.StopAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var initial = new StartupMembershipProbe("urn:wot:startup:initial-membership", 9101u, 53);
            var runtime = new StartupMembershipProbe("urn:wot:startup:runtime-membership", 9102u, 73);
            initial.Release.TrySetResult(true);
            m_fixture = new ServerFixture<ReferenceServer>(telemetry =>
            {
                var server = new StartupMembershipServer(
                    telemetry, runtime, synchronousRuntimeFactory, cancellation.Token);
                if (synchronousInitialFactory)
                {
                    server.AddNodeManager((INodeManagerFactory)initial);
                }
                else
                {
                    server.AddNodeManager((IAsyncNodeManagerFactory)initial);
                }
                m_server = server;
                return server;
            })
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            Task<ReferenceServer> startup = m_fixture.StartAsync(m_pkiRoot);
            try
            {
                try
                {
                    await runtime.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                finally
                {
                    runtime.Release.TrySetResult(true);
                }
                m_server = await startup.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(runtime.AddTask, Is.Not.Null);
                NodeManagerRegistration registration = await runtime.AddTask!
                    .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                (m_requestHeader, m_secureChannelContext) = await m_server
                    .CreateAndActivateSessionAsync(TestContext.CurrentContext.Test.Name).ConfigureAwait(false);
                DataValue initialValue = await ReadStartupMembershipValueAsync(initial).ConfigureAwait(false);
                DataValue runtimeValue = await ReadStartupMembershipValueAsync(runtime).ConfigureAwait(false);
                List<NodeManagerRegistration> registrations = m_server.NodeManagerLifecycle.Registrations.ToList();
                NodeManagerRegistration initialRegistration = registrations
                    .Single(candidate => candidate.NamespaceUris.Contains(initial.NamespaceUri));
                NodeManagerRegistration runtimeRegistration = registrations
                    .Single(candidate => candidate.NamespaceUris.Contains(runtime.NamespaceUri));
                Assert.That(runtimeRegistration, Is.SameAs(registration));
                Assert.That(initialRegistration.Id, Is.Not.EqualTo(registration.Id));
                Assert.That(initialRegistration.Generation, Is.EqualTo(1));
                Assert.That(registration.NamespaceUris.Contains(initial.NamespaceUri), Is.False);
                Assert.That(registration.NamespaceUris.Contains(runtime.NamespaceUri), Is.True);
                Assert.That(registration.Generation, Is.EqualTo(1));
                List<NodeManagerRegistration> retainedRegistrations = registrations
                    .Where(candidate => !ReferenceEquals(candidate, registration)).ToList();

                using var removal = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await m_server.NodeManagerLifecycle.RemoveAsync(registration, null, removal.Token)
                    .ConfigureAwait(false);
                Assert.That(m_server.NodeManagerLifecycle.Registrations.ToList(),
                    Is.EquivalentTo(retainedRegistrations),
                    "Removing the runtime generation must preserve the adopted initial registrations.");
                DataValue removed = await ReadStartupMembershipValueAsync(runtime).ConfigureAwait(false);
                Assert.That(removed.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(initialValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(initialValue.GetValue<int>(-1), Is.EqualTo(53));
                Assert.That(runtimeValue.StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(runtimeValue.GetValue<int>(-1), Is.EqualTo(73));
                Assert.That(runtime.PublishedAtHookReturn, Is.True,
                    "The real generation must be published before the Running-state hook returns.");
                Assert.That(initial.FactoryCalls, Is.EqualTo(1));
                Assert.That(runtime.FactoryCalls, Is.EqualTo(1));
                Assert.That(initial.ReadinessCalls, Is.EqualTo(1),
                    "The initial sweep must still await each static manager exactly once.");
                Assert.That(runtime.ReadinessCalls, Is.EqualTo(1),
                    "Runtime Add owns readiness; the initial sweep must not repeat it for that generation.");
                Assert.That(runtime.ReadinessToken, Is.EqualTo(cancellation.Token));
            }
            finally
            {
                runtime.Release.TrySetResult(true);
                try
                {
                    m_server = await startup.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    if (runtime.AddTask is not null)
                    {
                        await runtime.AddTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await CloseActiveSessionAsync().ConfigureAwait(false);
                    await m_fixture.StopAsync().ConfigureAwait(false);
                    if (synchronousRuntimeFactory)
                    {
                        runtime.Manager?.Dispose();
                    }
                    if (synchronousInitialFactory)
                    {
                        initial.Manager?.Dispose();
                    }
                }
            }
        }

        private Task<DataValue> ReadStartupMembershipValueAsync(StartupMembershipProbe probe)
        {
            int index = m_server.CurrentInstance.NamespaceUris.GetIndex(probe.NamespaceUri);
            Assert.That(index, Is.GreaterThan(0));
            return ReadValueAsync(new NodeId(probe.Identifier, checked((ushort)index)));
        }

        private sealed class StartupMembershipProbe(string namespaceUri, uint identifier, int value) :
            IAsyncNodeManagerFactory, INodeManagerFactory
        {
            /// <summary>
            /// Gets the namespace URI for <c>StartupMembershipProbe</c>.
            /// </summary>
            public string NamespaceUri => namespaceUri;

            public uint Identifier => identifier;

            /// <summary>
            /// Gets the namespaces URIs for <c>StartupMembershipProbe</c>.
            /// </summary>
            public ArrayOf<string> NamespacesUris { get; } = [namespaceUri];

            /// <summary>
            /// Gets the entered for <c>StartupMembershipProbe</c>.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the release for <c>StartupMembershipProbe</c>.
            /// </summary>
            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets or sets the add task for <c>StartupMembershipProbe</c>.
            /// </summary>
            public Task<NodeManagerRegistration>? AddTask { get; set; }

            /// <summary>
            /// Gets or sets the manager for <c>StartupMembershipProbe</c>.
            /// </summary>
            public StartupMembershipNodeManager? Manager { get; private set; }

            /// <summary>
            /// Gets or sets the published at hook return for <c>StartupMembershipProbe</c>.
            /// </summary>
            public bool PublishedAtHookReturn { get; set; }

            public int FactoryCalls => Volatile.Read(ref m_factoryCalls);

            public int ReadinessCalls => Volatile.Read(ref m_readinessCalls);

            /// <summary>
            /// Gets or sets the readiness token for <c>StartupMembershipProbe</c>.
            /// </summary>
            public CancellationToken ReadinessToken { get; private set; }

            /// <summary>
            /// Creates the node manager for the server.
            /// </summary>
            /// <param name="server">The server internal.</param>
            /// <param name="configuration">The application configuration.</param>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IAsyncNodeManager>(CreateManager(server, configuration));
            }

            /// <summary>
            /// Creates the synchronous adapter over the node manager.
            /// </summary>
            /// <param name="server">The server internal.</param>
            /// <param name="configuration">The application configuration.</param>
            public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
            {
                return new SyncNodeManagerAdapter(CreateManager(server, configuration));
            }

            /// <summary>
            /// Counts the readiness call and waits until the test releases it.
            /// </summary>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public async ValueTask ReadyAsync(CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref m_readinessCalls);
                ReadinessToken = cancellationToken;
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }

            private StartupMembershipNodeManager CreateManager(
                IServerInternal server, ApplicationConfiguration configuration)
            {
                Interlocked.Increment(ref m_factoryCalls);
                Manager = new StartupMembershipNodeManager(server, configuration, this, value);
                return Manager;
            }

            private int m_factoryCalls;
            private int m_readinessCalls;
        }

        private sealed class StartupMembershipNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            StartupMembershipProbe probe,
            int value) : AsyncCustomNodeManager(server, configuration, probe.NamespaceUri),
            INodeManagerReadinessNotification
        {
            /// <summary>
            /// Signals the readiness probe that the server is ready.
            /// </summary>
            /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
            public ValueTask OnServerReadyAsync(CancellationToken cancellationToken = default)
            {
                return probe.ReadyAsync(cancellationToken);
            }

            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ushort index = Server.NamespaceUris.GetIndexOrAppend(probe.NamespaceUri);
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId(probe.Identifier, index),
                    BrowseName = new QualifiedName("MembershipValue", index),
                    DisplayName = new LocalizedText("MembershipValue"),
                    TypeDefinitionId = Opc.Ua.VariableTypeIds.BaseDataVariableType,
                    DataType = Opc.Ua.DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    StatusCode = StatusCodes.Good,
                    Value = new Variant(value)
                };
                node.AddReference(Opc.Ua.ReferenceTypeIds.Organizes, true, Opc.Ua.ObjectIds.ObjectsFolder);
                return new ValueTask<NodeStateCollection>(new NodeStateCollection { node });
            }
        }

        private sealed class StartupMembershipServer(
            ITelemetryContext telemetry,
            StartupMembershipProbe runtime,
            bool synchronousFactory,
            CancellationToken cancellationToken) : ReferenceServer(telemetry)
        {
            protected override void SetServerState(ServerState state)
            {
                base.SetServerState(state);
                if (state == ServerState.Running && runtime.AddTask is null)
                {
                    runtime.AddTask = synchronousFactory
                        ? NodeManagerLifecycle.AddAsync((INodeManagerFactory)runtime, null, cancellationToken).AsTask()
                        : NodeManagerLifecycle.AddAsync((IAsyncNodeManagerFactory)runtime, null, cancellationToken)
                            .AsTask();
                    runtime.PublishedAtHookReturn = NodeManagerLifecycle.Registrations.Find(
                        registration => registration.NamespaceUris.Contains(runtime.NamespaceUri)) is not null;
                }
            }
        }
    }
}
