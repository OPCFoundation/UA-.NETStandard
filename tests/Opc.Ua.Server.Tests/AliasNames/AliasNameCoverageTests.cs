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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server.Tests.AliasNames
{
    /// <summary>
    /// Direct coverage tests for the Part 17 alias configuration value type
    /// <see cref="AliasDefinition"/>: constructor validation and property
    /// exposure.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasDefinitionTests
    {
        private static readonly ExpandedNodeId s_target = new("Target1", 1);

        [Test]
        public void ValidConstructionExposesProperties()
        {
            var name = new QualifiedName("Alias", 1);
            var targets = new List<ExpandedNodeId> { s_target };
            var serverUris = new List<string?> { null };

            var definition = new AliasDefinition(
                name, targets, ReferenceTypeIds.HasComponent, serverUris);

            Assert.That(definition.Name, Is.EqualTo(name));
            Assert.That(definition.ReferencedNodes, Is.EqualTo(targets));
            Assert.That(definition.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
            Assert.That(definition.ServerUris, Is.EqualTo(serverUris));
        }

        [Test]
        public void DefaultServerUrisIsNull()
        {
            var definition = new AliasDefinition(
                new QualifiedName("Alias", 1),
                new List<ExpandedNodeId> { s_target },
                ReferenceTypeIds.HasComponent);

            Assert.That(definition.ServerUris, Is.Null);
        }

        [Test]
        public void NullReferencedNodesThrowsArgumentNullException()
        {
            Assert.That(
                () => new AliasDefinition(
                    new QualifiedName("Alias", 1), null!, ReferenceTypeIds.HasComponent),
                Throws.TypeOf<ArgumentNullException>());
        }

        [Test]
        public void NullNameThrowsArgumentException()
        {
            Assert.That(
                () => new AliasDefinition(
                    QualifiedName.Null,
                    new List<ExpandedNodeId> { s_target },
                    ReferenceTypeIds.HasComponent),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void EmptyReferencedNodesThrowsArgumentException()
        {
            Assert.That(
                () => new AliasDefinition(
                    new QualifiedName("Alias", 1),
                    new List<ExpandedNodeId>(),
                    ReferenceTypeIds.HasComponent),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void NullReferenceTypeIdThrowsArgumentException()
        {
            Assert.That(
                () => new AliasDefinition(
                    new QualifiedName("Alias", 1),
                    new List<ExpandedNodeId> { s_target },
                    NodeId.Null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ServerUrisLongerThanReferencedNodesThrowsArgumentException()
        {
            Assert.That(
                () => new AliasDefinition(
                    new QualifiedName("Alias", 1),
                    new List<ExpandedNodeId> { s_target },
                    ReferenceTypeIds.HasComponent,
                    new List<string?> { "uri:a", "uri:b" }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void ServerUrisEqualLengthIsAccepted()
        {
            var definition = new AliasDefinition(
                new QualifiedName("Alias", 1),
                new List<ExpandedNodeId> { s_target },
                ReferenceTypeIds.HasComponent,
                new List<string?> { "uri:a" });

            Assert.That(definition.ServerUris, Has.Count.EqualTo(1));
        }
    }

    /// <summary>
    /// Direct coverage tests for <see cref="AliasNameCategoryDescriptor"/>:
    /// constructor validation, defaults and property exposure.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameCategoryDescriptorTests
    {
        [Test]
        public void ValidConstructionAppliesDefaults()
        {
            var nodeId = new NodeId("Category", 1);
            var browseName = new QualifiedName("Category", 1);

            var descriptor = new AliasNameCategoryDescriptor(nodeId, browseName);

            Assert.That(descriptor.NodeId, Is.EqualTo(nodeId));
            Assert.That(descriptor.BrowseName, Is.EqualTo(browseName));
            Assert.That(descriptor.Capabilities, Is.EqualTo(AliasNameCapabilities.None));
            Assert.That(descriptor.SubCategories, Is.Empty);
        }

        [Test]
        public void SubCategoriesAndCapabilitiesArePreserved()
        {
            var child = new AliasNameCategoryDescriptor(
                new NodeId("Child", 1), new QualifiedName("Child", 1));

            var descriptor = new AliasNameCategoryDescriptor(
                new NodeId("Category", 1),
                new QualifiedName("Category", 1),
                AliasNameCapabilities.All,
                [child]);

            Assert.That(descriptor.Capabilities, Is.EqualTo(AliasNameCapabilities.All));
            Assert.That(descriptor.SubCategories, Has.Count.EqualTo(1));
            Assert.That(descriptor.SubCategories[0], Is.SameAs(child));
        }

        [Test]
        public void NullNodeIdThrowsArgumentException()
        {
            Assert.That(
                () => new AliasNameCategoryDescriptor(
                    NodeId.Null, new QualifiedName("Category", 1)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void NullBrowseNameThrowsArgumentException()
        {
            Assert.That(
                () => new AliasNameCategoryDescriptor(
                    new NodeId("Category", 1), QualifiedName.Null),
                Throws.TypeOf<ArgumentException>());
        }
    }

    /// <summary>
    /// Direct coverage tests for <see cref="AliasStoreChangedEventArgs"/>.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasStoreChangedEventArgsTests
    {
        [Test]
        public void ValidConstructionExposesProperties()
        {
            var categoryId = new NodeId("Category", 1);

            var args = new AliasStoreChangedEventArgs(categoryId, 42u);

            Assert.That(args.CategoryId, Is.EqualTo(categoryId));
            Assert.That(args.LastChange, Is.EqualTo(42u));
        }

        [Test]
        public void NullCategoryIdThrowsArgumentException()
        {
            Assert.That(
                () => new AliasStoreChangedEventArgs(NodeId.Null, 1u),
                Throws.TypeOf<ArgumentException>());
        }
    }

    /// <summary>
    /// Coverage tests for the <see cref="AliasNameNodeManager"/> code paths
    /// not exercised by <c>AliasNameNodeManagerTests</c>: sub-category tree
    /// construction, the <c>Changed</c>-event fallback that locates a nested
    /// category node, and server-registry registration / dispose handling.
    /// </summary>
    [TestFixture]
    [Category("AliasNames")]
    [Parallelizable]
    public class AliasNameNodeManagerCoverageTests
    {
        private const string c_namespaceUri = "http://example.org/AliasNames/Coverage/";

        private ApplicationConfiguration m_configuration = null!;

        [SetUp]
        public void SetUp()
        {
            m_configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MaxNotificationQueueSize = 100,
                    MaxDurableNotificationQueueSize = 200
                }
            };
        }

        private static Mock<IServerInternal> CreateMockServer(
            IAliasNameStoreRegistry? registry = null)
        {
            var mock = new Mock<IServerInternal>();

            if (registry != null)
            {
                mock.As<IAliasNameStoreRegistryProvider>()
                    .Setup(p => p.AliasNameStoreRegistry)
                    .Returns(registry);
            }

            var namespaceTable = new NamespaceTable();
            namespaceTable.Append(c_namespaceUri);
            var typeTable = new TypeTable(namespaceTable);
            ITelemetryContext telemetry = Ua.Tests.NUnitTelemetryContext.Create();

            mock.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            mock.Setup(s => s.ServerUris).Returns(new StringTable());
            mock.Setup(s => s.TypeTree).Returns(typeTable);
            mock.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            mock.Setup(s => s.Telemetry).Returns(telemetry);
            mock.Setup(s => s.DefaultSystemContext)
                .Returns(new ServerSystemContext(mock.Object));

            return mock;
        }

        private AliasNameNodeManager CreateManager(
            Mock<IServerInternal> mockServer,
            IAliasNameStore store,
            bool registerWithServerRegistry)
        {
            return new AliasNameNodeManager(
                mockServer.Object,
                m_configuration,
                store,
                new AliasNameNodeManagerOptions
                {
                    NamespaceUri = c_namespaceUri,
                    RegisterWithServerRegistry = registerWithServerRegistry
                });
        }

        [Test]
        public async Task SubCategoryChangeUpdatesNestedLastChangeAsync()
        {
            var subCategoryId = new NodeId("SubCategory", 1);
            var subDescriptor = new AliasNameCategoryDescriptor(
                subCategoryId,
                new QualifiedName("SubCategory", 1),
                AliasNameCapabilities.All);
            var rootDescriptor = new AliasNameCategoryDescriptor(
                new NodeId("RootCategory", 1),
                new QualifiedName("RootCategory", 1),
                AliasNameCapabilities.All,
                [subDescriptor]);

            using var store = new InMemoryAliasNameStore([rootDescriptor]);
            Mock<IServerInternal> mockServer = CreateMockServer();
            using AliasNameNodeManager manager = CreateManager(mockServer, store, false);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None).ConfigureAwait(false);

            uint before = store.GetLastChange(subCategoryId) ?? 0u;

            StatusCode[] results = await store.AddAliasesAsync(
                subCategoryId,
                [
                    new AliasAddRequest(
                        "NestedAlias",
                        new ExpandedNodeId("Nested", 1),
                        null,
                        ReferenceTypeIds.HasComponent)
                ]).ConfigureAwait(false);

            Assert.That(results, Has.Length.EqualTo(1));
            Assert.That(StatusCode.IsGood(results[0]), Is.True);
            Assert.That(store.GetLastChange(subCategoryId) ?? 0u, Is.Not.EqualTo(before));
        }

        [Test]
        public async Task RegistersWithServerRegistryAndUnregistersOnDisposeAsync()
        {
            var descriptor = new AliasNameCategoryDescriptor(
                new NodeId("RegCategory", 1),
                new QualifiedName("RegCategory", 1),
                AliasNameCapabilities.All);
            using var store = new InMemoryAliasNameStore([descriptor]);
            using var registry = new AliasNameStoreRegistry();
            Mock<IServerInternal> mockServer = CreateMockServer(registry);

            AliasNameNodeManager manager = CreateManager(mockServer, store, true);
            try
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                await manager.CreateAddressSpaceAsync(externalReferences, CancellationToken.None).ConfigureAwait(false);

                Assert.That(registry.Stores, Does.Contain(store));
            }
            finally
            {
                manager.Dispose();
            }

            Assert.That(registry.Stores, Does.Not.Contain(store));
        }

        [Test]
        public Task RegistryRegistrationConflictIsSwallowedAsync()
        {
            var categoryId = new NodeId("SharedCategory", 1);
            var descriptor = new AliasNameCategoryDescriptor(
                categoryId,
                new QualifiedName("SharedCategory", 1),
                AliasNameCapabilities.All);

            using var registry = new AliasNameStoreRegistry();
            using var blocker = new InMemoryAliasNameStore([descriptor]);
            registry.Register(blocker);

            using var store = new InMemoryAliasNameStore([descriptor]);
            Mock<IServerInternal> mockServer = CreateMockServer(registry);
            using AliasNameNodeManager manager = CreateManager(mockServer, store, true);

            var externalReferences = new Dictionary<NodeId, IList<IReference>>();
            Assert.That(
                async () => await manager.CreateAddressSpaceAsync(
                    externalReferences, CancellationToken.None).ConfigureAwait(false),
                Throws.Nothing);

            // Registration was rejected, so only the pre-existing blocker
            // store remains registered.
            Assert.That(registry.Stores, Does.Not.Contain(store));
            Assert.That(registry.Stores, Does.Contain(blocker));
            return Task.CompletedTask;
        }
    }
}
