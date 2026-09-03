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

using System.Collections.Generic;
using System.IO;
using System.Text;
using Opc.Ua.Export;
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.Tests.Nodes
{
    [NodeSource(
        NamespaceUri = "urn:opcfoundation.org:2026-09:GeneratedNodeSource",
        AdditionalNamespaceUris =
        [
            "urn:opcfoundation.org:2026-09:GeneratedNodeSource:Instance"
        ])]
    public sealed partial class GeneratedPhase5NodeSource
    {
        public int UntypedConfigureCount { get; private set; }

        public int TypedConfigureCount { get; private set; }

        public int BehaviorRegistrationConfigureCount { get; private set; }

        public NodeId AuthoredObjectId { get; private set; }

        public NodeId AuthoredVariableId { get; private set; }

        public NodeId AuthoredMethodId { get; private set; }

        public NodeId StringNamedObjectId { get; private set; }

        public NodeId StringNamedVariableId { get; private set; }

        public NodeId StringNamedMethodId { get; private set; }

        public QualifiedName StringNamedBrowseName { get; private set; } = QualifiedName.Null;

        public GeneratedNodeSourceModel.DeviceState ExternalServerDevice { get; private set; } = null!;

        public GeneratedNodeSourceModel.DeviceState ExternalCapabilitiesDevice { get; private set; } = null!;

        public NodeId ImportedGeneratedValueId { get; private set; }

        public List<GeneratedNodeSourceModel.DeviceState> MaterializedDevices { get; } = [];

        partial void Configure(INodeGraphBuilder builder)
        {
            UntypedConfigureCount++;
            ushort namespaceIndex = builder.Context.NamespaceUris.GetIndexOrAppend(
                GeneratedNodeSourceModel.Namespaces.GeneratedNodeSourceModel);
            NodeId deviceId = NodeId.Create(
                GeneratedNodeSourceModel.Objects.Device,
                GeneratedNodeSourceModel.Namespaces.GeneratedNodeSourceModel,
                builder.Context.NamespaceUris);
            MaterializedDevices.Add(
                builder.Node<GeneratedNodeSourceModel.DeviceState>(deviceId).Node);
            builder.Import(ReadGeneratedValueReplacement());
            ImportedGeneratedValueId = new NodeId(3300u, namespaceIndex);

            INodeBuilder<FolderState> root = builder.AddFolder(
                new QualifiedName("GeneratedPhase5Root", namespaceIndex));
            INodeBuilder<GeneratedNodeSourceModel.DeviceState> stringNamedObject =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddDeviceType(
                    builder,
                    "StringNamed",
                    root.Node.NodeId);
            IVariableBuilder<int> stringNamedVariable =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddCustomValueType(
                    builder,
                    "StringValue",
                    stringNamedObject.Node.NodeId);
            INodeBuilder<GeneratedNodeSourceModel.CalibrateMethodState> stringNamedMethod =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddCalibrateMethodType(
                    builder,
                    "StringMethod",
                    stringNamedObject.Node.NodeId);
            INodeBuilder<GeneratedNodeSourceModel.DeviceState> authoredObject =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddDeviceType(
                    builder,
                    new QualifiedName("AuthoredObject", namespaceIndex),
                    root.Node.NodeId);
            IVariableBuilder<int> authoredVariable =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddCustomValueType(
                    builder,
                    new QualifiedName("AuthoredVariable", namespaceIndex),
                    authoredObject.Node.NodeId);
            INodeBuilder<GeneratedNodeSourceModel.CalibrateMethodState> authoredMethod =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddCalibrateMethodType(
                    builder,
                    new QualifiedName("AuthoredMethod", namespaceIndex),
                    authoredObject.Node.NodeId);
            ExternalServerDevice =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddDeviceType(
                        builder,
                        "SharedExternal",
                        ObjectIds.Server).Node;
            ExternalCapabilitiesDevice =
                GeneratedNodeSourceModel.GeneratedNodeSourceModelNodeGraphBuilderExtensions.
                    AddDeviceType(
                        builder,
                        "SharedExternal",
                        ObjectIds.Server_ServerCapabilities).Node;

            StringNamedObjectId = stringNamedObject.Node.NodeId;
            StringNamedVariableId = stringNamedVariable.Node.NodeId;
            StringNamedMethodId = stringNamedMethod.Node.NodeId;
            StringNamedBrowseName = stringNamedObject.Node.BrowseName;
            AuthoredObjectId = authoredObject.Node.NodeId;
            AuthoredVariableId = authoredVariable.Node.NodeId;
            AuthoredMethodId = authoredMethod.Node.NodeId;
        }

        partial void Configure(IGeneratedPhase5NodeSourceBuilder builder)
        {
            TypedConfigureCount++;
            _ = builder.Device;
        }

        partial void ConfigureBehaviorRegistrations(INodeGraphBuilder builder)
        {
            _ = builder;
            BehaviorRegistrationConfigureCount++;
        }

        private static UANodeSet ReadGeneratedValueReplacement()
        {
            const string xml =
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<UANodeSet xmlns=\"http://opcfoundation.org/UA/2011/03/UANodeSet.xsd\">\r\n" +
                "  <NamespaceUris>\r\n" +
                "    <Uri>urn:opcfoundation.org:2026-09:GeneratedNodeSource</Uri>\r\n" +
                "  </NamespaceUris>\r\n" +
                "  <UAVariable NodeId=\"ns=1;i=3300\" BrowseName=\"1:Value\" " +
                "ParentNodeId=\"ns=1;i=2000\" DataType=\"i=6\">\r\n" +
                "    <DisplayName>Value</DisplayName>\r\n" +
                "    <References>\r\n" +
                "      <Reference ReferenceType=\"i=40\">ns=1;i=1001</Reference>\r\n" +
                "      <Reference ReferenceType=\"i=47\" IsForward=\"false\">ns=1;i=2000</Reference>\r\n" +
                "    </References>\r\n" +
                "  </UAVariable>\r\n" +
                "</UANodeSet>";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
            return UANodeSet.Read(stream);
        }
    }
}
