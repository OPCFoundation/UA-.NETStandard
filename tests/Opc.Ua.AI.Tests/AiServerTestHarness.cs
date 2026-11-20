// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using Opc.Ua;
using Opc.Ua.AI.Inference;
using Opc.Ua.AI.Server;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.AI.Tests
{
    /// <summary>
    /// Builds the node manager the AI tests are exercised against.
    /// </summary>
    /// <remarks>
    /// A mocked <see cref="IServerInternal"/> rather than a running Server, because
    /// every claim these tests make is about the address space and the routing, and
    /// neither needs a socket. The tests that do need one say so.
    /// </remarks>
    internal static class AIServerTestHarness
    {
        /// <summary>
        /// Creates a node manager with its address space already built.
        /// </summary>
        public static async Task<AINodeManager> CreateAsync(
            InferenceBackends backends,
            AIOptions? options = null,
            InferenceBackendOptions? backendOptions = null)
        {
            Mock<IServerInternal> server = CreateServer();

#pragma warning disable CA2000 // the caller disposes the node manager
            var manager = new AINodeManager(
                server.Object,
                null!,
                backends,
                Options.Create(options ?? new AIOptions()),
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
            namespaceUris.GetIndexOrAppend(Opc.Ua.AI.Namespaces.AI);
            namespaceUris.GetIndexOrAppend(Opc.Ua.AI.Namespaces.xRegistry);

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

            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.FolderType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(Opc.Ua.ObjectTypeIds.FileType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(
                Opc.Ua.ObjectTypeIds.BaseEventType,
                Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(
                Opc.Ua.ObjectTypeIds.StateMachineType, Opc.Ua.ObjectTypeIds.BaseObjectType);
            typeTable.AddSubtype(
                Opc.Ua.ObjectTypeIds.FiniteStateMachineType, Opc.Ua.ObjectTypeIds.StateMachineType);
            typeTable.AddSubtype(
                Opc.Ua.ObjectTypeIds.ProgramStateMachineType, Opc.Ua.ObjectTypeIds.FiniteStateMachineType);

            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.BaseVariableType, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.VariableTypeIds.BaseDataVariableType, Opc.Ua.VariableTypeIds.BaseVariableType);
            typeTable.AddSubtype(
                Opc.Ua.VariableTypeIds.PropertyType, Opc.Ua.VariableTypeIds.BaseVariableType);

            typeTable.AddSubtype(Opc.Ua.DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Structure, Opc.Ua.DataTypeIds.BaseDataType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Enumeration, Opc.Ua.DataTypeIds.BaseDataType);

            typeTable.AddSubtype(Opc.Ua.ReferenceTypeIds.References, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.ReferenceTypeIds.NonHierarchicalReferences, Opc.Ua.ReferenceTypeIds.References);
            typeTable.AddSubtype(
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences, Opc.Ua.ReferenceTypeIds.References);

            return typeTable;
        }
    }
}
