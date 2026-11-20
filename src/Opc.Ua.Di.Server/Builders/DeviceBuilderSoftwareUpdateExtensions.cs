// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
