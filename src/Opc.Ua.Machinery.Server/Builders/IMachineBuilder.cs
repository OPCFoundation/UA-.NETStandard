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
using Opc.Ua.Machinery.Server.Results;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Assembles one machine below the OPC 40001-1 <c>Machines</c> folder and
    /// the building blocks it publishes.
    /// </summary>
    /// <typeparam name="TState">
    /// The machine's state type — a plain <see cref="BaseObjectState"/>, or the
    /// state of a companion-specification type when the machine was adopted
    /// from one.
    /// </typeparam>
    /// <remarks>
    /// Nothing reaches the address space until
    /// <see cref="BuildAsync(CancellationToken)"/> runs; a failure anywhere in
    /// the chain leaves the server exactly as it was.
    /// </remarks>
    public interface IMachineBuilder<TState> : IMachineryNodeBuilder<TState>
        where TState : BaseObjectState
    {
        /// <summary>
        /// Adds the OPC 40001-1 <c>Identification</c> add-in, typed
        /// <c>MachineIdentificationType</c>.
        /// </summary>
        IMachineBuilder<TState> WithIdentification(
            Action<MachineryIdentificationData> configure);

        /// <summary>
        /// Adds the <c>Monitoring</c> add-in — the entry point for the item
        /// state machine, the operation mode, the stacklight, device health and
        /// the process and consumption folders.
        /// </summary>
        IMachineBuilder<TState> WithMonitoring(Action<IMonitoringBuilder> configure);

        /// <summary>
        /// Adds the <c>Components</c> add-in and the machine components below it.
        /// </summary>
        IMachineBuilder<TState> WithComponents(Action<IMachineComponentsBuilder> configure);

        /// <summary>
        /// Adds the <c>MachineryEquipment</c> folder and the equipment below it.
        /// </summary>
        IMachineBuilder<TState> WithMachineryEquipment(
            Action<IMachineryEquipmentBuilder> configure);

        /// <summary>
        /// Adds the <c>Notifications</c> add-in, the event notifier
        /// OPC 40001-1 machines publish their events on.
        /// </summary>
        /// <param name="configure">
        /// Optional configuration. Use it to register an event source with
        /// <see cref="IMachineryNotificationsBuilder.Publish{TEvent}"/>, or
        /// keep the returned handle and report events imperatively.
        /// </param>
        IMachineBuilder<TState> WithNotifications(
            Action<IMachineryNotificationsBuilder>? configure = null);

        /// <summary>
        /// Adds the <c>OperationCounters</c> functional group.
        /// </summary>
        IMachineBuilder<TState> WithOperationCounters(
            Action<IMachineryOperationCounterBuilder> configure);

        /// <summary>
        /// Adds the <c>LifetimeCounters</c> add-in and the lifetime variables
        /// below it.
        /// </summary>
        IMachineBuilder<TState> WithLifetimeCounters(
            Action<IMachineryLifetimeCounterBuilder> configure);

        /// <summary>
        /// Adds an OPC 40001-2 process value to the machine.
        /// </summary>
        /// <param name="browseName">The browse name of the process value.</param>
        /// <param name="configure">Configures the process value.</param>
        IMachineBuilder<TState> WithProcessValue(
            QualifiedName browseName,
            Action<IProcessValueBuilder> configure);

        /// <summary>
        /// Adds the OPC 40001-2 device object: an object carrying a
        /// <c>MachineryComponentIdentificationType</c> nameplate that
        /// implements OPC 30081's <c>ISignalSetType</c> and points at the
        /// machine's process values through its <c>SignalSet</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The unit exists so a client can find the nameplate of the device
        /// that produced a reading: the process values themselves carry no
        /// identification. The <c>SignalSet</c> references the process values
        /// rather than owning them — they keep hanging off the machine, where
        /// OPC 40001-2 puts them.
        /// </para>
        /// <para>
        /// Call it after the process values; every process value declared on
        /// the machine is linked when the machine is registered, so the order
        /// of the calls does not matter, but a process value added to the
        /// address space outside the builder is not seen.
        /// </para>
        /// </remarks>
        /// <param name="browseName">Browse name of the device object.</param>
        /// <param name="configureIdentification">
        /// Configures the device nameplate. <c>Manufacturer</c> and
        /// <c>SerialNumber</c> are mandatory, as on any machinery item.
        /// </param>
        IMachineBuilder<TState> WithProcessValueDevice(
            QualifiedName browseName,
            Action<MachineryIdentificationData> configureIdentification);

        /// <summary>
        /// Adds the OPC 40001-3 <c>JobManagement</c> object. The eleven job
        /// verbs come from the ISA-95 Job Control V2 model underneath and are
        /// bound to the registered provider, so this adds no verbs of its own.
        /// </summary>
        /// <param name="configure">Configures the job management object.</param>
        IMachineBuilder<TState> WithJobManagement(
            Action<IJobManagementBuilder>? configure = null);

        /// <summary>
        /// Adds OPC 40001-4 energy carriers to the machine.
        /// </summary>
        IMachineBuilder<TState> WithEnergy(Action<IMachineryEnergyBuilder> configure);

        /// <summary>
        /// Adds the OPC 40001-101 <c>ResultManagement</c> object, including the
        /// <c>ResultTransfer</c> download path.
        /// </summary>
        IMachineBuilder<TState> WithResultManagement(
            Action<IResultManagementBuilder> configure);

        /// <summary>
        /// Applies low-level state configuration before registration.
        /// </summary>
        new IMachineBuilder<TState> Configure(Action<TState, ISystemContext> configure);

        /// <summary>
        /// Registers the assembled machine with the node manager and returns
        /// the runtime handle.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        ValueTask<IMachineHandle<TState>> BuildAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The runtime surface of a registered machine.
    /// </summary>
    /// <typeparam name="TState">The machine's state type.</typeparam>
    public interface IMachineHandle<out TState>
        where TState : BaseObjectState
    {
        /// <summary>
        /// Gets the registered machine state.
        /// </summary>
        TState State { get; }

        /// <summary>
        /// Gets the machine's NodeId.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets the item-state controller, or <see langword="null"/> when the
        /// machine publishes no <c>MachineryItemState</c>.
        /// </summary>
        IMachineryItemStateController? ItemState { get; }

        /// <summary>
        /// Gets the operation-mode controller, or <see langword="null"/> when
        /// the machine publishes no <c>MachineryOperationMode</c>.
        /// </summary>
        IMachineryOperationModeController? OperationMode { get; }

        /// <summary>
        /// Gets the result publisher, or <see langword="null"/> when the
        /// machine publishes no <c>ResultManagement</c>.
        /// </summary>
        IMachineryResultPublisher? Results { get; }

        /// <summary>
        /// Gets the notification publisher, or <see langword="null"/> when the
        /// machine publishes no <c>Notifications</c> add-in.
        /// </summary>
        IMachineryNotificationPublisher? Notifications { get; }

        /// <summary>
        /// Resolves the registered machine through the shared fluent node
        /// builder.
        /// </summary>
        INodeBuilder<TState> AsNode();
    }
}
