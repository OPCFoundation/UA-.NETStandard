/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;

namespace UaLens.Subscriptions;

internal enum NotificationKind
{
    DataChange,
    Event,
    KeepAlive
}

internal readonly record struct NotificationEvent(
    NotificationKind Kind,
    int ItemId,
    int ValueCount,
    uint SequenceNumber,
    DateTime ReceivedAtUtc,
    double? Value = null);
