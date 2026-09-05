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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.PubSub.SchemaRegistry;
using Opc.Ua.PubSub.Server.SchemaRegistry;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry;


namespace Opc.Ua.PubSub.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// Client↔server round-trip tests for the in-server Schema Registry feature. A real client
    /// connects to a <see cref="SchemaRegistryTestServer"/> (which enables the optional
    /// Schema Registry feature) and uses the <see cref="SchemaRegistryClient"/> from
    /// <c>Opc.Ua.PubSub</c> to resolve a schema document from its content-derived on-wire
    /// <c>SchemaId</c> through the Opaque SchemaId-NodeId fast path (§6.4) in a single Read — the
    /// core capability a decoder needs when it receives a SchemaId on the wire.
    /// </summary>
    [TestFixture]
    [Category("SchemaRegistry")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class SchemaRegistryClientIntegrationTests
    {
        // The seed schema published by the fast-path manager, and its content-derived SchemaId.
        private static readonly ByteString s_seedDocument = SchemaRegistryOptions.SeedSchemaDocument;
        private static readonly ByteString s_seedSchemaId =
            SchemaContentIdProvider.Instance.ComputeContentId(
                "avro", SchemaRegistryOptions.SeedSchemaDocument.Span);

        private ITelemetryContext m_telemetry;
        private ServerFixture<SchemaRegistryTestServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private SchemaRegistryTestServer m_server;
        private Client.ISession m_session;
        private string m_pkiRoot;

        /// <summary>
        /// Starts the Schema Registry server and connects a client session.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                nameof(SchemaRegistryClientIntegrationTests),
                Guid.NewGuid().ToString("N"));

            m_serverFixture = new ServerFixture<SchemaRegistryTestServer>(
                t => new SchemaRegistryTestServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true
            };

            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture(m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            var url = new Uri(
                Utils.UriSchemeOpcTcp + "://localhost:" +
                m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));

            try
            {
                m_session = await m_clientFixture
                    .ConnectAsync(url, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Assert.Ignore($"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}");
            }
        }

        /// <summary>
        /// Disconnects the session, stops the server and cleans up PKI artefacts.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync().ConfigureAwait(false);
                await m_session.DisposeAsync().ConfigureAwait(false);
            }

            m_clientFixture?.Dispose();
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
        /// The client resolves the schema registry namespace exposed by the server.
        /// </summary>
        [Test]
        [Order(100)]
        public void ClientResolvesSchemaRegistryNamespace()
        {
            var client = new SchemaRegistryClient(m_session, m_telemetry);
            Assert.That(
                client.NamespaceIndex,
                Is.EqualTo(m_session.NamespaceUris.GetIndex(
                    SchemaRegistryTestServer.SchemaRegistryNamespaceUri)));
        }

        /// <summary>
        /// The startup-seeded schema resolves over the wire in a single Read of its Opaque
        /// SchemaId-NodeId (§6.4): the client builds the NodeId from the raw SchemaId bytes and gets
        /// back the exact schema document.
        /// </summary>
        [Test]
        [Order(200)]
        public async Task ClientResolvesSeedSchemaByOpaqueSchemaIdAsync()
        {
            var client = new SchemaRegistryClient(m_session, m_telemetry);

            ByteString resolved = await client
                .ResolveSchemaAsync(s_seedSchemaId)
                .ConfigureAwait(false);

            Assert.That(resolved, Is.EqualTo(s_seedDocument),
                "ResolveSchema returns the seed document for its content-derived SchemaId.");
        }

        /// <summary>
        /// An unregistered SchemaId has no fast-path node, so <see cref="SchemaRegistryClient"/>
        /// reports a null document (the consumer then falls back to Browse/GetSchema).
        /// </summary>
        [Test]
        [Order(300)]
        public async Task ClientReportsNullForUnregisteredSchemaIdAsync()
        {
            var client = new SchemaRegistryClient(m_session, m_telemetry);
            var unknown = ByteString.From(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x11, 0x22, 0x33 });

            ByteString resolved = await client.ResolveSchemaAsync(unknown).ConfigureAwait(false);

            Assert.That(resolved.IsNull, Is.True,
                "An unregistered SchemaId resolves to a null document.");
        }

        /// <summary>
        /// End-to-end round-trip: a schema is registered on the server through the new registry
        /// root → group → resource client model, and the client then resolves that freshly
        /// registered document over the wire by the server-computed SchemaId.
        /// </summary>
        [Test]
        [Order(400)]
        public async Task ClientResolvesRuntimeRegisteredSchemaAsync()
        {
            byte[] document = System.Text.Encoding.UTF8.GetBytes(
                "{\"type\":\"record\",\"name\":\"ClientResolved\",\"fields\":[]}");
            (NodeId resourceNodeId, string versionId, ByteString registeredSchemaId) =
                await RegisterSchemaOnServerAsync(
                    m_server.CurrentInstance,
                    "client-runtime",
                    "urn:schema:client-resolved",
                    document).ConfigureAwait(false);

            var client = new SchemaRegistryClient(m_session, m_telemetry);
            ByteString resolved = await client
                .ResolveSchemaAsync(registeredSchemaId)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(resourceNodeId.IsNull, Is.False);
                Assert.That(versionId, Is.Not.Empty);
                Assert.That(resolved, Is.EqualTo(ByteString.From(document)),
                    "The client resolves the runtime-registered schema by its content-derived SchemaId.");
            });
        }

        private static async Task<(NodeId ResourceNodeId, string VersionId, ByteString SchemaId)>
            RegisterSchemaOnServerAsync(
                IServerInternal server,
                string groupId,
                string resourceId,
                byte[] document)
        {
            ISystemContext ctx = server.DefaultSystemContext;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);
            NodeState registryNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(new NodeId(XRegistryWellKnown.RegistryObject, ns))
                .ConfigureAwait(false);
            var registry = registryNode as RegistryState;
            Assert.That(registry, Is.Not.Null);

            GetOrCreateGroupMethodStateResult groupResult = await registry!.GetOrCreateGroup!.OnCallAsync!(
                ctx, registry.GetOrCreateGroup, registry.NodeId, groupId, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(groupResult.ServiceResult), Is.True);

            NodeState groupNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(groupResult.GroupNodeId)
                .ConfigureAwait(false);
            var group = groupNode as GroupState;
            Assert.That(group, Is.Not.Null);

            CreateResourceMethodStateResult created = await group!.CreateResource!.OnCallAsync!(
                ctx, group.CreateResource, group.NodeId, resourceId, string.Empty, true,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ServiceResult.IsGood(created.ServiceResult), Is.True);

            NodeState resourceNode = await server.NodeManager
                .FindNodeInAddressSpaceAsync(created.ResourceNodeId)
                .ConfigureAwait(false);
            var resource = resourceNode as ResourceState;
            Assert.That(resource, Is.Not.Null);

            WriteMethodStateResult written = await resource!.Write!.OnCallAsync!(
                ctx, resource.Write, resource.NodeId, created.FileHandle, ByteString.From(document),
                CancellationToken.None).ConfigureAwait(false);
            CloseMethodStateResult closed = await resource.Close!.OnCallAsync!(
                ctx, resource.Close, resource.NodeId, created.FileHandle, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(written.ServiceResult), Is.True);
                Assert.That(ServiceResult.IsGood(closed.ServiceResult), Is.True);
            });

            ByteString schemaId = SchemaContentIdProvider.Instance.ComputeContentId("avro", document);
            return (created.ResourceNodeId, created.AssignedVersionId, schemaId);
        }
    }
}
