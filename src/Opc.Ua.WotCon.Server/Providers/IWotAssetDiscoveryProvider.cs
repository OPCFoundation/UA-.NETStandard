// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.WotCon.Server.ThingDescriptions;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Optional server-level discovery facade backing the three optional
    /// methods of <c>WoTAssetConnectionManagementType</c>:
    /// <c>DiscoverAssets</c>, <c>CreateAssetForEndpoint</c> and
    /// <c>ConnectionTest</c> (OPC 10100-1 §6.3.4–§6.3.6).
    /// </summary>
    /// <remarks>
    /// Each method is independently optional: implementations may throw
    /// <see cref="System.NotSupportedException"/> from any of them and the
    /// node manager will translate that to <see cref="StatusCodes.BadNotSupported"/>.
    /// </remarks>
    public interface IWotAssetDiscoveryProvider
    {
        /// <summary>
        /// Enumerates asset endpoints reachable on the local network.
        /// </summary>
        ValueTask<IReadOnlyList<string>> DiscoverAsync(CancellationToken ct);

        /// <summary>
        /// Tests connectivity to a single asset endpoint.
        /// </summary>
        ValueTask<(bool Success, string Status)> TestAsync(
            string assetEndpoint,
            CancellationToken ct);

        /// <summary>
        /// Generates a WoT Thing Description for an asset reached via
        /// <paramref name="assetEndpoint"/>. The returned descriptor is the
        /// same TD the server will subsequently materialise.
        /// </summary>
        ValueTask<ThingDescription> CreateThingDescriptionAsync(
            string assetName,
            string assetEndpoint,
            CancellationToken ct);
    }
}
