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

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Typed materialisation helpers for OPC 10000-100 §5.4 device
    /// sub-types (<see cref="SoftwareState"/>, <see cref="BlockState"/>,
    /// <see cref="ConfigurableObjectState"/>) on top of an existing
    /// <see cref="IDeviceBuilder{TDevice}"/>.
    /// </summary>
    /// <remarks>
    /// Each helper creates a child instance under the device,
    /// normalises browse-/symbolic name + NodeId via the manager's
    /// <see cref="INodeIdFactory"/>, and registers it with the
    /// owning <see cref="DiNodeManager"/> via
    /// <c>AddPredefinedNodeAsync</c>. The returned typed instance is
    /// ready for further configuration through standard
    /// <see cref="NodeState"/> APIs.
    /// </remarks>
    public static class DeviceBuilderTypeExtensions
    {
        /// <summary>
        /// Adds a <see cref="SoftwareState"/> instance under the device.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        public static SoftwareState AddSoftware<TDevice>(
            this IDeviceBuilder<TDevice> device,
            QualifiedName browseName,
            Action<SoftwareState>? configure = null)
            where TDevice : ComponentState
        {
            return AddTypedInstance(device, browseName,
                parent => new SoftwareState(parent), configure);
        }

        /// <summary>
        /// Adds a <see cref="BlockState"/> instance under the device.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        public static BlockState AddBlock<TDevice>(
            this IDeviceBuilder<TDevice> device,
            QualifiedName browseName,
            Action<BlockState>? configure = null)
            where TDevice : ComponentState
        {
            return AddTypedInstance(device, browseName,
                parent => new BlockState(parent), configure);
        }

        /// <summary>
        /// Adds a <see cref="ConfigurableObjectState"/> instance under
        /// the device.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        public static ConfigurableObjectState AddConfigurableObject<TDevice>(
            this IDeviceBuilder<TDevice> device,
            QualifiedName browseName,
            Action<ConfigurableObjectState>? configure = null)
            where TDevice : ComponentState
        {
            return AddTypedInstance(device, browseName,
                parent => new ConfigurableObjectState(parent), configure);
        }

        internal static T AddTypedInstance<TDevice, T>(
            IDeviceBuilder<TDevice> device,
            QualifiedName browseName,
            Func<NodeState, T> factory,
            Action<T>? configure)
            where TDevice : ComponentState
            where T : BaseObjectState
        {
            if (device == null) { throw new ArgumentNullException(nameof(device)); }
            if (browseName.IsNull)
            {
                throw new ArgumentException(
                    "browseName must be non-null.", nameof(browseName));
            }
            if (factory == null) { throw new ArgumentNullException(nameof(factory)); }

            T child = factory(device.Device);
            child.SymbolicName = browseName.Name ?? string.Empty;
            child.BrowseName = browseName;
            child.DisplayName = new LocalizedText(browseName.Name ?? string.Empty);
            child.NodeId = device.Context.NodeIdFactory.New(device.Context, child);
            child.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;

            device.Device.AddChild(child);

            device.Manager.AddPredefinedNodeAsync(child, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            configure?.Invoke(child);
            return child;
        }
    }
}
