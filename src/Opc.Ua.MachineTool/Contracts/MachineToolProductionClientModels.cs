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

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Bounds an entire retained-production read across all jobs, programs, part sets and parts, not just one page.
    /// </summary>
    public sealed record MachineToolProductionReadOptions
    {
        /// <summary>
        /// Gets the combined element bound, including ActiveProgram, from one through 65536.
        /// </summary>
        public int MaximumElements { get; init; } = 4096;

        /// <summary>
        /// Gets the combined browse-reference bound, including nonmatching vendor members.
        /// </summary>
        public int MaximumReferences { get; init; } = 16384;

        /// <summary>
        /// Gets the decoded sample-payload limit, from one through one mebibyte.
        /// </summary>
        public int MaximumPayloadBytes { get; init; } = 1024 * 1024;

        /// <summary>
        /// Rejects invalid bounds before any service operation.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Validate()
        {
            if (MaximumElements is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumElements));
            }
            if (MaximumReferences is < 1 or > 100000)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumReferences));
            }
            if (MaximumPayloadBytes is < 1 or > 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(MaximumPayloadBytes));
            }
        }
    }

    /// <summary>
    /// Reads the actual retained production topology. Samples retain independent acquisition timestamps;
    /// this is not an atomic equipment snapshot or a history beyond the modeled LastTransition.
    /// </summary>
    /// <param name="MachineId">The actual machine instance.</param>
    /// <param name="ProductionPlanId">The optional plan, or NodeId.Null.</param>
    /// <param name="NodeVersion">The plan's optional topology version.</param>
    /// <param name="ActiveProgram">The mandatory current active-program view.</param>
    /// <param name="Jobs">The bounded ordered job inventory.</param>
    /// <param name="PartsProducedInLifetime">The optional retained UInt32 statistic, or DataValue.Null.</param>
    public sealed record MachineToolProductionReadSnapshot(
        NodeId MachineId, NodeId ProductionPlanId, DataValue NodeVersion,
        MachineToolProgramReadSnapshot ActiveProgram, ArrayOf<MachineToolJobReadSnapshot> Jobs,
        DataValue PartsProducedInLifetime);

    /// <summary>
    /// Preserves the standard state and last-transition history, including canonical IDs, numbers and raw quality.
    /// Missing optional history uses DataValue.Null, never an invented transition.
    /// </summary>
    /// <param name="NodeId">The actual state machine.</param>
    /// <param name="CurrentState">The current localized state.</param>
    /// <param name="CurrentStateId">The canonical state declaration identifier.</param>
    /// <param name="CurrentStateNumber">The optional standard state number.</param>
    /// <param name="LastTransition">The optional last-transition name.</param>
    /// <param name="LastTransitionId">The optional canonical transition identifier.</param>
    /// <param name="LastTransitionNumber">The optional standard transition number.</param>
    /// <param name="LastTransitionTime">The optional equipment transition time.</param>
    public sealed record MachineToolProductionStateReadSnapshot(
        NodeId NodeId, DataValue CurrentState, DataValue CurrentStateId, DataValue CurrentStateNumber,
        DataValue LastTransition, DataValue LastTransitionId,
        DataValue LastTransitionNumber, DataValue LastTransitionTime);

    /// <summary>
    /// Reads a generated program or active-program subtype without replacing its real identity or optional state.
    /// </summary>
    /// <param name="NodeId">The actual program.</param>
    /// <param name="TypeDefinitionId">Its actual companion or vendor type.</param>
    /// <param name="BrowseName">Its qualified browse name.</param>
    /// <param name="Name">Its reported program name.</param>
    /// <param name="NumberInList">Its actual ordered-list position.</param>
    /// <param name="JobIdentifier">The active-program job identifier, absent on ordinary programs.</param>
    /// <param name="JobNodeId">The active-program job reference, absent on ordinary programs.</param>
    /// <param name="State">Its state and history, null when an ordinary program omits the optional state.</param>
    public sealed record MachineToolProgramReadSnapshot(
        NodeId NodeId, NodeId TypeDefinitionId, QualifiedName BrowseName,
        DataValue Name, DataValue NumberInList, DataValue JobIdentifier, DataValue JobNodeId,
        MachineToolProductionStateReadSnapshot? State);

    /// <summary>
    /// Reads one retained job, its cumulative counters, programs and current-run parts.
    /// </summary>
    /// <param name="NodeId">The actual job.</param>
    /// <param name="TypeDefinitionId">Its actual companion or vendor type.</param>
    /// <param name="BrowseName">Its qualified browse name.</param>
    /// <param name="Identifier">The job identifier.</param>
    /// <param name="NumberInList">Its ordered-list position.</param>
    /// <param name="RunsCompleted">The cumulative completed-run count.</param>
    /// <param name="RunsPlanned">The planned-run count.</param>
    /// <param name="RunsPlannedIsValid">The validity of the planned-run count.</param>
    /// <param name="CustomerOrderIdentifier">The optional customer order.</param>
    /// <param name="OrderIdentifier">The optional internal order.</param>
    /// <param name="PartsCompleted">The optional cumulative completed-part count.</param>
    /// <param name="PartsGood">The optional cumulative good-part count.</param>
    /// <param name="State">The standard state and last transition.</param>
    /// <param name="Programs">The bounded ordered programs.</param>
    /// <param name="PartSets">The bounded current-run part sets.</param>
    public sealed record MachineToolJobReadSnapshot(
        NodeId NodeId, NodeId TypeDefinitionId, QualifiedName BrowseName,
        DataValue Identifier, DataValue NumberInList, DataValue RunsCompleted, DataValue RunsPlanned,
        DataValue RunsPlannedIsValid, DataValue CustomerOrderIdentifier, DataValue OrderIdentifier,
        DataValue PartsCompleted, DataValue PartsGood, MachineToolProductionStateReadSnapshot State,
        ArrayOf<MachineToolProgramReadSnapshot> Programs, ArrayOf<MachineToolPartSetReadSnapshot> PartSets);

    /// <summary>
    /// Reads a part set's actual current-run counters and optional individual parts.
    /// </summary>
    /// <param name="NodeId">The actual part set.</param>
    /// <param name="TypeDefinitionId">Its actual companion or vendor type.</param>
    /// <param name="BrowseName">Its qualified browse name.</param>
    /// <param name="Name">The optional set name.</param>
    /// <param name="ContainsMixedParts">Whether the set contains different kinds of part.</param>
    /// <param name="PartsPlannedPerRun">The current-run target.</param>
    /// <param name="PartsCompletedPerRun">The current-run completed count.</param>
    /// <param name="Parts">The bounded actual part nodes, empty for aggregate-only reporting.</param>
    public sealed record MachineToolPartSetReadSnapshot(
        NodeId NodeId, NodeId TypeDefinitionId, QualifiedName BrowseName,
        DataValue Name, DataValue ContainsMixedParts, DataValue PartsPlannedPerRun, DataValue PartsCompletedPerRun,
        ArrayOf<MachineToolPartReadSnapshot> Parts);

    /// <summary>
    /// Reads a generated part subtype's identity, quality, irregularity and standard state history.
    /// </summary>
    /// <param name="NodeId">The actual part.</param>
    /// <param name="TypeDefinitionId">Its actual companion or vendor type.</param>
    /// <param name="BrowseName">Its qualified browse name.</param>
    /// <param name="Name">The part name.</param>
    /// <param name="Quality">The process-step quality enumeration.</param>
    /// <param name="Irregularity">The process irregularity enumeration.</param>
    /// <param name="Identifier">The optional workpiece identifier.</param>
    /// <param name="CustomerOrderIdentifier">The optional customer order.</param>
    /// <param name="State">The state and last transition.</param>
    public sealed record MachineToolPartReadSnapshot(
        NodeId NodeId, NodeId TypeDefinitionId, QualifiedName BrowseName,
        DataValue Name, DataValue Quality, DataValue Irregularity,
        DataValue Identifier, DataValue CustomerOrderIdentifier, MachineToolProductionStateReadSnapshot State);
}
