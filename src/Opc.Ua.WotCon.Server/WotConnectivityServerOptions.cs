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

using System;
using System.Collections.Generic;
using Opc.Ua.WotCon.Server.Assets;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Configuration for an OPC UA WoT Connectivity server (OPC 10100-1).
    /// </summary>
    public sealed class WotConnectivityServerOptions
    {
        /// <summary>
        /// Default namespace URI used for dynamically created asset nodes.
        /// </summary>
        public const string DefaultAssetNamespaceUri = "http://opcfoundation.org/UA/WoT-Con/Assets/";

        /// <summary>
        /// The namespace URI used for dynamically created assets, property
        /// variables and action methods. Defaults to
        /// <see cref="DefaultAssetNamespaceUri"/>.
        /// </summary>
        public string AssetNamespaceUri { get; set; } = DefaultAssetNamespaceUri;

        /// <summary>
        /// Folder on disk where persisted Thing Descriptions are stored
        /// (one <c>{assetName}.jsonld</c> per asset). Created on startup
        /// when missing. Use <c>null</c> for memory-only mode (no
        /// persistence; assets are lost on restart).
        /// </summary>
        public string? ThingDescriptionStorageFolder { get; set; }

        /// <summary>
        /// Maximum size in bytes of a Thing Description file written via
        /// the OPC UA file primitives. Defaults to 1 MiB.
        /// </summary>
        /// <remarks>
        /// Enforced on both the write side (file primitives) and the read
        /// side (<c>EnumeratePersistedAsync</c>): persisted files larger
        /// than this limit are skipped with a warning at startup so an
        /// adversarial persistence directory cannot wedge server startup
        /// through memory exhaustion.
        /// </remarks>
        public int MaxThingDescriptionSize { get; set; } = 1024 * 1024;

        /// <summary>
        /// Maximum number of persisted Thing Description files processed
        /// from <see cref="ThingDescriptionStorageFolder"/> at startup
        /// (defence-in-depth bound to keep startup time linear in a
        /// known limit even when the folder grows unbounded). Defaults
        /// to 10 000 — enough headroom for production deployments while
        /// preventing a malicious or corrupted persistence directory
        /// from wedging startup through CPU exhaustion. Files beyond the
        /// limit are skipped with a single warning.
        /// </summary>
        public int MaxPersistedThingDescriptionFiles { get; set; } = 10_000;

        /// <summary>
        /// Maximum allowed JSON nesting depth for a persisted Thing
        /// Description. Files exceeding this depth are skipped with a
        /// warning rather than deserialized; this prevents
        /// stack-overflow attacks via pathologically deep JSON.
        /// Defaults to 64, which comfortably accommodates standard W3C
        /// Thing Descriptions while staying well below the default .NET
        /// recursion budget.
        /// </summary>
        public int MaxThingDescriptionJsonDepth { get; set; } = 64;

        /// <summary>
        /// Maximum number of concurrent open file handles per asset.
        /// </summary>
        public int MaxOpenFileHandlesPerAsset { get; set; } = 10;

        /// <summary>
        /// Provider factories used to instantiate per-asset providers.
        /// Order matters: the first factory whose
        /// <see cref="IWotAssetProviderFactory.CanHandle"/> returns
        /// <c>true</c> is selected.
        /// </summary>
        public IList<IWotAssetProviderFactory> Bindings { get; } = [];

        /// <summary>
        /// Optional server-level discovery provider backing the three
        /// optional methods of <c>WoTAssetConnectionManagementType</c>
        /// (Discover / CreateForEndpoint / ConnectionTest).
        /// </summary>
        public IWotAssetDiscoveryProvider? Discovery { get; set; }

        /// <summary>
        /// Allow-list / deny-list policy applied to every endpoint URI
        /// that flows from a remote OPC UA client through
        /// <c>CreateAssetForEndpoint</c> or <c>ConnectionTest</c>.
        /// Defaults are safe: only <c>http</c>, <c>https</c>,
        /// <c>opc.tcp</c> schemes; loopback and private-range hosts
        /// blocked; 30 s per-operation timeout. See
        /// <see cref="AssetEndpointPolicy"/> for the full default set.
        /// </summary>
        public AssetEndpointPolicy AssetEndpointPolicy { get; set; }
            = new AssetEndpointPolicy();

        /// <summary>
        /// Vendor-specific configuration parameters exposed under the
        /// optional <c>Configuration</c> object
        /// (OPC 10100-1 §6.3.7). When empty the Configuration object is
        /// not created.
        /// </summary>
        public IDictionary<string, WotConfigurationParameter> Configuration { get; }
            = new Dictionary<string, WotConfigurationParameter>(StringComparer.Ordinal);

        /// <summary>
        /// Optional license string surfaced as the <c>License</c> property
        /// of the <c>Configuration</c> object.
        /// </summary>
        public string? License { get; set; }

        /// <summary>
        /// Access policy applied to the standard
        /// <c>WoTAssetConnectionManagement</c> methods (CreateAsset,
        /// DeleteAsset, DiscoverAssets, CreateAssetForEndpoint,
        /// ConnectionTest). Defaults to a restrictive policy that
        /// requires <see cref="MessageSecurityMode.SignAndEncrypt"/>,
        /// a non-anonymous identity, and the
        /// <c>WellKnownRole_SecurityAdmin</c> role.
        /// </summary>
        public WotManagementAccessPolicy ManagementAccess { get; set; }
            = new WotManagementAccessPolicy();

        /// <summary>
        /// Optional bridge into the WoT Connectivity V2 registry. When set, a
        /// legacy 1.02 asset's Thing Description is mirrored as a Thing
        /// Description resource in <see cref="RegistryBridgeGroupId"/> whenever
        /// the asset is (re)built, and removed when the asset is deleted, so
        /// legacy assets participate in V2 materialization without making the
        /// flat V1 asset list canonical. Defaults to <c>null</c> (no bridge).
        /// </summary>
        public Registry.IWotRegistryService? RegistryBridge { get; set; }

        /// <summary>
        /// The registry group id legacy assets are mirrored into when
        /// <see cref="RegistryBridge"/> is set. Defaults to the reserved
        /// Thing Description group.
        /// </summary>
        public string RegistryBridgeGroupId { get; set; }
            = Registry.WotRegistryGroups.ThingDescriptions;
    }

    /// <summary>
    /// One vendor-specific configuration parameter under
    /// <c>WoTAssetConfigurationType</c>.
    /// </summary>
    public sealed class WotConfigurationParameter
    {
        /// <summary>The OPC UA <c>DataType</c> for the parameter.</summary>
        public NodeId DataType { get; init; } = DataTypeIds.String;

        /// <summary>The initial value (must be assignable to a <c>Variant</c>).</summary>
        public Variant? InitialValue { get; init; }

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Whether the parameter is writable.
        /// </summary>
        public bool Writable { get; init; } = true;
    }
}
