using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Di.Tests
{
    [TestFixture]
    [Category("DI")]
    public sealed class DiNodeManagerResourceAndOrganizationTests
    {
        [Test]
        public async Task ResourceDrainUsesReverseOrderAndReferenceIdentityAsync()
        {
            DiNodeManager manager = CreateManager();
            var trace = new List<string>();
            var first = new Probe("same", trace);
            var equalButDistinct = new Probe("same", trace);
            var last = new Probe("last", trace);
            manager.RegisterResource(first);
            manager.RegisterResource(equalButDistinct);
            manager.RegisterResource(first);
            manager.RegisterResource(last);

            await manager.DisposeResourcesAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "last", "same", "same" }));
                Assert.That(last.Count, Is.EqualTo(1));
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(equalButDistinct.Count, Is.EqualTo(1));
            });
            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ResourceDrainHandlesEmptyRepeatedAndLaterBatchesAsync()
        {
            DiNodeManager manager = CreateManager();
            var trace = new List<string>();
            await manager.DisposeResourcesAsync().ConfigureAwait(false);
            var first = new Probe("first", trace);
            manager.RegisterResource(first);
            await manager.DisposeResourcesAsync().ConfigureAwait(false);
            await manager.DisposeResourcesAsync().ConfigureAwait(false);
            var later = new Probe("later", trace);
            manager.RegisterResource(later);
            await manager.DisposeResourcesAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "first", "later" }));
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(later.Count, Is.EqualTo(1));
            });
            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task FailedDrainAttemptsEveryResourceAggregatesAndClearsBatchAsync()
        {
            DiNodeManager manager = CreateManager();
            var trace = new List<string>();
            var firstError = new InvalidOperationException("first");
            var lastError = new ArgumentException("last");
            var first = new Probe("first", trace, firstError);
            var middle = new Probe("middle", trace);
            var last = new Probe("last", trace, lastError);
            manager.RegisterResource(first);
            manager.RegisterResource(middle);
            manager.RegisterResource(last);

            AggregateException error = (await Assert.ThrowsAsync<AggregateException>(
                () => manager.DisposeResourcesAsync().AsTask()).ConfigureAwait(false))!;
            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "last", "middle", "first" }));
                Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(error.InnerExceptions[0], Is.SameAs(lastError));
                Assert.That(error.InnerExceptions[1], Is.SameAs(firstError));
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(middle.Count, Is.EqualTo(1));
                Assert.That(last.Count, Is.EqualTo(1));
            });
            await manager.DisposeResourcesAsync().ConfigureAwait(false);
            Assert.That(trace, Is.EqualTo(new List<string> { "last", "middle", "first" }));
            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task NullResourceIsRejectedWithoutDroppingExistingRegistrationAsync()
        {
            DiNodeManager manager = CreateManager();
            var trace = new List<string>();
            var valid = new Probe("valid", trace);
            manager.RegisterResource(valid);
            ArgumentNullException error = Assert.Throws<ArgumentNullException>(
                () => manager.RegisterResource(null!))!;
            await manager.DisposeResourcesAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(error.ParamName, Is.EqualTo("resource"));
                Assert.That(trace, Is.EqualTo(new List<string> { "valid" }));
                Assert.That(valid.Count, Is.EqualTo(1));
            });
            await manager.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task ManagerDisposalAutomaticallyDrainsResourcesAsync()
        {
            DiNodeManager manager = CreateManager();
            var trace = new List<string>();
            var first = new Probe("first", trace);
            var last = new Probe("last", trace);
            manager.RegisterResource(first);
            manager.RegisterResource(last);

            await manager.DisposeAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(trace, Is.EqualTo(new List<string> { "last", "first" }));
                Assert.That(last.Count, Is.EqualTo(1));
                Assert.That(first.Count, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task TryOrganizeUnderAddsCorrectBidirectionalOrganizesReferencesAsync()
        {
            OrganizationManager manager = CreateOrganizationManager();
            var target = new BaseObjectState(null)
            {
                NodeId = new NodeId("target", 1),
                BrowseName = new QualifiedName("Target")
            };
            var instance = new BaseObjectState(null)
            {
                NodeId = new NodeId("instance", 1),
                BrowseName = new QualifiedName("Instance")
            };
            await manager.AddForTestAsync(target).ConfigureAwait(false);
            int targetBefore = References(manager, target).Count;
            int instanceBefore = References(manager, instance).Count;

            bool organized = manager.TryOrganizeUnder(target.NodeId, instance);
            List<IReference> targetRefs = References(manager, target);
            List<IReference> instanceRefs = References(manager, instance);
            Assert.Multiple(() =>
            {
                Assert.That(organized, Is.True);
                Assert.That(targetRefs, Has.Count.EqualTo(targetBefore + 1));
                Assert.That(instanceRefs, Has.Count.EqualTo(instanceBefore + 1));
                Assert.That(targetRefs.Where(reference =>
                    reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.Organizes &&
                    !reference.IsInverse && reference.TargetId == instance.NodeId).ToList(),
                    Has.Count.EqualTo(1));
                Assert.That(instanceRefs.Where(reference =>
                    reference.ReferenceTypeId == Opc.Ua.Types.ReferenceTypeIds.Organizes &&
                    reference.IsInverse && reference.TargetId == target.NodeId).ToList(),
                    Has.Count.EqualTo(1));
            });
            manager.Dispose();
        }

        [Test]
        public async Task TryOrganizeUnderFalsePartitionsLeaveBothNodesUnchangedAsync()
        {
            OrganizationManager manager = CreateOrganizationManager();
            var target = new BaseObjectState(null)
            {
                NodeId = new NodeId("object", 1),
                BrowseName = new QualifiedName("Object")
            };
            var variable = new BaseDataVariableState(null)
            {
                NodeId = new NodeId("variable", 1),
                BrowseName = new QualifiedName("Variable")
            };
            var instance = new BaseObjectState(null)
            {
                NodeId = new NodeId("instance", 1),
                BrowseName = new QualifiedName("Instance")
            };
            await manager.AddForTestAsync(target).ConfigureAwait(false);
            await manager.AddForTestAsync(variable).ConfigureAwait(false);
            List<IReference> targetBefore = References(manager, target);
            List<IReference> variableBefore = References(manager, variable);
            List<IReference> instanceBefore = References(manager, instance);

            bool nullTarget = manager.TryOrganizeUnder(NodeId.Null, instance);
            bool missingTarget = manager.TryOrganizeUnder(new NodeId("absent", 1), instance);
            bool nonObjectTarget = manager.TryOrganizeUnder(variable.NodeId, instance);
            Assert.Multiple(() =>
            {
                Assert.That(nullTarget, Is.False);
                Assert.That(missingTarget, Is.False);
                Assert.That(nonObjectTarget, Is.False);
                Assert.That(References(manager, target), Is.EqualTo(targetBefore));
                Assert.That(References(manager, variable), Is.EqualTo(variableBefore));
                Assert.That(References(manager, instance), Is.EqualTo(instanceBefore));
            });
            manager.Dispose();
        }

        [Test]
        public void TryOrganizeUnderRejectsNullInstanceBeforeTargetLookup()
        {
            OrganizationManager manager = CreateOrganizationManager();
            ArgumentNullException missing = Assert.Throws<ArgumentNullException>(
                () => manager.TryOrganizeUnder(new NodeId("absent", 1), null!))!;
            ArgumentNullException nullTarget = Assert.Throws<ArgumentNullException>(
                () => manager.TryOrganizeUnder(NodeId.Null, null!))!;
            Assert.Multiple(() =>
            {
                Assert.That(missing.ParamName, Is.EqualTo("instance"));
                Assert.That(nullTarget.ParamName, Is.EqualTo("instance"));
            });
            manager.Dispose();
        }

        private static DiNodeManager CreateManager() =>
            new(CreateServer().Object, Configuration());

        private static OrganizationManager CreateOrganizationManager() =>
            new(CreateServer().Object, Configuration());

        private static Mock<IServerInternal> CreateServer()
        {
            var server = new Mock<IServerInternal>();
            var namespaces = new NamespaceTable();
            var master = new Mock<IMasterNodeManager>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(new TypeTable(namespaces));
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(value => value.NodeManager).Returns(master.Object);
            server.SetupGet(value => value.CoreNodeManager).Returns(Mock.Of<ICoreNodeManager>());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        private static ApplicationConfiguration Configuration() => new()
        {
            ApplicationUri = "urn:opcfoundation:di-resource-tests",
            ServerConfiguration = new ServerConfiguration()
        };

        private static List<IReference> References(DiNodeManager manager, NodeState node)
        {
            var references = new List<IReference>();
            node.GetReferences(manager.SystemContext, references);
            return references;
        }

        private sealed class OrganizationManager(
            IServerInternal server,
            ApplicationConfiguration configuration) : DiNodeManager(server, configuration)
        {
            public ValueTask AddForTestAsync(NodeState node) => AddPredefinedNodeAsync(node);
        }

        private sealed class Probe : IAsyncDisposable
        {
            private readonly List<string> m_trace;
            private readonly Exception? m_error;

            public Probe(string label, List<string> trace, Exception? error = null)
            {
                Label = label;
                m_trace = trace;
                m_error = error;
            }

            public string Label { get; }
            public int Count { get; private set; }

            public ValueTask DisposeAsync()
            {
                Count++;
                m_trace.Add(Label);
                return m_error == null ? default : new ValueTask(Task.FromException(m_error));
            }

            public override bool Equals(object? obj) => obj is Probe probe && probe.Label == Label;
            public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Label);
        }
    }
}
