/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that captured work cannot retain ambient guards after the owning operation ends.
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("NodeManagerLifecycle")]
    public sealed class AsyncLocalEscapedContextTests
    {
        /// <summary>
        /// Nested registration suppresses duplicate notifications, but escaped registration publishes its own node.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RegistrationDepthExpiresAfterCallbackAsync(bool escaped)
        {
            await using ProbeNodeManager manager = CreateManager();
            await using var work = new CapturedWork<NodeId>();
            BaseObjectState root = CreateObject(manager, "Registered");
            BaseObjectState child = CreateObject(manager, "IndependentChild");
            ILocalAddressSpace addressSpace = ((ILocalAddressSpaceSource)manager).CreateLocalAddressSpace();
            int rootNotifications = 0;
            int childNotifications = 0;
            addressSpace.NodeAdded += node =>
            {
                if (ReferenceEquals(node, root))
                {
                    Interlocked.Increment(ref rootNotifications);
                }
                if (ReferenceEquals(node, child))
                {
                    Interlocked.Increment(ref childNotifications);
                }
            };
            manager.RegistrationProbe = _ => work.StartAsync(async () =>
            {
                await manager.RegisterAsync(child).ConfigureAwait(false);
                return child.NodeId;
            }, escaped);

            await manager.RegisterAsync(root).AsTask()
                .WaitAsync(kDeadline).ConfigureAwait(false);
            Assert.That(Volatile.Read(ref rootNotifications), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref childNotifications), Is.Zero);
            work.Release();
            NodeId registered = await work.Result.WaitAsync(kDeadline).ConfigureAwait(false);

            Assert.That(registered, Is.EqualTo(child.NodeId));
            Assert.That(addressSpace.TryGetNode(root.NodeId, out NodeState? indexedRoot), Is.True);
            Assert.That(indexedRoot, Is.SameAs(root));
            Assert.That(addressSpace.TryGetNode(child.NodeId, out NodeState? indexedChild), Is.True);
            Assert.That(indexedChild, Is.SameAs(child));
            Assert.That(Volatile.Read(ref rootNotifications), Is.EqualTo(1));
            Assert.That(Volatile.Read(ref childNotifications), Is.EqualTo(escaped ? 1 : 0),
                "An ended registration must not suppress an escaped task's later top-level registration.");
        }

        /// <summary>
        /// AddNodes duplicate indexing is guarded in scope, but same-instance registration succeeds after return.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReservedNodeIdExpiresAfterCallbackAsync(bool escaped)
        {
            await using ProbeNodeManager manager = CreateManager();
            await using var work = new CapturedWork<StatusCode>();
            BaseObjectState parent = CreateObject(manager, "Parent");
            await manager.RegisterAsync(parent).ConfigureAwait(false);
            var requested = new NodeId("Requested", manager.NamespaceIndexes[0]);
            NodeState? candidate = null;
            manager.RegistrationProbe = async node =>
            {
                candidate = node;
                Assert.That(node.NodeId, Is.EqualTo(requested));
                manager.Index(node);
                try
                {
                    await work.StartAsync(async () =>
                    {
                        try
                        {
                            await manager.RegisterAsync(node).ConfigureAwait(false);
                            return StatusCodes.Good;
                        }
                        catch (ServiceResultException failure)
                        {
                            return failure.StatusCode;
                        }
                    }, escaped).ConfigureAwait(false);
                }
                finally
                {
                    manager.Unindex(node);
                }
            };
            var session = new Mock<ISession>();
            session.Setup(value => value.EffectiveIdentity).Returns(new Mock<IUserIdentity>().Object);
            session.Setup(value => value.PreferredLocales).Returns([]);
            var operation = new OperationContext(
                new RequestHeader(), null!, RequestType.AddNodes, RequestLifetime.None, session.Object);

            (ServiceResult result, NodeId added) = await manager.AddNodeAsync(operation, new AddNodesItem
            {
                ParentNodeId = parent.NodeId,
                ReferenceTypeId = ReferenceTypeIds.Organizes,
                BrowseName = new QualifiedName("Requested", manager.NamespaceIndexes[0]),
                NodeClass = NodeClass.Object,
                TypeDefinition = ObjectTypeIds.BaseObjectType,
                RequestedNewNodeId = requested
            }).AsTask().WaitAsync(kDeadline).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"AddNodes failed: {result}");
            Assert.That(added, Is.EqualTo(requested));
            Assert.That(candidate, Is.Not.Null);
            work.Release();
            StatusCode observed = await work.Result.WaitAsync(kDeadline).ConfigureAwait(false);

            Assert.That(observed, Is.EqualTo(
                (StatusCode)(escaped ? StatusCodes.Good : StatusCodes.BadNodeIdExists)),
                "A completed AddNodes request must not reject later same-instance registration.");
            ILocalAddressSpace addressSpace = ((ILocalAddressSpaceSource)manager).CreateLocalAddressSpace();
            Assert.That(addressSpace.TryGetNode(requested, out NodeState? indexed), Is.True);
            Assert.That(indexed, Is.SameAs(candidate));
            Assert.That(indexed!.NodeId, Is.EqualTo(requested));
        }

        /// <summary>
        /// Each adapter operation suppresses in-scope notifications but not notifications from escaped work.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        [TestCase("add", false)]
        [TestCase("add", true)]
        [TestCase("range", false)]
        [TestCase("range", true)]
        [TestCase("remove", false)]
        [TestCase("remove", true)]
        public async Task OperationSuppressionExpiresAfterCallbackAsync(string operation, bool escaped)
        {
            await using ProbeNodeManager manager = CreateManager();
            await using var work = new CapturedWork<int>();
            BaseObjectState node = CreateObject(manager, "Operation");
            PredefinedNodesAddressSpace? addressSpace = null;
            int addedNotifications = 0;
            int removedNotifications = 0;

            ValueTask Capture()
            {
                PredefinedNodesAddressSpace active = addressSpace ??
                    throw new InvalidOperationException("The adapter has not been created.");
                return work.StartAsync(() =>
                {
                    active.NotifyAdded(node);
                    active.NotifyRemoved(node.NodeId);
                    return new ValueTask<int>(Volatile.Read(ref addedNotifications));
                }, escaped);
            }

            addressSpace = manager.CreateProbeAddressSpace(
                async (added, _) =>
                {
                    manager.Index(added);
                    await Capture().ConfigureAwait(false);
                },
                async (_, _) =>
                {
                    await Capture().ConfigureAwait(false);
                    return true;
                });
            addressSpace.NodeAdded += _ => Interlocked.Increment(ref addedNotifications);
            addressSpace.NodeRemoved += _ => Interlocked.Increment(ref removedNotifications);

            switch (operation)
            {
                case "add":
                    await addressSpace.AddOrUpdateNodeAsync(node).AsTask()
                        .WaitAsync(kDeadline).ConfigureAwait(false);
                    break;
                case "range":
                    await addressSpace.AddOrUpdateRangeAsync([node]).AsTask()
                        .WaitAsync(kDeadline).ConfigureAwait(false);
                    break;
                case "remove":
                    Assert.That(await addressSpace.RemoveNodeAsync(node.NodeId).AsTask()
                        .WaitAsync(kDeadline).ConfigureAwait(false), Is.True);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }

            int expectedAdded = operation == "add" ? 1 : 0;
            int expectedRemoved = operation == "remove" ? 1 : 0;
            Assert.That(Volatile.Read(ref addedNotifications), Is.EqualTo(expectedAdded));
            Assert.That(Volatile.Read(ref removedNotifications), Is.EqualTo(expectedRemoved));

            work.Release();
            int observed = await work.Result.WaitAsync(kDeadline).ConfigureAwait(false);
            Assert.That(observed, Is.EqualTo(escaped ? expectedAdded + 1 : 0));
            Assert.That(Volatile.Read(ref addedNotifications), Is.EqualTo(expectedAdded + (escaped ? 1 : 0)));
            Assert.That(Volatile.Read(ref removedNotifications), Is.EqualTo(expectedRemoved + (escaped ? 1 : 0)));
        }

        /// <summary>
        /// An external write in escaped work must not inherit the framework-bump notification suppression.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FrameworkBumpSuppressionExpiresAfterCallbackAsync(bool escaped)
        {
            await using var work = new CapturedWork<bool>();
            var context = new SystemContext(NUnitTelemetryContext.Create());
            var node = new BaseObjectState(null)
            {
                NodeId = new NodeId("Versioned", 2),
                BrowseName = new QualifiedName("Versioned", 2)
            };
            int notifications = 0;
            PropertyState<string> version = node.EnableModelChangeTracking(
                2, (_, _) => Interlocked.Increment(ref notifications));
            version.OnStateChangedAsync = async (_, _, _, _) =>
            {
                Assert.That(NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump, Is.True);
                await work.StartAsync(() =>
                {
                    bool inside = NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump;
                    var value = new Variant("42");
                    StatusCode status = StatusCodes.Good;
                    DateTimeUtc timestamp = DateTime.UtcNow;
                    ServiceResult result = version.OnWriteValue!(
                        context, version, NumericRange.Null, QualifiedName.Null,
                        ref value, ref status, ref timestamp);
                    Assert.That(ServiceResult.IsGood(result), Is.True);
                    return new ValueTask<bool>(inside);
                }, escaped).ConfigureAwait(false);
            };

            await Task.Run(() => node.BumpNodeVersion(context)).WaitAsync(kDeadline).ConfigureAwait(false);
            Assert.That(NodeStateModelChangeExtensions.IsInsideFrameworkNodeVersionBump, Is.False);
            work.Release();
            bool observed = await work.Result.WaitAsync(kDeadline).ConfigureAwait(false);

            Assert.That(observed, Is.EqualTo(!escaped));
            Assert.That(Volatile.Read(ref notifications), Is.EqualTo(escaped ? 1 : 0));
            Assert.That(version.Value, Is.EqualTo("42"));
        }

        /// <summary>
        /// Captured nested work falls back only to enclosing scopes that are still active.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task EndedInnerScopeUsesOnlyAnActiveParentAsync(bool keepParent)
        {
            var current = new AsyncLocal<NodeManagerAmbientScope<int>?>();
            using var parent = new NodeManagerAmbientScope<int>(current, 1);
            await using var work = new CapturedWork<int>();
            using (new NodeManagerAmbientScope<int>(current, 2))
            {
                await work.StartAsync(
                    () => new ValueTask<int>(NodeManagerAmbientScope<int>.GetValue(current)),
                    escaped: true).ConfigureAwait(false);
            }
            if (!keepParent)
            {
                parent.Dispose();
            }
            work.Release();

            Assert.That(await work.Result.WaitAsync(kDeadline).ConfigureAwait(false),
                Is.EqualTo(keepParent ? 1 : 0));
            Assert.That(NodeManagerAmbientScope<int>.GetValue(current), Is.EqualTo(keepParent ? 1 : 0));
        }

        /// <summary>
        /// Readiness grants survive an ended inner scope only while their own enclosing initializer is active.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task ReadinessScopeUsesOnlyActiveInitializerAncestorsAsync(int scopeState)
        {
            IServerInternal parentServer = Mock.Of<IServerInternal>();
            IServerInternal innerServer = Mock.Of<IServerInternal>();
            NodeManagerGeneration parentGeneration = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            NodeManagerGeneration innerGeneration = NodeManagerGenerationRegistry.GetOrCreate(
                Mock.Of<IAsyncNodeManager>());
            using var parent = new NodeManagerReadinessScope(parentServer, parentGeneration);
            await using var work = new CapturedWork<(
                bool ParentGeneration, bool InnerGeneration, bool ParentServer, bool InnerServer)>();
            using (var inner = new NodeManagerReadinessScope(innerServer, innerGeneration, parent))
            {
                await work.StartAsync(() => new((
                    inner.Includes(parentGeneration),
                    inner.Includes(innerGeneration),
                    inner.Includes(parentServer),
                    inner.Includes(innerServer))), escaped: scopeState != 0).ConfigureAwait(false);
            }
            Assert.That(parent.Includes(parentGeneration), Is.True);
            Assert.That(parent.Includes(innerGeneration), Is.False);
            if (scopeState == 2)
            {
                parent.Dispose();
            }
            work.Release();
            (bool ParentGeneration, bool InnerGeneration, bool ParentServer, bool InnerServer) observed =
                await work.Result.WaitAsync(kDeadline).ConfigureAwait(false);
            Assert.That(observed.ParentGeneration, Is.EqualTo(scopeState != 2));
            Assert.That(observed.InnerGeneration, Is.EqualTo(scopeState == 0));
            Assert.That(observed.ParentServer, Is.EqualTo(scopeState != 2));
            Assert.That(observed.InnerServer, Is.EqualTo(scopeState == 0));
            parent.Dispose();
            Assert.That(parent.Includes(parentGeneration), Is.False);
            Assert.That(parent.Includes(parentServer), Is.False);
        }

        /// <summary>
        /// Failure revokes a captured guard as well as restoring the caller's slot.
        /// </summary>
        [Test]
        public async Task FailedOperationRevokesCapturedScopeAsync()
        {
            var current = new AsyncLocal<NodeManagerAmbientScope<int>?>();
            await using var work = new CapturedWork<int>();
            var expected = new InvalidOperationException("The owning callback failed.");
            InvalidOperationException? observed = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                using var scope = new NodeManagerAmbientScope<int>(current, 1);
                await work.StartAsync(
                    () => new ValueTask<int>(NodeManagerAmbientScope<int>.GetValue(current)),
                    escaped: true).ConfigureAwait(false);
                throw expected;
            }).ConfigureAwait(false);
            Assert.That(observed, Is.SameAs(expected));
            Assert.That(NodeManagerAmbientScope<int>.GetValue(current), Is.Zero);
            work.Release();
            Assert.That(await work.Result.WaitAsync(kDeadline).ConfigureAwait(false), Is.Zero);
        }

        /// <summary>
        /// Ending one flow does not revoke a separate flow that uses the same ambient slot.
        /// </summary>
        [Test]
        public async Task IndependentScopesRemainActiveUntilTheirOwnExitAsync()
        {
            var current = new AsyncLocal<NodeManagerAmbientScope<int>?>();
            var secondActive = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstEnded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<int> first = Task.Run(async () =>
            {
                try
                {
                    using var scope = new NodeManagerAmbientScope<int>(current, 1);
                    await secondActive.Task.WaitAsync(kDeadline).ConfigureAwait(false);
                    Assert.That(NodeManagerAmbientScope<int>.GetValue(current), Is.EqualTo(1));
                }
                finally
                {
                    firstEnded.TrySetResult(true);
                }
                return NodeManagerAmbientScope<int>.GetValue(current);
            });
            Task<int> second = Task.Run(async () =>
            {
                using var scope = new NodeManagerAmbientScope<int>(current, 2);
                secondActive.TrySetResult(true);
                await firstEnded.Task.WaitAsync(kDeadline).ConfigureAwait(false);
                return NodeManagerAmbientScope<int>.GetValue(current);
            });
            int[] values = await Task.WhenAll(first, second).ConfigureAwait(false);

            Assert.That(values, Is.EqualTo(s_siblingScopeValues));
            Assert.That(NodeManagerAmbientScope<int>.GetValue(current), Is.Zero);
        }

        /// <summary>
        /// Creates a socket-free manager using the neighboring node-management fixture's mock setup.
        /// </summary>
        private static ProbeNodeManager CreateManager()
        {
            var server = new Mock<IServerInternal>();
            var master = new Mock<IMasterNodeManager>();
            var configurationManager = new Mock<IConfigurationNodeManager>();
            var namespaces = new NamespaceTable();
            namespaces.Append("urn:opcfoundation.org:Tests:F12");
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var queues = new MonitoredItemQueueFactory(telemetry);
            server.Setup(value => value.NamespaceUris).Returns(namespaces);
            server.Setup(value => value.ServerUris).Returns(new StringTable());
            server.Setup(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.Setup(value => value.Factory).Returns(EncodeableFactory.Create());
            server.Setup(value => value.Telemetry).Returns(telemetry);
            server.Setup(value => value.NodeManager).Returns(master.Object);
            server.Setup(value => value.MonitoredItemQueueFactory).Returns(queues);
            master.Setup(value => value.ConfigurationNodeManager).Returns(configurationManager.Object);
            server.Setup(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return new ProbeNodeManager(server.Object, queues);
        }

        /// <summary>
        /// Creates a node with a fixed nonstandard namespace identifier.
        /// </summary>
        private static BaseObjectState CreateObject(ProbeNodeManager manager, string name)
        {
            return new BaseObjectState(null)
            {
                NodeId = new NodeId(name, manager.NamespaceIndexes[0]),
                BrowseName = new QualifiedName(name, manager.NamespaceIndexes[0])
            };
        }

        /// <summary>
        /// Captures a real child task and positively holds it until either in-scope or post-return release.
        /// </summary>
        /// <typeparam name="T">The observation produced by the captured child task.</typeparam>
        private sealed class CapturedWork<T> : IAsyncDisposable
        {
            /// <summary>
            /// Gets the observation task after the callback has started it.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            public Task<T> Result => m_worker ??
                throw new InvalidOperationException("The production callback did not start the child task.");

            /// <summary>
            /// Starts the child inside the production scope and optionally awaits it inside that same scope.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            public async ValueTask StartAsync(Func<ValueTask<T>> observe, bool escaped)
            {
                if (m_worker is not null)
                {
                    throw new InvalidOperationException("The probe callback ran more than once.");
                }
                m_worker = Task.Run(async () =>
                {
                    m_entered.TrySetResult(true);
                    await m_release.Task.ConfigureAwait(false);
                    return await observe().ConfigureAwait(false);
                });
                await m_entered.Task.WaitAsync(kDeadline).ConfigureAwait(false);
                if (!escaped)
                {
                    Release();
                    await m_worker.WaitAsync(kDeadline).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Allows the captured task to observe its context.
            /// </summary>
            public void Release()
            {
                m_release.TrySetResult(true);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Release();
                if (m_worker is not null)
                {
                    await m_worker.WaitAsync(kDeadline).ConfigureAwait(false);
                }
            }

            /// <summary>The observation worker, joined before the owner is disposed.</summary>
            private Task<T>? m_worker;

            /// <summary>Positive evidence that the child started inside the callback.</summary>
            private readonly TaskCompletionSource<bool> m_entered =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The explicit observation release, not an elapsed-time signal.</summary>
            private readonly TaskCompletionSource<bool> m_release =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// Test-only subclass exposing existing registration callbacks and its own mock queue ownership.
        /// </summary>
        private sealed class ProbeNodeManager : AsyncCustomNodeManager
        {
            /// <summary>
            /// Creates the probe manager.
            /// </summary>
            public ProbeNodeManager(IServerInternal server, MonitoredItemQueueFactory queues)
                : base(server, new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration()
                }, new Mock<ILogger>().Object, "urn:opcfoundation.org:Tests:F12")
            {
                m_queues = queues;
            }

            /// <summary>
            /// Gets or sets the next registration callback.
            /// </summary>
            public Func<NodeState, ValueTask>? RegistrationProbe { get; set; }

            /// <inheritdoc/>
            public override bool AllowNodeManagement => true;

            /// <summary>
            /// Enters the existing asynchronous registration path.
            /// </summary>
            public ValueTask RegisterAsync(NodeState node)
            {
                return AddPredefinedNodeAsync(SystemContext, node);
            }

            /// <summary>
            /// Seeds the adapter's dictionary inside its supplied add delegate.
            /// </summary>
            public void Index(NodeState node)
            {
                PredefinedNodes.AddOrUpdate(node.NodeId, node, (_, _) => node);
            }

            /// <summary>
            /// Removes the exact temporary collision seed before the outer AddNodes pipeline indexes it.
            /// </summary>
            public void Unindex(NodeState node)
            {
                Assert.That(PredefinedNodes.TryGetValue(node.NodeId, out NodeState? indexed), Is.True);
                Assert.That(indexed, Is.SameAs(node));
                Assert.That(PredefinedNodes.TryRemove(node.NodeId, out _), Is.True);
            }

            /// <summary>
            /// Creates an adapter using this manager's actual predefined dictionary type.
            /// </summary>
            public PredefinedNodesAddressSpace CreateProbeAddressSpace(
                Func<NodeState, CancellationToken, ValueTask> add,
                Func<NodeId, CancellationToken, ValueTask<bool>> remove)
            {
                return new PredefinedNodesAddressSpace(SystemContext, PredefinedNodes, add, remove);
            }

            /// <inheritdoc/>
            public override async ValueTask DisposeAsync()
            {
                await base.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            /// <inheritdoc/>
            protected override async ValueTask<NodeState> AddBehaviourToPredefinedNodeAsync(
                ISystemContext context,
                NodeState predefinedNode,
                CancellationToken cancellationToken = default)
            {
                Func<NodeState, ValueTask>? probe = RegistrationProbe;
                RegistrationProbe = null;
                if (probe is not null)
                {
                    await probe(predefinedNode).ConfigureAwait(false);
                }
                return predefinedNode;
            }

            /// <summary>The test-owned queue factory.</summary>
            private readonly MonitoredItemQueueFactory m_queues;
        }

        /// <summary>The failure deadline, never used as evidence of scope expiration.</summary>
        private static readonly TimeSpan kDeadline = TimeSpan.FromSeconds(30);

        /// <summary>The values observed by the sibling whose frame expired and the still-active sibling.</summary>
        private static readonly int[] s_siblingScopeValues = [0, 2];
    }
}
