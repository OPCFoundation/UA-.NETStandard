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

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Export;
using Opc.Ua.Server;
using Opc.Ua.Server.Fluent;
using Quickstarts.ReferenceServer;

namespace NodeSetImportModel
{
    /// <summary>
    /// A source-generated node manager for the NodeSet import test model
    /// which overlays a NodeSet2 document on top of its generated nodes.
    /// </summary>
    /// <remarks>
    /// The class is declared in the generated model namespace so the emitted
    /// partial can reach the model's node states, and the generator makes it
    /// implement <c>INodeSetImportFactoryProvider</c>, so the overlay is
    /// imported into this model's typed states.
    /// </remarks>
    [NodeManager(NamespaceUri = NodeSetImportTestModel.NamespaceUri)]
    public sealed partial class NodeSetImportOverlayNodeManager
    {
        /// <summary>
        /// The Device instance the overlay document adds.
        /// </summary>
        public DeviceState OverlayDevice { get; private set; }

        /// <summary>
        /// The Value variable the overlay document supplies for the generated
        /// <c>Device</c> instance, replacing its generated child.
        /// </summary>
        public CustomValueState ReplacedDeviceValue { get; private set; }

        /// <summary>
        /// The node the generated <c>Device.Value</c> child had before the
        /// overlay replaced it.
        /// </summary>
        public NodeId DisplacedDeviceValueId { get; private set; }

        partial void Configure(INodeManagerBuilder builder)
        {
            ushort namespaceIndex = builder.Context.NamespaceUris.GetIndexOrAppend(
                NodeSetImportTestModel.NamespaceUri);
            DisplacedDeviceValueId = new NodeId(
                NodeSetImportTestModel.GeneratedDeviceValueIdentifier,
                namespaceIndex);

            // Two documents of one batch: the second one declares a parent
            // which only exists in the first one.
            builder.Import(NodeSetImportTestModel.ReadOverlayDevice());
            builder.Import(NodeSetImportTestModel.ReadOverlayDeviceChildren());

            OverlayDevice = builder
                .Node<DeviceState>(
                    new NodeId(
                        NodeSetImportTestModel.OverlayDeviceIdentifier,
                        namespaceIndex))
                .Node;
            ReplacedDeviceValue = builder
                .Node<CustomValueState>(
                    new NodeId(
                        NodeSetImportTestModel.ReplacedDeviceValueIdentifier,
                        namespaceIndex))
                .Node;
        }
    }

    /// <summary>
    /// Captures the node manager instance the server creates so the tests can
    /// assert against its address space.
    /// </summary>
    public sealed class NodeSetImportOverlayNodeManagerCapturingFactory :
        NodeSetImportOverlayNodeManagerFactory
    {
        /// <summary>
        /// The manager created for the running server.
        /// </summary>
        public NodeSetImportOverlayNodeManager Manager { get; private set; }

        /// <inheritdoc/>
        public override ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            var manager = new NodeSetImportOverlayNodeManager(server, configuration);
            Manager = manager;
            return new ValueTask<IAsyncNodeManager>(manager);
        }
    }

    /// <summary>
    /// Constants and overlay documents shared by the NodeSet import tests.
    /// </summary>
    public static class NodeSetImportTestModel
    {
        /// <summary>
        /// The model namespace generated from
        /// <c>Fluent/Assets/NodeSetImport.NodeSet2.xml</c>.
        /// </summary>
        public const string NamespaceUri = "urn:opcfoundation.org:2026-09:NodeSetImport";

        /// <summary>
        /// The <c>DeviceType</c> instance the model itself declares.
        /// </summary>
        public const uint GeneratedDeviceIdentifier = 2000u;

        /// <summary>
        /// The generated <c>Device.Value</c> child the overlay displaces.
        /// </summary>
        public const uint GeneratedDeviceValueIdentifier = 2001u;

        /// <summary>
        /// The <c>DeviceType</c> instance the overlay adds.
        /// </summary>
        public const uint OverlayDeviceIdentifier = 3000u;

        /// <summary>
        /// The <c>Calibrate</c> method the overlay adds to
        /// <see cref="OverlayDeviceIdentifier"/> from a second document.
        /// </summary>
        public const uint OverlayDeviceCalibrateIdentifier = 3001u;

        /// <summary>
        /// The <c>Value</c> variable the overlay supplies for the generated
        /// <c>Device</c> instance.
        /// </summary>
        public const uint ReplacedDeviceValueIdentifier = 3300u;

        /// <summary>
        /// A document adding one more instance of the model's DeviceType, plus
        /// a Value variable which replaces the generated child of the model's
        /// own Device instance.
        /// </summary>
        public static UANodeSet ReadOverlayDevice()
        {
            return Read(
                """
                  <UAObject NodeId="ns=1;i=3000" BrowseName="1:OverlayDevice" ParentNodeId="i=85">
                    <DisplayName>OverlayDevice</DisplayName>
                    <References>
                      <Reference ReferenceType="i=35" IsForward="false">i=85</Reference>
                      <Reference ReferenceType="i=40">ns=1;i=1000</Reference>
                    </References>
                  </UAObject>
                  <UAVariable NodeId="ns=1;i=3300" BrowseName="1:Value"
                              ParentNodeId="ns=1;i=2000" DataType="i=6">
                    <DisplayName>Value</DisplayName>
                    <References>
                      <Reference ReferenceType="i=40">ns=1;i=1001</Reference>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=2000</Reference>
                    </References>
                  </UAVariable>
                """);
        }

        /// <summary>
        /// A second document whose nodes hang off the instance declared by
        /// <see cref="ReadOverlayDevice"/>.
        /// </summary>
        public static UANodeSet ReadOverlayDeviceChildren()
        {
            return Read(
                """
                  <UAMethod NodeId="ns=1;i=3001" BrowseName="1:Calibrate"
                            ParentNodeId="ns=1;i=3000" MethodDeclarationId="ns=1;i=1003">
                    <DisplayName>Calibrate</DisplayName>
                    <References>
                      <Reference ReferenceType="i=47" IsForward="false">ns=1;i=3000</Reference>
                    </References>
                  </UAMethod>
                """);
        }

        private static UANodeSet Read(string nodes)
        {
            string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                "  <NamespaceUris>\r\n" +
                "    <Uri>" + NamespaceUri + "</Uri>\r\n" +
                "  </NamespaceUris>\r\n" +
                nodes.Replace("\n", "\r\n", System.StringComparison.Ordinal) + "\r\n" +
                "</UANodeSet>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return UANodeSet.Read(stream);
        }
    }

    /// <summary>
    /// A reference server hosting the generated node manager which overlays a
    /// NodeSet2 document onto its own model.
    /// </summary>
    public sealed class NodeSetImportServer : ReferenceServer
    {
        /// <summary>
        /// Initializes the server and registers the overlay node manager.
        /// </summary>
        public NodeSetImportServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            NodeManagerFactory = new NodeSetImportOverlayNodeManagerCapturingFactory();
            AddNodeManager(NodeManagerFactory);
        }

        /// <summary>
        /// The factory which captured the created node manager.
        /// </summary>
        public NodeSetImportOverlayNodeManagerCapturingFactory NodeManagerFactory { get; }
    }
}
