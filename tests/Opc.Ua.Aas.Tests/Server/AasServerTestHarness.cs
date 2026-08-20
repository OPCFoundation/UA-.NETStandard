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

using Moq;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Aas.Tests.Server
{
    /// <summary>
    /// Builds the mocked <see cref="IServerInternal"/> the AAS node managers are exercised against.
    /// </summary>
    /// <remarks>
    /// Mirrors the xRegistry node-manager harness: a real server supplies the base types with the
    /// core NodeSet, and the type table rejects a subtype whose supertype it does not already know,
    /// so the standard type and reference trees have to be seeded by hand.
    /// </remarks>
    internal static class AasServerTestHarness
    {
        /// <summary>
        /// Creates a mocked server that hosts the given companion namespaces.
        /// </summary>
        /// <param name="namespaceUris">The companion namespace URIs to publish.</param>
        /// <returns>The mocked server.</returns>
        public static Mock<IServerInternal> CreateServer(params string[] namespaceUris)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceTable = new NamespaceTable();
            foreach (string namespaceUri in namespaceUris)
            {
                namespaceTable.GetIndexOrAppend(namespaceUri);
            }

            var server = new Mock<IServerInternal>();
            var masterNodeManager = new Mock<IMasterNodeManager>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceTable);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(CreateTypeTable(namespaceTable));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry);
            masterNodeManager.Setup(m => m.AsyncNodeManagers).Returns([]);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.MonitoredItemQueueFactory).Returns(new MonitoredItemQueueFactory(telemetry));
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        /// <summary>
        /// Seeds the standard base types and reference types the compiled AAS and xRegistry models
        /// derive from.
        /// </summary>
        /// <param name="namespaceUris">The server namespace table.</param>
        /// <returns>The seeded type table.</returns>
        public static TypeTable CreateTypeTable(NamespaceTable namespaceUris)
        {
            var typeTable = new TypeTable(namespaceUris);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.FolderType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.FileType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseEventType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(
                Opc.Ua.ObjectTypeIds.BaseInterfaceType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(
                Opc.Ua.ObjectTypeIds.NamespaceMetadataType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.BaseVariableType, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.VariableTypeIds.BaseDataVariableType, Opc.Ua.VariableTypeIds.BaseVariableType);
            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.PropertyType, Opc.Ua.VariableTypeIds.BaseVariableType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Structure, Opc.Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Enumeration, Opc.Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.String, Opc.Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.ByteString, Opc.Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Number, Opc.Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Integer, Opc.Ua.DataTypeIds.Number);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.UInteger, Opc.Ua.DataTypeIds.Number);

            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.References, NodeId.Null, new QualifiedName("References"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                Opc.Ua.ReferenceTypeIds.References,
                new QualifiedName("HierarchicalReferences"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.HasChild,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                new QualifiedName("HasChild"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.Aggregates,
                Opc.Ua.ReferenceTypeIds.HasChild,
                new QualifiedName("Aggregates"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.HasComponent,
                Opc.Ua.ReferenceTypeIds.Aggregates,
                new QualifiedName("HasComponent"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.HasProperty,
                Opc.Ua.ReferenceTypeIds.Aggregates,
                new QualifiedName("HasProperty"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.Organizes,
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                new QualifiedName("Organizes"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.NonHierarchicalReferences,
                Opc.Ua.ReferenceTypeIds.References,
                new QualifiedName("NonHierarchicalReferences"));
            typeTable.AddReferenceSubtype(
                Opc.Ua.ReferenceTypeIds.HasInterface,
                Opc.Ua.ReferenceTypeIds.NonHierarchicalReferences,
                new QualifiedName("HasInterface"));
            return typeTable;
        }
    }
}
