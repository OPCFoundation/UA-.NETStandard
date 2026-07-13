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

using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Integration tests for the server side complex type system. A server
    /// hosts custom DataTypes that were loaded from a NodeSet at runtime and
    /// have no compiled .NET backing; the server builds dynamic stand-in
    /// encodeables for them. A real client then reads and writes values of
    /// those types over the wire, exercising the full server encode / decode
    /// round-trip (issue #3961).
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [Category("ComplexTypes")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class ServerComplexTypeSystemIntegrationTests
    {
        private ITelemetryContext m_telemetry;
        private ServerFixture<ServerComplexTypesTestServer> m_serverFixture;
        private ClientFixture m_clientFixture;
        private ServerComplexTypesTestServer m_server;
        private Client.ISession m_session;
        private string m_pkiRoot;

        /// <summary>
        /// Starts a server that hosts the runtime-loaded complex types, connects
        /// a client and loads the client complex type system.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_pkiRoot = Path.GetTempPath() + Path.GetRandomFileName();

            m_serverFixture = new ServerFixture<ServerComplexTypesTestServer>(
                t => new ServerComplexTypesTestServer(t))
            {
                UriScheme = Utils.UriSchemeOpcTcp,
                SecurityNone = true,
                AutoAccept = true,
                OperationLimits = true
            };

            m_server = await m_serverFixture.StartAsync(m_pkiRoot).ConfigureAwait(false);

            m_clientFixture = new ClientFixture(m_telemetry);
            await m_clientFixture.LoadClientConfigurationAsync(m_pkiRoot).ConfigureAwait(false);

            var url = new Uri(
                Utils.UriSchemeOpcTcp +
                "://localhost:" +
                m_serverFixture.Port.ToString(CultureInfo.InvariantCulture));

            try
            {
                m_session = await m_clientFixture
                    .ConnectAsync(url, SecurityPolicies.Basic256Sha256)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Assert.Ignore(
                    $"OneTimeSetup failed to create session, tests skipped. Error: {e.Message}");
            }

            // Load the client complex type system so the runtime types are
            // registered in the session factory and read values decode correctly.
            var clientTypeSystem = ComplexTypeSystem.Create(m_session, m_telemetry);
            clientTypeSystem.DisableDataTypeDictionary = true;
            bool loaded = await clientTypeSystem.LoadAsync(false, true).ConfigureAwait(false);
            Assert.That(loaded, Is.True, "the client complex type system should load");
        }

        /// <summary>
        /// Closes the session and stops the server.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync().ConfigureAwait(false);
                m_session.Dispose();
                m_session = null;
            }

            m_clientFixture?.Dispose();
            m_server?.Dispose();

            if (m_serverFixture != null)
            {
                await m_serverFixture.StopAsync().ConfigureAwait(false);
            }

            try
            {
                if (!string.IsNullOrEmpty(m_pkiRoot) && Directory.Exists(m_pkiRoot))
                {
                    Directory.Delete(m_pkiRoot, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }

        /// <summary>
        /// The server must build and register stand-in encodeables for the
        /// runtime-loaded structure and enumeration DataTypes.
        /// </summary>
        [Test]
        [Order(100)]
        public void ServerRegistersRuntimeStandInTypes()
        {
            IServerInternal server = m_server.CurrentInstance;
            ushort namespaceIndex = ServerNamespaceIndex(server);

            var structureTypeId = NodeId.ToExpandedNodeId(
                new NodeId(ServerComplexTypesTestNodeManager.TestPointDataType, namespaceIndex),
                server.NamespaceUris);
            var binaryEncodingId = NodeId.ToExpandedNodeId(
                new NodeId(ServerComplexTypesTestNodeManager.TestPointBinaryEncoding, namespaceIndex),
                server.NamespaceUris);
            var enumTypeId = NodeId.ToExpandedNodeId(
                new NodeId(ServerComplexTypesTestNodeManager.TestColorDataType, namespaceIndex),
                server.NamespaceUris);

            Assert.Multiple(() =>
            {
                Assert.That(
                    server.Factory.TryGetEncodeableType(structureTypeId, out IEncodeableType structType),
                    Is.True,
                    "the runtime TestPoint structure should be registered in the server factory");
                Assert.That(structType, Is.Not.Null);

                Assert.That(
                    server.Factory.TryGetEncodeableType(binaryEncodingId, out _),
                    Is.True,
                    "the TestPoint binary encoding id should resolve to the structure stand-in");

                Assert.That(
                    server.Factory.TryGetEnumeratedType(enumTypeId, out IEnumeratedType enumType),
                    Is.True,
                    "the runtime TestColor enumeration should be registered in the server factory");
                Assert.That(enumType, Is.Not.Null);
            });
        }

        /// <summary>
        /// A client reads the runtime structure variable; the server encodes it
        /// via its stand-in and the client decodes it into a structured value.
        /// </summary>
        [Test]
        [Order(200)]
        public async Task ClientReadsRuntimeStructureValue()
        {
            NodeId nodeId = ClientNodeId(ServerComplexTypesTestNodeManager.PointValueVariable);

            DataValue dataValue = await m_session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(dataValue.IsNull, Is.False);
            Assert.That(StatusCode.IsGood(dataValue.StatusCode), Is.True);

            IStructure point = ReadStructure(dataValue);
            Assert.Multiple(() =>
            {
                Assert.That(point["X"].TryGetValue(out int x), Is.True);
                Assert.That(x, Is.EqualTo(3));
                Assert.That(point["Y"].TryGetValue(out int y), Is.True);
                Assert.That(y, Is.EqualTo(4));
                Assert.That(point["Name"].TryGetValue(out string name), Is.True);
                Assert.That(name, Is.EqualTo("origin"));
            });
        }

        /// <summary>
        /// A client reads the runtime enumeration variable and gets the raw
        /// enumeration value.
        /// </summary>
        [Test]
        [Order(300)]
        public async Task ClientReadsRuntimeEnumValue()
        {
            NodeId nodeId = ClientNodeId(ServerComplexTypesTestNodeManager.ColorValueVariable);

            DataValue dataValue = await m_session.ReadValueAsync(nodeId).ConfigureAwait(false);
            Assert.That(dataValue.IsNull, Is.False);
            Assert.That(StatusCode.IsGood(dataValue.StatusCode), Is.True);

            // TestColor.Green == 1
            Assert.That(dataValue.WrappedValue.TryGetValue(out int color), Is.True);
            Assert.That(color, Is.EqualTo(1));
        }

        /// <summary>
        /// A client writes a modified runtime structure value; the server
        /// decodes it via its stand-in, stores it, and returns the updated value
        /// on the next read.
        /// </summary>
        [Test]
        [Order(400)]
        public async Task ClientWritesRuntimeStructureValue()
        {
            NodeId nodeId = ClientNodeId(ServerComplexTypesTestNodeManager.PointValueVariable);

            DataValue dataValue = await m_session.ReadValueAsync(nodeId).ConfigureAwait(false);
            IStructure point = ReadStructure(dataValue);

            point["X"] = new Variant(7);
            point["Y"] = new Variant(8);
            point["Name"] = new Variant("updated");

            ArrayOf<WriteValue> writeValues =
            [
                new WriteValue
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    Value = new DataValue(dataValue.WrappedValue)
                }
            ];

            WriteResponse response = await m_session
                .WriteAsync(null, writeValues, default)
                .ConfigureAwait(false);
            Assert.That(response.Results.IsNull, Is.False);
            Assert.That(StatusCode.IsGood(response.Results[0]), Is.True,
                "the server should decode and accept the runtime structure value");

            DataValue readBack = await m_session.ReadValueAsync(nodeId).ConfigureAwait(false);
            IStructure updated = ReadStructure(readBack);
            Assert.Multiple(() =>
            {
                Assert.That(updated["X"].TryGetValue(out int x), Is.True);
                Assert.That(x, Is.EqualTo(7));
                Assert.That(updated["Y"].TryGetValue(out int y), Is.True);
                Assert.That(y, Is.EqualTo(8));
                Assert.That(updated["Name"].TryGetValue(out string name), Is.True);
                Assert.That(name, Is.EqualTo("updated"));
            });
        }

        /// <summary>
        /// Extracts the decoded structured value from a read <see cref="DataValue"/>.
        /// </summary>
        private static IStructure ReadStructure(DataValue dataValue)
        {
            Assert.That(dataValue.WrappedValue.TryGetValue(out ExtensionObject extensionObject), Is.True);
            Assert.That(extensionObject.TryGetValue(out IEncodeable encodeable), Is.True);
            var structure = encodeable as IStructure;
            Assert.That(structure, Is.Not.Null, "the decoded value should be a complex structure");
            return structure;
        }

        /// <summary>
        /// Resolves a node id in the test namespace on the client side.
        /// </summary>
        private NodeId ClientNodeId(uint identifier)
        {
            return new NodeId(
                identifier,
                (ushort)m_session.NamespaceUris.GetIndex(
                    ServerComplexTypesTestNodeManager.NamespaceUri));
        }

        /// <summary>
        /// Resolves the test namespace index on the server side.
        /// </summary>
        private static ushort ServerNamespaceIndex(IServerInternal server)
        {
            return (ushort)server.NamespaceUris.GetIndex(
                ServerComplexTypesTestNodeManager.NamespaceUri);
        }
    }
}
