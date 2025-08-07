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

namespace Opc.Ua.Server
{
    /// <inheritdoc/>
    public class StoredMonitoredItem : IStoredMonitoredItem
    {
        /// <inheritdoc/>
        public bool IsRestored { get; set; } = false;
        /// <inheritdoc/>
        public uint SubscriptionId { get; set; }
        /// <inheritdoc/>
        public uint Id { get; set; }
        /// <inheritdoc/>
        public int TypeMask { get; set; }
        /// <inheritdoc/>
        public NodeId NodeId { get; set; }
        /// <inheritdoc/>
        public uint AttributeId { get; set; }
        /// <inheritdoc/>
        public string IndexRange { get; set; }
        /// <inheritdoc/>
        public QualifiedName Encoding { get; set; }
        /// <inheritdoc/>
        public DiagnosticsMasks DiagnosticsMasks { get; set; }
        /// <inheritdoc/>
        public TimestampsToReturn TimestampsToReturn { get; set; }
        /// <inheritdoc/>
        public uint ClientHandle { get; set; }
        /// <inheritdoc/>
        public MonitoringMode MonitoringMode { get; set; }
        /// <inheritdoc/>
        public MonitoringFilter OriginalFilter { get; set; }
        /// <inheritdoc/>
        public MonitoringFilter FilterToUse { get; set; }
        /// <inheritdoc/>
        public double Range { get; set; }
        /// <inheritdoc/>
        public double SamplingInterval { get; set; }
        /// <inheritdoc/>
        public uint QueueSize { get; set; }
        /// <inheritdoc/>
        public bool DiscardOldest { get; set; }
        /// <inheritdoc/>
        public int SourceSamplingInterval { get; set; }
        /// <inheritdoc/>
        public bool AlwaysReportUpdates { get; set; }
        /// <inheritdoc/>
        public bool IsDurable { get; set; }
        /// <inheritdoc/>
        public DataValue LastValue { get; set; }
        /// <inheritdoc/>
        public ServiceResult LastError { get; set; }
        /// <inheritdoc/>
        public NumericRange ParsedIndexRange { get; set; }
    }
}
