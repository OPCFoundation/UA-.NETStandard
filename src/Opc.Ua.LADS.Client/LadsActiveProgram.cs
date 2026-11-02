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

namespace Opc.Ua.LADS.Client
{
    /// <summary>
    /// The progress of the program a functional unit runs, read from
    /// <c>ProgramManager/ActiveProgram</c>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the <c>ActiveProgram</c> object.</param>
    public sealed record LadsActiveProgram(NodeId NodeId)
    {
        /// <summary>
        /// Gets the name of the current program template.
        /// </summary>
        public string? CurrentProgramTemplate { get; init; }

        /// <summary>
        /// Gets the NodeId of the current program template.
        /// </summary>
        public NodeId CurrentProgramTemplateId { get; init; }

        /// <summary>
        /// Gets the run id the device assigned.
        /// </summary>
        public string? DeviceProgramRunId { get; init; }

        /// <summary>
        /// Gets the name of the current step.
        /// </summary>
        public string? CurrentStepName { get; init; }

        /// <summary>
        /// Gets the number of the current step.
        /// </summary>
        public uint? CurrentStepNumber { get; init; }

        /// <summary>
        /// Gets the estimated number of steps.
        /// </summary>
        public uint? EstimatedStepNumbers { get; init; }

        /// <summary>
        /// Gets the runtime of the program.
        /// </summary>
        public TimeSpan? CurrentRuntime { get; init; }

        /// <summary>
        /// Gets the pause time of the program.
        /// </summary>
        public TimeSpan? CurrentPauseTime { get; init; }

        /// <summary>
        /// Gets the runtime of the current step.
        /// </summary>
        public TimeSpan? CurrentStepRuntime { get; init; }

        /// <summary>
        /// Gets the estimated runtime of the program.
        /// </summary>
        public TimeSpan? EstimatedRuntime { get; init; }

        /// <summary>
        /// Gets the estimated runtime of the current step.
        /// </summary>
        public TimeSpan? EstimatedStepRuntime { get; init; }

        /// <summary>
        /// Gets the progress between 0 and 1 from the runtime and the estimated
        /// runtime, or <c>null</c> when either is unknown.
        /// </summary>
        public double? Progress
        {
            get
            {
                if (CurrentRuntime is not { } runtime ||
                    EstimatedRuntime is not { } estimated ||
                    estimated <= TimeSpan.Zero)
                {
                    return null;
                }
                return Math.Min(1.0, Math.Max(0.0, runtime.TotalMilliseconds / estimated.TotalMilliseconds));
            }
        }
    }
}
