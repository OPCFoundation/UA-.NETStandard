/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Describes one standard OPC UA aggregate function and the optional
    /// conformance unit names the "Historical Aggregate 2022 Server
    /// Facet" and "Historical Aggregate Client Facet" profiles use to
    /// advertise support for it individually.
    /// </summary>
    /// <param name="Name">
    /// The aggregate function's short name, e.g. <c>Average</c>. Matches
    /// the suffix of the corresponding <see cref="ObjectIds"/> /
    /// <see cref="BrowseNames"/> <c>AggregateFunction_*</c>
    /// members.
    /// </param>
    /// <param name="AggregateId">
    /// The well-known <see cref="NodeId"/> of the
    /// <c>AggregateFunction</c> Object, e.g.
    /// <see cref="ObjectIds.AggregateFunction_Average"/>.
    /// </param>
    /// <param name="BrowseName">
    /// The Object's browse name, e.g.
    /// <see cref="BrowseNames.AggregateFunction_Average"/>.
    /// </param>
    /// <param name="ServerConformanceUnit">
    /// The name of the optional conformance unit on the "Historical
    /// Aggregate 2022 Server Facet" that covers this function, e.g.
    /// <c>Aggregate – Average</c>.
    /// </param>
    /// <param name="ClientConformanceUnit">
    /// The name of the optional conformance unit on the "Historical
    /// Aggregate Client Facet" that covers this function, e.g.
    /// <c>Aggregate – Client Average</c>.
    /// </param>
    public sealed record HistoricalAggregateFunctionDescriptor(
        string Name,
        NodeId AggregateId,
        QualifiedName BrowseName,
        string ServerConformanceUnit,
        string ClientConformanceUnit);
}
