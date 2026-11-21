// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Export;
using UaLens.NodeSets;
using UaLens.NodeSets.Loading;
using UaLens.Telemetry;
using UaLens.ViewModels;
using UaLens.Workspace;

namespace UaLens.Tests.NodeSets
{
    public sealed partial class NodeSetAddressSpaceTests
    {
        [OneTimeSetUp]
        public async Task CreateGraphAsync()
        {
            m_graph = await CreateAsync().ConfigureAwait(false);
        }

        internal static NodeSetDocument ReadDocument(string xml, string source)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return new NodeSetDocument(source, UANodeSet.Read(stream)!);
        }

        internal static async Task<NodeSetAddressSpace> CreateAsync(NodeSetDocument? extra = null)
        {
            using Stream core = typeof(MainViewModel).Assembly.GetManifestResourceStream(
                "UaLens.NodeSets.Opc.Ua.NodeSet2.xml")!;
            ArrayOf<NodeSetDocument> documents =
            [
                new("OPC UA core (bundled)", UANodeSet.Read(core)!),
                ReadDocument(kApp, "app.xml"),
                ReadDocument(kDependency, "dependency.xml")
            ];
            if (extra is not null)
            {
                documents = documents.AddItem(extra);
            }
            return await new NodeSetAddressSpaceFactory(Telemetry()).CreateAsync(documents).ConfigureAwait(false);
        }

        internal static AppTelemetryContext Telemetry()
        {
            return new(new LogRingBuffer());
        }

        private Task<BrowseResponse> BrowseAsync(
            NodeId nodeId, NodeId referenceTypeId, BrowseDirection direction = BrowseDirection.Forward)
        {
            return m_graph.BrowseAsync(
                [new BrowseDescription
                {
                    NodeId = nodeId,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = true,
                    BrowseDirection = direction
                }]);
        }

        internal const string kApp = """
        <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd">
          <NamespaceUris><Uri>urn:lens:app</Uri><Uri>urn:lens:dependency</Uri></NamespaceUris>
          <Models><Model ModelUri="urn:lens:app"><RequiredModel ModelUri="urn:lens:dependency" /></Model></Models>
          <UAObject NodeId="ns=1;i=1" BrowseName="1:Machine">
            <DisplayName>Machine</DisplayName>
            <References>
              <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
              <Reference ReferenceType="ns=2;i=4">ns=2;i=2</Reference>
              <Reference ReferenceType="i=35">ns=1;i=404</Reference>
            </References>
          </UAObject>
          <UAObject NodeId="ns=1;i=99" BrowseName="1:Orphan"><DisplayName>Orphan</DisplayName></UAObject>
          <UAObjectType NodeId="ns=1;i=10" BrowseName="1:MachineType">
            <DisplayName>MachineType</DisplayName>
            <References><Reference ReferenceType="i=45" IsForward="false">i=58</Reference></References>
          </UAObjectType>
          <UAMethod NodeId="ns=1;i=11" BrowseName="1:Run" Executable="true">
            <DisplayName>Run</DisplayName>
            <References><Reference ReferenceType="i=47" IsForward="false">ns=1;i=10</Reference></References>
          </UAMethod>
        </UANodeSet>
        """;

        internal const string kDependency = """
        <UANodeSet xmlns="http://opcfoundation.org/UA/2011/03/UANodeSet.xsd"
                   xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd">
          <NamespaceUris><Uri>urn:lens:dependency</Uri><Uri>urn:lens:app</Uri></NamespaceUris>
          <Models><Model ModelUri="urn:lens:dependency" /></Models>
          <UAVariable NodeId="ns=1;i=2" BrowseName="1:Reading" DataType="i=6">
            <DisplayName>Reading</DisplayName><Value><uax:Int32>42</uax:Int32></Value>
          </UAVariable>
          <UADataType NodeId="ns=1;i=3" BrowseName="1:Sample">
            <DisplayName>Sample</DisplayName>
            <References><Reference ReferenceType="i=45" IsForward="false">i=22</Reference></References>
            <Definition Name="1:Sample"><Field Name="Reading" DataType="i=6" /></Definition>
          </UADataType>
          <UAReferenceType NodeId="ns=1;i=4" BrowseName="1:CustomComponent">
            <DisplayName>CustomComponent</DisplayName>
            <References><Reference ReferenceType="i=45" IsForward="false">i=47</Reference></References>
          </UAReferenceType>
        </UANodeSet>
        """;

        private NodeSetAddressSpace m_graph = null!;
    }
}
