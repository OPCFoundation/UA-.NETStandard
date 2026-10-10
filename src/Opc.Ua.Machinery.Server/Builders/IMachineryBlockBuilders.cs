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
using Opc.Ua.Di;
using Opc.Ua.Machinery.Server.StateMachines;
using Opc.Ua.IA;

namespace Opc.Ua.Machinery.Server.Builders
{
    /// <summary>
    /// Assembles the OPC 40001-1 <c>Monitoring</c> add-in.
    /// </summary>
    /// <remarks>
    /// Every child of <c>MonitoringType</c> is optional, so nothing appears
    /// that was not asked for, and the server advertises only the conformance
    /// units whose structure it actually built.
    /// </remarks>
    public interface IMonitoringBuilder
    {
        /// <summary>
        /// Gets the monitoring state being configured.
        /// </summary>
        MonitoringState State { get; }

        /// <summary>
        /// Adds the <c>Status/MachineryItemState</c> state machine.
        /// </summary>
        /// <param name="initialState">The state the machine starts in.</param>
        IMonitoringBuilder WithMachineryItemState(
            MachineryItemStateValue initialState = MachineryItemStateValue.NotAvailable);

        /// <summary>
        /// Adds the <c>Status/MachineryOperationMode</c> state machine.
        /// </summary>
        /// <param name="initialMode">The mode the machine starts in.</param>
        IMonitoringBuilder WithOperationMode(
            MachineryOperationModeValue initialMode = MachineryOperationModeValue.None);

        /// <summary>
        /// Adds the <c>Status/Stacklight</c> object. The type comes from
        /// OPC 10000-200 Industrial Automation — the single edge that makes IA
        /// a dependency of the machine model.
        /// </summary>
        /// <param name="configure">Optional configuration of the stacklight.</param>
        IMonitoringBuilder WithStacklight(Action<BasicStacklightState>? configure = null);

        /// <summary>
        /// Adds the <c>Health</c> folder together with the Device Integration
        /// <c>DeviceHealth</c> variable and the <c>DeviceHealthAlarms</c>
        /// folder it declares.
        /// </summary>
        /// <remarks>
        /// Both children are optional in the model, so the generated instance
        /// factory does not create them; a bare folder would leave a Machinery
        /// client with nothing to read, because DI's <c>DeviceHealth</c> is the
        /// anchor it looks for.
        /// </remarks>
        /// <param name="configure">Optional configuration of the health block.</param>
        IMonitoringBuilder WithHealth(Action<IMachineryHealthBuilder>? configure = null);

        /// <summary>
        /// Adds the <c>Process</c> folder, the entry point for OPC 40001-2
        /// process values that describe the running process.
        /// </summary>
        IMonitoringBuilder WithProcess(Action<FolderState>? configure = null);

        /// <summary>
        /// Adds the <c>Consumption</c> folder, the entry point for OPC 40001-4
        /// energy consumption.
        /// </summary>
        IMonitoringBuilder WithConsumption(Action<FolderState>? configure = null);
    }

    /// <summary>
    /// Assembles the <c>Monitoring/Health</c> block of a machinery item.
    /// </summary>
    /// <remarks>
    /// OPC 40001-1 leaves health to Device Integration: the folder carries the
    /// DI <c>IDeviceHealthType</c> interface, the <c>DeviceHealth</c>
    /// enumeration and the <c>DeviceHealthAlarms</c> folder that holds the
    /// conditions derived from it.
    /// </remarks>
    public interface IMachineryHealthBuilder
    {
        /// <summary>
        /// Gets the <c>Health</c> folder being configured.
        /// </summary>
        FolderState State { get; }

        /// <summary>
        /// Gets the DI <c>DeviceHealth</c> variable. Write
        /// <see cref="BaseVariableState.WrappedValue"/> and call
        /// <see cref="NodeState.ClearChangeMasks(ISystemContext, bool)"/> to
        /// publish a new health state at runtime.
        /// </summary>
        BaseDataVariableState<DeviceHealthEnumeration> DeviceHealth { get; }

        /// <summary>
        /// Gets the DI <c>DeviceHealthAlarms</c> folder, the place the
        /// conditions derived from the health state are published in.
        /// </summary>
        FolderState DeviceHealthAlarms { get; }

        /// <summary>
        /// Sets the initial device health.
        /// </summary>
        /// <param name="deviceHealth">The health state to publish.</param>
        IMachineryHealthBuilder WithDeviceHealth(DeviceHealthEnumeration deviceHealth);
    }

    /// <summary>
    /// Assembles the OPC 40001-1 <c>Components</c> add-in.
    /// </summary>
    public interface IMachineComponentsBuilder
    {
        /// <summary>
        /// Gets the components state being configured.
        /// </summary>
        MachineComponentsState State { get; }

        /// <summary>
        /// Adds one machine component.
        /// </summary>
        /// <param name="browseName">The browse name of the component.</param>
        /// <param name="configure">Configures the component.</param>
        IMachineComponentsBuilder AddComponent(
            QualifiedName browseName,
            Action<IMachineComponentBuilder> configure);
    }

    /// <summary>
    /// Assembles one machine component.
    /// </summary>
    public interface IMachineComponentBuilder
    {
        /// <summary>
        /// Gets the component state being configured.
        /// </summary>
        BaseObjectState State { get; }

        /// <summary>
        /// Configures the component's mandatory <c>Identification</c> add-in,
        /// typed <c>MachineryComponentIdentificationType</c>.
        /// </summary>
        IMachineComponentBuilder WithIdentification(
            Action<MachineryIdentificationData> configure);

