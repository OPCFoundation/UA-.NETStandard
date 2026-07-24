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
using Opc.Ua.Server;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Serves the xRegistry Opaque content-id-NodeId fast path (§6.4): a registered resource is
    /// addressable by an Opaque NodeId in the registry namespace whose Identifier is the raw
    /// content-id bytes, and a single <c>Read</c> of that node's Value returns the resource document
    /// — no Browse and no fingerprint recomputation. When configured with a seed resource, it
    /// pre-publishes that resource so a fresh server can resolve at least one content-addressed
    /// resource before any registration.
    /// </summary>
    public class XRegistryFastPathNodeManager : CustomNodeManager2
    {
        private readonly string m_namespaceUri;
        private readonly IResourceContentIdProvider? m_contentIdProvider;
        private readonly bool m_publishSeed;
        private readonly byte[]? m_seedDocument;
        private readonly string m_seedFormat;
        private readonly string m_seedBrowseName;

        /// <summary>
        /// Initializes the fast-path node manager for the registry namespace.
        /// </summary>
        /// <param name="server">The server that owns the node manager.</param>
        /// <param name="configuration">The application configuration.</param>
        /// <param name="options">The registry server options.</param>
        public XRegistryFastPathNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            XRegistryServerOptions options)
            : base(server, configuration, (options ?? new XRegistryServerOptions()).RegistryNamespaceUri)
        {
            XRegistryServerOptions opts = options ?? new XRegistryServerOptions();
            m_namespaceUri = opts.RegistryNamespaceUri;
            m_contentIdProvider = opts.ContentIdProvider;
            m_publishSeed = opts.PublishSeedResource;
            m_seedDocument = opts.SeedDocument;
            m_seedFormat = opts.SeedFormat;
            m_seedBrowseName = opts.SeedBrowseName;
        }

        /// <summary>
        /// Materializes the content-addressed Opaque content-id node. Its NodeId is constructed
        /// deterministically from the raw content-id bytes (§6.4) so a decoder that received the id
        /// on the wire can reach the resource document in one Read.
        /// </summary>
        /// <param name="externalReferences">External reference sink (unused).</param>
        public override void CreateAddressSpace(
            IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            base.CreateAddressSpace(externalReferences);

            if (!m_publishSeed || m_seedDocument is null)
            {
                return;
            }

            if (m_contentIdProvider is null)
            {
                throw new InvalidOperationException(
                    "A ContentIdProvider is required to publish a seed resource.");
            }

            ushort ns = (ushort)Server.NamespaceUris.GetIndex(m_namespaceUri);
            ByteString contentId = m_contentIdProvider.ComputeContentId(m_seedFormat, m_seedDocument);

            var resource = new BaseDataVariableState(null)
            {
                // IdentifierType = Opaque, Identifier = the exact raw on-wire content-id bytes.
                NodeId = new NodeId(contentId, ns),
                BrowseName = new QualifiedName(m_seedBrowseName, ns),
                DisplayName = new LocalizedText(m_seedBrowseName),
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                DataType = DataTypeIds.ByteString,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Historizing = false,
                Value = new Variant(ByteString.From(m_seedDocument))
            };

            AddPredefinedNode(SystemContext, resource);
        }
    }
}
