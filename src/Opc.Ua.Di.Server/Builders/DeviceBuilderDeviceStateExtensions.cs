/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Extension methods on <see cref="IDeviceBuilder{TDevice}"/> that
    /// expose <see cref="DeviceState"/>-specific configuration. These are
    /// segregated from the core builder interface so that
    /// <see cref="ComponentState"/>-typed builders (used for sub-
    /// components) don't expose properties that only exist on full
    /// <see cref="DeviceState"/> instances.
    /// </summary>
    public static class DeviceBuilderDeviceStateExtensions
    {
        /// <summary>
        /// Sets the <c>DeviceHealth</c> variable on the device to the
        /// supplied <paramref name="health"/> value. Equivalent to
        /// writing the NAMUR NE 107 health state.
        /// </summary>
        /// <typeparam name="TDevice">
        /// Concrete device state; constrained to <see cref="DeviceState"/>
        /// because <c>DeviceHealth</c> only exists on that subclass.
        /// </typeparam>
        /// <param name="builder">The device builder.</param>
        /// <param name="health">The DI health enumeration value.</param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public static IDeviceBuilder<TDevice> WithDeviceHealth<TDevice>(
            this IDeviceBuilder<TDevice> builder,
            DeviceHealthEnumeration health)
            where TDevice : DeviceState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            BaseDataVariableState<DeviceHealthEnumeration>? deviceHealth =
                builder.Device.DeviceHealth ??
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "Device '{0}' does not expose a DeviceHealth variable. " +
                    "Use a typed factory that instantiates the DeviceType children, " +
                    "or pre-populate DeviceHealth via Configure().",
                    builder.Device.BrowseName);

            deviceHealth.Value = health;
            deviceHealth.ClearChangeMasks(builder.Context, false);
            return builder;
        }
    }
}
