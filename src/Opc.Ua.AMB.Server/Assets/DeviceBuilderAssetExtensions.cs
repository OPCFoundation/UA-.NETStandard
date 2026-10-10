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
