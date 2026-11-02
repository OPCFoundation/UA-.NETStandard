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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Builds the nodes of one device before it is published.
    /// </summary>
    internal sealed class LadsDeviceBuilder : ILadsDeviceBuilder
    {
        /// <summary>
        /// Creates the builder.
        /// </summary>
        public LadsDeviceBuilder(LadsDevice device)
        {
            Device = device;
        }

        /// <summary>
        /// Gets the device.
        /// </summary>
        public LadsDevice Device { get; }

        /// <inheritdoc/>
        public string Name => Device.Name;

        /// <inheritdoc/>
        public ILadsDeviceBuilder Identification(Action<LadsIdentification> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var identification = new LadsIdentification();
            configure(identification);
            LadsIdentificationWriter.Apply(
                Device.Runtime.Context,
                Device.Node,
                Device.Node.Identification,
                identification);
            return this;
        }

        /// <inheritdoc/>
        public ILadsDeviceBuilder Location(string? operationalLocation, string? hierarchicalLocation = null)
        {
            ISystemContext context = Device.Runtime.Context;
            LADSDeviceState node = Device.Node;
            if (operationalLocation != null)
            {
                if (node.OperationalLocation == null)
                {
                    node.AddOperationalLocation(context);
                }
                node.OperationalLocation!.Value = operationalLocation;
            }
            if (hierarchicalLocation != null)
            {
                if (node.HierarchicalLocation == null)
                {
                    node.AddHierarchicalLocation(context);
                }
                node.HierarchicalLocation!.Value = hierarchicalLocation;
            }
            return this;
        }

        /// <inheritdoc/>
        public ILadsDeviceBuilder InitialState(
            LadsDeviceStateValue state,
            MachineryOperationModeValue operationMode = MachineryOperationModeValue.Processing)
        {
            Device.InitialDeviceState = state;
            Device.InitialOperationMode = operationMode;
            return this;
        }

        /// <inheritdoc/>
        public ILadsDeviceBuilder FunctionalUnit(string name, Action<ILadsFunctionalUnitBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            LadsFunctionalUnit unit = Device.AddFunctionalUnit(name);
            configure(new LadsFunctionalUnitBuilder(unit));
            return this;
        }

        /// <inheritdoc/>
        public ILadsDeviceBuilder MaintenanceTask(string name, Action<LadsMaintenanceTaskOptions>? configure = null)
        {
            var options = new LadsMaintenanceTaskOptions();
            configure?.Invoke(options);
            Device.AddMaintenanceTask(name, options);
            return this;
        }

        /// <inheritdoc/>
        public ILadsDeviceBuilder Component(string name, Action<LadsIdentification>? configure = null)
        {
            var identification = new LadsIdentification();
            configure?.Invoke(identification);
            Device.AddComponent(name, identification);
            return this;
        }

        /// <inheritdoc/>
        public ILadsDeviceBuilder Provider(ILadsDeviceProvider provider)
        {
            Device.SetProvider(provider);
            return this;
        }
    }

    /// <summary>
    /// The setup context of one <c>ConfigureLads</c> callback: builds devices and publishes them.
    /// </summary>
    internal sealed class LadsSetupContext : ILadsSetupContext
    {
        /// <summary>
        /// Creates the context.
        /// </summary>
        public LadsSetupContext(LadsRuntime runtime, IMachineryBuildContext buildContext)
        {
            Runtime = runtime;
            BuildContext = buildContext;
        }

        /// <summary>
        /// Gets the runtime.
        /// </summary>
        public LadsRuntime Runtime { get; }

        /// <inheritdoc/>
        public IMachineryBuildContext BuildContext { get; }

        /// <inheritdoc/>
        public ArrayOf<ILadsDevice> Devices
        {
            get
            {
                lock (m_lock)
                {
                    var devices = new ILadsDevice[m_devices.Count];
                    for (int ii = 0; ii < devices.Length; ii++)
                    {
                        devices[ii] = m_devices[ii];
                    }
                    return devices;
                }
            }
        }

        /// <inheritdoc/>
        public ILadsDevice AddDevice(string name, Action<ILadsDeviceBuilder> configure)
        {
            LadsDevice device = Build(name, configure);
            lock (m_lock)
            {
                m_devices.Add(device);
                m_pending.Add(device);
            }
            return device;
        }

        /// <inheritdoc/>
        public async ValueTask<ILadsDevice> AddDeviceAsync(
            string name,
            Action<ILadsDeviceBuilder> configure,
            CancellationToken cancellationToken = default)
        {
            LadsDevice device = Build(name, configure);
            try
            {
                await Runtime.RegisterDeviceAsync(device, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Runtime.ReleaseDeviceName(device.Name);
            }
            lock (m_lock)
            {
                m_devices.Add(device);
            }
            return device;
        }

        /// <summary>
        /// Publishes the devices added with <see cref="AddDevice"/>.
        /// </summary>
        public async ValueTask CompleteAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                LadsDevice device;
                lock (m_lock)
                {
                    if (m_pending.Count == 0)
                    {
                        return;
                    }
                    device = m_pending[0];
                    m_pending.RemoveAt(0);
                }
                try
                {
                    await Runtime.RegisterDeviceAsync(device, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    lock (m_lock)
                    {
                        m_devices.Remove(device);
                        foreach (LadsDevice pending in m_pending)
                        {
                            Runtime.ReleaseDeviceName(pending.Name);
                            m_devices.Remove(pending);
                        }
                        m_pending.Clear();
                    }
                    throw;
                }
                finally
                {
                    Runtime.ReleaseDeviceName(device.Name);
                }
            }
        }

        /// <summary>
        /// Reserves the name and builds the nodes of a device.
        /// </summary>
        private LadsDevice Build(string name, Action<ILadsDeviceBuilder> configure)
        {
            LadsRuntime.ValidateName(name, nameof(name));
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            if (!Runtime.TryReserveDeviceName(name))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameDuplicated,
                    "A device named '{0}' already exists.",
                    name);
            }
            try
            {
                var device = new LadsDevice(Runtime, name);
                configure(new LadsDeviceBuilder(device));
                return device;
            }
            catch
            {
                Runtime.ReleaseDeviceName(name);
                throw;
            }
        }

        private readonly Lock m_lock = new();
        private readonly List<LadsDevice> m_devices = [];
        private readonly List<LadsDevice> m_pending = [];
    }

    /// <summary>
    /// The registry of the published devices of a server.
    /// </summary>
    internal sealed class LadsDeviceRegistry : ILadsDeviceRegistry
    {
        /// <inheritdoc/>
        public ArrayOf<ILadsDevice> Devices
        {
            get
            {
                lock (m_lock)
                {
                    var devices = new List<ILadsDevice>(m_devices.Count);
                    foreach (LadsDevice device in m_devices.Values)
                    {
                        devices.Add(device);
                    }
                    return devices.ToArray();
                }
            }
        }

        /// <inheritdoc/>
        public event Action<ILadsDevice>? DeviceAdded;

        /// <inheritdoc/>
        public bool TryGetDevice(
            string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ILadsDevice? device)
        {
            lock (m_lock)
            {
                if (name != null && m_devices.TryGetValue(name, out LadsDevice? found))
                {
                    device = found;
                    return true;
                }
            }
            device = null;
            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<ILadsDevice> WaitForDeviceAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }
            TaskCompletionSource<ILadsDevice> waiter;
            lock (m_lock)
            {
                if (m_devices.TryGetValue(name, out LadsDevice? found))
                {
                    return found;
                }
                waiter = new TaskCompletionSource<ILadsDevice>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!m_waiters.TryGetValue(name, out List<TaskCompletionSource<ILadsDevice>>? waiters))
                {
                    waiters = [];
                    m_waiters.Add(name, waiters);
                }
                waiters.Add(waiter);
            }
            using (cancellationToken.Register(() => Cancel(name, waiter, cancellationToken)))
            {
                return await waiter.Task.ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Adds a published device and completes the waiters for it.
        /// </summary>
        public void Add(LadsDevice device)
        {
            List<TaskCompletionSource<ILadsDevice>>? waiters;
            lock (m_lock)
            {
                m_devices[device.Name] = device;
                if (m_waiters.TryGetValue(device.Name, out waiters))
                {
                    m_waiters.Remove(device.Name);
                }
            }
            if (waiters != null)
            {
                foreach (TaskCompletionSource<ILadsDevice> waiter in waiters)
                {
                    waiter.TrySetResult(device);
                }
            }
            DeviceAdded?.Invoke(device);
        }

        /// <summary>
        /// Removes a device that is no longer published.
        /// </summary>
        public void Remove(LadsDevice device)
        {
            lock (m_lock)
            {
                if (m_devices.TryGetValue(device.Name, out LadsDevice? existing) && ReferenceEquals(existing, device))
                {
                    m_devices.Remove(device.Name);
                }
            }
        }

        /// <summary>
        /// Cancels a waiter.
        /// </summary>
        private void Cancel(string name, TaskCompletionSource<ILadsDevice> waiter, CancellationToken cancellationToken)
        {
            lock (m_lock)
            {
                if (m_waiters.TryGetValue(name, out List<TaskCompletionSource<ILadsDevice>>? waiters))
                {
                    waiters.Remove(waiter);
                    if (waiters.Count == 0)
                    {
                        m_waiters.Remove(name);
                    }
                }
            }
            waiter.TrySetCanceled(cancellationToken);
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, LadsDevice> m_devices = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TaskCompletionSource<ILadsDevice>>> m_waiters =
            new(StringComparer.Ordinal);
    }
}
