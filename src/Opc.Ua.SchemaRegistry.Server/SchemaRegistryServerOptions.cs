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
using Opc.Ua.XRegistry.Server;

namespace Opc.Ua.SchemaRegistry.Server
{
    /// <summary>
    /// Configures one hosted in-server Schema Registry instance.
    /// </summary>
    public sealed class SchemaRegistryServerOptions
    {
        /// <summary>
        /// Gets or sets whether the well-known SchemaRegistry root is hosted.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Gets or sets the registry identifier used for the hosted catalog.
        /// </summary>
        public string RegistryId { get; set; } = "schema-registry";

        /// <summary>
        /// Gets or sets complete-state storage. The host creates an in-memory store when unset;
        /// the application owns a supplied store.
        /// </summary>
        public IRegistryStateStore? Store { get; set; }

        /// <summary>
        /// Gets or sets an authoritative OriginUri. When unset, identity uses the server ApplicationUri
        /// and the portable well-known SchemaRegistry root NodeId.
        /// </summary>
        public string? OriginUri { get; set; }

        /// <summary>
        /// Gets or sets the format-owner binding of an entity URI and an explicit selector.
        /// </summary>
        public Func<string, string, string, string>? BindSelector { get; set; }

        /// <summary>
        /// Gets configured Schema Group namespace URIs keyed by their GroupId.
        /// A Group is not inferred from a network locator.
        /// </summary>
        public Dictionary<string, string> NamespaceUris { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets schema subject names keyed by logical Resource Xid. These are provider-owned
        /// source identities, not names inferred by reversing a ResourceId or fingerprint.
        /// </summary>
        public Dictionary<string, string> SchemaNames { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets whether the xRegistry dependency model is loaded by this node manager.
        /// </summary>
        public bool LoadDependencyModels { get; set; } = true;

        /// <summary>
        /// Gets or sets the schema format providers. The default uses the first-party providers.
        /// </summary>
        public ArrayOf<ISchemaFormatProvider>? SchemaFormatProviders { get; set; }

        /// <summary>
        /// Gets or sets the access policy. The default opens reads and requires SignAndEncrypt with
        /// ConfigureAdmin or SecurityAdmin for writes.
        /// </summary>
        public Func<ISystemContext, RegistryAccessKind, ServiceResult>? Authorize { get; set; }

        /// <summary>
        /// Gets or sets per-Version visibility. Hidden Versions are excluded before legacy
        /// fingerprint ambiguity is evaluated and cannot be read through typed selection.
        /// </summary>
        public Func<ISystemContext, SchemaReferenceDataType, bool>? IsVisible { get; set; }

        /// <summary>
        /// Gets or sets authoritative lineage and known model/DataSet provenance.
        /// </summary>
        public Func<SchemaReferenceDataType, SchemaContentDataType, SchemaVersionMetadata>? DescribeVersion { get; set; }

        /// <summary>
        /// Gets or sets the verifier for explicitly advertised schema compatibility contracts.
        /// </summary>
        public Func<SchemaContentDataType, SchemaContentDataType, string, bool>? VerifyCompatibility { get; set; }

        /// <summary>
        /// Gets or sets authoritative entity URI assignment for inherited creation.
        /// The default assigns opaque URNs, not guessed HTTP locators.
        /// </summary>
        public Func<string, string>? EntityUri { get; set; }
    }
}
