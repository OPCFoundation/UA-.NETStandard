#if OPCUA_CLIENT_V2
/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
