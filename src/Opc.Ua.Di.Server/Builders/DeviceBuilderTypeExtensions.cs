// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            this IDeviceBuilder<TDevice> device,
            QualifiedName browseName,
            Func<NodeState, T> factory,
            Action<T>? configure)
            where TDevice : ComponentState
            where T : BaseObjectState
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (browseName.IsNull)
            {
                throw new ArgumentException(
                    "browseName must be non-null.", nameof(browseName));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            T child = factory(device.Device);
            child.SymbolicName = browseName.Name ?? string.Empty;
            child.BrowseName = browseName;
            child.DisplayName = new LocalizedText(browseName.Name ?? string.Empty);
            child.NodeId = device.Context.RequireNodeIdFactory().New(device.Context, child);
            child.ReferenceTypeId = Types.ReferenceTypeIds.HasComponent;

            device.Device.AddChild(child);

            device.Manager.AddPredefinedNodeAsync(child, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();

            configure?.Invoke(child);
            return child;
        }
    }
}
