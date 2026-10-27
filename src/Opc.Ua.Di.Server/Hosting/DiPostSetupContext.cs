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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Di.Server.Hosting
{
    /// <summary>
    /// Default <see cref="IDiPostSetupContext"/> implementation. Created
    /// per-manager-run by <see cref="DiPostSetupRunner"/>.
    /// </summary>
    internal sealed class DiPostSetupContext : IDiPostSetupContext
    {
        private readonly IServiceProvider m_services;

        /// <summary>
        /// Creates the context for one owner's post-setup run.
        /// </summary>
        internal DiPostSetupContext(
            DiNodeManager manager,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Gets the DI owner being configured.
        /// </summary>
        public DiNodeManager Manager { get; }

        /// <summary>
        /// Gets the owner's setup cancellation token.
        /// </summary>
        public CancellationToken CancellationToken { get; }

        /// <summary>
        /// Resolves a required service, throwing when it is not registered.
        /// </summary>
        /// <typeparam name="T">The required service type.</typeparam>
        public T GetRequiredService<T>() where T : notnull
        {
            object? svc = m_services.GetService(typeof(T)) ??
                throw new InvalidOperationException(
                    $"No service for type '{typeof(T).FullName}' has been registered.");
            return (T)svc;
        }

        /// <summary>
        /// Resolves an optional service from the owner's service provider.
        /// </summary>
        /// <typeparam name="T">The optional service type.</typeparam>
        public T? GetService<T>() where T : class
        {
            return m_services.GetService(typeof(T)) as T;
        }

        /// <summary>
        /// Starts a DI DeviceType instance using the setup cancellation token.
        /// </summary>
        public ValueTask<IDeviceBuilder<DeviceState>> CreateDeviceAsync(
            QualifiedName browseName,
            NodeState? parent = null)
        {
            return Manager.CreateDeviceAsync(browseName, parent, CancellationToken);
        }

        /// <summary>
        /// Starts a typed DI component using the setup cancellation token.
        /// </summary>
        /// <typeparam name="TDevice">The DI component state type.</typeparam>
        public ValueTask<IDeviceBuilder<TDevice>> CreateDeviceAsync<TDevice>(
            QualifiedName browseName,
            NodeId typeDefinitionId,
            Func<NodeState, TDevice> factory,
            NodeState? parent = null)
            where TDevice : ComponentState
        {
            return Manager.CreateDeviceAsync(
                        browseName, typeDefinitionId, factory, parent, CancellationToken);
        }

        /// <summary>
        /// Opens a device builder by its node identifier.
        /// </summary>
        /// <typeparam name="TDevice">The device state type.</typeparam>
        public IDeviceBuilder<TDevice> Device<TDevice>(NodeId nodeId)
            where TDevice : ComponentState
        {
            return Manager.Device<TDevice>(nodeId);
        }

        /// <summary>
        /// Opens a device builder by its BrowseName beneath the specified parent.
        /// </summary>
        /// <typeparam name="TDevice">The device state type.</typeparam>
        public IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(
            QualifiedName browseName,
            NodeState? parent = null)
            where TDevice : ComponentState
        {
            return Manager.DeviceByBrowseName<TDevice>(browseName, parent);
        }

        /// <summary>
        /// Opens a topology element builder by its node identifier.
        /// </summary>
        /// <typeparam name="TElement">The topology element state type.</typeparam>
        public ITopologyElementBuilder<TElement> TopologyElement<TElement>(
            NodeId nodeId)
            where TElement : TopologyElementState
        {
            return Manager.TopologyElement<TElement>(nodeId);
        }

        /// <summary>
        /// Opens a topology element builder by its BrowseName beneath the specified parent.
        /// </summary>
        /// <typeparam name="TElement">The topology element state type.</typeparam>
        public ITopologyElementBuilder<TElement> TopologyElementByBrowseName<TElement>(
            QualifiedName browseName,
            NodeState? parent = null)
            where TElement : TopologyElementState
        {
            return Manager.TopologyElementByBrowseName<TElement>(browseName, parent);
        }
    }
}
