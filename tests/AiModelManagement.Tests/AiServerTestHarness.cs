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
using System.Threading.Tasks;
using AiModelManagement.Bridge;
using AiModelManagement.Server;
using Microsoft.Extensions.Options;
using Moq;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace AiModelManagement.Tests
{
    /// <summary>
    /// Builds the node manager the AI tests are exercised against.
    /// </summary>
    /// <remarks>
    /// A mocked <see cref="IServerInternal"/> rather than a running Server, because
    /// every claim these tests make is about the address space and the routing, and
    /// neither needs a socket. The tests that do need one say so.
    /// </remarks>
    internal static class AiServerTestHarness
    {
        /// <summary>
        /// Creates a node manager with its address space already built.
        /// </summary>
        public static async Task<AiModelManagementNodeManager> CreateAsync(
            InferenceBackends backends,
            AiModelManagementOptions? options = null,
            InferenceBackendOptions? backendOptions = null)
        {
            Mock<IServerInternal> server = CreateServer();

#pragma warning disable CA2000 // the caller disposes the node manager
            var manager = new AiModelManagementNodeManager(
                server.Object,
                null!,
                backends,
                Options.Create(options ?? new AiModelManagementOptions()),
                Options.Create(backendOptions ?? new InferenceBackendOptions()));
#pragma warning restore CA2000

            await manager
                .CreateAddressSpaceAsync(new Dictionary<NodeId, IList<IReference>>())
                .ConfigureAwait(false);

            return manager;
        }

        /// <summary>
        /// Creates the mocked server the node manager runs inside.
        /// </summary>
        public static Mock<IServerInternal> CreateServer()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(Opc.Ua.AiModelManagement.Namespaces.AI);
            namespaceUris.GetIndexOrAppend(Opc.Ua.AiModelManagement.Namespaces.xRegistry);

            var server = new Mock<IServerInternal>();
            var masterNodeManager = new Mock<IMasterNodeManager>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(new StringTable());
            server.Setup(s => s.TypeTree).Returns(CreateTypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext)
                .Returns(new ServerSystemContext(server.Object));
            return server;
        }

        /// <summary>
        /// Seeds the base types the AI model derives from.
        /// </summary>
        /// <remarks>
        /// A real Server loads these with the core NodeSet. The type table rejects a
        /// subtype whose supertype it does not already know, so anything the model
        /// reaches for has to be here - including the Part 10 program state machine,
        /// which <c>AiJobType</c> subtypes so that a long inference has a lifecycle
        /// clients already understand.
        /// </remarks>
        public static TypeTable CreateTypeTable(NamespaceTable namespaceUris)
        {
            var typeTable = new TypeTable(namespaceUris);

            typeTable.AddSubtype(ObjectTypeIds.BaseObjectType, NodeId.Null);
            typeTable.AddSubtype(ObjectTypeIds.FolderType, ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(ObjectTypeIds.FileType, ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(
                ObjectTypeIds.StateMachineType, ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(
                ObjectTypeIds.FiniteStateMachineType, ObjectTypeIds.StateMachineType);
            typeTable.AddSubtype(
                ObjectTypeIds.ProgramStateMachineType, ObjectTypeIds.FiniteStateMachineType);

            typeTable.AddSubtype(VariableTypeIds.BaseVariableType, NodeId.Null);
            typeTable.AddSubtype(
                VariableTypeIds.BaseDataVariableType, VariableTypeIds.BaseVariableType);
            typeTable.AddSubtype(
                VariableTypeIds.PropertyType, VariableTypeIds.BaseVariableType);

            typeTable.AddSubtype(DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(DataTypeIds.Structure, DataTypeIds.BaseDataType);
            typeTable.AddSubtype(DataTypeIds.Enumeration, DataTypeIds.BaseDataType);

            typeTable.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
            typeTable.AddSubtype(
                ReferenceTypeIds.NonHierarchicalReferences, ReferenceTypeIds.References);
            typeTable.AddSubtype(
                ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References);

            return typeTable;
        }
    }
}
