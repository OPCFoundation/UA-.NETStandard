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
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// A LADS device published in the address space.
    /// </summary>
    public interface ILadsDevice
    {
        /// <summary>
        /// The browse name of the device.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The NodeId of the device object.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// The current state of the device state machine.
        /// </summary>
        LadsDeviceStateValue State { get; }

        /// <summary>
        /// The current Machinery operation mode.
        /// </summary>
        MachineryOperationModeValue OperationMode { get; }

        /// <summary>
        /// The current Machinery item state derived from the device and functional unit states.
        /// </summary>
        MachineryItemStateValue ItemState { get; }

        /// <summary>
        /// The functional units of the device.
        /// </summary>
        ArrayOf<ILadsFunctionalUnit> FunctionalUnits { get; }

        /// <summary>
        /// The maintenance tasks of the device.
        /// </summary>
        ArrayOf<ILadsMaintenanceTask> MaintenanceTasks { get; }

        /// <summary>
        /// The number of completed operation cycles.
        /// </summary>
        uint OperationCycleCounter { get; }

        /// <summary>
        /// The accumulated duration of program runs.
        /// </summary>
        TimeSpan OperationDuration { get; }

        /// <summary>
        /// The time since the device was published.
        /// </summary>
        TimeSpan PowerOnDuration { get; }

        /// <summary>
        /// Raised after the device state changed.
        /// </summary>
        event Action<LadsDeviceStateValue>? StateChanged;

        /// <summary>
        /// Raised after the operation mode changed.
        /// </summary>
        event Action<MachineryOperationModeValue>? OperationModeChanged;

        /// <summary>
        /// Looks a functional unit up by browse name.
        /// </summary>
        /// <param name="name">The browse name.</param>
        /// <returns>The functional unit.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when it does not exist.
        /// </exception>
        ILadsFunctionalUnit GetFunctionalUnit(string name);

        /// <summary>
        /// Looks a maintenance task up by browse name.
        /// </summary>
        /// <param name="name">The browse name.</param>
        /// <returns>The maintenance task.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when it does not exist.
        /// </exception>
        ILadsMaintenanceTask GetMaintenanceTask(string name);

        /// <summary>
        /// Moves the device to the Operate state.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the state changed.</returns>
        ValueTask GotoOperateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves the device to the Sleep state. Rejected while a functional unit is running.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the state changed.</returns>
        ValueTask GotoSleepAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Moves the device to the Shutdown state. Rejected while a functional unit is running.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the state changed.</returns>
        ValueTask GotoShutdownAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes the Machinery operation mode.
        /// </summary>
        /// <param name="mode">The operation mode.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the mode changed.</returns>
        ValueTask SetOperationModeAsync(
            MachineryOperationModeValue mode,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Increments the operation cycle counter.
        /// </summary>
        void IncrementOperationCycles();
    }

    /// <summary>
    /// A functional unit of a LADS device.
    /// </summary>
    public interface ILadsFunctionalUnit
    {
        /// <summary>
        /// The browse name of the functional unit.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The NodeId of the functional unit object.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// The device the functional unit belongs to.
        /// </summary>
        ILadsDevice Device { get; }

        /// <summary>
        /// The current state of the functional unit state machine.
        /// </summary>
        LadsFunctionalStateValue State { get; }

        /// <summary>
        /// The current state of the running sub state machine.
        /// </summary>
        LadsRunningStateValue RunningState { get; }

        /// <summary>
        /// The top level functions of the functional unit.
        /// </summary>
        ArrayOf<ILadsFunction> Functions { get; }

        /// <summary>
        /// The program manager, or <c>null</c> when the functional unit has none.
        /// </summary>
        ILadsProgramManager? Programs { get; }

        /// <summary>
        /// Raised after the functional unit state changed.
        /// </summary>
        event Action<LadsFunctionalStateValue>? StateChanged;

        /// <summary>
        /// Looks a function up by browse name; nested functions are addressed as <c>Parent/Child</c>.
        /// </summary>
        /// <typeparam name="TFunction">The expected handle type.</typeparam>
        /// <param name="name">The browse name or path.</param>
        /// <returns>The function.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when no function of the expected type has the name.
        /// </exception>
        TFunction GetFunction<TFunction>(string name)
            where TFunction : class, ILadsFunction;

        /// <summary>
        /// Looks a function up by browse name; nested functions are addressed as <c>Parent/Child</c>.
        /// </summary>
        /// <typeparam name="TFunction">The expected handle type.</typeparam>
        /// <param name="name">The browse name or path.</param>
        /// <param name="function">The function.</param>
        /// <returns><see langword="true"/> when a function of the expected type has the name.</returns>
        bool TryGetFunction<TFunction>(string name, [NotNullWhen(true)] out TFunction? function)
            where TFunction : class, ILadsFunction;

        /// <summary>
        /// Starts the functional unit without a program.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the functional unit runs.</returns>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the functional unit and the active program run, if any.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the functional unit stopped.</returns>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Aborts the functional unit and the active program run, if any.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the functional unit aborted.</returns>
        ValueTask AbortAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears an aborted functional unit.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the functional unit stopped.</returns>
        ValueTask ClearAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The program manager of a functional unit.
    /// </summary>
    public interface ILadsProgramManager
    {
        /// <summary>
        /// The functional unit the program manager belongs to.
        /// </summary>
        ILadsFunctionalUnit FunctionalUnit { get; }

        /// <summary>
        /// The available program templates.
        /// </summary>
        ArrayOf<LadsProgramTemplate> Templates { get; }

        /// <summary>
        /// The retained results, oldest first.
        /// </summary>
        ArrayOf<ILadsResult> Results { get; }

        /// <summary>
        /// The active program run, or <c>null</c> when none runs.
        /// </summary>
        LadsProgramRun? ActiveRun { get; }

        /// <summary>
        /// Raised after a run ended and its result was published.
        /// </summary>
        event Action<ILadsResult>? ResultAdded;

        /// <summary>
        /// Adds or replaces a program template.
        /// </summary>
        /// <param name="template">The template.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the template was published.</returns>
        ValueTask AddTemplateAsync(LadsProgramTemplate template, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a program template.
        /// </summary>
        /// <param name="templateId">The template identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><see langword="true"/> when the template existed.</returns>
        ValueTask<bool> RemoveTemplateAsync(string templateId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a program run.
        /// </summary>
        /// <param name="templateId">The template identifier.</param>
        /// <param name="properties">The run properties.</param>
        /// <param name="supervisoryJobId">The supervisory job identifier.</param>
        /// <param name="supervisoryTaskId">The supervisory task identifier.</param>
        /// <param name="samples">The samples processed by the run.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The device program run identifier.</returns>
        ValueTask<string> StartProgramAsync(
            string templateId,
            ArrayOf<KeyValueType> properties = default,
            string? supervisoryJobId = null,
            string? supervisoryTaskId = null,
            ArrayOf<SampleInfoType> samples = default,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Waits until a run ended and returns its result.
        /// </summary>
        /// <param name="runId">The device program run identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result.</returns>
        ValueTask<ILadsResult> WaitForResultAsync(string runId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a result and its files.
        /// </summary>
        /// <param name="runId">The device program run identifier.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><see langword="true"/> when the result existed.</returns>
        ValueTask<bool> RemoveResultAsync(string runId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The result of a program run.
    /// </summary>
    public interface ILadsResult
    {
        /// <summary>
        /// The browse name of the result object.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The NodeId of the result object.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// The device program run identifier.
        /// </summary>
        string RunId { get; }

        /// <summary>
        /// The template the run executed.
        /// </summary>
        string TemplateId { get; }

        /// <summary>
        /// The outcome of the run.
        /// </summary>
        LadsProgramOutcome Outcome { get; }

        /// <summary>
        /// When the run started.
        /// </summary>
        DateTimeUtc Started { get; }

        /// <summary>
        /// When the run ended.
        /// </summary>
        DateTimeUtc Stopped { get; }

        /// <summary>
        /// The files of the result.
        /// </summary>
        ArrayOf<LadsResultFile> Files { get; }
    }

    /// <summary>
    /// A function of a functional unit.
    /// </summary>
    public interface ILadsFunction
    {
        /// <summary>
        /// The browse name of the function.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The NodeId of the function object.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// The functional unit the function belongs to.
        /// </summary>
        ILadsFunctionalUnit FunctionalUnit { get; }

        /// <summary>
        /// Whether the function is enabled.
        /// </summary>
        bool IsEnabled { get; set; }
    }

    /// <summary>
    /// A control function with the control function state machine.
    /// </summary>
    public interface ILadsControlFunction : ILadsFunction
    {
        /// <summary>
        /// The current state of the control function state machine.
        /// </summary>
        LadsFunctionalStateValue State { get; }

        /// <summary>
        /// Raised after the control function state changed.
        /// </summary>
        event Action<LadsFunctionalStateValue>? StateChanged;

        /// <summary>
        /// Starts the control function with its current target value.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the function runs.</returns>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the control function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the function stopped.</returns>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Aborts the control function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the function aborted.</returns>
        ValueTask AbortAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears an aborted control function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the function stopped.</returns>
        ValueTask ClearAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The kind of an analog control function.
    /// </summary>
    public enum LadsAnalogControlKind
    {
        /// <summary>
        /// AnalogControlFunctionType.
        /// </summary>
        Plain,

        /// <summary>
        /// AnalogControlFunctionWithTotalizerType.
        /// </summary>
        Totalizer,

        /// <summary>
        /// AnalogControlFunctionWithRelativeTargetValueType.
        /// </summary>
        Relative,

        /// <summary>
        /// AnalogControlFunctionWithComposedTargetValueType.
        /// </summary>
        Composed
    }

    /// <summary>
    /// An analog control function, for example a temperature or a shaker speed controller.
    /// </summary>
    public interface IAnalogControlFunction : ILadsControlFunction
    {
        /// <summary>
        /// The kind of the analog control function.
        /// </summary>
        LadsAnalogControlKind Kind { get; }

        /// <summary>
        /// The measured value.
        /// </summary>
        double CurrentValue { get; }

        /// <summary>
        /// The target value.
        /// </summary>
        double TargetValue { get; }

        /// <summary>
        /// The totalized value of a <see cref="LadsAnalogControlKind.Totalizer"/> function.
        /// </summary>
        double TotalizedValue { get; }

        /// <summary>
        /// Publishes a new measured value; a totalizer adds it to the totalized value.
        /// </summary>
        /// <param name="value">The measured value.</param>
        void UpdateCurrentValue(double value);

        /// <summary>
        /// Writes a new target value through the function provider.
        /// </summary>
        /// <param name="value">The target value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the target value was applied.</returns>
        ValueTask SetTargetValueAsync(double value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts the function with a new target value.
        /// </summary>
        /// <param name="value">The target value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the function runs.</returns>
        ValueTask StartWithTargetValueAsync(double value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Modifies the target value of a <see cref="LadsAnalogControlKind.Relative"/> function.
        /// </summary>
        /// <param name="delta">The change.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the target value was applied.</returns>
        ValueTask ModifyTargetValueByAsync(double delta, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets the totalized value of a <see cref="LadsAnalogControlKind.Totalizer"/> function.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the totalizer was reset.</returns>
        ValueTask ResetTotalizerAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes a part of the target value of a <see cref="LadsAnalogControlKind.Composed"/> function.
        /// </summary>
        /// <param name="name">The name of the composed target value.</param>
        /// <param name="value">The value.</param>
        void SetComposedTargetValue(string name, double value);
    }

    /// <summary>
    /// A multi-mode analog control function with one controller parameter per mode.
    /// </summary>
    public interface IMultiModeAnalogControlFunction : ILadsControlFunction
    {
        /// <summary>
        /// The index of the current controller mode.
        /// </summary>
        uint CurrentMode { get; }

        /// <summary>
        /// The names of the controller modes.
        /// </summary>
        ArrayOf<string> Modes { get; }

        /// <summary>
        /// Selects the controller mode through the function provider.
        /// </summary>
        /// <param name="mode">The mode index.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the mode was applied.</returns>
        ValueTask SetModeAsync(uint mode, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reads the target value of a controller parameter.
        /// </summary>
        /// <param name="mode">The mode name.</param>
        /// <returns>The target value.</returns>
        double GetTargetValue(string mode);

        /// <summary>
        /// Writes the target value of a controller parameter.
        /// </summary>
        /// <param name="mode">The mode name.</param>
        /// <param name="value">The target value.</param>
        void SetTargetValue(string mode, double value);

        /// <summary>
        /// Publishes the measured value of a controller parameter.
        /// </summary>
        /// <param name="mode">The mode name.</param>
        /// <param name="value">The measured value.</param>
        void UpdateCurrentValue(string mode, double value);
    }

    /// <summary>
    /// A two-state discrete control function, for example a valve.
    /// </summary>
    public interface ITwoStateDiscreteControlFunction : ILadsControlFunction
    {
        /// <summary>
        /// The measured value.
        /// </summary>
        bool CurrentValue { get; }

        /// <summary>
        /// The target value.
        /// </summary>
        bool TargetValue { get; }

        /// <summary>
        /// Publishes a new measured value.
        /// </summary>
        /// <param name="value">The value.</param>
        void UpdateCurrentValue(bool value);

        /// <summary>
        /// Writes a new target value through the function provider.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the target value was applied.</returns>
        ValueTask SetTargetValueAsync(bool value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts the function with a new target value.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the function runs.</returns>
        ValueTask StartWithTargetValueAsync(bool value, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A multi-state discrete control function.
    /// </summary>
    public interface IMultiStateDiscreteControlFunction : ILadsControlFunction
    {
        /// <summary>
        /// The measured state index.
        /// </summary>
        uint CurrentValue { get; }

        /// <summary>
        /// The target state index.
        /// </summary>
        uint TargetValue { get; }

        /// <summary>
        /// Publishes a new measured state index.
        /// </summary>
        /// <param name="value">The value.</param>
        void UpdateCurrentValue(uint value);

        /// <summary>
        /// Writes a new target state index through the function provider.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the target value was applied.</returns>
        ValueTask SetTargetValueAsync(uint value, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts the function with a new target state index.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the function runs.</returns>
        ValueTask StartWithTargetValueAsync(uint value, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A timer control function.
    /// </summary>
    public interface ITimerFunction : ILadsControlFunction
    {
        /// <summary>
        /// The elapsed time.
        /// </summary>
        double CurrentValue { get; }

        /// <summary>
        /// The duration of the timer.
        /// </summary>
        double TargetValue { get; }

        /// <summary>
        /// The remaining time.
        /// </summary>
        double DifferenceValue { get; }

        /// <summary>
        /// Publishes the elapsed time; the remaining time follows.
        /// </summary>
        /// <param name="value">The elapsed time.</param>
        void UpdateCurrentValue(double value);

        /// <summary>
        /// Writes the duration through the function provider.
        /// </summary>
        /// <param name="value">The duration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the duration was applied.</returns>
        ValueTask SetTargetValueAsync(double value, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// An analog scalar sensor function, for example a CO2 sensor.
    /// </summary>
    public interface IAnalogScalarSensorFunction : ILadsFunction
    {
        /// <summary>
        /// The sensor value.
        /// </summary>
        double SensorValue { get; }

        /// <summary>
        /// The raw value.
        /// </summary>
        double RawValue { get; }

        /// <summary>
        /// Whether the sensor has a compensation value.
        /// </summary>
        bool HasCompensation { get; }

        /// <summary>
        /// Publishes a new sensor value.
        /// </summary>
        /// <param name="value">The sensor value.</param>
        /// <param name="rawValue">The raw value; defaults to the sensor value.</param>
        void UpdateValue(double value, double? rawValue = null);

        /// <summary>
        /// Publishes a new compensation value.
        /// </summary>
        /// <param name="value">The compensation value.</param>
        void UpdateCompensationValue(double value);
    }

    /// <summary>
    /// An analog array sensor function, for example a spectrum.
    /// </summary>
    public interface IAnalogArraySensorFunction : ILadsFunction
    {
        /// <summary>
        /// The sensor values.
        /// </summary>
        ArrayOf<double> SensorValue { get; }

        /// <summary>
        /// Publishes new sensor values.
        /// </summary>
        /// <param name="values">The sensor values.</param>
        /// <param name="rawValues">The raw values; defaults to the sensor values.</param>
        void UpdateValue(ArrayOf<double> values, ArrayOf<double> rawValues = default);
    }

    /// <summary>
    /// A two-state discrete sensor function.
    /// </summary>
    public interface ITwoStateDiscreteSensorFunction : ILadsFunction
    {
        /// <summary>
        /// The sensor value.
        /// </summary>
        bool SensorValue { get; }

        /// <summary>
        /// Publishes a new sensor value.
        /// </summary>
        /// <param name="value">The value.</param>
        void UpdateValue(bool value);
    }

    /// <summary>
    /// A multi-state discrete sensor function.
    /// </summary>
    public interface IMultiStateDiscreteSensorFunction : ILadsFunction
    {
        /// <summary>
        /// The sensor state index.
        /// </summary>
        uint SensorValue { get; }

        /// <summary>
        /// Publishes a new sensor state index.
        /// </summary>
        /// <param name="value">The value.</param>
        void UpdateValue(uint value);
    }

    /// <summary>
    /// A multi sensor function grouping sensor functions.
    /// </summary>
    public interface IMultiSensorFunction : ILadsFunction
    {
        /// <summary>
        /// The grouped functions.
        /// </summary>
        ArrayOf<ILadsFunction> Functions { get; }
    }

    /// <summary>
    /// A cover function, for example a door or a lid.
    /// </summary>
    public interface ICoverFunction : ILadsFunction
    {
        /// <summary>
        /// The current state of the cover state machine.
        /// </summary>
        LadsCoverStateValue State { get; }

        /// <summary>
        /// Raised after the cover state changed.
        /// </summary>
        event Action<LadsCoverStateValue>? StateChanged;

        /// <summary>
        /// Opens the cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the cover is open.</returns>
        ValueTask OpenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the cover is closed.</returns>
        ValueTask CloseAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Locks the closed cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the cover is locked.</returns>
        ValueTask LockAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Unlocks the cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the cover is closed.</returns>
        ValueTask UnlockAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets a cover in the error state to open.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the cover is open.</returns>
        ValueTask ResetAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports a hardware error of the closed cover.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the cover is in the error state.</returns>
        ValueTask ReportErrorAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A maintenance task of a LADS device.
    /// </summary>
    public interface ILadsMaintenanceTask
    {
        /// <summary>
        /// The browse name of the task.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The NodeId of the task.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// The device the task belongs to.
        /// </summary>
        ILadsDevice Device { get; }

        /// <summary>
        /// The current maintenance state.
        /// </summary>
        LadsMaintenanceStateValue State { get; }

        /// <summary>
        /// When the task last finished, or <see cref="DateTimeUtc.MinValue"/> when it never ran.
        /// </summary>
        DateTimeUtc LastExecutionDate { get; }

        /// <summary>
        /// The result of the last execution.
        /// </summary>
        MaintenanceTaskResultEnum LastResult { get; }

        /// <summary>
        /// Raised after the maintenance state changed.
        /// </summary>
        event Action<LadsMaintenanceStateValue>? StateChanged;

        /// <summary>
        /// Starts the planned task.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the task executes.</returns>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Finishes the executing task.
        /// </summary>
        /// <param name="result">The result of the maintenance.</param>
        /// <param name="comment">A comment.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the task finished.</returns>
        ValueTask StopAsync(
            MaintenanceTaskResultEnum result = MaintenanceTaskResultEnum.Success,
            LocalizedText comment = default,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resets the finished task to planned.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the task is planned.</returns>
        ValueTask ResetAsync(CancellationToken cancellationToken = default);
    }
}
