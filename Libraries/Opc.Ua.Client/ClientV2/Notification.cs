#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using System.Collections.Generic;
    using System;
    using Opc.Ua.Client.Subscriptions;

    /// <summary>
    /// Notification
    /// </summary>
    /// <param name="SequenceNumber"></param>
    /// <param name="PublishTime"></param>
    /// <param name="PublishStateMask"></param>
    public abstract record class Notification(uint SequenceNumber,
        DateTime PublishTime, PublishState PublishStateMask);

    /// <summary>
    /// Data changes
    /// </summary>
    /// <param name="SequenceNumber"></param>
    /// <param name="PublishTime"></param>
    /// <param name="Values"></param>
    /// <param name="PublishStateMask"></param>
    /// <param name="StringTable"></param>
    public sealed record DataChanges(uint SequenceNumber,
        DateTime PublishTime, IReadOnlyList<DataValueChange> Values,
        PublishState PublishStateMask, IReadOnlyList<string> StringTable) :
        Notification(SequenceNumber, PublishTime, PublishStateMask);

    /// <summary>
    /// Event
    /// </summary>
    /// <param name="SequenceNumber"></param>
    /// <param name="PublishTime"></param>
    /// <param name="Fields"></param>
    /// <param name="PublishStateMask"></param>
    /// <param name="StringTable"></param>
    public sealed record Event(uint SequenceNumber,
        DateTime PublishTime, EventNotification Fields,
        PublishState PublishStateMask, IReadOnlyList<string> StringTable) :
        Notification(SequenceNumber, PublishTime, PublishStateMask);

    /// <summary>
    /// Keep alive
    /// </summary>
    /// <param name="SequenceNumber"></param>
    /// <param name="PublishTime"></param>
    /// <param name="PublishStateMask"></param>
    public sealed record KeepAlive(uint SequenceNumber,
        DateTime PublishTime, PublishState PublishStateMask) :
        Notification(SequenceNumber, PublishTime, PublishStateMask);

    /// <summary>
    /// Status change of a subscription
    /// </summary>
    /// <param name="SequenceNumber"></param>
    /// <param name="PublishTime"></param>
    /// <param name="Status"></param>
    /// <param name="PublishStateMask"></param>
    /// <param name="StringTable"></param>
    public sealed record StatusChange(uint SequenceNumber,
        DateTime PublishTime, StatusChangeNotification Status,
        PublishState PublishStateMask, IReadOnlyList<string> StringTable) :
        Notification(SequenceNumber, PublishTime, PublishStateMask);

    /// <summary>
    /// Result of a periodic read is a periodic value
    /// </summary>
    /// <param name="Name"></param>
    /// <param name="Item"></param>
    /// <param name="Value"></param>
    /// <param name="DiagnosticInfo"></param>
    public record struct PeriodicValue(string Name, ReadValueId Item,
        DataValue Value, DiagnosticInfo? DiagnosticInfo);

    /// <summary>
    /// Periodically (cyclic) read data
    /// </summary>
    /// <param name="SequenceNumber"></param>
    /// <param name="PublishTime"></param>
    /// <param name="Values"></param>
    /// <param name="PublishStateMask"></param>
    /// <param name="StringTable"></param>
    public sealed record PeriodicData(uint SequenceNumber,
        DateTime PublishTime, IReadOnlyList<PeriodicValue> Values,
        PublishState PublishStateMask, IReadOnlyList<string> StringTable) :
        Notification(SequenceNumber, PublishTime, PublishStateMask);
}
#endif
