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
    public interface IStoredMonitoredItem
    {
        /// <summary>
        /// If the item was restored by a node manager
        /// </summary>
        bool IsRestored { get; set; }
        bool AlwaysReportUpdates { get; set; }
        uint AttributeId { get; set; }
        uint ClientHandle { get; set; }
        DiagnosticsMasks DiagnosticsMasks { get; set; }
        bool DiscardOldest { get; set; }
        QualifiedName Encoding { get; set; }
        MonitoringFilter FilterToUse { get; set; }
        uint Id { get; set; }
        string IndexRange { get; set; }
        bool IsDurable { get; set; }
        ServiceResult LastError { get; set; }
        DataValue LastValue { get; set; }
        MonitoringMode MonitoringMode { get; set; }
        NodeId NodeId { get; set; }
        MonitoringFilter OriginalFilter { get; set; }
        uint QueueSize { get; set; }
        double Range { get; set; }
        double SamplingInterval { get; set; }
        int SourceSamplingInterval { get; set; }
        uint SubscriptionId { get; set; }
        TimestampsToReturn TimestampsToReturn { get; set; }
        int TypeMask { get; set; }
        NumericRange ParsedIndexRange { get; set; }
    }

    public class StoredMonitoredItem : IStoredMonitoredItem
    {
        public bool IsRestored { get; set; } = false;
        public uint SubscriptionId { get; set; }
        public uint Id { get; set; }
        public int TypeMask { get; set; }
        public NodeId NodeId { get; set; }
        public uint AttributeId { get; set; }
        public string IndexRange { get; set; }
        public QualifiedName Encoding { get; set; }
        public DiagnosticsMasks DiagnosticsMasks { get; set; }
        public TimestampsToReturn TimestampsToReturn { get; set; }
        public uint ClientHandle { get; set; }
        public MonitoringMode MonitoringMode { get; set; }
        public MonitoringFilter OriginalFilter { get; set; }
        public MonitoringFilter FilterToUse { get; set; }
        public double Range { get; set; }
        public double SamplingInterval { get; set; }
        public uint QueueSize { get; set; }
        public bool DiscardOldest { get; set; }
        public int SourceSamplingInterval { get; set; }
        public bool AlwaysReportUpdates { get; set; }
        public bool IsDurable { get; set; }
        public DataValue LastValue { get; set; }
        public ServiceResult LastError { get; set; }
        public NumericRange ParsedIndexRange { get; set; }
    }

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
