// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Preserves one selected event field, including null, status and quality values, without boxed conversions.
    /// An empty browse path denotes the Part 9 ConditionId attribute.
    /// </summary>
    /// <param name="BrowsePath">The namespace-qualified selected field path.</param>
    /// <param name="Value">The original wire value.</param>
    public sealed record MachineToolEventField(ArrayOf<QualifiedName> BrowsePath, Variant Value);

    /// <summary>
    /// Couples the shared generated decoder's result with raw field values and publication ordering.
    /// </summary>
    /// <param name="NotifierId">The actual notifier monitored by the workflow.</param>
    /// <param name="Event">The most specific known standard or MachineTool event record.</param>
    /// <param name="Fields">Raw fields for native OPC UA JSON and optional-field inspection.</param>
    /// <param name="PublishTime">The server publication timestamp.</param>
    /// <param name="SequenceNumber">The subscription publication sequence.</param>
    public sealed record MachineToolObservedEvent(
        NodeId NotifierId, BaseEventTypeRecord Event, ArrayOf<MachineToolEventField> Fields,
        DateTimeUtc PublishTime, uint SequenceNumber);

    /// <summary>
    /// Returns a bounded passive observation. A complete retained refresh requires RefreshStart and RefreshEnd;
    /// reaching a bound or observing queue overflow never silently claims a complete retained inventory.
    /// </summary>
    /// <param name="Events">The collected events or conditions.</param>
    /// <param name="LimitReached">Whether additional records could not be retained within the requested count.</param>
    /// <param name="DroppedNotifications">Observed server overflow markers and local discards.</param>
    /// <param name="RefreshCompleted">Whether a condition refresh ended without losses or truncation.</param>
    public sealed record MachineToolEventObservation(
        ArrayOf<MachineToolObservedEvent> Events, bool LimitReached,
        long DroppedNotifications, bool RefreshCompleted);
}
