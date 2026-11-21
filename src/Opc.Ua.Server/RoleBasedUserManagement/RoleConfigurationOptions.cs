// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Collections.Generic;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Configures the default <see cref="RoleManager"/> implementation.
    /// </summary>
    public sealed class RoleConfigurationOptions
    {
        /// <summary>
        /// Roles and identity mappings applied to the default <see cref="RoleManager"/>.
        /// </summary>
        public IList<RoleDefinitionOptions> Roles { get; } = [];
    }

    /// <summary>
    /// Configures one role in the default <see cref="RoleManager"/>.
    /// </summary>
    public sealed class RoleDefinitionOptions
    {
        /// <summary>
        /// Role browse name. Existing well-known roles are matched by browse name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Namespace URI used when creating a custom role.
        /// </summary>
        public string? NamespaceUri { get; set; }

        /// <summary>
        /// Identity-mapping rules to add to the role.
        /// </summary>
        public IList<RoleIdentityMappingOptions> Identities { get; } = [];

        /// <summary>
        /// Application URIs to add to the role.
        /// </summary>
        public IList<string> Applications { get; } = [];

        /// <summary>
        /// Whether <see cref="Applications"/> is an exclude list.
        /// </summary>
        public bool ApplicationsExclude { get; set; }

        /// <summary>
        /// Endpoint filters to add to the role.
        /// </summary>
        public IList<EndpointType> Endpoints { get; } = [];

        /// <summary>
        /// Whether <see cref="Endpoints"/> is an exclude list.
        /// </summary>
        public bool EndpointsExclude { get; set; }

        /// <summary>
        /// Whether the role uses vendor-specific custom configuration.
        /// </summary>
        public bool CustomConfiguration { get; set; }
    }

    /// <summary>
    /// Binder-friendly representation of an OPC UA role identity-mapping rule.
    /// </summary>
    public sealed class RoleIdentityMappingOptions
    {
        /// <summary>
        /// Identity criteria type to apply.
        /// </summary>
        public IdentityCriteriaType CriteriaType { get; set; }

        /// <summary>
        /// Optional criteria value.
        /// </summary>
        public string? Criteria { get; set; }
    }
}
