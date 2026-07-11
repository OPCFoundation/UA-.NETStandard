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
 *
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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Server.Hosting
{
    internal sealed class FluentNodeManagerFactory : IAsyncNodeManagerFactory
    {
        public FluentNodeManagerFactory(string namespaceUri, Action<INodeManagerBuilder> build)
        {
            if (string.IsNullOrWhiteSpace(namespaceUri))
            {
                throw new ArgumentException("Namespace URI must not be empty.", nameof(namespaceUri));
            }
            m_namespaceUri = namespaceUri;
            m_build = build ?? throw new ArgumentNullException(nameof(build));
            NamespacesUris = [namespaceUri];
        }

        public ArrayOf<string> NamespacesUris { get; }

        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            ILogger logger = server.Telemetry.CreateLogger<FluentNodeManager>();
#pragma warning disable CA2000 // Ownership transfers to the master node manager.
            var manager = new FluentNodeManager(
                server,
                configuration,
                logger,
                m_namespaceUri,
                m_build);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly Action<INodeManagerBuilder> m_build;
        private readonly string m_namespaceUri;
    }

    internal sealed class FluentNodeManager : FluentNodeManagerBase
    {
        public FluentNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            string namespaceUri,
            Action<INodeManagerBuilder> build)
            : base(server, configuration, logger, namespaceUri)
        {
            m_server = server ?? throw new ArgumentNullException(nameof(server));
            m_namespaceUri = namespaceUri ?? throw new ArgumentNullException(nameof(namespaceUri));
            m_build = build ?? throw new ArgumentNullException(nameof(build));
        }

        public override async ValueTask CreateAddressSpaceAsync(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            CancellationToken cancellationToken = default)
        {
            if (externalReferences is null)
            {
                throw new ArgumentNullException(nameof(externalReferences));
            }
            ushort namespaceIndex = (ushort)m_server.NamespaceUris.GetIndex(m_namespaceUri);
            FolderState root = CreateRootFolder(namespaceIndex);
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out IList<IReference>? references))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = [];
            }
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));
            await AddPredefinedNodeAsync(root, cancellationToken).ConfigureAwait(false);
            NodeManagerBuilder builder = CreateFluentBuilder(namespaceIndex);
            m_build(builder);
            builder.Seal();
        }

        private static FolderState CreateRootFolder(ushort namespaceIndex)
        {
            const string browseName = "ReferenceServer";
            var root = new FolderState(null)
            {
                NodeId = new NodeId(browseName, namespaceIndex),
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = new LocalizedText(browseName),
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
            root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
            return root;
        }

        private readonly Action<INodeManagerBuilder> m_build;
        private readonly IServerInternal m_server;
        private readonly string m_namespaceUri;
    }
}
