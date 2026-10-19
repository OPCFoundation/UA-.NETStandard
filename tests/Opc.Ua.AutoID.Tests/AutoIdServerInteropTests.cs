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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Server;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.Tests;
using SessionFixture = Opc.Ua.Client.TestFramework.ClientFixture;
using UaSession = Opc.Ua.Client.ISession;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// Serves the AutoID model from a stock server over opc.tcp and reads it back with a client
    /// session, so the generated nodes are checked on the wire rather than in process.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    [NonParallelizable]
    public sealed class AutoIdServerInteropTests
    {
        /// <summary>
        /// Starts the server with the model-only node manager and connects one client session.
        /// </summary>
        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_pkiRoot = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "autoid-pki",
                Guid.NewGuid().ToString("N")[..8]);
            m_server = new ServerFixture<StandardServer>(telemetry =>
            {
                var server = new StandardServer(telemetry);
                server.AddNodeManager(new ModelOnlyNodeManagerFactory());
                return server;
            })
            {
                AutoAccept = true,
                SecurityNone = true
            };
            await m_server.LoadConfigurationAsync(Path.Combine(m_pkiRoot, "server")).ConfigureAwait(false);
            await m_server.StartAsync().ConfigureAwait(false);

            m_sessionFixture = new SessionFixture(NUnitTelemetryContext.Create());
            await m_sessionFixture.LoadClientConfigurationAsync(Path.Combine(m_pkiRoot, "client"), "AutoIdClient")
                .ConfigureAwait(false);
            m_session = await m_sessionFixture.ConnectAsync(
                new Uri(m_server.Config.ServerConfiguration!.BaseAddresses[0]),
                SecurityPolicies.None).ConfigureAwait(false);
            m_session.MessageContext.Factory.Builder.AddAutoIdTypeSystem().Commit();
        }

        /// <summary>
        /// Closes the session, stops the server and removes the test PKI.
        /// </summary>
        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_session != null)
            {
                await m_session.CloseAsync(1000, true).ConfigureAwait(false);
                m_session.Dispose();
            }
            if (m_sessionFixture != null)
            {
                await m_sessionFixture.DisposeAsync().ConfigureAwait(false);
            }
            if (m_server != null)
            {
                await m_server.StopAsync().ConfigureAwait(false);
            }
            if (m_pkiRoot != null && Directory.Exists(m_pkiRoot))
            {
                Directory.Delete(m_pkiRoot, recursive: true);
            }
        }

        /// <summary>
        /// The server advertises both model namespaces and the AutoID device types sit below the DI DeviceType.
        /// </summary>
        [Test]
        public async Task DeviceTypeHierarchyIsBrowsableAsync()
        {
            NodeId deviceType = ToNodeId(Di.ObjectTypeIds.DeviceType);
            NodeId autoIdDeviceType = ToNodeId(ObjectTypeIds.AutoIdDeviceType);

            (ArrayOf<ArrayOf<ReferenceDescription>> results, ArrayOf<ServiceResult> errors) =
                await m_session!.ManagedBrowseAsync(
                    null,
                    null,
                    [deviceType, autoIdDeviceType],
                    0,
                    BrowseDirection.Forward,
                    ReferenceTypeIds.HasSubtype,
                    false,
                    0,
                    CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(m_session!.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri), Is.GreaterThan(0));
                Assert.That(m_session.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi), Is.GreaterThan(0));
                Assert.That(errors.ToArray()!.All(error => ServiceResult.IsGood(error)), Is.True);
                Assert.That(results[0].ToArray()!.Select(reference => ToNodeId(reference.NodeId)),
                    Does.Contain(autoIdDeviceType));
                Assert.That(results[1].ToArray()!.Select(reference => reference.BrowseName.Name), Is.SupersetOf(
                    [
                        BrowseNames.RfidReaderDeviceType,
                        BrowseNames.OcrReaderDeviceType,
                        BrowseNames.OpticalReaderDeviceType,
                        BrowseNames.RtlsDeviceType
                    ]));
            });
        }

        /// <summary>
        /// The DataTypeDefinition of the ScanData union and of RfidAccessResult, a subtype with
        /// optional fields, arrive as published.
        /// </summary>
        [Test]
        public async Task DataTypeDefinitionsAreServedAsync()
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = ToNodeId(DataTypeIds.ScanData),
                    AttributeId = Attributes.DataTypeDefinition
                },
                new ReadValueId
                {
                    NodeId = ToNodeId(DataTypeIds.RfidAccessResult),
                    AttributeId = Attributes.DataTypeDefinition
                }
            ];
            ReadResponse response = await m_session!.ReadAsync(
                null, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None).ConfigureAwait(false);

            StructureDefinition scanData = ToStructureDefinition(response.Results[0]);
            StructureDefinition rfidAccessResult = ToStructureDefinition(response.Results[1]);
            Assert.Multiple(() =>
            {
                Assert.That(scanData.StructureType, Is.EqualTo(StructureType.Union));
                Assert.That(scanData.Fields.ToArray()!.Select(field => field.Name),
                    Is.EqualTo(s_scanDataFields));
                Assert.That(scanData.Fields[2].DataType, Is.EqualTo(ToNodeId(DataTypeIds.ScanDataEpc)));
                Assert.That(rfidAccessResult.StructureType, Is.EqualTo(StructureType.StructureWithOptionalFields));
                Assert.That(rfidAccessResult.BaseDataType, Is.EqualTo(ToNodeId(DataTypeIds.AccessResult)));
                Assert.That(rfidAccessResult.Fields.ToArray()!.Select(field => field.Name),
                    Does.Contain("RWData").And.Contain("Antenna"));
                Assert.That(rfidAccessResult.Fields.ToArray()!.All(field => field.IsOptional), Is.True);
            });
        }

        /// <summary>
        /// A scan event subtype serves its ScanResult property with the refined DataType.
        /// </summary>
        [Test]
        public async Task RefinedScanResultPropertyIsServedWithRefinedDataTypeAsync()
        {
            ArrayOf<ReadValueId> nodesToRead =
            [
                new ReadValueId
                {
                    NodeId = ToNodeId(VariableIds.AutoIdScanEventType_ScanResult),
                    AttributeId = Attributes.DataType
                },
                new ReadValueId
                {
                    NodeId = ToNodeId(VariableIds.RfidScanEventType_ScanResult),
                    AttributeId = Attributes.DataType
                },
                new ReadValueId
                {
                    NodeId = ToNodeId(VariableIds.RfidScanEventType_ScanResult),
                    AttributeId = Attributes.ValueRank
                }
            ];
            ReadResponse response = await m_session!.ReadAsync(
                null, 0, TimestampsToReturn.Neither, nodesToRead, CancellationToken.None).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(response.Results[0].WrappedValue.TryGetValue(out NodeId baseType), Is.True);
                Assert.That(baseType, Is.EqualTo(ToNodeId(DataTypeIds.ScanResult)));
                Assert.That(response.Results[1].WrappedValue.TryGetValue(out NodeId refinedType), Is.True);
                Assert.That(refinedType, Is.EqualTo(ToNodeId(DataTypeIds.RfidScanResult)));
                Assert.That(response.Results[2].WrappedValue.TryGetValue(out int valueRank), Is.True);
                Assert.That(valueRank, Is.EqualTo(ValueRanks.OneDimension));
            });
        }

        /// <summary>
        /// The ReadTag input arguments decode on the client with their AutoID DataTypes.
        /// </summary>
        [Test]
        public async Task MethodArgumentsAreReadableAsync()
        {
            DataValue value = await m_session!.ReadValueAsync(
                ToNodeId(VariableIds.RfidReaderDeviceType_ReadTag_InputArguments),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(value.WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> arguments), Is.True);
            Argument[] decoded =
            [
                .. arguments.ToArray()!.Select(argument => argument.TryGetValue(out Argument? typed) ? typed! : null!)
            ];
            Assert.Multiple(() =>
            {
                Assert.That(decoded, Has.None.Null);
                Assert.That(decoded[0].Name, Is.EqualTo("Identifier"));
                Assert.That(decoded[0].DataType, Is.EqualTo(ToNodeId(DataTypeIds.ScanData)));
            });
        }

        private NodeId ToNodeId(ExpandedNodeId nodeId)
        {
            return ExpandedNodeId.ToNodeId(nodeId, m_session!.NamespaceUris);
        }

        private static StructureDefinition ToStructureDefinition(DataValue value)
        {
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, value.StatusCode.ToString());
            Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
            Assert.That(extension.TryGetValue(out StructureDefinition? definition), Is.True);
            return definition!;
        }

        private static readonly string[] s_scanDataFields = ["ByteString", "String", "Epc", "Custom"];
        private string? m_pkiRoot;
        private ServerFixture<StandardServer>? m_server;
        private SessionFixture? m_sessionFixture;
        private UaSession? m_session;

        /// <summary>
        /// Loads nothing but the DI and AutoID models.
        /// </summary>
        private sealed class ModelOnlyNodeManager : AsyncCustomNodeManager
        {
            public ModelOnlyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, Di.Namespaces.OpcUaDi, AutoIdModel.NamespaceUri)
            {
            }

            protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
                ISystemContext context,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeStateCollection>(new NodeStateCollection().AddAutoIdTypeSystem(context));
            }
        }

        /// <summary>
        /// Creates the model-only node manager for the DI and AutoID namespaces.
        /// </summary>
        private sealed class ModelOnlyNodeManagerFactory : IAsyncNodeManagerFactory
        {
            public ArrayOf<string> NamespacesUris => AutoIdModel.GetNamespaceUris();

            public ValueTask<IAsyncNodeManager> CreateAsync(
                IServerInternal server,
                ApplicationConfiguration configuration,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<IAsyncNodeManager>(new ModelOnlyNodeManager(server, configuration));
            }
        }
    }
}
