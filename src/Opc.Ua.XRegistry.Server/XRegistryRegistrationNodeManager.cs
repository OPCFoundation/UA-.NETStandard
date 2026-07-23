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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Serves the xRegistry registration lifecycle (§5.2) and auto-bootstrap (§10.1): a writer
    /// creates a resource, writes the document bytes, and closes it; on <c>Close</c> the server
    /// computes the content-derived id + algorithm from the document via the configured
    /// <see cref="IResourceContentIdProvider"/> (§6.6) and <b>dynamically, at runtime</b>, makes the
    /// document reachable by its Opaque content-id NodeId (§6.4). The generic FileType Open/Write/Close
    /// machinery is exercised elsewhere in the stack; this manager focuses on the registry-specific
    /// auto-bootstrap on close and the dynamic runtime creation of the content-addressed fast-path node.
    /// </summary>
    public class XRegistryRegistrationNodeManager : CustomNodeManager2
    {
        private readonly object m_gate = new();
        private readonly Dictionary<uint, List<byte>> m_buffers = [];
        private readonly Dictionary<uint, string> m_versions = [];
        private readonly string m_namespaceUri;
        private readonly IResourceContentIdProvider? m_contentIdProvider;
        private uint m_nextHandle;
        private int m_registeredResourceCount;

        // Bounds so a remote caller cannot exhaust memory or the address space
        // via the registration Methods: the number of concurrently open upload
        // handles, the cumulative bytes buffered per handle, and the number of
        // permanently registered resource nodes.
        private const int MaxConcurrentUploads = 64;
        private const int MaxResourceBytes = 16 * 1024 * 1024;
        private const int MaxRegisteredResources = 4096;

        /// <summary>
        /// Initializes the registration node manager for the registry namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">The registry server options.</param>
        public XRegistryRegistrationNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            XRegistryServerOptions options)
            : base(server, configuration, (options ?? new XRegistryServerOptions()).RegistryNamespaceUri)
        {
            XRegistryServerOptions opts = options ?? new XRegistryServerOptions();
            m_namespaceUri = opts.RegistryNamespaceUri;
            m_contentIdProvider = opts.ContentIdProvider;
        }

        /// <summary>
        /// Materializes the registration resource group and its <c>CreateResource</c>, <c>Write</c>,
        /// <c>Close</c> and <c>Delete</c> methods.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);

            var group = new BaseObjectState(null)
            {
                NodeId = new NodeId(XRegistryWellKnown.ResourceGroupObject, ns),
                BrowseName = new QualifiedName("ResourceGroup", ns),
                DisplayName = new LocalizedText("ResourceGroup"),
                TypeDefinitionId = ObjectTypeIds.BaseObjectType
            };

            AddMethod(group, XRegistryWellKnown.CreateResourceMethod, ns, "CreateResource", OnCreateResource);
            AddMethod(group, XRegistryWellKnown.WriteMethod, ns, "Write", OnWrite);
            AddMethod(group, XRegistryWellKnown.CloseMethod, ns, "Close", OnClose);
            AddMethod(group, XRegistryWellKnown.DeleteMethod, ns, "Delete", OnDelete);

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
            _ = inputs[0].TryGetValue(out string? _); // ResourceId (unused by the base lifecycle)
            _ = inputs[1].TryGetValue(out string? versionId);
            if (string.IsNullOrEmpty(versionId))
            {
                versionId = "1";
            }

            uint handle;
            lock (m_gate)
            {
                if (m_buffers.Count >= MaxConcurrentUploads)
                {
                    return StatusCodes.BadTooManyOperations;
                }
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
                if (!m_buffers.TryGetValue(handle, out List<byte>? buffer))
                {
                    return StatusCodes.BadNotFound;
                }
                if (!data.IsNull && data.Span.Length > 0)
                {
                    if (buffer.Count + data.Span.Length > MaxResourceBytes)
                    {
                        return StatusCodes.BadRequestTooLarge;
                    }
                    buffer.AddRange(data.Span.ToArray());
                }
            }

            return ServiceResult.Good;
        }

        // Close(FileHandle: UInt32, Format: String) -> (ContentId: ByteString, Algorithm: String)
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

            if (!inputs[1].TryGetValue(out string? format) || string.IsNullOrEmpty(format))
            {
                format = "avro";
            }

            if (m_contentIdProvider is null)
            {
                return StatusCodes.BadNotSupported;
            }

            byte[] document;
            lock (m_gate)
            {
                if (!m_buffers.TryGetValue(handle, out List<byte>? buffer))
                {
                    return StatusCodes.BadNotFound;
                }
                document = buffer.ToArray();
                m_buffers.Remove(handle);
                m_versions.Remove(handle);
            }

            // Auto-bootstrap (§10.1 + §6.6): compute the content-id + algorithm from the document.
            ByteString contentId = m_contentIdProvider.ComputeContentId(format, document);
            string algorithm = m_contentIdProvider.GetAlgorithm(format) ?? string.Empty;

            // Make the document reachable by its Opaque content-id NodeId (§6.4), created at runtime.
            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            var fastPathNodeId = new NodeId(contentId, ns);

            if (Find(fastPathNodeId) is null)
            {
                if (Volatile.Read(ref m_registeredResourceCount) >= MaxRegisteredResources)
                {
                    return StatusCodes.BadTooManyOperations;
                }

                var node = new BaseDataVariableState(null)
                {
                    NodeId = fastPathNodeId,
                    BrowseName = new QualifiedName("RegisteredResource", ns),
                    DisplayName = new LocalizedText("RegisteredResource"),
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
                Interlocked.Increment(ref m_registeredResourceCount);
            }

            outputs.Add(new Variant(contentId));
            outputs.Add(new Variant(algorithm));
            return ServiceResult.Good;
        }

        // Delete(ContentId: ByteString) -> ()  (epoch-match args optional per spec §5.2)
        private ServiceResult OnDelete(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant> outputs)
        {
            if (!inputs[0].TryGetValue(out ByteString contentId))
            {
                return StatusCodes.BadInvalidArgument;
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);

            bool removed = DeleteNode(SystemContext, new NodeId(contentId, ns));
            if (removed)
            {
                Interlocked.Decrement(ref m_registeredResourceCount);
            }
            return removed ? ServiceResult.Good : StatusCodes.BadNotFound;
        }
    }
}
