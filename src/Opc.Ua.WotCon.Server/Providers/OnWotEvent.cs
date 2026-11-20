// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;

namespace Opc.Ua.WotCon.Server
{
    /// <summary>
    /// Delegate invoked by an <see cref="IWotAssetProvider"/> when an asset
    /// emits a subscribed WoT event.
    /// </summary>
    /// <param name="tag">The event affordance that fired.</param>
    /// <param name="fields">
    /// The event payload, one value per <see cref="WotEventTag.Fields"/>
    /// entry and in the same order. A provider that has no value for a field
    /// supplies <see cref="Variant.Null"/>.
    /// </param>
    /// <param name="message">
    /// The human-readable message published as
    /// <c>BaseEventType.Message</c>. When null the event name is used.
    /// </param>
    /// <param name="severity">
    /// The OPC 10000-5 <c>BaseEventType.Severity</c> (1..1000). When null
    /// the server fallback in <see cref="WotEventTag.Severity"/> is used.
    /// </param>
    /// <param name="timestamp">
    /// The time the asset reported the event, published as
    /// <c>BaseEventType.Time</c>.
    /// </param>
    public delegate void OnWotEvent(
        WotEventTag tag,
        IReadOnlyList<Variant> fields,
        LocalizedText? message,
        ushort? severity,
        DateTime timestamp);
}
