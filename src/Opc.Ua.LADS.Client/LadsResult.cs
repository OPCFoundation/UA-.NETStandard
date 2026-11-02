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
    /// A result of a functional unit's <c>ResultSet</c>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the result.</param>
    /// <param name="Name">The browse name of the result.</param>
    public sealed record LadsResult(NodeId NodeId, string Name)
    {
        /// <summary>
        /// Gets the display name.
        /// </summary>
        public string? DisplayName { get; init; }

        /// <summary>
        /// Gets the description.
        /// </summary>
        public string? Description { get; init; }

        /// <summary>
        /// Gets the application URI of the application that created the result.
        /// </summary>
        public string? ApplicationUri { get; init; }

        /// <summary>
        /// Gets the user that started the program.
        /// </summary>
        public string? User { get; init; }

        /// <summary>
        /// Gets the supervisory job id passed to <c>StartProgram</c>.
        /// </summary>
        public string? SupervisoryJobId { get; init; }

        /// <summary>
        /// Gets the supervisory task id passed to <c>StartProgram</c>.
        /// </summary>
        public string? SupervisoryTaskId { get; init; }

        /// <summary>
        /// Gets the run id the device assigned.
        /// </summary>
        public string? DeviceProgramRunId { get; init; }

        /// <summary>
        /// Gets the start time.
        /// </summary>
        public DateTimeUtc Started { get; init; }

        /// <summary>
        /// Gets the stop time.
        /// </summary>
        public DateTimeUtc Stopped { get; init; }

        /// <summary>
        /// Gets the total runtime.
        /// </summary>
        public TimeSpan? TotalRuntime { get; init; }

        /// <summary>
        /// Gets the total pause time.
        /// </summary>
        public TimeSpan? TotalPauseTime { get; init; }

        /// <summary>
        /// Gets the estimated runtime.
        /// </summary>
        public TimeSpan? EstimatedRuntime { get; init; }

        /// <summary>
        /// Gets the properties passed to <c>StartProgram</c>.
        /// </summary>
        public ArrayOf<KeyValueType> Properties { get; init; } = [];

        /// <summary>
        /// Gets the samples passed to <c>StartProgram</c>.
        /// </summary>
        public ArrayOf<SampleInfoType> Samples { get; init; } = [];

        /// <summary>
        /// Gets the result files.
        /// </summary>
        public ArrayOf<LadsResultFile> Files { get; init; } = [];
    }
}
