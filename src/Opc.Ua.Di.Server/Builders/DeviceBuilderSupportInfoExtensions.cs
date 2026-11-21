// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Threading;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Typed materialisation helpers for OPC 10000-100 §5.15
    /// <c>ISupportInfoType</c> on top of an existing
    /// <see cref="IDeviceBuilder{TDevice}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="WithSupportInfo"/> helper creates an
    /// <see cref="ISupportInfoState"/> child under the device (idempotent
    /// — re-uses the existing one if present), and surfaces it through
    /// a <c>configure</c> callback for further customisation.
    /// </para>
    /// <para>
    /// The <see cref="ISupportInfoState"/> exposes typed
    /// <c>DocumentationFiles</c>, <c>ImageSet</c>, and
    /// <c>ProtocolSupport</c> folder properties — callers attach
    /// children directly via standard NodeState APIs. For file
    /// content backed by <c>IFileSystemProvider</c>, wire the
    /// resulting <see cref="FileState"/> children to a provider via
    /// the existing FileSystem-server API.
    /// </para>
    /// </remarks>
    public static class DeviceBuilderSupportInfoExtensions
    {
        /// <summary>
        /// Adds (or re-uses) an <see cref="ISupportInfoState"/> child
        /// under the device and invokes <paramref name="configure"/>
        /// with it. The interface is created on first call with
        /// browse-name <c>"SupportInfo"</c> in the device's namespace.
        /// </summary>
        /// <typeparam name="TDevice">Concrete device state type.</typeparam>
        /// <exception cref="ArgumentNullException"><paramref name="device"/> is <c>null</c>.</exception>
        public static IDeviceBuilder<TDevice> WithSupportInfo<TDevice>(
            this IDeviceBuilder<TDevice> device,
            Action<ISupportInfoState> configure)
            where TDevice : ComponentState
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            ISupportInfoState? existing = FindExistingSupportInfo(device.Device) ?? CreateAndRegisterSupportInfo(device);
            configure(existing);
            return device;
        }

        private static ISupportInfoState? FindExistingSupportInfo(ComponentState parent)
        {
            var children = new System.Collections.Generic.List<BaseInstanceState>();
            parent.GetChildren(null!, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is ISupportInfoState info)
                {
                    return info;
                }
            }
            return null;
        }

        private static ISupportInfoState CreateAndRegisterSupportInfo<TDevice>(
            IDeviceBuilder<TDevice> device)
            where TDevice : ComponentState
        {
            ushort nsIndex = device.Device.BrowseName.IsNull
                ? (ushort)0
                : device.Device.BrowseName.NamespaceIndex;
            var info = new ISupportInfoState(device.Device)
            {
                SymbolicName = "SupportInfo",
                BrowseName = new QualifiedName("SupportInfo", nsIndex),
                DisplayName = new LocalizedText("SupportInfo"),
                ReferenceTypeId = Types.ReferenceTypeIds.HasInterface
            };
            info.NodeId = device.Context.RequireNodeIdFactory().New(device.Context, info);
            device.Device.AddChild(info);
            device.Manager.AddPredefinedNodeAsync(info, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            return info;
        }
    }
}
