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
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Server;

namespace Opc.Ua.PADIM.Server
{
    /// <summary>
    /// Composes source-generated PA-DIM devices with the shared DI hosting infrastructure.
    /// </summary>
    public static class PadimServerExtensions
    {
        /// <summary>
        /// Creates any standard device or analyser family using its compiled model factory.
        /// </summary>
        public static ValueTask<IDeviceBuilder<PADIMState>> CreatePadimDeviceAsync(
            this DiNodeManager manager,
            QualifiedName browseName,
            PadimDeviceKind kind,
            NodeState? parent = null,
            CancellationToken cancellationToken = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            return manager.CreateDeviceAsync(
                browseName,
                ExpandedNodeId.ToNodeId(PadimFactories.DeviceType(kind), manager.SystemContext.NamespaceUris),
                owner => PadimFactories.CreateDevice(manager.SystemContext, owner, browseName, kind),
                parent,
                cancellationToken);
        }

        /// <summary>
        /// Creates a base PA-DIM device with generated mandatory nameplate and health members.
        /// </summary>
        public static ValueTask<IDeviceBuilder<PADIMState>> CreatePadimDeviceAsync(
            this DiNodeManager manager,
            QualifiedName browseName,
            NodeState? parent = null,
            CancellationToken cancellationToken = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            return manager.CreatePadimDeviceAsync(
                browseName,
                ObjectTypeIds.PADIMType,
                static (context, owner, name) => context.CreateInstanceOfPADIMType(owner, name),
                parent,
                cancellationToken);
        }

        /// <summary>
        /// Creates an analyser or vendor subtype using its generated, AOT-compatible instance factory.
        /// </summary>
        /// <typeparam name="TDevice">The generated device state type.</typeparam>
        public static ValueTask<IDeviceBuilder<TDevice>> CreatePadimDeviceAsync<TDevice>(
            this DiNodeManager manager,
            QualifiedName browseName,
            ExpandedNodeId typeId,
            Func<ISystemContext, NodeState, QualifiedName, TDevice> factory,
            NodeState? parent = null,
            CancellationToken cancellationToken = default)
            where TDevice : ComponentState
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }
            ServerSystemContext context = manager.SystemContext;
            NodeId typeDefinition = ExpandedNodeId.ToNodeId(typeId, context.NamespaceUris);
            NodeId padimType = ExpandedNodeId.ToNodeId(ObjectTypeIds.PADIMType, context.NamespaceUris);
            if (!context.TypeTable.IsTypeOf(typeDefinition, padimType))
            {
                throw new ArgumentException("The selected type must derive from PADIMType.", nameof(typeId));
            }
            return manager.CreateDeviceAsync(
                browseName,
                typeDefinition,
                owner => factory(context, owner, browseName),
                parent,
                cancellationToken);
        }

        /// <summary>
        /// Binds an existing generated device without requiring a standalone server or Machinery.
        /// </summary>
        /// <typeparam name="TDevice">The generated device state type.</typeparam>
        public static PadimDevice BindPadim<TDevice>(this IDeviceBuilder<TDevice> builder)
            where TDevice : ComponentState
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (PadimDevice.TryGetHandle(builder.Device, out PadimDevice? existing))
            {
                return existing;
            }
            return new PadimDevice(builder.Manager, builder.Device);
        }
    }
}
