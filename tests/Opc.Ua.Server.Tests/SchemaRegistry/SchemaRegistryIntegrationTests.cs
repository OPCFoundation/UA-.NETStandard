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
        /// Returns the server-side namespace index for the Schema Registry companion model.
        /// </summary>
        private static ushort SchemaRegistryNamespaceIndex(IServerInternal server)
        {
            return (ushort)server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);
        }
    }
}
