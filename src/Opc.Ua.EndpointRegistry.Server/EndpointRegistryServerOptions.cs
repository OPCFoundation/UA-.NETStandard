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
using Opc.Ua.SchemaRegistry;
using Opc.Ua.XRegistry;
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.EndpointRegistry.Server
{
    /// <summary>
    /// Selects the well-known Endpoint Registry root that a catalog instance hosts.
    /// </summary>
    public enum EndpointRegistryRoot
    {
        /// <summary>
        /// The generic EndpointRegistry root with Endpoints and reusable Message Groups.
        /// </summary>
        Generic = 0,

        /// <summary>
        /// The separately selected MediaEndpointRegistry root.
        /// </summary>
        Media = 1
    }

    /// <summary>
    /// Configures one hosted Endpoint Registry catalog instance.
    /// </summary>
    public sealed class EndpointRegistryCatalogOptions
    {
        /// <summary>
        /// Gets or sets the registry identifier committed into an uninitialized store.
        /// </summary>
        public string RegistryId { get; set; } = "endpoint-registry";

        /// <summary>
        /// Gets or sets the collections of the instance. A Message-only catalog uses only
        /// <c>messagegroups</c>; the media root uses only <c>endpoints</c>.
        /// </summary>
        public ArrayOf<string> Collections { get; set; } = ["endpoints", "messagegroups"];

        /// <summary>
        /// Gets or sets the atomic complete-state store. The default is an in-memory store.
        /// </summary>
        public IRegistryStateStore? Store { get; set; }

        /// <summary>
        /// Gets or sets the facet profile URIs advertised by this instance. Advertise only facets
        /// whose behaviour the deployment actually provides.
        /// </summary>
        public ArrayOf<string> ProfileUris { get; set; } = [];

        /// <summary>
        /// Gets or sets additional domain validation of a complete resulting registry document.
        /// </summary>
        public Action<RegistryObjectValueDataType>? Validate { get; set; }
    }

    /// <summary>
    /// Configures the <see cref="EndpointRegistryNodeManager"/>.
    /// </summary>
    public sealed class EndpointRegistryServerOptions
    {
        /// <summary>
        /// Gets or sets the generic catalog, or <see langword="null"/> to omit the EndpointRegistry root.
        /// </summary>
        public EndpointRegistryCatalogOptions? Generic { get; set; } = new();

        /// <summary>
        /// Gets or sets the media catalog, or <see langword="null"/> to omit the MediaEndpointRegistry root.
        /// </summary>
        public EndpointRegistryCatalogOptions? Media { get; set; }

        /// <summary>
        /// Gets or sets whether this node manager loads the xRegistry and Schema Registry type
        /// models. Disable it when another node manager of the same server already owns them.
        /// The well-known SchemaRegistry root is never materialized by this node manager.
        /// </summary>
        public bool LoadDependencyModels { get; set; } = true;

        /// <summary>
        /// Gets or sets the schema format providers used to map inline Message schemas.
        /// The default contains the first-party JSON Schema and Avro providers.
        /// </summary>
        public ArrayOf<ISchemaFormatProvider>? SchemaFormatProviders { get; set; }

        /// <summary>
        /// Gets or sets the access policy. The default allows reads and requires a SignAndEncrypt
        /// channel and the ConfigureAdmin or SecurityAdmin role for mutations.
        /// </summary>
        public Func<ISystemContext, RegistryAccessKind, ServiceResult>? Authorize { get; set; }

        /// <summary>
        /// Gets or sets the advertised native snapshot limits, or <see langword="null"/> for the defaults.
        /// </summary>
        public RegistrySnapshotLimitsDataType? SnapshotLimits { get; set; }
    }
}
