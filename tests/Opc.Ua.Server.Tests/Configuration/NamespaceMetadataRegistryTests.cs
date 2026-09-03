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
using Opc.Ua.Server.Tests.NodeManager;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Unit tests for <see cref="NamespaceMetadataRegistry"/> against a
    /// hand-built <c>Server/Namespaces</c> subtree and a fake host, without a
    /// running server. The end-to-end behaviour through
    /// <see cref="IConfigurationNodeManager"/> is covered by
    /// <see cref="ConfigurationNodeManagerTests"/>.
    /// </summary>
    [TestFixture]
    [Category("ConfigurationNodeManager")]
    [Parallelizable(ParallelScope.All)]
    public class NamespaceMetadataRegistryTests
    {
        private static readonly ITelemetryContext s_telemetry = NUnitTelemetryContext.Create();

        /// <summary>
        /// Minimal host: a mocked server, its system context and an optional
        /// <c>Server/Namespaces</c> node. Registered nodes are collected so
        /// tests can assert on them.
        /// </summary>
        private sealed class FakeHost : INamespaceMetadataHost, INodeIdFactory
        {
            public FakeHost(bool withNamespacesNode)
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out _);
                Server = server.Object;
                SystemContext = new ServerSystemContext(Server) { NodeIdFactory = this };

                if (withNamespacesNode)
                {
                    NamespacesNode = new NamespacesState(null)
                    {
                        NodeId = ObjectIds.Server_Namespaces,
                        BrowseName = new QualifiedName(BrowseNames.Namespaces, 0)
                    };
                }
            }

            public ServerSystemContext SystemContext { get; }

            public ushort NamespaceIndex => 1;

            public IServerInternal Server { get; }

            public NamespacesState? NamespacesNode { get; }

            public List<NodeState> RegisteredNodes { get; } = [];

            public NamespacesState? FindServerNamespacesNode()
            {
                return NamespacesNode;
            }

            public ValueTask AddPredefinedNodeAsync(NodeState node, CancellationToken cancellationToken)
            {
                RegisteredNodes.Add(node);
                return default;
            }

            public NodeId New(ISystemContext context, NodeState node)
            {
                return new NodeId(Guid.NewGuid(), NamespaceIndex);
            }
        }

        private static NamespaceMetadataRegistry CreateRegistry(FakeHost host)
        {
            return new NamespaceMetadataRegistry(host, s_telemetry.CreateLogger<NamespaceMetadataRegistry>());
        }

        /// <summary>
        /// Adds a metadata node with a <c>DefaultRolePermissions</c> property
        /// under the namespaces node the way a NodeSet or another manager would.
        /// </summary>
        private static NamespaceMetadataState AddMetadataNode(NamespacesState parent, string namespaceUri)
        {
            var metadata = new NamespaceMetadataState(parent)
            {
                NodeId = new NodeId(Guid.NewGuid(), 1),
                BrowseName = new QualifiedName(namespaceUri, 1)
            };
            metadata.NamespaceUri = new PropertyState<string>.Implementation<VariantBuilder>(metadata)
            {
                Value = namespaceUri
            };
            metadata.DefaultRolePermissions = new PropertyState<ArrayOf<RolePermissionType>>
                .Implementation<StructureBuilder<RolePermissionType>>(metadata)
            {
                NodeId = new NodeId(Guid.NewGuid(), 1),
                BrowseName = new QualifiedName(BrowseNames.DefaultRolePermissions, 1),
                Value = []
            };
            metadata.AddChild(metadata.DefaultRolePermissions);
            parent.AddChild(metadata);
            return metadata;
        }

        /// <summary>
        /// Writes a new, distinct permission set so a <c>Value</c> change mask
        /// is raised on every call, then flushes the masks to fire
        /// <c>StateChanged</c>.
        /// </summary>
        private static void ChangeDefaultRolePermissions(NamespaceMetadataState metadata, ISystemContext context)
        {
            uint permissions = (uint)Interlocked.Increment(ref s_permissionCounter);
            metadata.DefaultRolePermissions!.Value =
            [
                new RolePermissionType
                {
                    RoleId = ObjectIds.WellKnownRole_Observer,
                    Permissions = permissions
                }
            ];
            metadata.ClearChangeMasks(context, true);
        }

        private static int s_permissionCounter;

        [Test]
        public void ConstructorRejectsNullArguments()
        {
            var host = new FakeHost(withNamespacesNode: false);
            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => new NamespaceMetadataRegistry(null!, s_telemetry.CreateLogger<NamespaceMetadataRegistry>()));
                Assert.Throws<ArgumentNullException>(() => new NamespaceMetadataRegistry(host, null!));
            });
        }

        [Test]
        public async Task GetWithNullUriReturnsNullAsync()
        {
            NamespaceMetadataRegistry registry = CreateRegistry(new FakeHost(withNamespacesNode: true));
            Assert.That(await registry.GetAsync((string)null!).ConfigureAwait(false), Is.Null);
        }

        [Test]
        public async Task WithoutNamespacesNodeNothingIsFoundOrCreatedAsync()
        {
            var host = new FakeHost(withNamespacesNode: false);
            NamespaceMetadataRegistry registry = CreateRegistry(host);

            NamespaceMetadataState? found = await registry.GetAsync("urn:missing").ConfigureAwait(false);
            NamespaceMetadataState created = await registry.CreateAsync("urn:missing").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.Null);
                Assert.That(created, Is.Null);
                Assert.That(host.RegisteredNodes, Is.Empty);
                Assert.DoesNotThrow(() => registry.Attach(host.SystemContext));
                Assert.DoesNotThrow(() => registry.Detach([]));
            });
        }

        [Test]
        public async Task GetFindsExistingChildByUriAndByIndexAndCachesItAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState metadata = AddMetadataNode(host.NamespacesNode!, DeterministicServerMock.TestNamespaceUri);
            NamespaceMetadataRegistry registry = CreateRegistry(host);

            NamespaceMetadataState? byUri = await registry.GetAsync(DeterministicServerMock.TestNamespaceUri).ConfigureAwait(false);
            NamespaceMetadataState? byUriAgain = await registry.GetAsync(DeterministicServerMock.TestNamespaceUri).ConfigureAwait(false);
            ushort index = (ushort)host.Server.NamespaceUris.GetIndex(DeterministicServerMock.TestNamespaceUri);
            NamespaceMetadataState? byIndex = await registry.GetAsync(index).ConfigureAwait(false);
            NamespaceMetadataState? unknown = await registry.GetAsync("urn:unknown").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(byUri, Is.SameAs(metadata));
                Assert.That(byUriAgain, Is.SameAs(metadata));
                Assert.That(byIndex, Is.SameAs(metadata));
                Assert.That(unknown, Is.Null);
                Assert.That(host.RegisteredNodes, Is.Empty, "lookups never register nodes");
            });
        }

        [Test]
        public async Task CreateReturnsExistingNodeOrRegistersANewOneAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState existing = AddMetadataNode(host.NamespacesNode!, "urn:existing");
            NamespaceMetadataRegistry registry = CreateRegistry(host);

            NamespaceMetadataState found = await registry.CreateAsync("urn:existing").ConfigureAwait(false);
            NamespaceMetadataState created = await registry.CreateAsync("urn:created").ConfigureAwait(false);
            NamespaceMetadataState createdAgain = await registry.CreateAsync("urn:created").ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(found, Is.SameAs(existing));
                Assert.That(created, Is.Not.Null);
                Assert.That(created.NamespaceUri!.Value, Is.EqualTo("urn:created"));
                Assert.That(created.BrowseName.NamespaceIndex, Is.EqualTo(host.NamespaceIndex));
                Assert.That(created.DefaultRolePermissions, Is.Not.Null);
                Assert.That(createdAgain, Is.SameAs(created), "a second create finds the registered node");
                Assert.That(host.RegisteredNodes, Is.EqualTo(new[] { created }));
            });
        }

        [Test]
        public async Task AttachTracksDefaultPermissionChangesAndDetachStopsAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataState metadata = AddMetadataNode(host.NamespacesNode!, "urn:tracked");
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            int raised = 0;
            registry.DefaultPermissionsChanged += (_, _) => raised++;

            // Not attached: changes are not observed.
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            Assert.That(raised, Is.Zero);

            registry.Attach(host.SystemContext);
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            Assert.That(raised, Is.EqualTo(1));

            // A node created through the registry is tracked as well.
            NamespaceMetadataState created = await registry.CreateAsync("urn:created").ConfigureAwait(false);
            ChangeDefaultRolePermissions(created, host.SystemContext);
            Assert.That(raised, Is.EqualTo(2));

            registry.Detach([metadata, created]);
            ChangeDefaultRolePermissions(metadata, host.SystemContext);
            ChangeDefaultRolePermissions(created, host.SystemContext);
            Assert.That(raised, Is.EqualTo(2), "detached nodes no longer raise the event");
        }

        [Test]
        public async Task NamespacesChangeInvalidatesCacheAndTracksNewChildrenAsync()
        {
            var host = new FakeHost(withNamespacesNode: true);
            NamespaceMetadataRegistry registry = CreateRegistry(host);
            registry.Attach(host.SystemContext);
            int raised = 0;
            registry.DefaultPermissionsChanged += (_, _) => raised++;

            // Cache the negative result first.
            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.Null);

            // Add a child the way another manager would and signal the change.
            NamespaceMetadataState late = AddMetadataNode(host.NamespacesNode!, "urn:late");
            host.NamespacesNode!.ClearChangeMasks(host.SystemContext, true);

            Assert.That(await registry.GetAsync("urn:late").ConfigureAwait(false), Is.SameAs(late),
                "the cached negative result is dropped when Server/Namespaces changes");

            ChangeDefaultRolePermissions(late, host.SystemContext);
            Assert.That(raised, Is.EqualTo(1), "children discovered through the change are tracked");
        }
    }
}
