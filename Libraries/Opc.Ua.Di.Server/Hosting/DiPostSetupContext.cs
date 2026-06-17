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

        internal DiPostSetupContext(
            DiNodeManager manager,
            IServiceProvider services,
            CancellationToken cancellationToken)
        {
            Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            m_services = services ?? throw new ArgumentNullException(nameof(services));
            CancellationToken = cancellationToken;
        }

        public DiNodeManager Manager { get; }

        public CancellationToken CancellationToken { get; }

        public T GetRequiredService<T>() where T : notnull
        {
            object? svc = m_services.GetService(typeof(T)) ??
                throw new InvalidOperationException(
                    $"No service for type '{typeof(T).FullName}' has been registered.");
            return (T)svc;
        }

        public ValueTask<IDeviceBuilder<DeviceState>> CreateDeviceAsync(
            QualifiedName browseName,
            NodeState? parent = null)
        {
            return Manager.CreateDeviceAsync(browseName, parent, CancellationToken);
        }

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

        public IDeviceBuilder<TDevice> Device<TDevice>(NodeId nodeId)
            where TDevice : ComponentState
        {
            return Manager.Device<TDevice>(nodeId);
        }

        public IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(
            QualifiedName browseName,
            NodeState? parent = null)
            where TDevice : ComponentState
        {
            return Manager.DeviceByBrowseName<TDevice>(browseName, parent);
        }
    }
}
