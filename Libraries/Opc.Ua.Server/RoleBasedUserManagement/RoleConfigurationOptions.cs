/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
