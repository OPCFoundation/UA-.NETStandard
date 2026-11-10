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

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// A versioned, complete snapshot of durable device data.
    /// </summary>
    public sealed class LadsStoredDeviceData
    {
        /// <summary>
        /// Gets or sets the storage schema version; only version one is currently supported.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Gets or sets the stable identity used as the store key.
        /// </summary>
        public string Identity { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the functional unit snapshots.
        /// </summary>
        public ArrayOf<LadsStoredFunctionalUnitData> FunctionalUnits { get; set; }

        /// <summary>
        /// Gets or sets the maintenance task snapshots.
        /// </summary>
        public ArrayOf<LadsStoredMaintenanceData> MaintenanceTasks { get; set; }

        /// <summary>
        /// Gets or sets the accumulated operation cycle count.
        /// </summary>
        public uint OperationCycles { get; set; }

        /// <summary>
        /// Gets or sets the accumulated operation duration in milliseconds.
        /// </summary>
        public double OperationDurationMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the accumulated power-on duration in milliseconds.
        /// </summary>
        public double PowerOnDurationMilliseconds { get; set; }
    }

    /// <summary>
    /// Durable program data of a functional unit.
    /// </summary>
    public sealed class LadsStoredFunctionalUnitData
    {
        /// <summary>
        /// Gets or sets the functional unit name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the stored program templates.
        /// </summary>
        public ArrayOf<LadsStoredTemplateData> Templates { get; set; }

        /// <summary>
        /// Gets or sets explicitly deleted template identifiers, including deleted configured templates.
        /// Re-adding a template must remove its identifier from this collection.
        /// </summary>
        public ArrayOf<string> RemovedTemplateIds { get; set; }

        /// <summary>
        /// Gets or sets the stored program results.
        /// </summary>
        public ArrayOf<LadsStoredResultData> Results { get; set; }

        /// <summary>
        /// Gets or sets the pending run identifier recorded before execution, never automatic execution resume.
        /// </summary>
        public string? InterruptedRunId { get; set; }

        /// <summary>
        /// Gets or sets the last interrupted run identifier retained for reporting after later runs complete.
        /// </summary>
        public string? LastInterruptedRunId { get; set; }
    }

    /// <summary>
    /// Program template metadata and content produced by <see cref="LadsProgramTemplate.ToContent"/>.
    /// </summary>
    public sealed class LadsStoredTemplateData
    {
        /// <summary>
        /// Gets or sets the template identifier.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the author.
        /// </summary>
        public string Author { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the version.
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the localized description.
        /// </summary>
        public LocalizedText Description { get; set; }

        /// <summary>
        /// Gets or sets the supervisory system template identifier.
        /// </summary>
        public string? SupervisoryTemplateId { get; set; }

        /// <summary>
        /// Gets or sets the creation timestamp.
        /// </summary>
        public DateTimeUtc Created { get; set; }

        /// <summary>
        /// Gets or sets the last modification timestamp.
        /// </summary>
        public DateTimeUtc Modified { get; set; }

        /// <summary>
        /// Gets or sets the serialized program content.
        /// </summary>
        public ByteString Content { get; set; }
    }

    /// <summary>
    /// A complete program result, including embedded file bytes.
    /// </summary>
    public sealed class LadsStoredResultData
    {
        /// <summary>
        /// Gets or sets the run identifier.
        /// </summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the template snapshot used by the run.
        /// </summary>
        public LadsStoredTemplateData Template { get; set; } = new();

        /// <summary>
        /// Gets or sets the run outcome.
        /// </summary>
        public LadsProgramOutcome Outcome { get; set; }

        /// <summary>
        /// Gets or sets the start timestamp.
        /// </summary>
        public DateTimeUtc Started { get; set; }

        /// <summary>
        /// Gets or sets the stop timestamp.
        /// </summary>
        public DateTimeUtc Stopped { get; set; }

        /// <summary>
        /// Gets or sets the properties supplied when starting the run.
        /// </summary>
        public ArrayOf<KeyValueType> Properties { get; set; }

        /// <summary>
        /// Gets or sets the samples processed by the run.
        /// </summary>
        public ArrayOf<SampleInfoType> Samples { get; set; }

        /// <summary>
        /// Gets or sets the supervisory job identifier.
        /// </summary>
        public string SupervisoryJobId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the supervisory task identifier.
        /// </summary>
        public string SupervisoryTaskId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the recorded result variables.
        /// </summary>
        public ArrayOf<KeyValueType> Variables { get; set; }

        /// <summary>
        /// Gets or sets the result files with their complete contents.
        /// </summary>
        public ArrayOf<LadsResultFile> Files { get; set; }

        /// <summary>
        /// Gets or sets the total elapsed duration in milliseconds, including pauses.
        /// </summary>
        public double TotalRuntimeMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the total pause duration in milliseconds.
        /// </summary>
        public double TotalPauseTimeMilliseconds { get; set; }
    }

    /// <summary>
    /// Durable state of a maintenance task; executing state does not authorize physical execution resume.
    /// </summary>
    public sealed class LadsStoredMaintenanceData
    {
        /// <summary>
        /// Gets or sets the task name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the recorded maintenance state.
        /// </summary>
        public LadsMaintenanceStateValue State { get; set; }

        /// <summary>
        /// Gets or sets the last execution timestamp.
        /// </summary>
        public DateTimeUtc LastExecutionDate { get; set; }

        /// <summary>
        /// Gets or sets the last execution result.
        /// </summary>
        public MaintenanceTaskResultEnum LastResult { get; set; } = MaintenanceTaskResultEnum.Undetermined;

        /// <summary>
        /// Gets or sets the planned execution timestamp.
        /// </summary>
        public DateTimeUtc PlannedDate { get; set; }

        /// <summary>
        /// Gets or sets the localized execution comment.
        /// </summary>
        public LocalizedText Comment { get; set; }
    }
}
