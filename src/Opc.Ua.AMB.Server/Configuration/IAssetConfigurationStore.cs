/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AMB.Server.Configuration
{
    /// <summary>
    /// Persists the values a client configures on a manageable asset, such as
    /// its <c>AssetId</c>, so they survive a server restart (OPC 10000-110
    /// §7: configurable identification is stored persistently).
    /// </summary>
    /// <remarks>
    /// Values are keyed by the asset's <c>ProductInstanceUri</c>, which is
    /// globally unique and does not change over the asset's life, and by the
    /// name of the configured value (see <see cref="AssetConfigurationNames"/>).
    /// Implementations must be safe to call concurrently.
    /// </remarks>
    public interface IAssetConfigurationStore
    {
        /// <summary>
        /// Gets whether stored values survive a restart of the server.
        /// </summary>
        /// <remarks>
        /// "AMB Configurable Asset Identification" asks for the configuration
        /// to be stored persistently, so the server advertises that unit only
        /// over a store that says so.
        /// </remarks>
        bool IsPersistent { get; }

        /// <summary>
        /// Reads a configured value.
        /// </summary>
        /// <param name="productInstanceUri">The <c>ProductInstanceUri</c> of the asset.</param>
        /// <param name="name">The name of the value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// The value, or <see langword="null"/> when none has been stored.
        /// </returns>
        ValueTask<string?> GetValueAsync(
            string productInstanceUri,
            string name,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stores a configured value, replacing an earlier one.
        /// </summary>
        /// <param name="productInstanceUri">The <c>ProductInstanceUri</c> of the asset.</param>
        /// <param name="name">The name of the value.</param>
        /// <param name="value">The value; <see langword="null"/> removes it.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask SetValueAsync(
            string productInstanceUri,
            string name,
            string? value,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The names under which the asset management stores configured values.
    /// </summary>
    public static class AssetConfigurationNames
    {
        /// <summary>
        /// The user-assigned <c>2:AssetId</c> of an asset.
        /// </summary>
        public const string AssetId = "AssetId";

        /// <summary>
        /// The number of characters a stored value may have at most; the stores
        /// refuse longer values whoever writes them.
        /// </summary>
        public const int MaxValueLength = 1024 * 1024;

        /// <summary>
        /// The documentation links users added through <c>AddLink</c>, as a
        /// JSON array; a link the application declared editable is kept under
        /// this name, a slash and its browse name.
        /// </summary>
        public const string DocumentationLinks = "DocumentationLinks";

        /// <summary>A writable <c>HierarchicalLocation</c> (§13.3.2).</summary>
        public const string HierarchicalLocation = "HierarchicalLocation";

        /// <summary>A writable <c>OperationalLocation</c> (§13.4.2).</summary>
        public const string OperationalLocation = "OperationalLocation";

        /// <summary>A writable <c>DigitalLocation</c> (§13.5).</summary>
        public const string DigitalLocation = "DigitalLocation";

        /// <summary>A writable <c>0:LocalTime</c> (§13.2).</summary>
        public const string LocalTime = "LocalTime";
    }
}
