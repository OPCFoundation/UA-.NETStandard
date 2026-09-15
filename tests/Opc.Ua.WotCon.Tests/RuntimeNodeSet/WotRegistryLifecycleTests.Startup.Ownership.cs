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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.WotCon.Tests.RuntimeNodeSet
{
    public sealed partial class WotRegistryLifecycleTests
    {
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
                Assert.That(m_server.NodeManagerLifecycle.Registrations.Count, Is.EqualTo(1));
                Assert.That(m_server.NodeManagerLifecycle.Registrations[0], Is.SameAs(registration));
                Assert.That(registration.NamespaceUris.Contains(initial.NamespaceUri), Is.False);
                Assert.That(registration.NamespaceUris.Contains(runtime.NamespaceUri), Is.True);
                Assert.That(registration.Generation, Is.EqualTo(1));

                using var removal = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await m_server.NodeManagerLifecycle.RemoveAsync(registration, null, removal.Token)
                    .ConfigureAwait(false);
                Assert.That(m_server.NodeManagerLifecycle.Registrations, Is.Empty);
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
            public string NamespaceUri => namespaceUri;

            public uint Identifier => identifier;

            public ArrayOf<string> NamespacesUris { get; } = [namespaceUri];

            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> Release { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<NodeManagerRegistration>? AddTask { get; set; }

            public StartupMembershipNodeManager? Manager { get; private set; }

            public bool PublishedAtHookReturn { get; set; }

            public int FactoryCalls => Volatile.Read(ref m_factoryCalls);

            public int ReadinessCalls => Volatile.Read(ref m_readinessCalls);

            public CancellationToken ReadinessToken { get; private set; }

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<IAsyncNodeManager>(CreateManager(server, configuration));
            }

            public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
            {
                return new SyncNodeManagerAdapter(CreateManager(server, configuration));
            }

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
            INodeManagerReadinessParticipant
        {
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
