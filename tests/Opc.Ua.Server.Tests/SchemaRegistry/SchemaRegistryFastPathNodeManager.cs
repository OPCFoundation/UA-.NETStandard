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

namespace Opc.Ua.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// Proves the Schema Registry spec's Opaque <c>SchemaId</c>-NodeId fast path (§6.4): a
    /// registered schema document is additionally addressable by an Opaque NodeId in the
    /// Schema Registry namespace whose Identifier is the raw on-wire SchemaId bytes, and a
    /// single <c>Read</c> of that node's Value Attribute returns the schema document — no
    /// Browse and no fingerprint recomputation. This node manager shares the Schema Registry
    /// namespace with the runtime-loaded companion NodeSet (the numeric type/instance nodes are
    /// served there; the Opaque content-addressed fast-path nodes are served here), exactly as a
    /// production server would bind the Opaque-SchemaId-NodeId resolution to its schema store.
    /// </summary>
    internal sealed class SchemaRegistryFastPathNodeManager : CustomNodeManager2
    {
        /// <summary>
        /// The raw on-wire SchemaId bytes of the one registered schema. A consumer builds the
        /// Opaque fast-path NodeId directly from these bytes.
        /// </summary>
        public static readonly ByteString KnownSchemaId =
            ByteString.From([0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0xF6, 0x07, 0x18]);

        /// <summary>
        /// The schema document addressed by <see cref="KnownSchemaId"/>.
        /// </summary>
        public static readonly ByteString KnownDocument = ByteString.From(
            System.Text.Encoding.UTF8.GetBytes(
                "{\"type\":\"record\",\"name\":\"FastPath\",\"fields\":[]}"));

        /// <summary>
        /// Initializes the fast-path node manager for the Schema Registry namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        public SchemaRegistryFastPathNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, SchemaRegistryTestServer.SchemaRegistryNamespaceUri)
        {
        }

        /// <summary>
        /// Materializes the content-addressed Opaque SchemaId node. Its NodeId is constructed
        /// deterministically from the raw SchemaId bytes (§6.4) so a decoder that received the
        /// SchemaId on the wire can reach the schema document in one Read.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);

            var schema = new BaseDataVariableState(null)
            {
                // IdentifierType = Opaque, Identifier = the exact raw on-wire SchemaId bytes.
                NodeId = new NodeId(KnownSchemaId, ns),
                BrowseName = new QualifiedName("FastPathSchema", ns),
                DisplayName = new LocalizedText("FastPathSchema"),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                DataType = DataTypeIds.ByteString,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Historizing = false,
                Value = new Variant(KnownDocument)
            };

            AddPredefinedNode(SystemContext, schema);
        }
    }
}
