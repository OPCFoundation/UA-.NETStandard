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
using Moq;
using Opc.Ua.Server;
using Opc.Ua.Tests;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.XRegistry.Tests
{
    /// <summary>
    /// Builds the mocked <see cref="IServerInternal"/> the xRegistry node managers are exercised
    /// against, and the deterministic content-id provider the registration lifecycle needs.
    /// </summary>
    internal static class XRegistryServerTestHarness
    {
        /// <summary>
        /// Creates a mocked server that hosts the given registry namespace.
        /// </summary>
        /// <param name="namespaceUri">The registry companion namespace URI.</param>
        /// <returns>The mocked server.</returns>
        public static Mock<IServerInternal> CreateServer(string namespaceUri)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var namespaceUris = new NamespaceTable();
            namespaceUris.GetIndexOrAppend(namespaceUri);
            var serverUris = new StringTable();
            var server = new Mock<IServerInternal>();
            var masterNodeManager = new Mock<IMasterNodeManager>();
            server.Setup(s => s.NamespaceUris).Returns(namespaceUris);
            server.Setup(s => s.ServerUris).Returns(serverUris);
            server.Setup(s => s.TypeTree).Returns(CreateTypeTable(namespaceUris));
            server.Setup(s => s.Factory).Returns(EncodeableFactory.Create());
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NodeManager).Returns(masterNodeManager.Object);
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            return server;
        }

        /// <summary>
        /// Seeds the standard base types the compiled xRegistry model derives from. A real server
        /// loads these with the core NodeSet; the type table rejects a subtype whose supertype it
        /// does not already know.
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
            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.BaseVariableType, NodeId.Null);
            typeTable.AddSubtype(
                Opc.Ua.VariableTypeIds.BaseDataVariableType, Opc.Ua.VariableTypeIds.BaseVariableType);
            typeTable.AddSubtype(Opc.Ua.VariableTypeIds.PropertyType, Opc.Ua.VariableTypeIds.BaseVariableType);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.BaseDataType, NodeId.Null);
            typeTable.AddSubtype(Opc.Ua.DataTypeIds.Structure, Opc.Ua.DataTypeIds.BaseDataType);
            return typeTable;
        }

        /// <summary>
        /// A content-id provider whose identity is the document itself, so a test can predict the
        /// Opaque NodeId a document is published under.
        /// </summary>
        internal sealed class FakeContentIdProvider : IResourceContentIdProvider
        {
            /// <inheritdoc/>
            public ByteString ComputeContentId(string format, ReadOnlySpan<byte> document)
            {
                return ByteString.From(document.ToArray());
            }

            /// <inheritdoc/>
            public string? GetAlgorithm(string format)
            {
                return "test";
            }
        }
    }
}