        /// <summary>
        /// Adds a <c>Monitoring</c> add-in to the component. A component is a
        /// machinery item in its own right and may carry its own state machine.
        /// </summary>
        IMachineComponentBuilder WithMonitoring(Action<IMonitoringBuilder> configure);

        /// <summary>
        /// Adds the component's <c>OperationCounters</c> functional group.
        /// </summary>
        IMachineComponentBuilder WithOperationCounters(
            Action<IMachineryOperationCounterBuilder> configure);

        /// <summary>
        /// Adds the component's <c>LifetimeCounters</c> add-in.
        /// </summary>
        IMachineComponentBuilder WithLifetimeCounters(
            Action<IMachineryLifetimeCounterBuilder> configure);
    }

    /// <summary>
    /// Assembles the OPC 40001-1 <c>MachineryEquipment</c> folder.
    /// </summary>
    public interface IMachineryEquipmentBuilder
    {
        /// <summary>
        /// Gets the equipment folder being configured.
        /// </summary>
        MachineryEquipmentFolderState State { get; }

        /// <summary>
        /// Adds one piece of machinery equipment.
        /// </summary>
        /// <param name="browseName">The browse name of the equipment.</param>
        /// <param name="equipmentTypeId">
        /// The mandatory <c>MachineryEquipmentTypeId</c> — the semantic
        /// identifier of the equipment kind.
        /// </param>
        /// <param name="configure">Optional further configuration.</param>
        IMachineryEquipmentBuilder AddEquipment(
            QualifiedName browseName,
            string equipmentTypeId,
            Action<IMachineryEquipmentItemBuilder>? configure = null);
    }

    /// <summary>
    /// Assembles one piece of machinery equipment.
    /// </summary>
    public interface IMachineryEquipmentItemBuilder
    {
        /// <summary>
        /// Gets the equipment state being configured.
        /// </summary>
        BaseObjectState State { get; }

        /// <summary>
        /// Writes the equipment's description.
        /// </summary>
        IMachineryEquipmentItemBuilder WithDescription(LocalizedText description);

        /// <summary>
        /// Writes the vendor nameplate properties the equipment inherits from
        /// the Device Integration <c>IVendorNameplateType</c> interface.
        /// </summary>
        IMachineryEquipmentItemBuilder WithIdentification(
            Action<MachineryIdentificationData> configure);

        /// <summary>
        /// Adds the optional <c>EquipmentLife</c> variable — the lifetime
        /// indication <c>IMachineryEquipmentType</c> declares, typed with the
        /// Device Integration <c>LifetimeVariableType</c>.
        /// </summary>
        /// <remarks>
        /// The equipment placeholder carries <c>IMachineryEquipmentType</c> as
        /// an interface, and an interface's optional members are never
        /// materialised by the placeholder factory, so the variable and its
        /// mandatory <c>StartValue</c> / <c>LimitValue</c> properties are built
        /// here.
        /// </remarks>
        /// <param name="remaining">The remaining life.</param>
        /// <param name="startValue">
        /// The value the counter starts from, i.e. full life.
        /// </param>
        /// <param name="limitValue">
        /// The value at which the end of life is reached. Defaults to zero,
        /// the usual convention for a counter that runs down.
        /// </param>
        /// <param name="engineeringUnits">Optional engineering unit.</param>
        /// <param name="warningValues">Optional warning thresholds.</param>
        IMachineryEquipmentItemBuilder WithEquipmentLife(
            double remaining,
            double startValue,
            double limitValue = 0,
            EUInformation? engineeringUnits = null,
            ArrayOf<double> warningValues = default);
    }

    /// <summary>
    /// Assembles the OPC 40001-1 <c>OperationCounters</c> functional group.
    /// </summary>
    public interface IMachineryOperationCounterBuilder
    {
        /// <summary>
        /// Gets the counter state being configured.
        /// </summary>
        MachineryOperationCounterState State { get; }

        /// <summary>
        /// Adds <c>PowerOnDuration</c>, in hours.
        /// </summary>
        IMachineryOperationCounterBuilder WithPowerOnDuration(double hours);

        /// <summary>
        /// Adds <c>OperationDuration</c>, in hours.
        /// </summary>
        IMachineryOperationCounterBuilder WithOperationDuration(double hours);

        /// <summary>
        /// Adds <c>OperationCycleCounter</c>.
        /// </summary>
        IMachineryOperationCounterBuilder WithOperationCycleCounter(Variant value);
    }

    /// <summary>
    /// Assembles the OPC 40001-1 <c>LifetimeCounters</c> add-in.
    /// </summary>
    public interface IMachineryLifetimeCounterBuilder
    {
        /// <summary>
        /// Gets the counter state being configured.
        /// </summary>
        MachineryLifetimeCounterState State { get; }

        /// <summary>
        /// Adds one lifetime variable. The Device Integration
        /// <c>LifetimeVariableType</c> carries the remaining life and the
        /// indication that classifies it.
        /// </summary>
        /// <param name="browseName">The browse name of the variable.</param>
        /// <param name="startValue">The value the counter starts from.</param>
        /// <param name="remaining">The remaining life.</param>
        /// <param name="warningValues">Optional warning thresholds.</param>
        /// <param name="limitValue">
        /// The value at which the end of life is reached. Mandatory on
        /// <c>LifetimeVariableType</c> and therefore always published;
        /// defaults to zero.
        /// </param>
        IMachineryLifetimeCounterBuilder AddLifetimeVariable(
            QualifiedName browseName,
            double startValue,
            double remaining,
            ArrayOf<double> warningValues = default,
            double limitValue = 0);
    }
}
