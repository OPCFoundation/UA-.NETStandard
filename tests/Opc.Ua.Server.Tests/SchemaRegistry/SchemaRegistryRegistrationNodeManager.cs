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
    /// Proves the Schema Registry spec's registration lifecycle (§5.2) and auto-bootstrap
    /// (§10.1): a writer creates a schema resource, writes the document bytes, and closes it;
    /// on <c>Close</c> the server computes the <c>SchemaId</c> + <c>SchemaIdAlg</c> from the
    /// document via the pluggable per-format fingerprint provider (§6.6) and <b>dynamically, at
    /// runtime</b>, makes the document reachable by its Opaque <c>SchemaId</c> NodeId (§6.4).
    /// <para>
    /// The <c>CreateResource</c>/<c>Write</c>/<c>Close</c> methods model the base xRegistry
    /// <c>SchemaGroup.CreateResource</c> + <c>SchemaFileType</c> FileType write flow. The generic
    /// FileType Open/Write/Close machinery is already exercised elsewhere in the stack (TrustList,
    /// SoftwareUpdate), so this manager focuses on the Schema-Registry-specific de-risking: the
    /// auto-bootstrap on close and the dynamic runtime creation of the content-addressed fast-path
    /// node — exactly what a production server binds to its schema store.
    /// </para>
    /// </summary>
    internal sealed class SchemaRegistryRegistrationNodeManager : CustomNodeManager2
    {
        /// <summary>Provisional NodeId of the registration <c>SchemaGroup</c> object.</summary>
        public const uint SchemaGroupObject = 63001;

        /// <summary>Provisional NodeId of the <c>CreateResource</c> method.</summary>
        public const uint CreateResourceMethod = 63002;

        /// <summary>Provisional NodeId of the <c>Write</c> method.</summary>
        public const uint WriteMethod = 63003;

        /// <summary>Provisional NodeId of the <c>Close</c> method.</summary>
        public const uint CloseMethod = 63004;

        /// <summary>Provisional NodeId of the <c>Delete</c> method.</summary>
        public const uint DeleteMethod = 63005;

        private readonly object m_gate = new();
        private readonly Dictionary<uint, List<byte>> m_buffers = [];
        private readonly Dictionary<uint, string> m_versions = [];
        private uint m_nextHandle;

        /// <summary>
        /// Initializes the registration node manager for the Schema Registry namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        public SchemaRegistryRegistrationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(server, configuration, SchemaRegistryTestServer.SchemaRegistryNamespaceUri)
        {
        }

        /// <summary>
        /// Materializes the registration <c>SchemaGroup</c> and its <c>CreateResource</c>,
        /// <c>Write</c> and <c>Close</c> methods.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);

            var group = new BaseObjectState(null)
            {
                NodeId = new NodeId(SchemaGroupObject, ns),
                BrowseName = new QualifiedName("SchemaGroup", ns),
                DisplayName = new LocalizedText("SchemaGroup"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };

            AddMethod(group, CreateResourceMethod, ns, "CreateResource", OnCreateResource);
            AddMethod(group, WriteMethod, ns, "Write", OnWrite);
            AddMethod(group, CloseMethod, ns, "Close", OnClose);
            AddMethod(group, DeleteMethod, ns, "Delete", OnDelete);

            AddPredefinedNode(SystemContext, group);
        }

        private static void AddMethod(
            BaseObjectState parent,
            uint id,
            ushort ns,
            string name,
            GenericMethodCalledEventHandler2 handler)
        {
            var method = new MethodState(parent)
            {
                NodeId = new NodeId(id, ns),
                BrowseName = new QualifiedName(name, ns),
                DisplayName = new LocalizedText(name),
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                Executable = true,
                UserExecutable = true,
                OnCallMethod2 = handler
            };

            parent.AddChild(method);
        }

        // CreateResource(ResourceId: String, VersionId: String) -> (FileHandle: UInt32, VersionId: String)
        private ServiceResult OnCreateResource(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            _ = inputs[0].TryGetValue(out string _); // ResourceId (unused by the prove-out)
            _ = inputs[1].TryGetValue(out string versionId);
            if (string.IsNullOrEmpty(versionId))
            {
                versionId = "1";
            }

            uint handle;
            lock (m_gate)
            {
                handle = ++m_nextHandle;
                m_buffers[handle] = [];
                m_versions[handle] = versionId;
            }

            outputs.Add(new Variant(handle));
            outputs.Add(new Variant(versionId));
            return ServiceResult.Good;
        }

        // Write(FileHandle: UInt32, Data: ByteString) -> ()
        private ServiceResult OnWrite(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (!inputs[0].TryGetValue(out uint handle))
            {
                return StatusCodes.BadInvalidArgument;
            }

            _ = inputs[1].TryGetValue(out ByteString data);
            lock (m_gate)
            {
                if (!m_buffers.TryGetValue(handle, out List<byte> buffer))
                {
                    return StatusCodes.BadNotFound;
                }
                if (!data.IsNull && data.Span.Length > 0)
                {
                    buffer.AddRange(data.Span.ToArray());
                }
            }

            return ServiceResult.Good;
        }

        // Close(FileHandle: UInt32, Format: String) -> (SchemaId: ByteString, SchemaIdAlg: String)
        private ServiceResult OnClose(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (!inputs[0].TryGetValue(out uint handle))
            {
                return StatusCodes.BadInvalidArgument;
            }

            if (!inputs[1].TryGetValue(out string format) || string.IsNullOrEmpty(format))
            {
                format = "avro";
            }

            byte[] document;
            lock (m_gate)
            {
                if (!m_buffers.TryGetValue(handle, out List<byte> buffer))
                {
                    return StatusCodes.BadNotFound;
                }
                document = buffer.ToArray();
                m_buffers.Remove(handle);
                m_versions.Remove(handle);
            }

            // Auto-bootstrap (§10.1 + §6.6): compute the SchemaId + alg from the document.
            byte[] schemaId;
            string algorithm;
#pragma warning disable UA_NETStandard_Encoders // pluggable per-format fingerprint provider (§6.6)
            schemaId = SchemaIdProviders.ComputeSchemaId(format, document);
            algorithm = SchemaIdProviders.AlgorithmFor(format);
#pragma warning restore UA_NETStandard_Encoders

            // Make the document reachable by its Opaque SchemaId NodeId (§6.4), created at runtime.
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);
            var schemaIdBytes = ByteString.From(schemaId);
            var fastPathNodeId = new NodeId(schemaIdBytes, ns);

            if (Find(fastPathNodeId) is null)
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = fastPathNodeId,
                    BrowseName = new QualifiedName("RegisteredSchema", ns),
                    DisplayName = new LocalizedText("RegisteredSchema"),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,
                    DataType = DataTypeIds.ByteString,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Historizing = false,
                    Value = new Variant(ByteString.From(document))
                };

                AddPredefinedNode(SystemContext, node);
            }

            outputs.Add(new Variant(schemaIdBytes));
            outputs.Add(new Variant(algorithm));
            return ServiceResult.Good;
        }

        // Delete(SchemaId: ByteString) -> ()  (epoch-match args optional per spec §5.2)
        // Returns the Call StatusCode (void success), not a bool, to mirror the spec's
        // symmetric Delete on ResourceType : FileType: Good when removed, Bad_NotFound otherwise.
        private ServiceResult OnDelete(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (!inputs[0].TryGetValue(out ByteString schemaId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(
                SchemaRegistryTestServer.SchemaRegistryNamespaceUri);

            bool removed = DeleteNode(SystemContext, new NodeId(schemaId, ns));
            return removed ? ServiceResult.Good : StatusCodes.BadNotFound;
        }
    }
}
