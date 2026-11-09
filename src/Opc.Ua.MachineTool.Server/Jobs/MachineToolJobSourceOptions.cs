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

using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Bounds an isolated, single-host, non-durable reference job source, not a CNC execution system.
    /// </summary>
    public sealed record MachineToolJobSourceOptions
    {
        /// <summary>
        /// Gets explicit application mappings in legacy production-plan order.
        /// Only these job and work-master identities can be downloaded.
        /// </summary>
        public ArrayOf<MachineToolJobSourceDescriptor> Jobs { get; init; }

        /// <summary>
        /// Gets the maximum number of concurrently stored orders, from 1 through 65535.
        /// </summary>
        public int MaximumJobOrders { get; init; } = 64;

        /// <summary>
        /// Gets the maximum number of explicit responses retained until source disposal.
        /// Responses are not synthesized or silently expired.
        /// </summary>
        public int MaximumJobResponses { get; init; } = 128;

        /// <summary>
        /// Gets the maximum number of undelivered coherent revisions, including the initial observation.
        /// Mutations are refused rather than dropping intermediate production transitions.
        /// </summary>
        public int MaximumPendingRevisions { get; init; } = 128;

        /// <summary>
        /// Gets the maximum total number of independent change-stream subscribers.
        /// </summary>
        public int MaximumSubscribers { get; init; } = 16;

        /// <summary>
        /// Gets the maximum backlog per subscriber. A slow subscriber applies backpressure, never event loss.
        /// </summary>
        public int MaximumPendingChanges { get; init; } = 128;

        /// <summary>
        /// Gets the maximum encoded bytes in one order, response, or comment collection.
        /// </summary>
        public int MaximumPayloadBytes { get; init; } = 65536;

        /// <summary>
        /// Gets the maximum UTF-8 byte length of protocol text and character length of observation text.
        /// </summary>
        public int MaximumTextLength { get; init; } = 1024;

        /// <summary>
        /// Gets the maximum protocol array length and total monitoring inventory size.
        /// </summary>
        public int MaximumArrayLength { get; init; } = 1024;
    }

    /// <summary>
    /// Explicitly relates one modern order and work master to one legacy program and optional aggregate part set.
    /// This reference mapping deliberately does not infer programs or physical parts from arbitrary parameters.
    /// </summary>
    public sealed record MachineToolJobSourceDescriptor
    {
        /// <summary>
        /// Gets the exact modern and legacy job identifier.
        /// </summary>
        public required string JobOrderId { get; init; }

        /// <summary>
        /// Gets the sole work-master identifier required on Store, StoreAndStart and Update.
        /// </summary>
        public required string WorkMasterId { get; init; }

        /// <summary>
        /// Gets the application-supplied program name, also used as its stable legacy slot key.
        /// </summary>
        public required string ProgramName { get; init; }

        /// <summary>
        /// Gets the known positive number of planned runs.
        /// </summary>
        public uint RunsPlanned { get; init; } = 1;

        /// <summary>
        /// Gets the optional customer order identifier; it is never guessed from ISA-95 descriptions.
        /// </summary>
        public string CustomerOrderIdentifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets the optional internal production order identifier.
        /// </summary>
        public string OrderIdentifier { get; init; } = string.Empty;

        /// <summary>
        /// Gets an optional aggregate part-set key. An empty key disables part monitoring for this job.
        /// </summary>
        public string PartSetKey { get; init; } = string.Empty;

        /// <summary>
        /// Gets the application-supplied part-set name.
        /// </summary>
        public string PartSetName { get; init; } = string.Empty;

        /// <summary>
        /// Gets the positive planned number of parts per run when a part set is declared.
        /// Actual completed and good counts must be supplied explicitly.
        /// </summary>
        public uint PartsPlannedPerRun { get; init; }
    }

    /// <summary>
    /// Explicit aggregate observations for the current run; good includes the standard Good,
    /// CapabilityUnavailable and WillNotBeMeasured categories, not merely successfully measured parts.
    /// </summary>
    /// <param name="Completed">Completed parts in this run, including aborted parts.</param>
    /// <param name="Good">Good completed parts in this run.</param>
    /// <param name="Sample">Unmodified acquisition timestamp and quality of these observations.</param>
    public readonly record struct MachineToolJobSourcePartCounts(
        uint Completed, uint Good, MachineToolSampleInfo Sample);

    /// <summary>
    /// A detached, point-in-time capture of both production representations at Snapshot.Revision.
    /// Generated protocol objects are copied at the boundary and cannot mutate the source.
    /// </summary>
    public sealed record MachineToolJobSourceCapture
    {
        /// <summary>
        /// Gets the immutable monitoring and retained-production observation.
        /// </summary>
        public required MachineToolSnapshot Snapshot { get; init; }

        /// <summary>
        /// Gets the accepted modern orders and their engine-owned canonical state paths.
        /// </summary>
        public ArrayOf<V2.ISA95JobOrderAndStateDataType> JobOrders { get; init; }

        /// <summary>
        /// Gets explicitly ingested responses, which describe their accepted historical states.
        /// </summary>
        public ArrayOf<V2.ISA95JobResponseDataType> JobResponses { get; init; }
    }
}
