// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Client.Roles
{
    /// <summary>
    /// Immutable snapshot of a role as exposed by the server's
    /// <c>RoleSet</c> per OPC UA Part 18 §4.4. Returned from
    /// <see cref="IRoleManagementClient.ListRolesAsync"/> and
    /// <see cref="IRoleManagementClient.ReadRoleAsync"/>.
    /// </summary>
    public sealed class RoleInfo
    {
        /// <summary>
        /// Initializes the snapshot.
        /// </summary>
        public RoleInfo(
            NodeId roleId,
            QualifiedName browseName,
            IReadOnlyList<IdentityMappingRuleType> identities,
            IReadOnlyList<string> applications,
            bool applicationsExclude,
            IReadOnlyList<EndpointType> endpoints,
            bool endpointsExclude,
            bool customConfiguration)
        {
            RoleId = roleId;
            BrowseName = browseName;
            Identities = identities;
            Applications = applications;
            ApplicationsExclude = applicationsExclude;
            Endpoints = endpoints;
            EndpointsExclude = endpointsExclude;
            CustomConfiguration = customConfiguration;
        }

        /// <summary>The role's NodeId.</summary>
        public NodeId RoleId { get; }

        /// <summary>The role's browse name (e.g. <c>"Observer"</c>).</summary>
        public QualifiedName BrowseName { get; }

        /// <summary>Identity-mapping rules currently configured on the role.</summary>
        public IReadOnlyList<IdentityMappingRuleType> Identities { get; }

        /// <summary>ApplicationUris currently configured on the role.</summary>
        public IReadOnlyList<string> Applications { get; }

        /// <summary>True if <see cref="Applications"/> is an exclude list.</summary>
        public bool ApplicationsExclude { get; }

        /// <summary>Endpoints currently configured on the role.</summary>
        public IReadOnlyList<EndpointType> Endpoints { get; }

        /// <summary>True if <see cref="Endpoints"/> is an exclude list.</summary>
        public bool EndpointsExclude { get; }

        /// <summary>True if the role uses vendor-specific configuration.</summary>
        public bool CustomConfiguration { get; }
    }
}
