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
        public int MaxThingDescriptionSize { get; set; } = 1024 * 1024;

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
        public object? InitialValue { get; init; }

        /// <summary>Optional description.</summary>
        public string? Description { get; init; }

        /// <summary>Whether the parameter is writable.</summary>
        public bool Writable { get; init; } = true;
    }
}
