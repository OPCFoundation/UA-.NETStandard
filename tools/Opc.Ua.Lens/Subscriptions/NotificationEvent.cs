/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace UaLens.Subscriptions;

internal enum NotificationKind
{
    DataChange,
    Event,
    KeepAlive
}

/// <summary>
/// A sample with document-scoped item identity and attribution captured at delivery.
/// ItemId is not a server or adapter client handle; zero identifies subscription-level events.
/// </summary>
internal readonly record struct NotificationEvent(
    NotificationKind Kind,
    int ItemId,
    int ValueCount,
    uint SequenceNumber,
    DateTime ReceivedAtUtc,
    double? Value = null,
    string DisplayName = "",
    string NodeId = "");
