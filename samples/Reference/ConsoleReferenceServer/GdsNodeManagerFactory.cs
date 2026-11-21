// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Gds;
using Opc.Ua.Gds.Server;
using Opc.Ua.Gds.Server.Database.Linq;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Factory that creates the GDS ApplicationsNodeManager for the reference server.
    /// The node manager automatically wires handlers for:
    /// <list type="bullet">
    /// <item><description>CertificateDirectoryType (Part 12 §7.6)</description></item>
    /// <item><description>KeyCredentialServiceType (Part 12 §8)</description></item>
    /// <item><description>AuthorizationServiceType (Part 12 §9)</description></item>
    /// </list>
    /// </summary>
    public sealed class GdsNodeManagerFactory : IAsyncNodeManagerFactory
    {
        private readonly GlobalDiscoveryServerConfiguration m_gdsConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="GdsNodeManagerFactory"/> class.
        /// </summary>
        /// <param name="gdsConfiguration">The GDS configuration.</param>
        public GdsNodeManagerFactory(GlobalDiscoveryServerConfiguration gdsConfiguration)
        {
            m_gdsConfiguration = gdsConfiguration;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Taken from the manager itself so the two cannot drift: the master
        /// node manager routes a namespace to this manager only if it is
        /// advertised here.
        /// </remarks>
        public ArrayOf<string> NamespacesUris
            => new(ApplicationsNodeManager.DefaultNamespaceUris());

        /// <inheritdoc/>
        public ValueTask<IAsyncNodeManager> CreateAsync(
            IServerInternal server,
            ApplicationConfiguration configuration,
            CancellationToken cancellationToken = default)
        {
            // Ensure the GDS data types are registered in the message context
            if (!server.MessageContext.Factory.ContainsEncodeableType(
                Opc.Ua.Gds.DataTypeIds.ApplicationRecordDataType))
            {
                server.MessageContext.Factory.Builder
                    .AddOpcUaGds()
                    .Commit();
            }

            string? databaseStorePath = Utils.ReplaceSpecialFolderNames(
                m_gdsConfiguration.DatabaseStorePath);

            // Ensure the directory exists
            string? databaseDir = Path.GetDirectoryName(databaseStorePath);
            if (!string.IsNullOrEmpty(databaseDir))
            {
                Directory.CreateDirectory(databaseDir);
            }

            LinqApplicationsDatabase database = string.IsNullOrEmpty(databaseDir)
                ? new LinqApplicationsDatabase()
                : JsonApplicationsDatabase.Load(databaseStorePath!);

#pragma warning disable CA2000 // Ownership is transferred to the server via returned node manager instance.
            var nodeManager = new ApplicationsNodeManager(
                server,
                configuration,
                database,
                database,
                new CertificateGroup(server.Telemetry),
                autoApprove: true);
#pragma warning restore CA2000
            // GDS AliasName Server facet (OPC 10000-17 Annex C), enabled in
            // the CTT configuration.
            nodeManager.AliasNameAggregationEnabled |= m_gdsConfiguration.EnableAliasNameAggregation;
            return new ValueTask<IAsyncNodeManager>(nodeManager);
        }
    }
}
