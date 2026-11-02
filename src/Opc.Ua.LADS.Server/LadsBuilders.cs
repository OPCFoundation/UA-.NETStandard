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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Adds LADS devices to the address space of a Machinery node manager.
    /// </summary>
    public interface ILadsSetupContext
    {
        /// <summary>
        /// Gets the Machinery build context of the node manager.
        /// </summary>
        IMachineryBuildContext BuildContext { get; }

        /// <summary>
        /// Gets the devices added through this context.
        /// </summary>
        ArrayOf<ILadsDevice> Devices { get; }

        /// <summary>
        /// Builds a device and queues its publication. The device is published before the configuration
        /// callback completes.
        /// </summary>
        /// <param name="name">The browse name of the device.</param>
        /// <param name="configure">Configures the device.</param>
        /// <returns>The handle of the device.</returns>
        ILadsDevice AddDevice(string name, Action<ILadsDeviceBuilder> configure);

        /// <summary>
        /// Builds and publishes a device.
        /// </summary>
        /// <param name="name">The browse name of the device.</param>
        /// <param name="configure">Configures the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The handle of the published device.</returns>
        ValueTask<ILadsDevice> AddDeviceAsync(
            string name,
            Action<ILadsDeviceBuilder> configure,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Gives access to the published LADS devices of a server.
    /// </summary>
    public interface ILadsDeviceRegistry
    {
        /// <summary>
        /// Gets the published devices.
        /// </summary>
        ArrayOf<ILadsDevice> Devices { get; }

        /// <summary>
        /// Raised after a device was published.
        /// </summary>
        event Action<ILadsDevice>? DeviceAdded;

        /// <summary>
        /// Finds a published device by its name.
        /// </summary>
        /// <param name="name">The name of the device.</param>
        /// <param name="device">The device.</param>
        /// <returns><see langword="true"/> when the device was found.</returns>
        bool TryGetDevice(string name, [NotNullWhen(true)] out ILadsDevice? device);

        /// <summary>
        /// Waits until a device with the name is published.
        /// </summary>
        /// <param name="name">The name of the device.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The device.</returns>
        ValueTask<ILadsDevice> WaitForDeviceAsync(string name, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Builds a LADS device.
    /// </summary>
    public interface ILadsDeviceBuilder
    {
        /// <summary>
        /// Gets the name of the device.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Sets the identification of the device.
        /// </summary>
        /// <param name="configure">Configures the identification.</param>
        ILadsDeviceBuilder Identification(Action<LadsIdentification> configure);

        /// <summary>
        /// Sets the operational and the hierarchical location of the device.
        /// </summary>
        /// <param name="operationalLocation">The operational location.</param>
        /// <param name="hierarchicalLocation">The hierarchical location.</param>
        ILadsDeviceBuilder Location(string? operationalLocation, string? hierarchicalLocation = null);

        /// <summary>
        /// Sets the device state and the operation mode after the device was published.
        /// </summary>
        /// <param name="state">The device state.</param>
        /// <param name="operationMode">The operation mode.</param>
        ILadsDeviceBuilder InitialState(
            LadsDeviceStateValue state,
            MachineryOperationModeValue operationMode = MachineryOperationModeValue.Processing);

        /// <summary>
        /// Adds a functional unit.
        /// </summary>
        /// <param name="name">The browse name of the functional unit.</param>
        /// <param name="configure">Configures the functional unit.</param>
        ILadsDeviceBuilder FunctionalUnit(string name, Action<ILadsFunctionalUnitBuilder> configure);

        /// <summary>
        /// Adds a maintenance task.
        /// </summary>
        /// <param name="name">The browse name of the task.</param>
        /// <param name="configure">Configures the task.</param>
        ILadsDeviceBuilder MaintenanceTask(string name, Action<LadsMaintenanceTaskOptions>? configure = null);

        /// <summary>
        /// Adds a component of the device.
        /// </summary>
        /// <param name="name">The browse name of the component.</param>
        /// <param name="configure">Configures the identification of the component.</param>
        ILadsDeviceBuilder Component(string name, Action<LadsIdentification>? configure = null);

        /// <summary>
        /// Replaces the device provider of this device.
        /// </summary>
        /// <param name="provider">The provider.</param>
        ILadsDeviceBuilder Provider(ILadsDeviceProvider provider);
    }

    /// <summary>
    /// Adds functions to a function set.
    /// </summary>
    /// <typeparam name="TBuilder">The type of the builder returned for chaining.</typeparam>
    public interface ILadsFunctionSetBuilder<TBuilder>
        where TBuilder : ILadsFunctionSetBuilder<TBuilder>
    {
        /// <summary>
        /// Adds an analog control function.
        /// </summary>
        TBuilder AnalogControl(string name, Action<LadsAnalogControlOptions>? configure = null);

        /// <summary>
        /// Adds an analog control function and returns its handle.
        /// </summary>
        TBuilder AnalogControl(
            string name,
            Action<LadsAnalogControlOptions>? configure,
            out IAnalogControlFunction function);

        /// <summary>
        /// Adds a multi-mode analog control function.
        /// </summary>
        TBuilder MultiModeAnalogControl(string name, Action<LadsMultiModeAnalogControlOptions> configure);

        /// <summary>
        /// Adds a multi-mode analog control function and returns its handle.
        /// </summary>
        TBuilder MultiModeAnalogControl(
            string name,
            Action<LadsMultiModeAnalogControlOptions> configure,
            out IMultiModeAnalogControlFunction function);

        /// <summary>
        /// Adds a two-state discrete control function.
        /// </summary>
        TBuilder TwoStateDiscreteControl(string name, Action<LadsTwoStateDiscreteOptions>? configure = null);

        /// <summary>
        /// Adds a two-state discrete control function and returns its handle.
        /// </summary>
        TBuilder TwoStateDiscreteControl(
            string name,
            Action<LadsTwoStateDiscreteOptions>? configure,
            out ITwoStateDiscreteControlFunction function);

        /// <summary>
        /// Adds a multi-state discrete control function.
        /// </summary>
        TBuilder MultiStateDiscreteControl(string name, Action<LadsMultiStateDiscreteOptions> configure);

        /// <summary>
        /// Adds a multi-state discrete control function and returns its handle.
        /// </summary>
        TBuilder MultiStateDiscreteControl(
            string name,
            Action<LadsMultiStateDiscreteOptions> configure,
            out IMultiStateDiscreteControlFunction function);

        /// <summary>
        /// Adds a timer control function.
        /// </summary>
        TBuilder Timer(string name, Action<LadsTimerOptions>? configure = null);

        /// <summary>
        /// Adds a timer control function and returns its handle.
        /// </summary>
        TBuilder Timer(string name, Action<LadsTimerOptions>? configure, out ITimerFunction function);

        /// <summary>
        /// Adds an analog scalar sensor function.
        /// </summary>
        TBuilder AnalogScalarSensor(string name, Action<LadsAnalogSensorOptions>? configure = null);

        /// <summary>
        /// Adds an analog scalar sensor function and returns its handle.
        /// </summary>
        TBuilder AnalogScalarSensor(
            string name,
            Action<LadsAnalogSensorOptions>? configure,
            out IAnalogScalarSensorFunction function);

        /// <summary>
        /// Adds an analog array sensor function.
        /// </summary>
        TBuilder AnalogArraySensor(string name, Action<LadsAnalogSensorOptions>? configure = null);

        /// <summary>
        /// Adds an analog array sensor function and returns its handle.
        /// </summary>
        TBuilder AnalogArraySensor(
            string name,
            Action<LadsAnalogSensorOptions>? configure,
            out IAnalogArraySensorFunction function);

        /// <summary>
        /// Adds a two-state discrete sensor function.
        /// </summary>
        TBuilder TwoStateDiscreteSensor(string name, Action<LadsTwoStateDiscreteOptions>? configure = null);

        /// <summary>
        /// Adds a two-state discrete sensor function and returns its handle.
        /// </summary>
        TBuilder TwoStateDiscreteSensor(
            string name,
            Action<LadsTwoStateDiscreteOptions>? configure,
            out ITwoStateDiscreteSensorFunction function);

        /// <summary>
        /// Adds a multi-state discrete sensor function.
        /// </summary>
        TBuilder MultiStateDiscreteSensor(string name, Action<LadsMultiStateDiscreteOptions> configure);

        /// <summary>
        /// Adds a multi-state discrete sensor function and returns its handle.
        /// </summary>
        TBuilder MultiStateDiscreteSensor(
            string name,
            Action<LadsMultiStateDiscreteOptions> configure,
            out IMultiStateDiscreteSensorFunction function);

        /// <summary>
        /// Adds a multi-sensor function that groups sensor functions.
        /// </summary>
        TBuilder MultiSensor(string name, Action<ILadsMultiSensorBuilder> configure);

        /// <summary>
        /// Adds a multi-sensor function and returns its handle.
        /// </summary>
        TBuilder MultiSensor(string name, Action<ILadsMultiSensorBuilder> configure, out IMultiSensorFunction function);

        /// <summary>
        /// Adds a cover function.
        /// </summary>
        TBuilder Cover(string name, Action<LadsCoverOptions>? configure = null);

        /// <summary>
        /// Adds a cover function and returns its handle.
        /// </summary>
        TBuilder Cover(string name, Action<LadsCoverOptions>? configure, out ICoverFunction function);
    }

    /// <summary>
    /// Builds the functions of a multi-sensor function.
    /// </summary>
    public interface ILadsMultiSensorBuilder : ILadsFunctionSetBuilder<ILadsMultiSensorBuilder>
    {
    }

    /// <summary>
    /// Builds a functional unit.
    /// </summary>
    public interface ILadsFunctionalUnitBuilder : ILadsFunctionSetBuilder<ILadsFunctionalUnitBuilder>
    {
        /// <summary>
        /// Gets the name of the functional unit.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Adds a program manager.
        /// </summary>
        /// <param name="configure">Configures the program manager.</param>
        ILadsFunctionalUnitBuilder Programs(Action<ILadsProgramManagerBuilder>? configure = null);

        /// <summary>
        /// Adds an entry to the supported properties of the functional unit.
        /// </summary>
        /// <param name="name">The key of the property.</param>
        ILadsFunctionalUnitBuilder SupportedProperty(string name);

        /// <summary>
        /// Replaces the function provider of the functions of this functional unit.
        /// </summary>
        /// <param name="provider">The provider.</param>
        ILadsFunctionalUnitBuilder FunctionProvider(ILadsFunctionProvider provider);
    }

    /// <summary>
    /// Builds a program manager.
    /// </summary>
    public interface ILadsProgramManagerBuilder
    {
        /// <summary>
        /// Adds a program template.
        /// </summary>
        /// <param name="id">The id of the template.</param>
        /// <param name="configure">Configures the template.</param>
        ILadsProgramManagerBuilder Template(string id, Action<LadsProgramTemplate> configure);

        /// <summary>
        /// Adds a program template.
        /// </summary>
        /// <param name="template">The template.</param>
        ILadsProgramManagerBuilder Template(LadsProgramTemplate template);

        /// <summary>
        /// Replaces the executor of the programs of this program manager.
        /// </summary>
        /// <param name="executor">The executor.</param>
        ILadsProgramManagerBuilder Executor(ILadsProgramExecutor executor);
    }

    /// <summary>
    /// Entry points to add LADS devices to a Machinery node manager outside of the hosting extensions.
    /// </summary>
    public static class LadsSetup
    {
        /// <summary>
        /// Runs a LADS configuration against a Machinery build context.
        /// </summary>
        /// <param name="buildContext">The build context.</param>
        /// <param name="configure">The configuration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static async ValueTask ConfigureAsync(
            IMachineryBuildContext buildContext,
            Func<ILadsSetupContext, CancellationToken, ValueTask> configure,
            CancellationToken cancellationToken = default)
        {
            if (buildContext == null)
            {
                throw new ArgumentNullException(nameof(buildContext));
            }
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var context = new LadsSetupContext(LadsRuntime.GetOrCreate(buildContext), buildContext);
            await configure(context, cancellationToken).ConfigureAwait(false);
            await context.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs a synchronous LADS configuration against a Machinery build context.
        /// </summary>
        /// <param name="buildContext">The build context.</param>
        /// <param name="configure">The configuration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        public static ValueTask ConfigureAsync(
            IMachineryBuildContext buildContext,
            Action<ILadsSetupContext> configure,
            CancellationToken cancellationToken = default)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            return ConfigureAsync(
                buildContext,
                (context, _) =>
                {
                    configure(context);
                    return default;
                },
                cancellationToken);
        }
    }
}
