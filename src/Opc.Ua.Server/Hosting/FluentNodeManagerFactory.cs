// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        public FluentNodeManagerFactory(
            string namespaceUri,
            Action<INodeManagerBuilder> build,
            string? rootBrowseName = null)
        {
            if (string.IsNullOrWhiteSpace(namespaceUri))
            {
                throw new ArgumentException("Namespace URI must not be empty.", nameof(namespaceUri));
            }
            if (rootBrowseName != null && string.IsNullOrWhiteSpace(rootBrowseName))
            {
                throw new ArgumentException(
                    "Root browse name must not be empty.",
                    nameof(rootBrowseName));
            }
            m_namespaceUri = namespaceUri;
            m_build = build ?? throw new ArgumentNullException(nameof(build));
            m_rootBrowseName = rootBrowseName;
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
                m_build,
                m_rootBrowseName);
#pragma warning restore CA2000
            return new ValueTask<IAsyncNodeManager>(manager);
        }

        private readonly Action<INodeManagerBuilder> m_build;
        private readonly string m_namespaceUri;
        private readonly string? m_rootBrowseName;
    }

    internal sealed class FluentNodeManager : FluentNodeManagerBase
    {
        public FluentNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            ILogger logger,
            string namespaceUri,
            Action<INodeManagerBuilder> build,
            string? rootBrowseName)
            : base(server, configuration, logger, namespaceUri)
        {
            m_build = build ?? throw new ArgumentNullException(nameof(build));
            m_rootBrowseName = rootBrowseName;
        }

        protected override ValueTask ConfigureAsync(
            INodeManagerBuilder builder,
            CancellationToken cancellationToken)
        {
            // Staged before the build delegate runs, so it can parent nodes to
            // the root by NodeId or reach it by browse path.
            if (m_rootBrowseName != null)
            {
                builder.Add(CreateRootFolder(NamespaceIndex, m_rootBrowseName));
            }

            m_build(builder);
            return default;
        }

        /// <summary>
        /// Builds the root folder with an explicit string NodeId, which stays
        /// the address clients browse. The builder supplies the inverse
        /// Organizes reference to the Objects folder.
        /// </summary>
        private static FolderState CreateRootFolder(
            ushort namespaceIndex,
            string browseName)
        {
            return new FolderState(null)
            {
                NodeId = new NodeId(browseName, namespaceIndex),
                BrowseName = new QualifiedName(browseName, namespaceIndex),
                DisplayName = new LocalizedText(browseName),
                TypeDefinitionId = ObjectTypeIds.FolderType
            };
        }

        private readonly Action<INodeManagerBuilder> m_build;
        private readonly string? m_rootBrowseName;
    }
}
