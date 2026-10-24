/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

namespace Opc.Ua.SchemaRegistry.Server
{
    /// <summary>
    /// Provider-owned lineage and provenance for an exact schema Version.
    /// Optional facts are omitted when the provider does not know them.
    /// </summary>
    public sealed record SchemaVersionMetadata
    {
        /// <summary>
        /// Gets the parent VersionId, or empty for the server-assigned root/self lineage.
        /// </summary>
        public string Ancestor { get; init; } = string.Empty;

        /// <summary>
        /// Gets the Resource-wide compatibility contract, if explicitly verified by the provider.
        /// </summary>
        public string? Compatibility { get; init; }

        /// <summary>
        /// Gets the related DataTypeEncoding name, if known.
        /// </summary>
        public string? DataTypeEncoding { get; init; }

        /// <summary>
        /// Gets the originating NodeSet model version, if known.
        /// </summary>
        public string? ModelVersion { get; init; }

        /// <summary>
        /// Gets the optional PubSub DataSet correlation value.
        /// </summary>
        public ConfigurationVersionDataType? ConfigurationVersion { get; init; }

        internal SchemaVersionMetadata Copy() => this with
        {
            ConfigurationVersion = ConfigurationVersion is null ? null :
                (ConfigurationVersionDataType)ConfigurationVersion.Clone()
        };
    }
}
