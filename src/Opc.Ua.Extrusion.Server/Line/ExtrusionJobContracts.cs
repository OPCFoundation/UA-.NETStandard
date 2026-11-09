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

using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Extrusion.Line;
using Opc.Ua.PlasticsRubber;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Immutable metadata supplied to the standard AddJobGroup method.
    /// Generated structures are used unchanged for all wire-defined fields.
    /// </summary>
    /// <param name="Id">Unique identifier within the extrusion line.</param>
    /// <param name="Description">Job group description.</param>
    /// <param name="EquipmentDescription">Required production equipment.</param>
    /// <param name="ProductionDatasetName">The required OPC 40083 production dataset.</param>
    /// <param name="MaterialMapping">Material, lot and hopper assignments.</param>
    /// <param name="Priority">One is the highest priority.</param>
    /// <param name="PlannedStart">UTC planned start, or zero when not applicable.</param>
    /// <param name="PlannedProductionTime">Planned duration in milliseconds.</param>
    /// <param name="PlannedSetUpTime">Planned setup/teardown duration in milliseconds.</param>
    /// <param name="LatestEnd">Latest UTC end, or zero when not applicable.</param>
    public sealed record ExtrusionJobGroupDefinition(
        string Id,
        string Description,
        string EquipmentDescription,
        string ProductionDatasetName,
        ArrayOf<MaterialMappingType> MaterialMapping,
        uint Priority,
        DateTimeUtc PlannedStart,
        double PlannedProductionTime,
        double PlannedSetUpTime,
        DateTimeUtc LatestEnd);

    /// <summary>
    /// Immutable metadata supplied to the standard AddJob method.
    /// </summary>
    /// <param name="Id">Identifier unique within its group, not globally across groups.</param>
    /// <param name="Description">Job description.</param>
    /// <param name="CustomerName">Customer designation.</param>
    /// <param name="ProductId">The wire method calls this argument ProductName; the variable is ProductId.</param>
    /// <param name="ProductDescription">Product description.</param>
    /// <param name="Strand">One-based parallel production strand.</param>
    /// <param name="Sequence">One-based sequence within that strand.</param>
    /// <param name="ParameterSetting">Values for the group's offered configuration parameters.</param>
    /// <param name="SetOutput">Requested number of product units.</param>
    /// <param name="LotSize">Number of units in a production lot.</param>
    public sealed record ExtrusionJobDefinition(
        string Id,
        string Description,
        string CustomerName,
        string ProductId,
        string ProductDescription,
        uint Strand,
        uint Sequence,
        ArrayOf<ParameterSettingType> ParameterSetting,
        double SetOutput,
        double LotSize);

    /// <summary>
    /// Exact extrusion job operations, independent of Machinery/ISA-95 job semantics.
    /// </summary>
    public enum ExtrusionJobOperation
    {
        /// <summary>
        /// Adds a production group.
        /// </summary>
        AddGroup,

        /// <summary>
        /// Removes an inactive group and its jobs.
        /// </summary>
        RemoveGroup,

        /// <summary>
        /// Requests starting or resuming a group.
        /// </summary>
        StartGroup,

        /// <summary>
        /// Requests interruption of an active group.
        /// </summary>
        InterruptGroup,

        /// <summary>
        /// Requests completion of a group.
        /// </summary>
        FinishGroup,

        /// <summary>
        /// Adds a job to an existing group.
        /// </summary>
        AddJob,

        /// <summary>
        /// Removes an inactive job.
        /// </summary>
        RemoveJob,

        /// <summary>
        /// Requests starting or resuming a job.
        /// </summary>
        StartJob,

        /// <summary>
        /// Requests interruption of an active job.
        /// </summary>
        InterruptJob,

        /// <summary>
        /// Requests completion of a job.
        /// </summary>
        FinishJob
    }

    /// <summary>
    /// A typed provider request retaining the end-user authorization context.
    /// </summary>
    /// <param name="Context">The originating request, not the line service identity.</param>
    /// <param name="Operation">The exact method semantics.</param>
    /// <param name="TargetId">The object on which the method was invoked.</param>
    /// <param name="Group">The complete group metadata.</param>
    /// <param name="Job">The complete job metadata for a job-specific operation.</param>
    public sealed record ExtrusionJobRequest(
        ISystemContext Context,
        ExtrusionJobOperation Operation,
        NodeId TargetId,
        ExtrusionJobGroupDefinition Group,
        ExtrusionJobDefinition? Job);

    /// <summary>
    /// Result of an authoritative job operation.
    /// </summary>
    /// <param name="StatusCode">The operation result.</param>
    /// <param name="CommittedStatus">The actual committed state, not a guessed physical transition.</param>
    /// <param name="ConfigurationParameters">Parameters offered by a newly added group's dataset.</param>
    public sealed record ExtrusionJobResult(
        StatusCode StatusCode,
        JobStatusEnumeration CommittedStatus,
        ArrayOf<ConfigurationParameterType> ConfigurationParameters = default);

    /// <summary>
    /// Commits job requests to the actual line controller or durable application.
    /// </summary>
    /// <remarks>
    /// Dataset/material/hopper references can cross server endpoints, so their authoritative validation
    /// belongs here. A successful response must mean the operation has committed. Add operations must
    /// be idempotent by group/job identifier so a transport or publication retry cannot duplicate work.
    /// Nothing in this interface grants a forwarding service more authority than the originating user.
    /// </remarks>
    public interface IExtrusionJobProvider
    {
        /// <summary>
        /// Authorizes the exact original request before committing anything.
        /// </summary>
        ValueTask<StatusCode> AuthorizeAsync(
            ExtrusionJobRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Commits the exact extrusion operation, or reports a real failure.
        /// </summary>
        ValueTask<ExtrusionJobResult> ExecuteAsync(
            ExtrusionJobRequest request,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A durable unit-completion fact accompanying an absolute production snapshot.
    /// </summary>
    /// <param name="Unit">The absolute one-based completed-unit number.</param>
    /// <param name="GoodProduct">The quality of that completed unit, not the current later unit.</param>
    public readonly record struct ExtrusionFinishedUnit(uint Unit, bool GoodProduct);

    /// <summary>
    /// Absolute committed production state and its exact completion facts.
    /// </summary>
    /// <param name="Revision">Monotonic commit sequence for this job.</param>
    /// <param name="GroupId">The containing group identifier.</param>
    /// <param name="JobId">The job identifier within that group.</param>
    /// <param name="Status">The authoritative job state.</param>
    /// <param name="ActualLot">The currently produced lot.</param>
    /// <param name="ActualOutput">Total finished units, never a delta.</param>
    /// <param name="ActualGoodOutput">Total good units.</param>
    /// <param name="ActualBadOutput">Total bad units.</param>
    /// <param name="ActualSampleOutput">Total sample units.</param>
    /// <param name="ActualOutputRate">Average completed units per hour.</param>
    /// <param name="GoodProduct">Current production quality.</param>
    /// <param name="SourceTimestamp">The original commit timestamp.</param>
    /// <param name="FinishedUnits">Exact unit events in this commit; empty for restoration baselines.</param>
    /// <param name="FinishedLots">Exact finished lot numbers; empty for restoration baselines.</param>
    public sealed record ExtrusionJobProductionSnapshot(
        ulong Revision,
        string GroupId,
        string JobId,
        JobStatusEnumeration Status,
        uint ActualLot,
        double ActualOutput,
        double ActualGoodOutput,
        double ActualBadOutput,
        double ActualSampleOutput,
        double ActualOutputRate,
        bool GoodProduct,
        DateTimeUtc SourceTimestamp,
        ArrayOf<ExtrusionFinishedUnit> FinishedUnits = default,
        ArrayOf<uint> FinishedLots = default);
}
