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
            info.NodeId = device.Context.NodeIdFactory.New(device.Context, info);
            device.Device.AddChild(info);
            device.Manager.AddPredefinedNodeAsync(info, CancellationToken.None)
                .AsTask().GetAwaiter().GetResult();
            return info;
        }
    }
}
