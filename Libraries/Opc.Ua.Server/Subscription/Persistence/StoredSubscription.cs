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

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Opc.Ua.Server
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public interface IStoredSubscription
    {
        uint Id { get; set; }
        bool IsDurable { get; set; }
        int LastSentMessage { get; set; }
        uint LifetimeCounter { get; set; }
        uint MaxKeepaliveCount { get; set; }
        uint MaxLifetimeCount { get; set; }
        uint MaxMessageCount { get; set; }
        uint MaxNotificationsPerPublish { get; set; }
        IEnumerable<IStoredMonitoredItem> MonitoredItems { get; set; }
        byte Priority { get; set; }
        double PublishingInterval { get; set; }
        List<NotificationMessage> SentMessages { get; set; }
        long SequenceNumber { get; set; }
        UserIdentityToken UserIdentityToken { get; set; }
    }

    public class StoredSubscription : IStoredSubscription
    {
        public uint Id { get; set; }
        public uint LifetimeCounter { get; set; }
        public uint MaxLifetimeCount { get; set; }
        public uint MaxKeepaliveCount { get; set; }
        public uint MaxMessageCount { get; set; }
        public uint MaxNotificationsPerPublish { get; set; }
        public double PublishingInterval { get; set; }
        public byte Priority { get; set; }
        public UserIdentityToken UserIdentityToken { get; set; }
        public int LastSentMessage { get; set; }
        public bool IsDurable { get; set; }
        public long SequenceNumber { get; set; }
        public List<NotificationMessage> SentMessages { get; set; }
        public IEnumerable<IStoredMonitoredItem> MonitoredItems { get; set; }
    }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
