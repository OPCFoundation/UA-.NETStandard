/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// Makes a Device Integration device a manageable asset.
    /// </summary>
    public static class DeviceBuilderAssetExtensions
    {
        /// <summary>
        /// Registers the device with the asset management.
        /// </summary>
        /// <typeparam name="TDevice">The device state type.</typeparam>
        /// <param name="device">The device builder.</param>
        /// <param name="assets">The asset management of the server.</param>
        /// <param name="configure">Adds the AMB building blocks to the asset.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The handle of the asset.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="device"/> or <paramref name="assets"/> is <c>null</c>.
        /// </exception>
        public static ValueTask<IAssetHandle> RegisterAsAssetAsync<TDevice>(
            this IDeviceBuilder<TDevice> device,
            IAssetManagement assets,
            Action<IAssetBuilder>? configure = null,
            CancellationToken cancellationToken = default)
            where TDevice : ComponentState
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (assets == null)
            {
                throw new ArgumentNullException(nameof(assets));
            }
            return assets.RegisterAssetAsync(device.Node, configure, cancellationToken);
        }
    }
}
