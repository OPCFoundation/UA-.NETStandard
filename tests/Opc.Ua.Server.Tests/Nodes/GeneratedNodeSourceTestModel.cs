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
using Opc.Ua.Server.Fluent;
using Opc.Ua.Server.Nodes;

namespace Opc.Ua.Server.Tests.Nodes
{
    [NodeManager(
        NamespaceUri = "urn:opcfoundation.org:2026-09:GeneratedNodeSource",
        GenerateFactory = false,
        GenerateNodeSource = true,
        AdditionalNamespaceUris =
        [
            "urn:opcfoundation.org:2026-09:GeneratedNodeSource:Instance"
        ])]
    public partial class GeneratedPhase5NodeManager
    {
    }

    public sealed partial class GeneratedPhase5NodeManagerSource
    {
        public int UntypedConfigureCount { get; private set; }

        public int TypedConfigureCount { get; private set; }

        public int BehaviorRegistrationConfigureCount { get; private set; }

        public NodeId AuthoredObjectId { get; private set; }

        public NodeId AuthoredVariableId { get; private set; }

        public NodeId AuthoredMethodId { get; private set; }

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

            INodeBuilder<FolderState> root = builder.AddFolder(
                new QualifiedName("GeneratedPhase5Root", namespaceIndex));
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

            AuthoredObjectId = authoredObject.Node.NodeId;
            AuthoredVariableId = authoredVariable.Node.NodeId;
            AuthoredMethodId = authoredMethod.Node.NodeId;
        }

        partial void Configure(IGeneratedPhase5NodeManagerBuilder builder)
        {
            TypedConfigureCount++;
            _ = builder.Device;
        }

        partial void ConfigureBehaviorRegistrations(INodeGraphBuilder builder)
        {
            _ = builder;
            BehaviorRegistrationConfigureCount++;
        }
    }
}
