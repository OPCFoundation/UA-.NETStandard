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
using Microsoft.Extensions.Logging;
using Opc.Ua.LADS.Server.StateMachines;
using Opc.Ua.Server;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The base of all function handles.
    /// </summary>
    internal abstract class LadsFunction : ILadsFunction, IDisposable
    {
        /// <summary>
        /// Creates the handle of a function node.
        /// </summary>
        protected LadsFunction(LadsFunctionalUnit unit, FunctionState node, string path)
        {
            Unit = unit;
            Node = node;
            Path = path;
            Runtime.Update(node.IsEnabled, Variant.From(true));
        }

        /// <inheritdoc/>
        public string Name => Node.BrowseName.Name ?? string.Empty;

        /// <summary>
        /// Gets the path of the function relative to the function set of its functional unit.
        /// </summary>
        public string Path { get; }

        /// <inheritdoc/>
        public NodeId NodeId => Node.NodeId;

        /// <inheritdoc/>
        public ILadsFunctionalUnit FunctionalUnit => Unit;

        /// <summary>
        /// Gets the functional unit.
        /// </summary>
        public LadsFunctionalUnit Unit { get; }

        /// <summary>
        /// Gets the node of the function.
        /// </summary>
        public FunctionState Node { get; }

        /// <summary>
        /// Gets the runtime.
        /// </summary>
        public LadsRuntime Runtime => Unit.Runtime;

        /// <inheritdoc/>
        public bool IsEnabled
        {
            get => Volatile.Read(ref m_enabled);
            set
            {
                Volatile.Write(ref m_enabled, value);
                Runtime.Update(Node.IsEnabled, Variant.From(value));
            }
        }

        /// <summary>
        /// Converts a numeric value to a double.
        /// </summary>
        protected static double ToDouble(Variant value)
        {
            if (value.TryGetValue(out double number))
            {
                return number;
            }
            if (value.TryGetValue(out float single))
            {
                return single;
            }
            if (value.TryGetValue(out int signed))
            {
                return signed;
            }
            if (value.TryGetValue(out uint unsigned))
            {
                return unsigned;
            }
            throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A numeric value is expected.");
        }

        /// <summary>
        /// Rejects a value outside of the range.
        /// </summary>
        protected static void CheckRange(Range? range, double value)
        {
            if (range != null && (value < range.Low || value > range.High))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                    "The value {0} is outside of the range [{1}, {2}].",
                    value,
                    range.Low,
                    range.High);
            }
        }

        /// <summary>
        /// Configures the engineering units and the range of an analog variable.
        /// </summary>
        protected static void ConfigureAnalog(BaseVariableState? variable, EUInformation? units, Range? range)
        {
            if (variable is not BaseAnalogState analog)
            {
                return;
            }
            if (units != null && analog.EngineeringUnits != null)
            {
                analog.EngineeringUnits.Value = units;
            }
            if (range != null && analog.EURange != null)
            {
                analog.EURange.Value = range;
            }
        }

        /// <summary>
        /// Lets the function provider accept or reject a command.
        /// </summary>
        protected async ValueTask InvokeProviderAsync(
            LadsFunctionCommandKind kind,
            Variant value,
            CancellationToken cancellationToken)
        {
            ServiceResult result = await Unit.FunctionProvider
                .OnCommandAsync(new LadsFunctionCommand(this, kind, value), cancellationToken)
                .ConfigureAwait(false);
            if (ServiceResult.IsBad(result))
            {
                Runtime.Logger.CommandRejected(Path, kind.ToString(), result.StatusCode);
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Runs a command exclusively.
        /// </summary>
        protected async ValueTask ExclusiveAsync(Func<ValueTask> command, CancellationToken cancellationToken)
        {
            await m_gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await command().ConfigureAwait(false);
            }
            finally
            {
                m_gate.Release();
            }
        }

        /// <summary>
        /// Rejects a command while the function is disabled.
        /// </summary>
        protected void RequireEnabled()
        {
            if (!IsEnabled)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "The function '{0}' is disabled.",
                    Path);
            }
        }

        /// <summary>
        /// Releases the command gate.
        /// </summary>
        public void Dispose()
        {
            m_gate.Dispose();
        }

        private readonly SemaphoreSlim m_gate = new(1, 1);
        private bool m_enabled = true;
    }

    /// <summary>
    /// The base of the control functions with a control function state machine.
    /// </summary>
    internal abstract class LadsControlFunction : LadsFunction, ILadsControlFunction
    {
        /// <summary>
        /// Creates the handle and binds the methods of the control function state machine.
        /// </summary>
        protected LadsControlFunction(LadsFunctionalUnit unit, BaseControlFunctionState node, string path)
            : base(unit, node, path)
        {
            ISystemContext context = Runtime.Context;
            ControlFunctionStateMachineState stateMachine = node.ControlFunctionState ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The control function state machine is missing.");
            stateMachine.AddStart(context).AddStartWithTargetValue(context);
            stateMachine.AddStop(context).AddAbort(context).AddClear(context);
            m_machine = new LadsStateMachine<LadsFunctionalStateValue>(
                stateMachine,
                context,
                LadsStateMaps.Functional,
                LadsFunctionalStateValue.Stopped);
            m_machine.StateChanged += state =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Path, state);
                }
                StateChanged?.Invoke(state);
            };
            Runtime.Bind(stateMachine.Start, (_, ct) => StartAsync(ct));
            Runtime.Bind(stateMachine.Stop, (_, ct) => StopAsync(ct));
            Runtime.Bind(stateMachine.Abort, (_, ct) => AbortAsync(ct));
            Runtime.Bind(stateMachine.Clear, (_, ct) => ClearAsync(ct));
            Runtime.Bind(
                stateMachine.StartWithTargetValue,
                (inputs, ct) => StartWithTargetVariantAsync(inputs.Count > 0 ? inputs[0] : default, ct));
        }

        /// <inheritdoc/>
        public LadsFunctionalStateValue State => m_machine.Current;

        /// <inheritdoc/>
        public event Action<LadsFunctionalStateValue>? StateChanged;

        /// <inheritdoc/>
        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    RequireEnabled();
                    m_machine.Require(LadsFunctionalStateValue.Stopped);
                    await InvokeProviderAsync(LadsFunctionCommandKind.Start, default, cancellationToken)
                        .ConfigureAwait(false);
                    await TransitionAsync(cancellationToken, LadsFunctionalStateValue.Running)
                        .ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StopAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    m_machine.Require(LadsFunctionalStateValue.Running);
                    await InvokeProviderAsync(LadsFunctionCommandKind.Stop, default, cancellationToken)
                        .ConfigureAwait(false);
                    await TransitionAsync(
                        cancellationToken,
                        LadsFunctionalStateValue.Stopping,
                        LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask AbortAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    m_machine.Require(LadsFunctionalStateValue.Running);
                    await InvokeProviderAsync(LadsFunctionCommandKind.Abort, default, cancellationToken)
                        .ConfigureAwait(false);
                    await TransitionAsync(
                        cancellationToken,
                        LadsFunctionalStateValue.Aborting,
                        LadsFunctionalStateValue.Aborted).ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    m_machine.Require(LadsFunctionalStateValue.Aborted);
                    await InvokeProviderAsync(LadsFunctionCommandKind.Clear, default, cancellationToken)
                        .ConfigureAwait(false);
                    await TransitionAsync(
                        cancellationToken,
                        LadsFunctionalStateValue.Clearing,
                        LadsFunctionalStateValue.Stopped).ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <summary>
        /// Validates a target value and converts it to the data type of the function.
        /// </summary>
        protected abstract Variant NormalizeTarget(Variant value);

        /// <summary>
        /// Writes an accepted target value.
        /// </summary>
        protected abstract void ApplyTarget(Variant value);

        /// <summary>
        /// Sets the target value after the provider accepted it.
        /// </summary>
        protected ValueTask SetTargetCoreAsync(Variant value, CancellationToken cancellationToken)
        {
            return ExclusiveAsync(
                async () =>
                {
                    Variant target = NormalizeTarget(value);
                    await InvokeProviderAsync(LadsFunctionCommandKind.SetTargetValue, target, cancellationToken)
                        .ConfigureAwait(false);
                    ApplyTarget(target);
                },
                cancellationToken);
        }

        /// <summary>
        /// Sets the target value and starts the function.
        /// </summary>
        protected ValueTask StartWithTargetVariantAsync(Variant value, CancellationToken cancellationToken)
        {
            return ExclusiveAsync(
                async () =>
                {
                    RequireEnabled();
                    Variant target = NormalizeTarget(value);
                    m_machine.Require(LadsFunctionalStateValue.Stopped);
                    await InvokeProviderAsync(
                        LadsFunctionCommandKind.StartWithTargetValue,
                        target,
                        cancellationToken).ConfigureAwait(false);
                    ApplyTarget(target);
                    await TransitionAsync(cancellationToken, LadsFunctionalStateValue.Running)
                        .ConfigureAwait(false);
                },
                cancellationToken);
        }

        /// <summary>
        /// Applies transitions and rejects a vetoed transition.
        /// </summary>
        private async ValueTask TransitionAsync(
            CancellationToken cancellationToken,
            params LadsFunctionalStateValue[] states)
        {
            if (!await m_machine.TransitionThroughAsync(cancellationToken, states).ConfigureAwait(false))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "A transition of the function '{0}' was vetoed.",
                    Path);
            }
        }

        private readonly LadsStateMachine<LadsFunctionalStateValue> m_machine;
    }

    /// <summary>
    /// The handle of an analog control function and its totalizer, relative and composed variants.
    /// </summary>
    internal sealed class LadsAnalogControlFunction : LadsControlFunction, IAnalogControlFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsAnalogControlFunction(
            LadsFunctionalUnit unit,
            AnalogControlFunctionState node,
            string path,
            LadsAnalogControlOptions options)
            : base(unit, node, path)
        {
            m_node = node;
            m_options = options;
            Kind = options.Kind;
            ServerSystemContext context = Runtime.Context;
            ConfigureAnalog(node.CurrentValue, options.EngineeringUnits, options.Range);
            ConfigureAnalog(node.TargetValue, options.EngineeringUnits, options.Range);
            m_current = options.InitialCurrentValue;
            m_target = options.InitialTargetValue;
            Runtime.Update(node.CurrentValue, Variant.From(m_current));
            Runtime.Update(node.TargetValue, Variant.From(m_target));
            m_lastUpdate = Runtime.TimeProvider.GetTimestamp();
            switch (node)
            {
                case AnalogControlFunctionWithTotalizerState totalizer:
                    totalizer.AddResetTotalizer(context);
                    ConfigureAnalog(totalizer.TotalizedValue, options.EngineeringUnits, null);
                    Runtime.Update(totalizer.TotalizedValue, Variant.From(0.0));
                    Runtime.Bind(totalizer.ResetTotalizer, (_, ct) => ResetTotalizerAsync(ct));
                    break;
                case AnalogControlFunctionWithRelativeTargetValueState relative:
                    relative.AddModifyTargetValueBy(context);
                    if (options.IncreaseRate.HasValue)
                    {
                        relative.AddIncreaseRate(context);
                        ConfigureAnalog(relative.IncreaseRate, options.EngineeringUnits, null);
                        Runtime.Update(relative.IncreaseRate, Variant.From(options.IncreaseRate.Value));
                    }
                    if (options.DecreaseRate.HasValue)
                    {
                        relative.AddDecreaseRate(context);
                        ConfigureAnalog(relative.DecreaseRate, options.EngineeringUnits, null);
                        Runtime.Update(relative.DecreaseRate, Variant.From(options.DecreaseRate.Value));
                    }
                    Runtime.Bind(
                        relative.ModifyTargetValueBy,
                        (inputs, ct) => ModifyTargetValueByAsync(
                            ToDouble(inputs.Count > 0 ? inputs[0] : default),
                            ct));
                    break;
                case AnalogControlFunctionWithComposedTargetValueState composed:
                    NodeState set = composed.TargetValueSet ??
                        throw ServiceResultException.Create(
                            StatusCodes.BadConfigurationError,
                            "The target value set is missing.");
                    foreach (string name in options.ComposedTargetValues)
                    {
                        BaseDataVariableState<double> variable =
                            BaseDataVariableState<double>.With<VariantBuilder>(set);
                        variable.SymbolicName = name;
                        variable.BrowseName = Runtime.InstanceName(name);
                        variable.DisplayName = new LocalizedText(name);
                        variable.ReferenceTypeId = global::Opc.Ua.ReferenceTypeIds.HasComponent;
                        variable.TypeDefinitionId = global::Opc.Ua.VariableTypeIds.BaseDataVariableType;
                        variable.DataType = global::Opc.Ua.DataTypeIds.Double;
                        variable.ValueRank = ValueRanks.Scalar;
                        variable.AccessLevel = AccessLevels.CurrentRead;
                        variable.UserAccessLevel = AccessLevels.CurrentRead;
                        variable.Value = 0.0;
                        variable.NodeId = context.NodeIdFactory!.New(context, variable);
                        set.AddChild(variable);
                    }
                    break;
            }
        }

        /// <inheritdoc/>
        public LadsAnalogControlKind Kind { get; }

        /// <inheritdoc/>
        public double CurrentValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_current;
                }
            }
        }

        /// <inheritdoc/>
        public double TargetValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_target;
                }
            }
        }

        /// <inheritdoc/>
        public double TotalizedValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_total;
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateCurrentValue(double value)
        {
            double total;
            lock (m_lock)
            {
                long now = Runtime.TimeProvider.GetTimestamp();
                if (State == LadsFunctionalStateValue.Running)
                {
                    m_total += m_current *
                        Runtime.TimeProvider.GetElapsedTime(m_lastUpdate, now).TotalSeconds;
                }
                m_lastUpdate = now;
                m_current = value;
                total = m_total;
            }
            Runtime.Update(m_node.CurrentValue, Variant.From(value));
            if (m_node is AnalogControlFunctionWithTotalizerState totalizer)
            {
                Runtime.Update(totalizer.TotalizedValue, Variant.From(total));
            }
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(double value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StartWithTargetValueAsync(double value, CancellationToken cancellationToken = default)
        {
            return StartWithTargetVariantAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ModifyTargetValueByAsync(double delta, CancellationToken cancellationToken = default)
        {
            RequireKind(LadsAnalogControlKind.Relative);
            return ExclusiveAsync(
                async () =>
                {
                    double target = TargetValue + delta;
                    CheckRange(m_options.Range, target);
                    await InvokeProviderAsync(
                        LadsFunctionCommandKind.ModifyTargetValueBy,
                        Variant.From(delta),
                        cancellationToken).ConfigureAwait(false);
                    ApplyTarget(Variant.From(target));
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask ResetTotalizerAsync(CancellationToken cancellationToken = default)
        {
            RequireKind(LadsAnalogControlKind.Totalizer);
            return ExclusiveAsync(
                async () =>
                {
                    await InvokeProviderAsync(LadsFunctionCommandKind.ResetTotalizer, default, cancellationToken)
                        .ConfigureAwait(false);
                    lock (m_lock)
                    {
                        m_total = 0;
                        m_lastUpdate = Runtime.TimeProvider.GetTimestamp();
                    }
                    Runtime.Update(
                        ((AnalogControlFunctionWithTotalizerState)m_node).TotalizedValue,
                        Variant.From(0.0));
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public void SetComposedTargetValue(string name, double value)
        {
            if (m_node is not AnalogControlFunctionWithComposedTargetValueState composed ||
                composed.TargetValueSet == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The function '{0}' has no composed target value.",
                    Path);
            }
            BaseVariableState variable = Runtime.FindChild<BaseVariableState>(composed.TargetValueSet, name) ??
                throw ServiceResultException.Create(StatusCodes.BadNotFound, "No target value '{0}'.", name);
            Runtime.Update(variable, Variant.From(value));
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            double target = ToDouble(value);
            CheckRange(m_options.Range, target);
            return Variant.From(target);
        }

        /// <inheritdoc/>
        protected override void ApplyTarget(Variant value)
        {
            double target = ToDouble(value);
            lock (m_lock)
            {
                m_target = target;
            }
            Runtime.Update(m_node.TargetValue, Variant.From(target));
        }

        /// <summary>
        /// Rejects a command the variant of the function does not support.
        /// </summary>
        private void RequireKind(LadsAnalogControlKind kind)
        {
            if (Kind != kind)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The function '{0}' is not a {1} analog control function.",
                    Path,
                    kind);
            }
        }

        private readonly AnalogControlFunctionState m_node;
        private readonly LadsAnalogControlOptions m_options;
        private readonly Lock m_lock = new();
        private double m_current;
        private double m_target;
        private double m_total;
        private long m_lastUpdate;
    }

    /// <summary>
    /// The handle of a multi-mode analog control function.
    /// </summary>
    internal sealed class LadsMultiModeAnalogControlFunction : LadsControlFunction, IMultiModeAnalogControlFunction
    {
        /// <summary>
        /// Creates the handle and the controller parameter of every mode.
        /// </summary>
        public LadsMultiModeAnalogControlFunction(
            LadsFunctionalUnit unit,
            MultiModeAnalogControlFunctionState node,
            string path,
            LadsMultiModeAnalogControlOptions options)
            : base(unit, node, path)
        {
            if (options.Modes.Count == 0)
            {
                throw new ArgumentException(
                    "A multi-mode analog control function needs at least one mode.",
                    nameof(options));
            }
            if (options.InitialMode >= options.Modes.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The initial mode is not one of the modes.");
            }
            m_node = node;
            m_options = options;
            Modes = options.Modes;
            m_mode = options.InitialMode;
            ISystemContext context = Runtime.Context;
            var texts = new LocalizedText[options.Modes.Count];
            for (int ii = 0; ii < texts.Length; ii++)
            {
                texts[ii] = new LocalizedText(options.Modes[ii]);
            }
            if (node.CurrentMode != null)
            {
                Runtime.Update(node.CurrentMode.EnumStrings, Variant.From((ArrayOf<LocalizedText>)texts));
                Runtime.Update(node.CurrentMode, Variant.From(m_mode));
            }
            m_parameters = new ControllerParameterState[options.Modes.Count];
            NodeState set = node.ControllerModeSet ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The controller mode set is missing.");
            for (int ii = 0; ii < m_parameters.Length; ii++)
            {
                ControllerParameterState parameter = LadsRuntime.Named(
                    context.CreateInstanceOfControllerParameterType(set, Runtime.InstanceName(options.Modes[ii])),
                    global::Opc.Ua.ReferenceTypeIds.HasComponent);
                ConfigureAnalog(parameter.CurrentValue, options.EngineeringUnits, options.Range);
                ConfigureAnalog(parameter.TargetValue, options.EngineeringUnits, options.Range);
                Runtime.Update(parameter.CurrentValue, Variant.From(0.0));
                Runtime.Update(parameter.TargetValue, Variant.From(0.0));
                set.AddChild(parameter);
                m_parameters[ii] = parameter;
            }
        }

        /// <inheritdoc/>
        public uint CurrentMode => Volatile.Read(ref m_mode);

        /// <inheritdoc/>
        public ArrayOf<string> Modes { get; }

        /// <inheritdoc/>
        public ValueTask SetModeAsync(uint mode, CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    if (mode >= m_parameters.Length)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadOutOfRange,
                            "The mode {0} is unknown.",
                            mode);
                    }
                    await InvokeProviderAsync(LadsFunctionCommandKind.SetMode, Variant.From(mode), cancellationToken)
                        .ConfigureAwait(false);
                    Volatile.Write(ref m_mode, mode);
                    Runtime.Update(m_node.CurrentMode, Variant.From(mode));
                },
                cancellationToken);
        }

        /// <inheritdoc/>
        public double GetTargetValue(string mode)
        {
            BaseVariableState? variable = Find(mode).TargetValue;
            return variable != null && variable.Value.TryGetValue(out double value) ? value : 0;
        }

        /// <inheritdoc/>
        public void SetTargetValue(string mode, double value)
        {
            CheckRange(m_options.Range, value);
            Runtime.Update(Find(mode).TargetValue, Variant.From(value));
        }

        /// <inheritdoc/>
        public void UpdateCurrentValue(string mode, double value)
        {
            Runtime.Update(Find(mode).CurrentValue, Variant.From(value));
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            double target = ToDouble(value);
            CheckRange(m_options.Range, target);
            return Variant.From(target);
        }

        /// <inheritdoc/>
        protected override void ApplyTarget(Variant value)
        {
            Runtime.Update(m_parameters[CurrentMode].TargetValue, Variant.From(ToDouble(value)));
        }

        /// <summary>
        /// Finds the controller parameter of a mode.
        /// </summary>
        private ControllerParameterState Find(string mode)
        {
            for (int ii = 0; ii < m_parameters.Length; ii++)
            {
                if (string.Equals(Modes[ii], mode, StringComparison.Ordinal))
                {
                    return m_parameters[ii];
                }
            }
            throw ServiceResultException.Create(StatusCodes.BadNotFound, "The mode '{0}' is unknown.", mode);
        }

        private readonly MultiModeAnalogControlFunctionState m_node;
        private readonly LadsMultiModeAnalogControlOptions m_options;
        private readonly ControllerParameterState[] m_parameters;
        private uint m_mode;
    }

    /// <summary>
    /// The handle of a two-state discrete control function.
    /// </summary>
    internal sealed class LadsTwoStateDiscreteControlFunction : LadsControlFunction,
        ITwoStateDiscreteControlFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsTwoStateDiscreteControlFunction(
            LadsFunctionalUnit unit,
            TwoStateDiscreteControlFunctionState node,
            string path,
            LadsTwoStateDiscreteOptions options)
            : base(unit, node, path)
        {
            m_current = Configure(node.CurrentValue, options);
            m_target = Configure(node.TargetValue, options);
            m_currentValue = options.InitialValue;
            m_targetValue = options.InitialValue;
            Runtime.Update(m_current, Variant.From(m_currentValue));
            Runtime.Update(m_target, Variant.From(m_targetValue));
        }

        /// <inheritdoc/>
        public bool CurrentValue => Volatile.Read(ref m_currentValue);

        /// <inheritdoc/>
        public bool TargetValue => Volatile.Read(ref m_targetValue);

        /// <inheritdoc/>
        public void UpdateCurrentValue(bool value)
        {
            Volatile.Write(ref m_currentValue, value);
            Runtime.Update(m_current, Variant.From(value));
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(bool value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StartWithTargetValueAsync(bool value, CancellationToken cancellationToken = default)
        {
            return StartWithTargetVariantAsync(Variant.From(value), cancellationToken);
        }

        /// <summary>
        /// Sets the texts of the states of a two-state variable.
        /// </summary>
        internal static TwoStateDiscreteState? Configure(
            BaseVariableState? variable,
            LadsTwoStateDiscreteOptions options)
        {
            if (variable is not TwoStateDiscreteState discrete)
            {
                return null;
            }
            if (discrete.TrueState != null)
            {
                discrete.TrueState.Value = options.TrueState;
            }
            if (discrete.FalseState != null)
            {
                discrete.FalseState.Value = options.FalseState;
            }
            return discrete;
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            if (value.TryGetValue(out bool flag))
            {
                return Variant.From(flag);
            }
            throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A Boolean value is expected.");
        }

        /// <inheritdoc/>
        protected override void ApplyTarget(Variant value)
        {
            value.TryGetValue(out bool flag);
            Volatile.Write(ref m_targetValue, flag);
            Runtime.Update(m_target, Variant.From(flag));
        }

        private readonly TwoStateDiscreteState? m_current;
        private readonly TwoStateDiscreteState? m_target;
        private bool m_currentValue;
        private bool m_targetValue;
    }

    /// <summary>
    /// The handle of a multi-state discrete control function.
    /// </summary>
    internal sealed class LadsMultiStateDiscreteControlFunction : LadsControlFunction,
        IMultiStateDiscreteControlFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsMultiStateDiscreteControlFunction(
            LadsFunctionalUnit unit,
            MultiStateDiscreteControlFunctionState node,
            string path,
            LadsMultiStateDiscreteOptions options)
            : base(unit, node, path)
        {
            m_states = Validate(options);
            m_current = Configure(Runtime, node.CurrentValue, options);
            m_target = Configure(Runtime, node.TargetValue, options);
            m_currentValue = options.InitialValue;
            m_targetValue = options.InitialValue;
            Runtime.Update(m_current, Variant.From(m_currentValue));
            Runtime.Update(m_target, Variant.From(m_targetValue));
        }

        /// <inheritdoc/>
        public uint CurrentValue => Volatile.Read(ref m_currentValue);

        /// <inheritdoc/>
        public uint TargetValue => Volatile.Read(ref m_targetValue);

        /// <inheritdoc/>
        public void UpdateCurrentValue(uint value)
        {
            Check(m_states, value);
            Volatile.Write(ref m_currentValue, value);
            Runtime.Update(m_current, Variant.From(value));
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(uint value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        public ValueTask StartWithTargetValueAsync(uint value, CancellationToken cancellationToken = default)
        {
            return StartWithTargetVariantAsync(Variant.From(value), cancellationToken);
        }

        /// <summary>
        /// Validates the options of a multi-state function and returns the number of states.
        /// </summary>
        internal static int Validate(LadsMultiStateDiscreteOptions options)
        {
            if (options.States.Count == 0)
            {
                throw new ArgumentException(
                    "A multi-state discrete function needs at least one state.",
                    nameof(options));
            }
            if (options.InitialValue >= options.States.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "The initial value is not one of the states.");
            }
            return options.States.Count;
        }

        /// <summary>
        /// Rejects an index outside of the states.
        /// </summary>
        internal static void Check(int states, uint value)
        {
            if (value >= states)
            {
                throw ServiceResultException.Create(StatusCodes.BadOutOfRange, "The state {0} is unknown.", value);
            }
        }

        /// <summary>
        /// Sets the texts of the states of a multi-state variable.
        /// </summary>
        internal static MultiStateDiscreteState<uint>? Configure(
            LadsRuntime runtime,
            BaseVariableState? variable,
            LadsMultiStateDiscreteOptions options)
        {
            if (variable is not MultiStateDiscreteState<uint> discrete)
            {
                return null;
            }
            runtime.Update(discrete.EnumStrings, Variant.From(options.States));
            return discrete;
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            if (!value.TryGetValue(out uint index))
            {
                double number = ToDouble(value);
                if (number < 0 || number > uint.MaxValue || Math.Floor(number) != number)
                {
                    throw ServiceResultException.Create(StatusCodes.BadTypeMismatch, "A state index is expected.");
                }
                index = (uint)number;
            }
            Check(m_states, index);
            return Variant.From(index);
        }

        /// <inheritdoc/>
        protected override void ApplyTarget(Variant value)
        {
            value.TryGetValue(out uint index);
            Volatile.Write(ref m_targetValue, index);
            Runtime.Update(m_target, Variant.From(index));
        }

        private readonly int m_states;
        private readonly MultiStateDiscreteState<uint>? m_current;
        private readonly MultiStateDiscreteState<uint>? m_target;
        private uint m_currentValue;
        private uint m_targetValue;
    }

    /// <summary>
    /// The handle of a timer control function.
    /// </summary>
    internal sealed class LadsTimerFunction : LadsControlFunction, ITimerFunction
    {
        /// <summary>
        /// Creates the handle and the optional values of the timer.
        /// </summary>
        public LadsTimerFunction(
            LadsFunctionalUnit unit,
            TimerControlFunctionState node,
            string path,
            LadsTimerOptions options)
            : base(unit, node, path)
        {
            m_node = node;
            m_options = options;
            ISystemContext context = Runtime.Context;
            node.AddCurrentValue(context).AddTargetValue(context).AddDifferenceValue(context);
            EUInformation units = options.EngineeringUnits ?? LadsUnits.Second;
            ConfigureAnalog(node.CurrentValue, units, options.Range);
            ConfigureAnalog(node.TargetValue, units, options.Range);
            ConfigureAnalog(node.DifferenceValue, units, null);
            m_target = options.InitialTargetValue;
            Publish();
        }

        /// <inheritdoc/>
        public double CurrentValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_current;
                }
            }
        }

        /// <inheritdoc/>
        public double TargetValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_target;
                }
            }
        }

        /// <inheritdoc/>
        public double DifferenceValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_target - m_current;
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateCurrentValue(double value)
        {
            lock (m_lock)
            {
                m_current = value;
            }
            Publish();
        }

        /// <inheritdoc/>
        public ValueTask SetTargetValueAsync(double value, CancellationToken cancellationToken = default)
        {
            return SetTargetCoreAsync(Variant.From(value), cancellationToken);
        }

        /// <inheritdoc/>
        protected override Variant NormalizeTarget(Variant value)
        {
            double target = ToDouble(value);
            if (target < 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange,
                    "A timer value must not be negative.");
            }
            CheckRange(m_options.Range, target);
            return Variant.From(target);
        }

        /// <inheritdoc/>
        protected override void ApplyTarget(Variant value)
        {
            lock (m_lock)
            {
                m_target = ToDouble(value);
            }
            Publish();
        }

        /// <summary>
        /// Writes the values of the timer.
        /// </summary>
        private void Publish()
        {
            double current;
            double target;
            lock (m_lock)
            {
                current = m_current;
                target = m_target;
            }
            Runtime.Update(m_node.CurrentValue, Variant.From(current));
            Runtime.Update(m_node.TargetValue, Variant.From(target));
            Runtime.Update(m_node.DifferenceValue, Variant.From(target - current));
        }

        private readonly TimerControlFunctionState m_node;
        private readonly LadsTimerOptions m_options;
        private readonly Lock m_lock = new();
        private double m_current;
        private double m_target;
    }
}
