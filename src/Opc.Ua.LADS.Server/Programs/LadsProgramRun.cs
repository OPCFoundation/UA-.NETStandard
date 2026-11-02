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

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// A program run of a functional unit, handed to the <see cref="ILadsProgramExecutor"/>.
    /// </summary>
    public sealed class LadsProgramRun
    {
        /// <summary>
        /// Creates a run.
        /// </summary>
        /// <param name="runId">The device program run identifier.</param>
        /// <param name="functionalUnit">The functional unit.</param>
        /// <param name="template">The template.</param>
        /// <param name="properties">The run properties.</param>
        /// <param name="supervisoryJobId">The supervisory job identifier.</param>
        /// <param name="supervisoryTaskId">The supervisory task identifier.</param>
        /// <param name="samples">The samples.</param>
        /// <param name="started">When the run started.</param>
        /// <param name="timeProvider">The time provider.</param>
        /// <param name="onStep">Called when the executor reports a step.</param>
        internal LadsProgramRun(
            string runId,
            ILadsFunctionalUnit functionalUnit,
            LadsProgramTemplate template,
            ArrayOf<KeyValueType> properties,
            string supervisoryJobId,
            string supervisoryTaskId,
            ArrayOf<SampleInfoType> samples,
            DateTimeUtc started,
            TimeProvider timeProvider,
            Action<uint, string> onStep)
        {
            RunId = runId;
            FunctionalUnit = functionalUnit;
            Template = template;
            Properties = properties;
            SupervisoryJobId = supervisoryJobId;
            SupervisoryTaskId = supervisoryTaskId;
            Samples = samples;
            Started = started;
            TimeProvider = timeProvider;
            m_onStep = onStep;
        }

        /// <summary>
        /// The device program run identifier.
        /// </summary>
        public string RunId { get; }

        /// <summary>
        /// The functional unit executing the run.
        /// </summary>
        public ILadsFunctionalUnit FunctionalUnit { get; }

        /// <summary>
        /// The template the run executes.
        /// </summary>
        public LadsProgramTemplate Template { get; }

        /// <summary>
        /// The properties passed to StartProgram.
        /// </summary>
        public ArrayOf<KeyValueType> Properties { get; }

        /// <summary>
        /// The supervisory job identifier.
        /// </summary>
        public string SupervisoryJobId { get; }

        /// <summary>
        /// The supervisory task identifier.
        /// </summary>
        public string SupervisoryTaskId { get; }

        /// <summary>
        /// The samples processed by the run.
        /// </summary>
        public ArrayOf<SampleInfoType> Samples { get; }

        /// <summary>
        /// When the run started.
        /// </summary>
        public DateTimeUtc Started { get; }

        /// <summary>
        /// The time provider used for delays and timestamps.
        /// </summary>
        public TimeProvider TimeProvider { get; }

        /// <summary>
        /// The number of the current step, starting at 1, or 0 before the first step.
        /// </summary>
        public uint CurrentStepNumber
        {
            get
            {
                lock (m_lock)
                {
                    return m_stepNumber;
                }
            }
        }

        /// <summary>
        /// The files added to the result so far.
        /// </summary>
        public ArrayOf<LadsResultFile> Files
        {
            get
            {
                lock (m_lock)
                {
                    return m_files.ToArray();
                }
            }
        }

        /// <summary>
        /// The variables added to the result so far.
        /// </summary>
        public ArrayOf<KeyValueType> Variables
        {
            get
            {
                lock (m_lock)
                {
                    var variables = new KeyValueType[m_variables.Count];
                    int index = 0;
                    foreach (KeyValuePair<string, string> variable in m_variables)
                    {
                        variables[index++] = new KeyValueType { Key = variable.Key, Value = variable.Value };
                    }
                    return variables;
                }
            }
        }

        /// <summary>
        /// Publishes the current step in the ActiveProgram object.
        /// </summary>
        /// <param name="number">The step number, starting at 1.</param>
        /// <param name="name">The step name.</param>
        public void ReportStep(uint number, string name)
        {
            lock (m_lock)
            {
                m_stepNumber = number;
            }
            m_onStep(number, name ?? string.Empty);
        }

        /// <summary>
        /// Adds a file to the result of the run.
        /// </summary>
        /// <param name="name">The file name.</param>
        /// <param name="mimeType">The MIME type.</param>
        /// <param name="content">The content.</param>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadBrowseNameDuplicated"/> when a file with the name exists.
        /// </exception>
        public void AddResultFile(string name, string mimeType, ByteString content)
        {
            var file = new LadsResultFile(name, mimeType, content);
            lock (m_lock)
            {
                foreach (LadsResultFile existing in m_files)
                {
                    if (existing.Name == file.Name)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadBrowseNameDuplicated,
                            "The result already has a file named '{0}'.",
                            name);
                    }
                }
                m_files.Add(file);
            }
        }

        /// <summary>
        /// Adds or replaces a variable of the result of the run.
        /// </summary>
        /// <param name="name">The variable name.</param>
        /// <param name="value">The value.</param>
        public void SetResultVariable(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A result variable requires a name.", nameof(name));
            }
            lock (m_lock)
            {
                m_variables[name] = value ?? string.Empty;
            }
        }

        /// <summary>
        /// Applies a setpoint to the matching function of the functional unit: control functions take the
        /// value as target value and are started when stopped.
        /// </summary>
        /// <param name="setpoint">The setpoint.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes when the setpoint was applied.</returns>
        /// <exception cref="ServiceResultException">
        /// <see cref="StatusCodes.BadNotFound"/> when no function has the name, or
        /// <see cref="StatusCodes.BadTypeMismatch"/> when the value does not match the function.
        /// </exception>
        public async ValueTask ApplySetpointAsync(LadsSetpoint setpoint, CancellationToken cancellationToken = default)
        {
            if (setpoint == null)
            {
                throw new ArgumentNullException(nameof(setpoint));
            }
            ILadsFunction function = FunctionalUnit.GetFunction<ILadsFunction>(setpoint.FunctionName);
            Variant value = setpoint.Value;
            bool stopped = function is ILadsControlFunction control &&
                control.State == LadsFunctionalStateValue.Stopped;
            switch (function)
            {
                case IAnalogControlFunction analog when value.TryGetValue(out double number):
                    if (stopped)
                    {
                        await analog.StartWithTargetValueAsync(number, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await analog.SetTargetValueAsync(number, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                case ITimerFunction timer when value.TryGetValue(out double duration):
                    await timer.SetTargetValueAsync(duration, cancellationToken).ConfigureAwait(false);
                    if (stopped)
                    {
                        await timer.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    return;
                case ITwoStateDiscreteControlFunction twoState when value.TryGetValue(out bool flag):
                    if (stopped)
                    {
                        await twoState.StartWithTargetValueAsync(flag, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await twoState.SetTargetValueAsync(flag, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                case IMultiStateDiscreteControlFunction multiState when value.TryGetValue(out uint index):
                    if (stopped)
                    {
                        await multiState.StartWithTargetValueAsync(index, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await multiState.SetTargetValueAsync(index, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                case ICoverFunction cover when value.TryGetValue(out bool open):
                    if (open)
                    {
                        await cover.OpenAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await cover.CloseAsync(cancellationToken).ConfigureAwait(false);
                    }
                    return;
                default:
                    throw ServiceResultException.Create(
                        StatusCodes.BadTypeMismatch,
                        "The setpoint value does not match the function '{0}'.",
                        setpoint.FunctionName);
            }
        }

        /// <summary>
        /// Waits for a duration on the run's time provider.
        /// </summary>
        /// <param name="delay">The duration.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A task that completes after the duration.</returns>
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (delay <= TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }
            return TimeProvider.Delay(delay, cancellationToken);
        }

        private readonly Action<uint, string> m_onStep;
        private readonly Lock m_lock = new();
        private readonly List<LadsResultFile> m_files = [];
        private readonly Dictionary<string, string> m_variables = new(StringComparer.Ordinal);
        private uint m_stepNumber;
    }
}
