// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AMB.Client
{
    public sealed partial class AmbClient
    {
        /// <summary>
        /// Reads everything the client knows about one asset.
        /// </summary>
        /// <param name="asset">The asset, for example from <see cref="DiscoverAssetsAsync"/>.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The snapshot.</returns>
        public async ValueTask<AssetSnapshot> ReadAssetAsync(
            NodeId asset,
            CancellationToken cancellationToken = default)
        {
            AssetIdentificationRecord identification = await ReadIdentificationAsync(asset, cancellationToken)
                .ConfigureAwait(false);
            AssetReadValue health = await ReadDeviceHealthValueAsync(asset, cancellationToken).ConfigureAwait(false);
            return new AssetSnapshot
            {
                Asset = asset,
                Identification = identification,
                DeviceHealth = StatusCode.IsGood(health.Value.StatusCode) &&
                    health.Value.WrappedValue.TryGetValue(out int state) ? (Di.DeviceHealthEnumeration)state : null,
                DeviceHealthValue = m_options is null ? null : health,
                HealthAlarms = await ReadHealthAlarmsAsync(asset, cancellationToken).ConfigureAwait(false),
                MaintenanceActivities = await ReadMaintenanceActivitiesAsync(asset, cancellationToken)
                    .ConfigureAwait(false),
                DocumentationLinks = await ReadDocumentationLinksAsync(asset, cancellationToken).ConfigureAwait(false),
                Context = await ReadContextAsync(asset, cancellationToken).ConfigureAwait(false),
                Locations = await ReadLocationsOfAssetAsync(asset, cancellationToken).ConfigureAwait(false)
            };
        }
    }
}
