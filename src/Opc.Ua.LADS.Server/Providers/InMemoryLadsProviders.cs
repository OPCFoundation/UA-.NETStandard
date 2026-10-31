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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The default device provider: accepts every device state, operation mode and maintenance change.
    /// </summary>
    public sealed class InMemoryLadsDeviceProvider : ILadsDeviceProvider
    {
        /// <inheritdoc/>
        public ValueTask<ServiceResult> OnDeviceStateChangingAsync(
            ILadsDevice device,
            LadsDeviceStateValue target,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> OnOperationModeChangingAsync(
            ILadsDevice device,
            MachineryOperationModeValue target,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }

        /// <inheritdoc/>
        public ValueTask<ServiceResult> OnMaintenanceTaskAsync(
            ILadsMaintenanceTask task,
            LadsMaintenanceCommand command,
            CancellationToken cancellationToken)
        {
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }
    }

    /// <summary>
    /// The default function provider: simulates ideal hardware. Every command is accepted and a new target
    /// value of an analog or discrete control function is reached immediately.
    /// </summary>
    public sealed class InMemoryLadsFunctionProvider : ILadsFunctionProvider
    {
        /// <inheritdoc/>
        public ValueTask<ServiceResult> OnCommandAsync(LadsFunctionCommand command, CancellationToken cancellationToken)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }
            if (command.Kind is LadsFunctionCommandKind.SetTargetValue or LadsFunctionCommandKind.StartWithTargetValue)
            {
                switch (command.Function)
                {
                    case IAnalogControlFunction analog when command.Value.TryGetValue(out double number):
                        analog.UpdateCurrentValue(number);
                        break;
                    case ITwoStateDiscreteControlFunction twoState when command.Value.TryGetValue(out bool flag):
                        twoState.UpdateCurrentValue(flag);
                        break;
                    case IMultiStateDiscreteControlFunction multiState when command.Value.TryGetValue(out uint index):
                        multiState.UpdateCurrentValue(index);
                        break;
                }
            }
            return new ValueTask<ServiceResult>(ServiceResult.Good);
        }
    }

    /// <summary>
    /// The default program executor: runs the template steps in order, applies their setpoints, waits for
    /// their durations on the run's time provider and writes a CSV protocol as result file.
    /// </summary>
    public sealed class InMemoryLadsProgramExecutor : ILadsProgramExecutor
    {
        /// <inheritdoc/>
        public async ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }
            var protocol = new StringBuilder("step;name;started;durationMs\n");
            uint number = 0;
            ArrayOf<LadsProgramStep> steps = run.Template.Steps;
            for (int ii = 0; ii < steps.Count; ii++)
            {
                LadsProgramStep step = steps[ii];
                cancellationToken.ThrowIfCancellationRequested();
                number++;
                run.ReportStep(number, step.Name);
                ArrayOf<LadsSetpoint> setpoints = step.Setpoints;
                for (int jj = 0; jj < setpoints.Count; jj++)
                {
                    await run.ApplySetpointAsync(setpoints[jj], cancellationToken).ConfigureAwait(false);
                }
                protocol.Append(number.ToString(CultureInfo.InvariantCulture))
                    .Append(';')
                    .Append(step.Name)
                    .Append(';')
                    .Append(run.TimeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture))
                    .Append(';')
                    .Append(((long)step.Duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
                await run.DelayAsync(step.Duration, cancellationToken).ConfigureAwait(false);
            }
            run.SetResultVariable("Steps", number.ToString(CultureInfo.InvariantCulture));
            run.AddResultFile("Protocol.csv", "text/csv", ByteString.From(Encoding.UTF8.GetBytes(protocol.ToString())));
        }
    }
}
