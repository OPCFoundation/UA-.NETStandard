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
using Microsoft.Extensions.Logging;
using Opc.Ua.LADS.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The handle of an analog scalar sensor function with or without compensation.
    /// </summary>
    internal sealed class LadsAnalogScalarSensorFunction : LadsFunction, IAnalogScalarSensorFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsAnalogScalarSensorFunction(
            LadsFunctionalUnit unit,
            AnalogScalarSensorFunctionState node,
            string path,
            LadsAnalogSensorOptions options)
            : base(unit, node, path)
        {
            m_node = node;
            ConfigureAnalog(node.SensorValue, options.EngineeringUnits, options.Range);
            ConfigureAnalog(node.RawValue, options.EngineeringUnits, options.Range);
            if (node is AnalogScalarSensorFunctionWithCompensationState compensated)
            {
                HasCompensation = true;
                ConfigureAnalog(compensated.CompensationValue, null, null);
                Runtime.Update(compensated.CompensationValue, Variant.From(0.0));
            }
            UpdateValue(options.InitialValue);
        }

        /// <inheritdoc/>
        public double SensorValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_value;
                }
            }
        }

        /// <inheritdoc/>
        public double RawValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_raw;
                }
            }
        }

        /// <inheritdoc/>
        public bool HasCompensation { get; }

        /// <inheritdoc/>
        public void UpdateValue(double value, double? rawValue = null)
        {
            double raw = rawValue ?? value;
            lock (m_lock)
            {
                m_value = value;
                m_raw = raw;
            }
            Runtime.Update(m_node.SensorValue, Variant.From(value));
            Runtime.Update(m_node.RawValue, Variant.From(raw));
        }

        /// <inheritdoc/>
        public void UpdateCompensationValue(double value)
        {
            if (m_node is not AnalogScalarSensorFunctionWithCompensationState compensated)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The sensor '{0}' has no compensation value.",
                    Path);
            }
            Runtime.Update(compensated.CompensationValue, Variant.From(value));
        }

        private readonly AnalogScalarSensorFunctionState m_node;
        private readonly Lock m_lock = new();
        private double m_value;
        private double m_raw;
    }

    /// <summary>
    /// The handle of an analog array sensor function.
    /// </summary>
    internal sealed class LadsAnalogArraySensorFunction : LadsFunction, IAnalogArraySensorFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsAnalogArraySensorFunction(
            LadsFunctionalUnit unit,
            AnalogArraySensorFunctionState node,
            string path,
            LadsAnalogSensorOptions options)
            : base(unit, node, path)
        {
            m_node = node;
            ConfigureAnalog(node.SensorValue, options.EngineeringUnits, options.Range);
            ConfigureAnalog(node.RawValue, options.EngineeringUnits, options.Range);
            UpdateValue(options.InitialValues);
        }

        /// <inheritdoc/>
        public ArrayOf<double> SensorValue
        {
            get
            {
                lock (m_lock)
                {
                    return m_values;
                }
            }
        }

        /// <inheritdoc/>
        public void UpdateValue(ArrayOf<double> values, ArrayOf<double> rawValues = default)
        {
            ArrayOf<double> raw = rawValues.IsNull ? values : rawValues;
            lock (m_lock)
            {
                m_values = values;
            }
            Runtime.Update(m_node.SensorValue, Variant.From(values));
            Runtime.Update(m_node.RawValue, Variant.From(raw));
        }

        private readonly AnalogArraySensorFunctionState m_node;
        private readonly Lock m_lock = new();
        private ArrayOf<double> m_values;
    }

    /// <summary>
    /// The handle of a two-state discrete sensor function.
    /// </summary>
    internal sealed class LadsTwoStateDiscreteSensorFunction : LadsFunction, ITwoStateDiscreteSensorFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsTwoStateDiscreteSensorFunction(
            LadsFunctionalUnit unit,
            TwoStateDiscreteSensorFunctionState node,
            string path,
            LadsTwoStateDiscreteOptions options)
            : base(unit, node, path)
        {
            m_variable = LadsTwoStateDiscreteControlFunction.Configure(node.SensorValue, options);
            UpdateValue(options.InitialValue);
        }

        /// <inheritdoc/>
        public bool SensorValue => Volatile.Read(ref m_value);

        /// <inheritdoc/>
        public void UpdateValue(bool value)
        {
            Volatile.Write(ref m_value, value);
            Runtime.Update(m_variable, Variant.From(value));
        }

        private readonly TwoStateDiscreteState? m_variable;
        private bool m_value;
    }

    /// <summary>
    /// The handle of a multi-state discrete sensor function.
    /// </summary>
    internal sealed class LadsMultiStateDiscreteSensorFunction : LadsFunction, IMultiStateDiscreteSensorFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsMultiStateDiscreteSensorFunction(
            LadsFunctionalUnit unit,
            MultiStateDiscreteSensorFunctionState node,
            string path,
            LadsMultiStateDiscreteOptions options)
            : base(unit, node, path)
        {
            m_states = LadsMultiStateDiscreteControlFunction.Validate(options);
            m_variable = LadsMultiStateDiscreteControlFunction.Configure(Runtime, node.SensorValue, options);
            UpdateValue(options.InitialValue);
        }

        /// <inheritdoc/>
        public uint SensorValue => Volatile.Read(ref m_value);

        /// <inheritdoc/>
        public void UpdateValue(uint value)
        {
            LadsMultiStateDiscreteControlFunction.Check(m_states, value);
            Volatile.Write(ref m_value, value);
            Runtime.Update(m_variable, Variant.From(value));
        }

        private readonly int m_states;
        private readonly MultiStateDiscreteState<uint>? m_variable;
        private uint m_value;
    }

    /// <summary>
    /// The handle of a multi sensor function that groups sensor functions.
    /// </summary>
    internal sealed class LadsMultiSensorFunction : LadsFunction, IMultiSensorFunction
    {
        /// <summary>
        /// Creates the handle.
        /// </summary>
        public LadsMultiSensorFunction(LadsFunctionalUnit unit, MultiSensorFunctionState node, string path)
            : base(unit, node, path)
        {
        }

        /// <inheritdoc/>
        public ArrayOf<ILadsFunction> Functions => m_functions.ToArray();

        /// <summary>
        /// Adds a function of the multi sensor.
        /// </summary>
        internal void Add(ILadsFunction function)
        {
            m_functions.Add(function);
        }

        private readonly List<ILadsFunction> m_functions = [];
    }

    /// <summary>
    /// The handle of a cover function.
    /// </summary>
    internal sealed class LadsCoverFunction : LadsFunction, ICoverFunction
    {
        /// <summary>
        /// Creates the handle and binds the methods of the cover state machine.
        /// </summary>
        public LadsCoverFunction(
            LadsFunctionalUnit unit,
            CoverFunctionState node,
            string path,
            LadsCoverOptions options)
            : base(unit, node, path)
        {
            m_lockable = options.Lockable;
            if (!m_lockable && options.InitialState is LadsCoverStateValue.Locked or
                LadsCoverStateValue.Locking or LadsCoverStateValue.Unlocking)
            {
                throw new ArgumentException("A cover that cannot be locked cannot start locked.", nameof(options));
            }
            ISystemContext context = Runtime.Context;
            CoverStateMachineState stateMachine = node.CoverState ??
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "The cover state machine is missing.");
            stateMachine.AddOpen(context).AddClose(context).AddReset(context);
            if (m_lockable)
            {
                stateMachine.AddLock(context).AddUnlock(context);
            }
            m_machine = new LadsStateMachine<LadsCoverStateValue>(
                stateMachine,
                context,
                LadsStateMaps.Cover,
                options.InitialState);
            m_machine.StateChanged += state =>
            {
                if (Runtime.Logger.IsEnabled(LogLevel.Debug))
                {
                    Runtime.Logger.StateChanged(Path, state);
                }
                StateChanged?.Invoke(state);
            };
            Runtime.Bind(stateMachine.Open, (_, ct) => OpenAsync(ct));
            Runtime.Bind(stateMachine.Close, (_, ct) => CloseAsync(ct));
            Runtime.Bind(stateMachine.Reset, (_, ct) => ResetAsync(ct));
            if (m_lockable)
            {
                Runtime.Bind(stateMachine.Lock, (_, ct) => LockAsync(ct));
                Runtime.Bind(stateMachine.Unlock, (_, ct) => UnlockAsync(ct));
            }
        }

        /// <inheritdoc/>
        public LadsCoverStateValue State => m_machine.Current;

        /// <inheritdoc/>
        public event Action<LadsCoverStateValue>? StateChanged;

        /// <inheritdoc/>
        public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        {
            return CommandAsync(
                LadsFunctionCommandKind.Open,
                LadsCoverStateValue.Closed,
                cancellationToken,
                LadsCoverStateValue.Opening,
                LadsCoverStateValue.Opened);
        }

        /// <inheritdoc/>
        public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        {
            return CommandAsync(
                LadsFunctionCommandKind.Close,
                LadsCoverStateValue.Opened,
                cancellationToken,
                LadsCoverStateValue.Closing,
                LadsCoverStateValue.Closed);
        }

        /// <inheritdoc/>
        public ValueTask LockAsync(CancellationToken cancellationToken = default)
        {
            RequireLockable();
            return CommandAsync(
                LadsFunctionCommandKind.Lock,
                LadsCoverStateValue.Closed,
                cancellationToken,
                LadsCoverStateValue.Locking,
                LadsCoverStateValue.Locked);
        }

        /// <inheritdoc/>
        public ValueTask UnlockAsync(CancellationToken cancellationToken = default)
        {
            RequireLockable();
            return CommandAsync(
                LadsFunctionCommandKind.Unlock,
                LadsCoverStateValue.Locked,
                cancellationToken,
                LadsCoverStateValue.Unlocking,
                LadsCoverStateValue.Closed);
        }

        /// <inheritdoc/>
        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            return CommandAsync(
                LadsFunctionCommandKind.Reset,
                LadsCoverStateValue.Error,
                cancellationToken,
                LadsCoverStateValue.Opened);
        }

        /// <inheritdoc/>
        public ValueTask ReportErrorAsync(CancellationToken cancellationToken = default)
        {
            return ExclusiveAsync(
                async () =>
                {
                    if (m_machine.Current == LadsCoverStateValue.Error)
                    {
                        return;
                    }
                    if (!m_machine.CanTransitionTo(LadsCoverStateValue.Error) ||
                        !await m_machine.TransitionToAsync(LadsCoverStateValue.Error, cancellationToken)
                            .ConfigureAwait(false))
                    {
                        m_machine.SetState(LadsCoverStateValue.Error);
                    }
                },
                cancellationToken);
        }

        /// <summary>
        /// Runs a command of the cover state machine.
        /// </summary>
        private ValueTask CommandAsync(
            LadsFunctionCommandKind kind,
            LadsCoverStateValue expected,
            CancellationToken cancellationToken,
            params LadsCoverStateValue[] states)
        {
            return ExclusiveAsync(
                async () =>
                {
                    RequireEnabled();
                    m_machine.Require(expected);
                    await InvokeProviderAsync(kind, default, cancellationToken).ConfigureAwait(false);
                    if (!await m_machine.TransitionThroughAsync(cancellationToken, states).ConfigureAwait(false))
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadInvalidState,
                            "A transition of the cover '{0}' was vetoed.",
                            Path);
                    }
                },
                cancellationToken);
        }

        /// <summary>
        /// Rejects a lock command of a cover that cannot be locked.
        /// </summary>
        private void RequireLockable()
        {
            if (!m_lockable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotSupported,
                    "The cover '{0}' cannot be locked.",
                    Path);
            }
        }

        private readonly LadsStateMachine<LadsCoverStateValue> m_machine;
        private readonly bool m_lockable;
    }
}
