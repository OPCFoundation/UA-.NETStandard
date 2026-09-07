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
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Covers the fluent behavior-attachment surface: type fan-out, subtype chaining,
    /// composition of two behaviors on one node, activation order, and the exact-reverse
    /// unwind on both teardown and mid-activation failure.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class NodeAttachBuilderExtensionsTests
    {
        [Test]
        public async Task ActivatesChildFirstAndBaseBeforeDerivedAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedParentAndChild();

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.BaseTypeId,
                (node, ctx, ct) => Record(log, $"base:{node.BrowseName.Name}"));
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.DerivedTypeId,
                (node, ctx, ct) => Record(log, $"derived:{node.BrowseName.Name}"));

            await manager.ActivateAsync().ConfigureAwait(false);

            // Child before parent; on each node, the base-type behavior before the
            // derived-type one.
            Assert.That(
                log,
                Is.EqualTo(s_activationOrder));

            log.Clear();
            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);

            Assert.That(
                log,
                Is.EqualTo(s_teardownOrder),
                "teardown must be the exact reverse of activation");
        }

        [Test]
        public async Task FansOutOverEveryInstanceOfTheTypeAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(3);

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.BaseTypeId,
                (node, ctx, ct) => Record(log, node.BrowseName.Name!));

            await manager.ActivateAsync().ConfigureAwait(false);

            Assert.That(log, Has.Count.EqualTo(3));
            Assert.That(
                log,
                Does.Contain("activate Sibling0")
                    .And.Contain("activate Sibling1")
                    .And.Contain("activate Sibling2"));
        }

        [Test]
        public void ZeroMatchesThrowsBadConfigurationError()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedParentAndChild();

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.UnmatchedTypeId,
                (node, ctx, ct) => new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null));

            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateAsync().ConfigureAwait(false))!;
            Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public async Task ZeroMatchesIsAcceptedWhenAllowedAsync()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedParentAndChild();

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.UnmatchedTypeId,
                (node, ctx, ct) => new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null),
                new NodeAttachOptions { AllowZeroMatches = true });

            Assert.DoesNotThrowAsync(
                async () => await manager.ActivateAsync().ConfigureAwait(false));
            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ExactTypeRegistrationSkipsSubtypesAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedParentAndChild();

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.BaseTypeId,
                (node, ctx, ct) => Record(log, node.BrowseName.Name!),
                new NodeAttachOptions
                {
                    IncludeSubtypes = false,
                    AllowZeroMatches = true
                });

            await manager.ActivateAsync().ConfigureAwait(false);

            // Both seeded nodes are of the derived type, so an exact-type registration
            // on the base type matches nothing.
            Assert.That(log, Is.Empty);
        }

        [Test]
        public async Task ManagerScopedBehaviorActivatesLastAndUnwindsFirstAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedParentAndChild();

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.DerivedTypeId,
                (node, ctx, ct) => Record(log, node.BrowseName.Name!));
            builder.Attach((ctx, ct) => Record(log, "manager"));

            await manager.ActivateAsync().ConfigureAwait(false);
            Assert.That(log[^1], Is.EqualTo("activate manager"));

            log.Clear();
            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(log[0], Is.EqualTo("dispose manager"));
        }

        [Test]
        public async Task DecliningAnInstanceRecordsNothingAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(3);

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.BaseTypeId,
                (node, ctx, ct) => node.BrowseName.Name == "Sibling1"
                    ? Record(log, node.BrowseName.Name!)
                    : new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null));

            await manager.ActivateAsync().ConfigureAwait(false);
            Assert.That(log, Is.EqualTo(s_activateSibling1));

            log.Clear();
            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(
                log,
                Is.EqualTo(s_disposeSibling1),
                "declined instances must not be disposed");
        }

        [Test]
        public async Task MidActivationFailureUnwindsWhatAlreadyActivatedAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedParentAndChild();

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.BaseTypeId,
                (node, ctx, ct) => Record(log, $"base:{node.BrowseName.Name}"));
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.DerivedTypeId,
                (node, ctx, ct) =>
                {
                    // Fails on the parent, after three behaviors are already live.
                    if (node.BrowseName.Name == "Parent")
                    {
                        throw new InvalidOperationException("activation failed");
                    }
                    return Record(log, $"derived:{node.BrowseName.Name}");
                });

            InvalidOperationException ex = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await manager.ActivateAsync().ConfigureAwait(false))!;
            Assert.That(ex.Message, Is.EqualTo("activation failed"));

            Assert.That(
                log,
                Is.EqualTo(s_rollbackOrder),
                "rollback must release exactly what activated, in reverse");
        }

        [Test]
        public async Task LifetimeTokenIsCancelledBeforeReleaseAsync()
        {
            CancellationToken lifetime = default;
            bool cancelledAtRelease = false;

            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.BaseTypeId,
                (node, ctx, ct) =>
                {
                    lifetime = ctx.Lifetime;
                    return new ValueTask<IAsyncDisposable?>(
                        new CallbackDisposable(() =>
                            cancelledAtRelease = lifetime.IsCancellationRequested));
                });

            await manager.ActivateAsync().ConfigureAwait(false);
            Assert.That(lifetime.IsCancellationRequested, Is.False);

            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(
                cancelledAtRelease,
                Is.True,
                "the lifetime token must be tripped before the handle is released");
        }

        [Test]
        public async Task SecondPassUnwindsBeforeTheFirstAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            INodeManagerBuilder first = manager.NewBuilder();
            first.Attach((ctx, ct) => Record(log, "first"));
            await manager.ActivateAsync().ConfigureAwait(false);

            INodeManagerBuilder second = manager.NewBuilder();
            second.Attach((ctx, ct) => Record(log, "second"));
            await manager.ActivateAsync().ConfigureAwait(false);

            Assert.That(
                log,
                Is.EqualTo(s_activateBothPasses));

            log.Clear();
            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);
            Assert.That(
                log,
                Is.EqualTo(s_disposeBothPasses));
        }

        [Test]
        public async Task DrainedRegistrationsDoNotReactivateAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            INodeManagerBuilder builder = manager.NewBuilder();
            builder.Attach((ctx, ct) => Record(log, "once"));

            await manager.ActivateAsync().ConfigureAwait(false);
            await manager.ActivateAsync().ConfigureAwait(false);

            Assert.That(log, Is.EqualTo(s_activateOnce));
        }

        [Test]
        public async Task MethodNodesAreNotMatchedByTypeAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedMethod();

            INodeManagerBuilder builder = manager.NewBuilder();

            // MethodState aliases MethodDeclarationId onto TypeDefinitionId, so without
            // an explicit filter this registration would fire on the method node.
            builder.AttachToType<BaseObjectState>(
                TestBehaviorManager.DerivedTypeId,
                (node, ctx, ct) => Record(log, node.BrowseName.Name!),
                new NodeAttachOptions { AllowZeroMatches = true });

            await manager.ActivateAsync().ConfigureAwait(false);

            Assert.That(log, Is.Empty, "method nodes must not match a type registration");
        }

        [Test]
        public async Task AlarmWiringIsUndoneOnTeardownAsync()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            NodeManagerBuilder builder = manager.NewBuilder();
            INodeBuilder parent = builder.Node(new NodeId(100u, 1));
            var parentObject = (BaseObjectState)parent.Node;

            Assert.That(
                parentObject.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.Zero,
                "precondition: the parent starts without SubscribeToEvents");

            NonExclusiveLimitAlarmState alarm = parent
                .CreateLimitAlarm(new QualifiedName("Level", 1))
                .Alarm;

            await manager.ActivateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    alarm.EnabledState?.Id?.Value,
                    Is.True,
                    "attaching an alarm enables it");
                Assert.That(
                    parentObject.EventNotifier & EventNotifiers.SubscribeToEvents,
                    Is.Not.Zero,
                    "attaching an alarm promotes the parent notifier");
            });

            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    alarm.EnabledState?.Id?.Value,
                    Is.False,
                    "teardown must disable the condition");
                Assert.That(
                    parentObject.EventNotifier & EventNotifiers.SubscribeToEvents,
                    Is.Zero,
                    "teardown must clear the notifier bit it set");
            });
        }

        [Test]
        public async Task PublishWiringIsUndoneOnTeardownAsync()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            NodeManagerBuilder builder = manager.NewBuilder();
            var notifier = (BaseObjectState)builder.Node(new NodeId(100u, 1)).Node;

            Assert.That(
                notifier.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.Zero,
                "precondition: the notifier starts without SubscribeToEvents");

            builder.Node<BaseObjectState>(notifier.NodeId)
                .Publish<BaseObjectState, BaseEventState>(
                    (_, _, _) => EmptyEventsAsync(),
                    new EventPublishOptions { RegisterAsRootNotifier = false });

            Assert.That(
                notifier.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.Not.Zero,
                "registering a Publish source promotes the notifier");

            await manager.ActivateAsync().ConfigureAwait(false);
            await manager.DeleteAddressSpaceAsync().ConfigureAwait(false);

            Assert.That(
                notifier.EventNotifier & EventNotifiers.SubscribeToEvents,
                Is.Zero,
                "teardown must clear the notifier bit registration set");
        }

        [Test]
        public async Task ReleaseFailuresAreAggregatedAndDoNotStopOtherReleasesAsync()
        {
            var log = new List<string>();
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            NodeManagerBuilder builder = manager.NewBuilder();
            builder.Attach((ctx, ct) => new ValueTask<IAsyncDisposable?>(
                new CallbackDisposable(() => log.Add("first"))));
            builder.Attach((ctx, ct) => new ValueTask<IAsyncDisposable?>(
                new CallbackDisposable(() => throw new InvalidOperationException("boom A"))));
            builder.Attach((ctx, ct) => new ValueTask<IAsyncDisposable?>(
                new CallbackDisposable(() => throw new InvalidOperationException("boom B"))));

            await manager.ActivateAsync().ConfigureAwait(false);

            AggregateException ex = Assert.ThrowsAsync<AggregateException>(
                async () => await manager.DeleteAddressSpaceAsync().ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(
                    ex.InnerExceptions.Select(e => e.Message),
                    Is.EquivalentTo(s_bothReleaseFailures),
                    "every release failure must surface, not just the first");
                Assert.That(
                    log,
                    Is.EqualTo(s_firstOnly),
                    "a failing release must not stop the remaining ones");
            });
        }

        [Test]
        public async Task SynchronousDisposeTripsTheLifetimeOfAnActivatedBehaviorAsync()
        {
            CancellationToken lifetime = default;
            var manager = new TestBehaviorManager();
            try
            {
                manager.SeedSiblings(1);

                NodeManagerBuilder builder = manager.NewBuilder();
                builder.Attach((ctx, ct) =>
                {
                    lifetime = ctx.Lifetime;
                    return new ValueTask<IAsyncDisposable?>(
                        new CallbackDisposable(() => { }));
                });

                await manager.ActivateAsync().ConfigureAwait(false);
                Assert.That(lifetime.IsCancellationRequested, Is.False);

                // Dispose is signal-only, but it still has to stop background work:
                // the awaited release belongs to DeleteAddressSpaceAsync.
                manager.Dispose();

                Assert.That(
                    lifetime.IsCancellationRequested,
                    Is.True,
                    "synchronous disposal must still trip every behavior's lifetime");
            }
            finally
            {
                manager.Dispose();
            }
        }

        [Test]
        public void ActivationFailureWithAFailingRollbackReportsBoth()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            NodeManagerBuilder builder = manager.NewBuilder();
            builder.Attach((ctx, ct) => new ValueTask<IAsyncDisposable?>(
                new CallbackDisposable(
                    () => throw new InvalidOperationException("release failed"))));
            builder.Attach((ctx, ct) =>
                throw new InvalidOperationException("activation failed"));

            AggregateException ex = Assert.ThrowsAsync<AggregateException>(
                async () => await manager.ActivateAsync().ConfigureAwait(false))!;

            Assert.That(
                ex.InnerExceptions.Select(e => e.Message),
                Is.EquivalentTo(s_activationAndRollbackFailures),
                "a failed rollback must not hide the failure that triggered it");
        }

        [Test]
        public void AttachToTypeRejectsATypeFromAnUnregisteredNamespace()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            NodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<BaseObjectState>(
                new NodeId(5000u, 99),
                (node, ctx, ct) => new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null),
                new NodeAttachOptions { AllowZeroMatches = true });

            // A namespace index the server does not know cannot be made
            // namespace-stable, so the registration cannot be matched at all.
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateAsync().ConfigureAwait(false))!;
            Assert.That(
                ex.StatusCode,
                Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void AttachToTypeThrowsWhenTheInstanceIsNotTheExpectedStateType()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            NodeManagerBuilder builder = manager.NewBuilder();
            builder.AttachToType<PropertyState>(
                TestBehaviorManager.DerivedTypeId,
                (node, ctx, ct) => new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null));

            // The seeded instances are objects. Failing loudly beats skipping them,
            // which would leave a whole set of nodes silently unwired.
            ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.ActivateAsync().ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadTypeMismatch));
                Assert.That(ex.Message, Does.Contain(nameof(PropertyState)));
                Assert.That(ex.Message, Does.Contain(nameof(BaseObjectState)));
            });
        }

        [Test]
        public async Task AttachContextResolvesNodesByNodeIdAsync()
        {
            NodeState? found = null;
            NodeState? missing = null;

            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);

            NodeManagerBuilder builder = manager.NewBuilder();
            builder.Attach((ctx, ct) =>
            {
                found = ctx.Find(new NodeId(100u, 1));
                missing = ctx.Find(new NodeId(4242u, 1));
                return new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null);
            });

            await manager.ActivateAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found?.BrowseName.Name, Is.EqualTo("Sibling0"));
                Assert.That(missing, Is.Null);
            });
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            using var manager = new TestBehaviorManager();
            manager.SeedSiblings(1);
            NodeManagerBuilder builder = manager.NewBuilder();

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => builder.AttachToType<BaseObjectState>(
                        TestBehaviorManager.DerivedTypeId,
                        null!));
                Assert.Throws<ArgumentNullException>(
                    () => builder.Attach(
                        (Func<INodeAttachContext, CancellationToken,
                            ValueTask<IAsyncDisposable?>>)null!));
                Assert.Throws<ArgumentNullException>(
                    () => builder.AttachToType<BaseObjectState>(
                        NodeId.Null,
                        (node, ctx, ct) =>
                            new ValueTask<IAsyncDisposable?>((IAsyncDisposable?)null)));
            });
        }

        private static async IAsyncEnumerable<BaseEventState> EmptyEventsAsync()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        private static readonly string[] s_firstOnly = ["first"];
        private static readonly string[] s_bothReleaseFailures = ["boom A", "boom B"];
        private static readonly string[] s_activationAndRollbackFailures =
            ["activation failed", "release failed"];

        private static readonly string[] s_activationOrder =
        [
            "activate base:Child",
            "activate derived:Child",
            "activate base:Parent",
            "activate derived:Parent"
        ];

        private static readonly string[] s_teardownOrder =
        [
            "dispose derived:Parent",
            "dispose base:Parent",
            "dispose derived:Child",
            "dispose base:Child"
        ];

        private static readonly string[] s_rollbackOrder =
        [
            "activate base:Child",
            "activate derived:Child",
            "activate base:Parent",
            "dispose base:Parent",
            "dispose derived:Child",
            "dispose base:Child"
        ];

        private static readonly string[] s_activateSibling1 = ["activate Sibling1"];
        private static readonly string[] s_disposeSibling1 = ["dispose Sibling1"];
        private static readonly string[] s_activateBothPasses =
            ["activate first", "activate second"];
        private static readonly string[] s_disposeBothPasses =
            ["dispose second", "dispose first"];
        private static readonly string[] s_activateOnce = ["activate once"];

        private static ValueTask<IAsyncDisposable?> Record(List<string> log, string label)
        {
            log.Add($"activate {label}");
            return new ValueTask<IAsyncDisposable?>(
                new CallbackDisposable(() => log.Add($"dispose {label}")));
        }

        private sealed class CallbackDisposable : IAsyncDisposable
        {
            public CallbackDisposable(Action onDispose)
            {
                m_onDispose = onDispose;
            }

            public ValueTask DisposeAsync()
            {
                m_onDispose();
                return default;
            }

            private readonly Action m_onDispose;
        }

        /// <summary>
        /// A fluent manager whose predefined nodes are seeded directly, so the tests
        /// exercise activation rather than the node-loading pipeline.
        /// </summary>
        private sealed class TestBehaviorManager : FluentNodeManagerBase
        {
            public static readonly NodeId BaseTypeId = new(5000u);
            public static readonly NodeId DerivedTypeId = new(5001u);
            public static readonly NodeId UnmatchedTypeId = new(5002u);

            public TestBehaviorManager()
                : base(CreateMockServer(), TestNamespaceUri)
            {
            }

            public NodeManagerBuilder NewBuilder()
            {
                return CreateFluentBuilder(1);
            }

            public ValueTask ActivateAsync()
            {
                return ActivateNodeBehaviorsAsync(CancellationToken.None);
            }

            public void SeedParentAndChild()
            {
                BaseObjectState parent = CreateNode("Parent", 1, DerivedTypeId, null);
                BaseObjectState child = CreateNode("Child", 2, DerivedTypeId, parent);
                PredefinedNodes.Add(parent.NodeId, parent);
                PredefinedNodes.Add(child.NodeId, child);
            }

            public void SeedMethod()
            {
                var method = new MethodState(null)
                {
                    NodeId = new NodeId(900u, 1),
                    BrowseName = new QualifiedName("DoWork", 1),
                    DisplayName = new LocalizedText("DoWork"),
                    // Setting the declaration id also sets TypeDefinitionId.
                    MethodDeclarationId = DerivedTypeId
                };
                PredefinedNodes.Add(method.NodeId, method);
            }

            public void SeedSiblings(int count)
            {
                for (int i = 0; i < count; i++)
                {
                    BaseObjectState node = CreateNode(
                        $"Sibling{i}",
                        (uint)(100 + i),
                        DerivedTypeId,
                        null);
                    PredefinedNodes.Add(node.NodeId, node);
                }
            }

            private static BaseObjectState CreateNode(
                string name,
                uint id,
                NodeId typeDefinitionId,
                NodeState? parent)
            {
                return new BaseObjectState(parent)
                {
                    NodeId = new NodeId(id, 1),
                    BrowseName = new QualifiedName(name, 1),
                    DisplayName = new LocalizedText(name),
                    TypeDefinitionId = typeDefinitionId
                };
            }

            private const string TestNamespaceUri = "urn:test:node-attach";

            private static IServerInternal CreateMockServer()
            {
                var ns = new NamespaceTable();
                ns.Append(Ua.Namespaces.OpcUa);
                ns.Append(TestNamespaceUri);

                var typeTree = new TypeTable(ns);
                // The supertype has to exist before a subtype can point at it.
                typeTree.AddSubtype(BaseTypeId, NodeId.Null);
                typeTree.AddSubtype(DerivedTypeId, BaseTypeId);

                var mockTelemetry = new Mock<ITelemetryContext>();
                var mock = new Mock<IServerInternal>();
                mock.SetupGet(m => m.NamespaceUris).Returns(ns);
                mock.SetupGet(m => m.TypeTree).Returns(typeTree);
                mock.SetupGet(m => m.Telemetry).Returns(mockTelemetry.Object);
                IServiceMessageContext msgCtx = ServiceMessageContext.Create(
                    mockTelemetry.Object);
                mock.SetupGet(m => m.MessageContext).Returns(msgCtx);
                mock.SetupGet(m => m.DefaultSystemContext).Returns(
                    new ServerSystemContext(mock.Object));
                return mock.Object;
            }
        }
    }
}
