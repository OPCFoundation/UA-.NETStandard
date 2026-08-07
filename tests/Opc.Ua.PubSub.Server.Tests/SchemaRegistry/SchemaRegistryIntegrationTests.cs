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

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.PubSub.Server.SchemaRegistry;
using Opc.Ua.PubSub.SchemaRegistry;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;


namespace Opc.Ua.PubSub.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// Address-space integration tests that prove the experimental in-server Schema Registry
    /// AddressSpace model materializes in a real <see cref="ReferenceServer"/> when the
    /// Schema Registry companion NodeSet is imported and the compiled xRegistry base model is loaded
    /// by the xRegistry node managers. This proves out the OPC UA — Schema Registry spec's
    /// structural claims (SchemaRegistryType and the well-known SchemaRegistry object attached
    /// to the Server object, i=2253) against a concrete implementation.
    /// </summary>
    [TestFixture]
    [Category("SchemaRegistry")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class SchemaRegistryIntegrationTests
    {
        // The seed schema published by the fast-path manager, and its content-derived SchemaId.
        private static readonly ByteString s_seedDocument = SchemaRegistryOptions.SeedSchemaDocument;
        private static readonly ByteString s_seedSchemaId =
            SchemaContentIdProvider.Instance.ComputeContentId(
                "avro", SchemaRegistryOptions.SeedSchemaDocument.Span);
        private static readonly string s_seedSchemaIdAlg =
            SchemaContentIdProvider.Instance.GetAlgorithm("avro")!;

        private ServerFixture<SchemaRegistryTestServer> m_serverFixture;
        private SchemaRegistryTestServer m_server;
        private string m_pkiRoot;

        /// <summary>
        /// Starts a <see cref="SchemaRegistryTestServer"/> that loads the companion NodeSets.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(SchemaRegistryIntegrationTests),
                Guid.NewGuid().ToString("N"));

            m_serverFixture = new ServerFixture<SchemaRegistryTestServer>(
                t => new SchemaRegistryTestServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_serverFixture.StartAsync(m_pkiRoot)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the server and cleans up PKI artefacts.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            m_server?.Dispose();

            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// Both the abstract xRegistry base namespace and the Schema Registry companion
        /// namespace must be registered after startup, proving the two NodeSets loaded in
        /// dependency order.
        /// </summary>
        [Test]
        [Order(100)]
        public void CompanionNamespacesRegisteredAfterServerStart()
        {
            IServerInternal server = m_server.CurrentInstance;

            Assert.Multiple(() =>
            {
                Assert.That(
                    server.NamespaceUris.GetIndex(SchemaRegistryTestServer.XRegistryNamespaceUri),
                    Is.GreaterThan(0),
                    "The xRegistry base namespace should be registered.");
                Assert.That(
                    server.NamespaceUris.GetIndex(SchemaRegistryTestServer.SchemaRegistryNamespaceUri),
                    Is.GreaterThan(0),
                    "The Schema Registry namespace should be registered.");
            });
        }

        /// <summary>
        /// The <c>SchemaRegistryType</c> ObjectType must be present in the address space after
        /// startup, proving the Schema Registry type model imported.
        /// </summary>
        [Test]
        [Order(200)]
        public async Task SchemaRegistryTypeIsInAddressSpaceAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(SchemaRegistryTestServer.SchemaRegistryType, ns))
                .ConfigureAwait(false);

            Assert.That(node, Is.Not.Null, "SchemaRegistryType should be in the address space.");
            Assert.That(node.BrowseName.Name, Is.EqualTo("SchemaRegistryType"));
        }

        /// <summary>
        /// The well-known <c>SchemaRegistry</c> object must be present in the address space
        /// after startup, proving the well-known instance imported.
        /// </summary>
        [Test]
        [Order(300)]
        public async Task WellKnownSchemaRegistryObjectIsInAddressSpaceAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(SchemaRegistryTestServer.SchemaRegistryObject, ns))
                .ConfigureAwait(false);

            Assert.That(node, Is.Not.Null, "The well-known SchemaRegistry object should be in the address space.");
            Assert.That(node.BrowseName.Name, Is.EqualTo("SchemaRegistry"));
        }

        /// <summary>
        /// The well-known <c>SchemaRegistry</c> object must reference the standard Server
        /// object (i=2253) via an inverse HasComponent reference, proving the spec's claim
        /// that the registry attaches under the Server object (independent of PubSub).
        /// </summary>
        [Test]
        [Order(400)]
        public async Task SchemaRegistryObjectIsComponentOfServerAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(SchemaRegistryTestServer.SchemaRegistryObject, ns))
                .ConfigureAwait(false);

            Assert.That(node, Is.Not.Null);

            var references = new List<IReference>();
            node.GetReferences(server.DefaultSystemContext, references);

            bool componentOfServer = false;
            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                    reference.IsInverse &&
                    reference.TargetId == ObjectIds.Server)
                {
                    componentOfServer = true;
                    break;
                }
            }

            Assert.That(componentOfServer, Is.True,
                "The well-known SchemaRegistry object should be an inverse HasComponent of the Server object (i=2253).");
        }

        /// <summary>
        /// The well-known <c>SchemaRegistry</c> object must materialize its <c>GetSchema</c>
        /// method as a concrete Method node (BrowseName <c>GetSchema</c>) attached to it,
        /// proving the generated NodeSet yields a callable registry - the mandatory
        /// download fast path (spec §5.1) is present on the well-known instance.
        /// </summary>
        [Test]
        [Order(500)]
        public async Task WellKnownSchemaRegistryObjectMaterializesGetSchemaMethodAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            NodeState method = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(SchemaRegistryTestServer.SchemaRegistryGetSchemaMethod, ns))
                .ConfigureAwait(false);

            Assert.That(method, Is.Not.Null, "The GetSchema method should be materialized in the address space.");
            Assert.That(method.NodeClass, Is.EqualTo(NodeClass.Method));
            Assert.That(method.BrowseName.Name, Is.EqualTo("GetSchema"));

            // It must be a component of the well-known SchemaRegistry object.
            var references = new List<IReference>();
            method.GetReferences(server.DefaultSystemContext, references);

            bool componentOfRegistry = false;
            NodeId registryId = new NodeId(SchemaRegistryTestServer.SchemaRegistryObject, ns);
            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId == ReferenceTypeIds.HasComponent &&
                    reference.IsInverse &&
                    reference.TargetId == registryId)
                {
                    componentOfRegistry = true;
                    break;
                }
            }

            Assert.That(componentOfRegistry, Is.True,
                "The GetSchema method should be an inverse HasComponent of the well-known SchemaRegistry object.");
        }

        /// <summary>
        /// Proves the mandatory download path (spec §5.1): the materialized <c>GetSchema</c>
        /// method resolves a registered on-wire SchemaId to its schema document and metadata,
        /// and returns <c>Bad_NotFound</c> for an unregistered SchemaId. The handler is wired on
        /// the concrete Method node exactly as a server node manager would bind it to its store.
        /// </summary>
        [Test]
        [Order(600)]
        public async Task GetSchemaMethodResolvesRegisteredSchemaAndReportsNotFoundAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(SchemaRegistryTestServer.SchemaRegistryGetSchemaMethod, ns))
                .ConfigureAwait(false);

            var method = node as MethodState;
            Assert.That(method, Is.Not.Null, "GetSchema should be a concrete MethodState.");

            // One registered schema keyed by its raw on-wire SchemaId, and the download handler
            // a server binds to its schema store.
            ByteString knownSchemaId = ByteString.From([1, 2, 3, 4, 5, 6, 7, 8]);
            ByteString document = ByteString.From(
                System.Text.Encoding.UTF8.GetBytes("{\"type\":\"record\",\"name\":\"X\",\"fields\":[]}"));
            const string format = "Avro/1.11";
            const string contentType = "application/vnd.apache.avro+json";

            method!.OnCallMethod2 = (ctx, m, objectId, inputs, outputs) =>
            {
                if (!inputs[0].TryGetValue(out ByteString requested))
                {
                    return StatusCodes.BadInvalidArgument;
                }
                if (requested.Span.SequenceEqual(knownSchemaId.Span))
                {
                    outputs.Add(new Variant(document));
                    outputs.Add(new Variant(format));
                    outputs.Add(new Variant(contentType));
                    return ServiceResult.Good;
                }

                return StatusCodes.BadNotFound;
            };

            NodeId objectId = new NodeId(SchemaRegistryTestServer.SchemaRegistryObject, ns);

            // A registered SchemaId resolves to the schema document and metadata.
            var okOutputs = new List<Variant>();
            ServiceResult okResult = method.OnCallMethod2(
                server.DefaultSystemContext, method, objectId, [new Variant(knownSchemaId)], okOutputs);

            ByteString outDocument = default;
            string outFormat = null;
            string outContentType = null;
            if (okOutputs.Count == 3)
            {
                okOutputs[0].TryGetValue(out outDocument);
                okOutputs[1].TryGetValue(out outFormat);
                okOutputs[2].TryGetValue(out outContentType);
            }

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(okResult), Is.True);
                Assert.That(okOutputs, Has.Count.EqualTo(3));
                Assert.That(outDocument, Is.EqualTo(document));
                Assert.That(outFormat, Is.EqualTo(format));
                Assert.That(outContentType, Is.EqualTo(contentType));
            });

            // An unregistered SchemaId returns Bad_NotFound.
            var missOutputs = new List<Variant>();
            ServiceResult missResult = method.OnCallMethod2(
                server.DefaultSystemContext, method, objectId,
                [new Variant(ByteString.From([9, 9, 9, 9, 9, 9, 9, 9]))], missOutputs);

            Assert.That(missResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
        }

        /// <summary>
        /// Proves the Opaque SchemaId-NodeId fast path (spec §6.4): a schema document is
        /// addressable by an Opaque NodeId in the Schema Registry namespace whose Identifier is
        /// the raw on-wire SchemaId bytes. A consumer that received the SchemaId constructs the
        /// NodeId deterministically and resolves the schema document in a single Read of the
        /// Value Attribute — no Browse. An unknown SchemaId resolves to no node (cache miss).
        /// </summary>
        [Test]
        [Order(700)]
        public async Task OpaqueSchemaIdNodeIdResolvesSchemaDocumentInOneReadAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            // Deterministic construction from the raw on-wire SchemaId bytes (§6.4):
            // NamespaceIndex = Schema Registry namespace, IdentifierType = Opaque, Identifier = bytes.
            var fastPathNodeId = new NodeId(s_seedSchemaId, ns);
            Assert.That(fastPathNodeId.IdType, Is.EqualTo(IdType.Opaque),
                "The SchemaId fast-path NodeId is an Opaque NodeId.");

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(fastPathNodeId)
                .ConfigureAwait(false);

            Assert.That(node, Is.Not.Null,
                "The Opaque SchemaId NodeId should resolve to the schema node in one Read.");

            var variable = node as BaseVariableState;
            Assert.That(variable, Is.Not.Null, "The fast-path node is a ByteString Variable.");

            variable!.Value.TryGetValue(out ByteString resolved);

            Assert.Multiple(() =>
            {
                Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.ByteString));
                Assert.That(resolved, Is.EqualTo(s_seedDocument),
                    "One Read of the Value Attribute returns the schema document.");
            });

            // A cache miss: an unregistered SchemaId resolves to no fast-path node.
            var unknownNodeId = new NodeId(
                ByteString.From([0, 0, 0, 0, 0, 0, 0, 0]), ns);
            NodeState missing = await server.NodeManager
                .FindNodeInAddressSpaceAsync(unknownNodeId)
                .ConfigureAwait(false);

            Assert.That(missing, Is.Null,
                "An unregistered SchemaId has no fast-path node (the consumer falls back to browse/GetSchema).");
        }

        /// <summary>
        /// Proves auto-bootstrap SchemaId consistency (spec §10.1 + §6.6): the SchemaId a server
        /// computes from a document on registration — via the pluggable per-format fingerprint
        /// provider — is the exact identifier the Opaque fast-path NodeId (§6.4) is built from.
        /// This composes the fingerprint pipeline (PR #4007) with the in-server fast path
        /// (PR #4018): recomputing the SchemaId from the document with the same provider yields
        /// the fast-path node's Opaque identifier bytes, and the document is reachable by it.
        /// </summary>
        [Test]
        [Order(800)]
        public async Task RegisteredSchemaIsAddressableByItsProviderComputedSchemaIdAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            // Recompute the SchemaId + alg from the document exactly as a server does on
            // registration, through the pluggable fingerprint provider.
            byte[] computed;
            string alg;
            computed = SchemaIdProviders.ComputeSchemaId(
                "avro",
                s_seedDocument.Span);
            alg = SchemaIdProviders.AlgorithmFor("avro");

            Assert.Multiple(() =>
            {
                Assert.That(alg, Is.EqualTo("CRC-64-AVRO"),
                    "Avro schemas fingerprint with the CRC-64-AVRO algorithm.");
                Assert.That(alg, Is.EqualTo(s_seedSchemaIdAlg));
                Assert.That(ByteString.From(computed),
                    Is.EqualTo(s_seedSchemaId),
                    "The provider-computed SchemaId equals the fast-path Opaque NodeId identifier bytes.");
            });

            // The document is reachable by the Opaque NodeId derived from the auto-bootstrapped SchemaId.
            var nodeId = new NodeId(ByteString.From(computed), ns);
            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(nodeId)
                .ConfigureAwait(false);

            Assert.That(node, Is.Not.Null,
                "The schema is reachable by the Opaque NodeId derived from its auto-bootstrapped SchemaId.");
        }

        /// <summary>
        /// Proves the registration lifecycle (spec §5.2) with auto-bootstrap (§10.1): a writer
        /// creates a group under the registry root, calls the group's <c>CreateResource</c> to obtain
        /// a write handle, streams the document with two inherited <c>FileType.Write</c> chunks, and
        /// closes the resource. On close the server computes the SchemaId via the fingerprint
        /// provider and creates the Opaque fast-path node <b>at runtime</b>, after which the freshly
        /// registered document is downloadable by its Opaque SchemaId NodeId (§6.4) in one Read.
        /// </summary>
        [Test]
        [Order(900)]
        public async Task RegisterSchemaCreateWriteCloseBootstrapsDownloadableSchemaAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);
            byte[] document = System.Text.Encoding.UTF8.GetBytes(
                "{\"type\":\"record\",\"name\":\"Registered\",\"fields\":[]}");

            (ResourceState resource, ByteString registeredSchemaId) = await RegisterSchemaAsync(
                server, "registered-group", "urn:schema:registered", document, splitAt: 20)
                .ConfigureAwait(false);

            byte[] expected = SchemaIdProviders.ComputeSchemaId("avro", document);
            var fastPathNodeId = new NodeId(registeredSchemaId, ns);
            NodeState resolved = await server.NodeManager
                .FindNodeInAddressSpaceAsync(fastPathNodeId)
                .ConfigureAwait(false);
            var variable = resolved as BaseVariableState;
            variable!.Value.TryGetValue(out ByteString downloaded);

            Assert.Multiple(() =>
            {
                Assert.That(resource.Xid!.Value, Is.EqualTo(ByteString.From(expected).ToHexString()));
                Assert.That(resource.Format!.Value, Is.EqualTo("avro"));
                Assert.That(registeredSchemaId, Is.EqualTo(ByteString.From(expected)));
                Assert.That(resolved, Is.Not.Null,
                    "After Close the registered schema resolves by its Opaque SchemaId NodeId.");
                Assert.That(downloaded, Is.EqualTo(ByteString.From(document)),
                    "One Read returns the exact bytes written across the two Write chunks.");
            });
        }

        /// <summary>
        /// Proves symmetric deletion (spec §5.2): a registered schema is removed by the
        /// resource's <c>Delete(ExpectedEpoch)</c> method, the method rejects a stale epoch for
        /// optimistic concurrency, and after a successful delete its Opaque SchemaId NodeId no
        /// longer resolves.
        /// </summary>
        [Test]
        [Order(1000)]
        public async Task DeleteResourceRejectsStaleEpochAndRemovesRegisteredSchemaAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);
            byte[] document = System.Text.Encoding.UTF8.GetBytes(
                "{\"type\":\"record\",\"name\":\"Deletable\",\"fields\":[]}");

            (ResourceState resource, ByteString schemaId) = await RegisterSchemaAsync(
                server, "delete-group", "urn:schema:deletable", document).ConfigureAwait(false);
            var nodeId = new NodeId(schemaId, ns);
            Assert.That(await server.NodeManager.FindNodeInAddressSpaceAsync(nodeId).ConfigureAwait(false),
                Is.Not.Null, "The registered schema resolves before deletion.");

            DeleteMethodStateResult stale = await resource.Delete!.OnCallAsync!(
                server.DefaultSystemContext, resource.Delete, resource.NodeId, resource.Epoch!.Value + 1,
                CancellationToken.None).ConfigureAwait(false);
            DeleteMethodStateResult deleted = await resource.Delete.OnCallAsync(
                server.DefaultSystemContext, resource.Delete, resource.NodeId, resource.Epoch.Value,
                CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(stale.ServiceResult.StatusCode.Code, Is.EqualTo(StatusCodes.BadInvalidState));
                Assert.That(ServiceResult.IsGood(deleted.ServiceResult), Is.True,
                    "Delete succeeds for a registered schema when the epoch matches.");
            });
            Assert.That(await server.NodeManager.FindNodeInAddressSpaceAsync(nodeId).ConfigureAwait(false),
                Is.Null, "After Delete the Opaque SchemaId NodeId no longer resolves.");
        }

        /// <summary>
        /// Proves the federation model (spec Annex B / §4.3): a schema hosted by another registry
        /// is represented by a local proxy carrying an <c>ExternalReference</c> (an
        /// <see cref="ExpandedNodeId"/> naming the remote server via <c>ServerIndex</c>, plus the
        /// remote <c>NamespaceUri</c> + content-addressed <c>Identifier</c>) and a
        /// <c>ResourceUrl</c>. Because <c>SchemaId</c> is content-derived it is stable across
        /// registries: the proxy's SchemaId equals the fingerprint a consumer computes for the
        /// same document, so the federated schema de-duplicates to one identity.
        /// </summary>
        [Test]
        [Order(1100)]
        public async Task FederatedProxyCarriesExternalReferenceAndDedupsBySchemaIdAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(XRegistryWellKnown.FederationProxyObject, ns))
                .ConfigureAwait(false);
            var proxy = node as ResourceState;
            Assert.That(proxy, Is.Not.Null, "The federation proxy should be a ResourceType instance.");

            ExpandedNodeId externalReference = proxy!.ExternalReference!.Value;
            string resourceUrl = proxy.ResourceUrl!.Value;
            ByteString proxySchemaId = ByteString.FromHexString(proxy.Xid!.Value);
            // De-dup by SchemaId: the proxy's identity is the content fingerprint a consumer
            // would compute for the same document (§4.3, Annex B step 4).
            byte[] expected;
            expected = SchemaIdProviders.ComputeSchemaId(
                "avro", SchemaRegistryOptions.FederatedSchemaDocument.Span);

            Assert.Multiple(() =>
            {
                // The federation link names the remote OPC UA registry and the remote schema node.
                Assert.That(externalReference.ServerIndex,
                    Is.EqualTo(SchemaRegistryOptions.RemoteServerIndex),
                    "ExternalReference.ServerIndex names the remote server via the ServerArray.");
                Assert.That(externalReference.NamespaceUri,
                    Is.EqualTo(SchemaRegistryTestServer.SchemaRegistryNamespaceUri),
                    "ExternalReference.NamespaceUri is the remote registry namespace.");
                Assert.That(resourceUrl,
                    Is.EqualTo(SchemaRegistryOptions.RemoteEndpointUrl),
                    "ResourceUrl carries the remote endpoint in string form.");

                Assert.That(proxySchemaId, Is.EqualTo(ByteString.From(expected)),
                    "The proxy's SchemaId is the content fingerprint — stable across registries.");
                // Cross-registry identity: the remote node is content-addressed by the same SchemaId.
                Assert.That(externalReference.InnerNodeId, Is.EqualTo(new NodeId(proxySchemaId)),
                    "The ExternalReference targets the remote node keyed by the same SchemaId.");
            });
        }

        /// <summary>
        /// Returns the server-side namespace index for the Schema Registry companion model.
        /// </summary>
        private static ushort SchemaRegistryNamespaceIndex(IServerInternal server)
        {
            return (ushort)server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);
        }

        /// <summary>
        /// Registers a schema document through the new registry root → group → resource model.
        /// </summary>
        private static async Task<(ResourceState Resource, ByteString SchemaId)> RegisterSchemaAsync(
            IServerInternal server,
            string groupId,
            string resourceId,
            byte[] document,
            int splitAt = 0)
        {
            GroupState group = await GetOrCreateSchemaGroupAsync(server, groupId).ConfigureAwait(false);
            ISystemContext ctx = server.DefaultSystemContext;

            CreateResourceMethodStateResult created = await group.CreateResource!.OnCallAsync!(
                ctx, group.CreateResource, group.NodeId, resourceId, string.Empty, true, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(created.ServiceResult), Is.True);
                Assert.That(created.ResourceNodeId.IsNull, Is.False);
                Assert.That(created.AssignedVersionId, Is.Not.Empty);
                Assert.That(created.FileHandle, Is.Not.Zero);
            });

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(created.ResourceNodeId)
                .ConfigureAwait(false);
            var resource = node as ResourceState;
            Assert.That(resource, Is.Not.Null, "CreateResource should materialize a ResourceType node.");

            if (splitAt > 0 && splitAt < document.Length)
            {
                await WriteChunkAsync(server, resource!, created.FileHandle, document.AsSpan(0, splitAt).ToArray())
                    .ConfigureAwait(false);
                await WriteChunkAsync(server, resource!, created.FileHandle, document.AsSpan(splitAt).ToArray())
                    .ConfigureAwait(false);
            }
            else
            {
                await WriteChunkAsync(server, resource!, created.FileHandle, document).ConfigureAwait(false);
            }

            CloseMethodStateResult closed = await resource!.Close!.OnCallAsync!(
                ctx, resource.Close, resource.NodeId, created.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);

            ByteString schemaId = SchemaContentIdProvider.Instance.ComputeContentId("avro", document);
            return (resource, schemaId);
        }

        /// <summary>
        /// Gets or creates a SchemaGroup below the registry root.
        /// </summary>
        private static async Task<GroupState> GetOrCreateSchemaGroupAsync(
            IServerInternal server,
            string groupId)
        {
            RegistryState registry = await FindRegistryAsync(server).ConfigureAwait(false);
            GetOrCreateGroupMethodStateResult result = await registry.GetOrCreateGroup!.OnCallAsync!(
                server.DefaultSystemContext, registry.GetOrCreateGroup, registry.NodeId, groupId,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(result.ServiceResult), Is.True);

            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(result.GroupNodeId)
                .ConfigureAwait(false);
            var group = node as GroupState;
            Assert.That(group, Is.Not.Null, "GetOrCreateGroup should materialize a GroupType node.");
            return group!;
        }

        /// <summary>
        /// Resolves the registry root materialized by the xRegistry registration node manager.
        /// </summary>
        private static async Task<RegistryState> FindRegistryAsync(IServerInternal server)
        {
            ushort ns = SchemaRegistryNamespaceIndex(server);
            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(XRegistryWellKnown.RegistryObject, ns))
                .ConfigureAwait(false);
            var registry = node as RegistryState;
            Assert.That(registry, Is.Not.Null, "The registry root should be a RegistryType instance.");
            return registry!;
        }

        /// <summary>
        /// Writes one document chunk through the resource's inherited <c>FileType.Write</c> method.
        /// </summary>
        private static async Task WriteChunkAsync(
            IServerInternal server,
            ResourceState resource,
            uint fileHandle,
            byte[] chunk)
        {
            WriteMethodStateResult written = await resource.Write!.OnCallAsync!(
                server.DefaultSystemContext, resource.Write, resource.NodeId, fileHandle,
                ByteString.From(chunk), CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(written.ServiceResult), Is.True);
        }
    }
}
