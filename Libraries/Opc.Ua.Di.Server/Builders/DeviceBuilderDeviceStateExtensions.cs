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
