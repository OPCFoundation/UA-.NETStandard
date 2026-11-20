// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Text.Json.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// The finite read-only facets of a machinery item.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MachineryReadFacet>))]
    public enum MachineryReadFacet
    {
        /// <summary>
        /// The identification add-in.
        /// </summary>
        Identification,

        /// <summary>
        /// Device health below Monitoring.
        /// </summary>
        Health,

        /// <summary>
        /// Power-on, operation and cycle counters.
        /// </summary>
        OperationCounters,

        /// <summary>
        /// The server-driven machinery item state.
        /// </summary>
        ItemState,

        /// <summary>
        /// The server-driven operation mode.
        /// </summary>
        OperationMode
    }

    /// <summary>
    /// The finite discovery levels of the energy model.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MachineryEnergyScope>))]
    public enum MachineryEnergyScope
    {
        /// <summary>
        /// Resource folders below a machine's Consumption block.
        /// </summary>
        Resources,

        /// <summary>
        /// Metering points below a discovered resource.
        /// </summary>
        MeteringPoints,

        /// <summary>
        /// Sub-meters reached through Contains.
        /// </summary>
        SubMeters
    }

    /// <summary>
    /// The two job lists published by Machinery job management.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MachineryJobList>))]
    public enum MachineryJobList
    {
        /// <summary>
        /// Job orders and their states.
        /// </summary>
        Orders,

        /// <summary>
        /// Published job responses.
        /// </summary>
        Responses
    }

    /// <summary>
    /// The supported result ordering keys, in ascending order.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MachineryResultOrder>))]
    public enum MachineryResultOrder
    {
        /// <summary>
        /// The result creation timestamp.
        /// </summary>
        CreationTime,

        /// <summary>
        /// The unique result identifier.
        /// </summary>
        ResultId,

        /// <summary>
        /// The associated job identifier.
        /// </summary>
        JobId
    }

    /// <summary>
    /// The finite read-only streams available for bounded observations.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MachineryObservationKind>))]
    public enum MachineryObservationKind
    {
        /// <summary>
        /// Machinery item state changes.
        /// </summary>
        ItemState,

        /// <summary>
        /// Operation mode changes.
        /// </summary>
        OperationMode,

        /// <summary>
        /// Result-ready events, including concrete vendor subtypes.
        /// </summary>
        Results,

        /// <summary>
        /// Base-event fields from the Notifications add-in.
        /// </summary>
        Notifications,

        /// <summary>
        /// Zero-adjustment events from a process value.
        /// </summary>
        ZeroPointAdjustments
    }
}
