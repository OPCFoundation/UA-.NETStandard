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
using Opc.Ua.Di.Server.SoftwareUpdate;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Fluent extensions that add the OPC 10000-100 §10.3 software-update
    /// facet to a device.
    /// </summary>
    public static class DeviceBuilderSoftwareUpdateExtensions
    {
        /// <summary>
        /// Materialises a <c>SoftwareUpdateType</c> instance under the
        /// device and binds its Loading subtype + state-machine method
        /// handlers to the supplied
        /// <see cref="ISoftwarePackageStore"/>.
        /// </summary>
        /// <typeparam name="TDevice">
        /// Concrete component/device state class.
        /// </typeparam>
        /// <param name="device">
        /// The owning device builder (typically the result of
        /// <c>IDiPostSetupContext.CreateDeviceAsync</c>).
        /// </param>
        /// <param name="packageStore">
        /// Server-wide package repository (commonly registered via DI as
        /// a single <see cref="ISoftwarePackageStore"/> instance and
        /// resolved with <c>ctx.GetRequiredService&lt;ISoftwarePackageStore&gt;()</c>).
        /// </param>
        /// <param name="configure">
        /// Optional callback that picks the Loading subtype + overrides
        /// the default state-machine method handlers. When omitted the
        /// library defaults to <see cref="SoftwareLoadingMode.Package"/>
        /// with "succeed immediately" stubs for every method.
        /// </param>
        /// <returns>
        /// The same <paramref name="device"/> builder, for fluent
        /// chaining.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="device"/> or <paramref name="packageStore"/>
        /// is <see langword="null"/>.
        /// </exception>
        public static IDeviceBuilder<TDevice> WithSoftwareUpdate<TDevice>(
            this IDeviceBuilder<TDevice> device,
            ISoftwarePackageStore packageStore,
            Action<ISoftwareUpdateBuilder>? configure = null)
            where TDevice : ComponentState
        {
            if (device is null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (packageStore is null)
            {
                throw new ArgumentNullException(nameof(packageStore));
            }
            var config = new SoftwareUpdateBuilder();
            configure?.Invoke(config);

            SoftwareUpdateFacetWiring.BuildAndAttach(
                device.Manager,
                device.Device,
                packageStore,
                config);

            return device;
        }
    }
}
