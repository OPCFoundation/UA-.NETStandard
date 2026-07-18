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
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// Address-space integration tests that prove the experimental in-server Schema Registry
    /// AddressSpace model materializes in a real <see cref="ReferenceServer"/> when the
    /// generated xRegistry base and Schema Registry companion NodeSets are loaded through the
    /// runtime NodeSet import path. This proves out the OPC UA — Schema Registry spec's
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
            var fastPathNodeId = new NodeId(SchemaRegistryFastPathNodeManager.KnownSchemaId, ns);
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
                Assert.That(resolved, Is.EqualTo(SchemaRegistryFastPathNodeManager.KnownDocument),
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
#pragma warning disable UA_NETStandard_Encoders // pluggable per-format fingerprint provider (§6.6)
            computed = SchemaIdProviders.ComputeSchemaId(
                SchemaRegistryFastPathNodeManager.KnownFormat,
                SchemaRegistryFastPathNodeManager.KnownDocument.Span);
            alg = SchemaIdProviders.AlgorithmFor(SchemaRegistryFastPathNodeManager.KnownFormat);
#pragma warning restore UA_NETStandard_Encoders

            Assert.Multiple(() =>
            {
                Assert.That(alg, Is.EqualTo("CRC-64-AVRO"),
                    "Avro schemas fingerprint with the CRC-64-AVRO algorithm.");
                Assert.That(alg, Is.EqualTo(SchemaRegistryFastPathNodeManager.KnownSchemaIdAlg));
                Assert.That(ByteString.From(computed),
                    Is.EqualTo(SchemaRegistryFastPathNodeManager.KnownSchemaId),
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
        /// calls <c>CreateResource</c> to obtain a write handle, streams the document with two
        /// <c>Write</c> chunks, and <c>Close</c>s it. On close the server computes the SchemaId
        /// via the fingerprint provider and creates the Opaque fast-path node <b>at runtime</b>,
        /// after which the freshly registered document is downloadable by its Opaque SchemaId
        /// NodeId (§6.4) in one Read — closing the register → resolve round-trip.
        /// </summary>
        [Test]
        [Order(900)]
        public async Task RegisterSchemaCreateWriteCloseBootstrapsDownloadableSchemaAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);

            MethodState createResource = await FindMethodAsync(
                server, SchemaRegistryRegistrationNodeManager.CreateResourceMethod, ns)
                .ConfigureAwait(false);
            MethodState write = await FindMethodAsync(
                server, SchemaRegistryRegistrationNodeManager.WriteMethod, ns)
                .ConfigureAwait(false);
            MethodState close = await FindMethodAsync(
                server, SchemaRegistryRegistrationNodeManager.CloseMethod, ns)
                .ConfigureAwait(false);

            var groupId = new NodeId(SchemaRegistryRegistrationNodeManager.SchemaGroupObject, ns);
            ISystemContext ctx = server.DefaultSystemContext;

            // A distinct document, registered fresh (not the startup-seeded fast-path schema).
            byte[] document = System.Text.Encoding.UTF8.GetBytes(
                "{\"type\":\"record\",\"name\":\"Registered\",\"fields\":[]}");
            byte[] chunk1 = document[..20];
            byte[] chunk2 = document[20..];

            // 1) CreateResource -> (fileHandle, versionId).
            var createOutputs = new List<Variant>();
            ServiceResult createResult = createResource.OnCallMethod2(
                ctx, createResource, groupId,
                [new Variant("urn:schema:registered"), new Variant(string.Empty)], createOutputs);
            Assert.That(ServiceResult.IsGood(createResult), Is.True);
            Assert.That(createOutputs, Has.Count.EqualTo(2));
            createOutputs[0].TryGetValue(out uint handle);
            createOutputs[1].TryGetValue(out string versionId);
            Assert.That(versionId, Is.Not.Empty, "CreateResource assigns a VersionId.");

            // 2) Write the document in two chunks.
            var writeOutputs = new List<Variant>();
            Assert.That(ServiceResult.IsGood(write.OnCallMethod2(
                ctx, write, groupId, [new Variant(handle), new Variant(ByteString.From(chunk1))], writeOutputs)),
                Is.True);
            Assert.That(ServiceResult.IsGood(write.OnCallMethod2(
                ctx, write, groupId, [new Variant(handle), new Variant(ByteString.From(chunk2))], writeOutputs)),
                Is.True);

            // 3) Close -> auto-bootstrap (SchemaId + alg) and dynamic fast-path node creation.
            var closeOutputs = new List<Variant>();
            ServiceResult closeResult = close.OnCallMethod2(
                ctx, close, groupId, [new Variant(handle), new Variant("avro")], closeOutputs);
            Assert.That(ServiceResult.IsGood(closeResult), Is.True);
            Assert.That(closeOutputs, Has.Count.EqualTo(2));
            closeOutputs[0].TryGetValue(out ByteString registeredSchemaId);
            closeOutputs[1].TryGetValue(out string registeredAlg);

            // The returned SchemaId matches the provider's fingerprint of the full document.
            byte[] expected;
#pragma warning disable UA_NETStandard_Encoders // pluggable per-format fingerprint provider (§6.6)
            expected = SchemaIdProviders.ComputeSchemaId("avro", document);
#pragma warning restore UA_NETStandard_Encoders
            Assert.Multiple(() =>
            {
                Assert.That(registeredAlg, Is.EqualTo("CRC-64-AVRO"));
                Assert.That(registeredSchemaId, Is.EqualTo(ByteString.From(expected)));
            });

            // 4) The freshly registered document is downloadable by its Opaque SchemaId NodeId.
            var fastPathNodeId = new NodeId(registeredSchemaId, ns);
            NodeState resolved = await server.NodeManager
                .FindNodeInAddressSpaceAsync(fastPathNodeId)
                .ConfigureAwait(false);
            Assert.That(resolved, Is.Not.Null,
                "After Close the registered schema resolves by its Opaque SchemaId NodeId.");

            var variable = resolved as BaseVariableState;
            variable!.Value.TryGetValue(out ByteString downloaded);
            Assert.That(downloaded, Is.EqualTo(ByteString.From(document)),
                "One Read returns the exact bytes written across the two Write chunks.");
        }

        /// <summary>
        /// Proves symmetric deletion (spec §5.2): a registered schema is removed by the
        /// <c>Delete</c> method (the symmetric counterpart of registration, replacing a generic
        /// DeleteNodes), after which its Opaque SchemaId NodeId no longer resolves. Delete reports
        /// success/failure through the Call <see cref="ServiceResult"/> (void), not a bool:
        /// <c>Good</c> when removed, <c>Bad_NotFound</c> on a second delete.
        /// </summary>
        [Test]
        [Order(1000)]
        public async Task DeleteRemovesRegisteredSchemaAndReportsNotFoundOnSecondDeleteAsync()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort ns = SchemaRegistryNamespaceIndex(server);
            ISystemContext ctx = server.DefaultSystemContext;
            var groupId = new NodeId(SchemaRegistryRegistrationNodeManager.SchemaGroupObject, ns);

            MethodState createResource = await FindMethodAsync(
                server, SchemaRegistryRegistrationNodeManager.CreateResourceMethod, ns).ConfigureAwait(false);
            MethodState write = await FindMethodAsync(
                server, SchemaRegistryRegistrationNodeManager.WriteMethod, ns).ConfigureAwait(false);
            MethodState close = await FindMethodAsync(
                server, SchemaRegistryRegistrationNodeManager.CloseMethod, ns).ConfigureAwait(false);
            MethodState delete = await FindMethodAsync(
                server, SchemaRegistryRegistrationNodeManager.DeleteMethod, ns).ConfigureAwait(false);

            // Register a fresh schema.
            byte[] document = System.Text.Encoding.UTF8.GetBytes(
                "{\"type\":\"record\",\"name\":\"Deletable\",\"fields\":[]}");

            var createOutputs = new List<Variant>();
            createResource.OnCallMethod2(ctx, createResource, groupId,
                [new Variant("urn:schema:deletable"), new Variant(string.Empty)], createOutputs);
            createOutputs[0].TryGetValue(out uint handle);
            write.OnCallMethod2(ctx, write, groupId,
                [new Variant(handle), new Variant(ByteString.From(document))], new List<Variant>());
            var closeOutputs = new List<Variant>();
            close.OnCallMethod2(ctx, close, groupId,
                [new Variant(handle), new Variant("avro")], closeOutputs);
            closeOutputs[0].TryGetValue(out ByteString schemaId);

            var nodeId = new NodeId(schemaId, ns);
            Assert.That(await server.NodeManager.FindNodeInAddressSpaceAsync(nodeId).ConfigureAwait(false),
                Is.Not.Null, "The registered schema resolves before deletion.");

            // Delete removes the fast-path node; success is reported via the ServiceResult.
            ServiceResult deleteResult = delete.OnCallMethod2(
                ctx, delete, groupId, [new Variant(schemaId)], new List<Variant>());
            Assert.That(ServiceResult.IsGood(deleteResult), Is.True, "Delete succeeds for a registered schema.");

            Assert.That(await server.NodeManager.FindNodeInAddressSpaceAsync(nodeId).ConfigureAwait(false),
                Is.Null, "After Delete the Opaque SchemaId NodeId no longer resolves.");

            // A second delete reports Bad_NotFound via the Call StatusCode (no bool return).
            ServiceResult secondDelete = delete.OnCallMethod2(
                ctx, delete, groupId, [new Variant(schemaId)], new List<Variant>());
            Assert.That(secondDelete.StatusCode.Code, Is.EqualTo(StatusCodes.BadNotFound));
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

            Variant externalReferenceValue = await ReadVariantAsync(
                server, SchemaRegistryFederationNodeManager.ExternalReferenceProperty, ns)
                .ConfigureAwait(false);
            Variant resourceUrlValue = await ReadVariantAsync(
                server, SchemaRegistryFederationNodeManager.ResourceUrlProperty, ns)
                .ConfigureAwait(false);
            Variant schemaIdValue = await ReadVariantAsync(
                server, SchemaRegistryFederationNodeManager.SchemaIdProperty, ns)
                .ConfigureAwait(false);

            externalReferenceValue.TryGetValue(out ExpandedNodeId externalReference);
            resourceUrlValue.TryGetValue(out string resourceUrl);
            schemaIdValue.TryGetValue(out ByteString proxySchemaId);

            // De-dup by SchemaId: the proxy's identity is the content fingerprint a consumer
            // would compute for the same document (§4.3, Annex B step 4).
            byte[] expected;
#pragma warning disable UA_NETStandard_Encoders // pluggable per-format fingerprint provider (§6.6)
            expected = SchemaIdProviders.ComputeSchemaId(
                "avro", SchemaRegistryFederationNodeManager.FederatedDocument);
#pragma warning restore UA_NETStandard_Encoders

            Assert.Multiple(() =>
            {
                // The federation link names the remote OPC UA registry and the remote schema node.
                Assert.That(externalReference.ServerIndex,
                    Is.EqualTo(SchemaRegistryFederationNodeManager.RemoteServerIndex),
                    "ExternalReference.ServerIndex names the remote server via the ServerArray.");
                Assert.That(externalReference.NamespaceUri,
                    Is.EqualTo(SchemaRegistryFederationNodeManager.RemoteRegistryNamespaceUri),
                    "ExternalReference.NamespaceUri is the remote registry namespace.");
                Assert.That(resourceUrl,
                    Is.EqualTo(SchemaRegistryFederationNodeManager.RemoteEndpointUrl),
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
        /// Resolves a registration <see cref="MethodState"/> by its provisional NodeId.
        /// </summary>
        private static async Task<MethodState> FindMethodAsync(
            IServerInternal server, uint id, ushort ns)
        {
            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(id, ns))
                .ConfigureAwait(false);

            var method = node as MethodState;
            Assert.That(method, Is.Not.Null, $"Registration method {id} should be a MethodState.");
            return method!;
        }

        /// <summary>
        /// Reads the Value of a Variable node identified by its provisional NodeId.
        /// </summary>
        private static async Task<Variant> ReadVariantAsync(
            IServerInternal server, uint id, ushort ns)
        {
            NodeState node = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(id, ns))
                .ConfigureAwait(false);

            var variable = node as BaseVariableState;
            Assert.That(variable, Is.Not.Null, $"Node {id} should be a Variable.");
            return variable!.Value;
        }
    }
}
