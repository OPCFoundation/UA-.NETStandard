/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua.Server.Historian;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Configuration for <see cref="SharedKeyValueHistorianProvider"/>.
    /// </summary>
    public sealed class SharedKeyValueHistorianOptions
    {
        /// <summary>
        /// Stable archive identity shared by every replica.
        /// </summary>
        public string ProviderId { get; set; } = "opcua.shared-key-value-historian.v1";

        /// <summary>
        /// Maximum logical records stored in one immutable segment.
        /// </summary>
        public int MaxRecordsPerSegment { get; set; } = 256;

        /// <summary>
        /// Maximum encoded plaintext or protected record size.
        /// </summary>
        public int MaxRecordBytes { get; set; } = 4 * 1024 * 1024;

        /// <summary>
        /// Maximum segment references in one manifest.
        /// </summary>
        public int MaxSegments { get; set; } = 256;

        /// <summary>
        /// Segment count that triggers an inline best-effort compaction.
        /// </summary>
        public int CompactionSegmentThreshold { get; set; } = 32;

        /// <summary>
        /// Minimum retention for manifests and segments superseded by compaction.
        /// </summary>
        public TimeSpan ContinuationRetentionTime { get; set; } =
            TimeSpan.FromDays(1);

        /// <summary>
        /// Minimum time superseded provider generations remain available.
        /// This must not be shorter than continuation retention.
        /// </summary>
        public TimeSpan GenerationRetentionTime { get; set; } =
            TimeSpan.FromDays(2);

        /// <summary>
        /// Duration of the protected historian writer fencing lease.
        /// </summary>
        public TimeSpan WriterFenceLeaseDuration { get; set; } =
            TimeSpan.FromSeconds(30);

        /// <summary>
        /// Grace period before an unreachable immutable record is collected.
        /// </summary>
        public TimeSpan GarbageCollectionGraceTime { get; set; } =
            TimeSpan.FromMinutes(5);

        /// <summary>
        /// Maximum encoded continuation envelope payload.
        /// </summary>
        public int ContinuationMaxPayloadBytes { get; set; } =
            4 * 1024 * 1024;

        /// <summary>
        /// Maximum portable history continuations retained per session.
        /// </summary>
        public int ContinuationMaxEnvelopesPerSession { get; set; } = 10_000;

        /// <summary>
        /// Maximum values returned by one provider page when the request has no limit.
        /// </summary>
        public uint MaxValuesPerPage { get; set; } = 1_000;

        /// <summary>
        /// Per-node capabilities advertised by the shared archive.
        /// </summary>
        public HistorianNodeCapabilities Capabilities { get; set; } = new()
        {
            ReadRawData = true,
            ReadModifiedData = true,
            ReadAtTime = true,
            ReadProcessedData = true,
            InsertData = true,
            ReplaceData = true,
            UpdateData = true,
            DeleteRaw = true,
            DeleteAtTime = true,
            InsertAnnotation = true,
            ReadEventHistory = true,
            InsertEvent = true,
            ReplaceEvent = true,
            UpdateEvent = true,
            DeleteEvent = true,
            PortableResumeTokens = true,
            ServerTimestampSupported = true
        };

        /// <summary>
        /// Structured-history key selectors configured for individual nodes.
        /// </summary>
        public ArrayOf<SharedKeyValueStructuredHistorianNode> StructuredNodes { get; set; }
            = [];

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(ProviderId))
            {
                throw new ArgumentException(
                    "A stable provider identity is required.",
                    nameof(ProviderId));
            }
            if (MaxRecordsPerSegment <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRecordsPerSegment));
            }
            if (MaxRecordBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxRecordBytes));
            }
            if (MaxSegments <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxSegments));
            }
            if (CompactionSegmentThreshold <= 0 ||
                CompactionSegmentThreshold > MaxSegments)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(CompactionSegmentThreshold));
            }
            if (ContinuationRetentionTime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ContinuationRetentionTime));
            }
            if (GenerationRetentionTime < ContinuationRetentionTime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(GenerationRetentionTime),
                    "Generation retention must cover continuation retention.");
            }
            if (WriterFenceLeaseDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(WriterFenceLeaseDuration));
            }
            if (GarbageCollectionGraceTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(GarbageCollectionGraceTime));
            }
            if (ContinuationMaxPayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ContinuationMaxPayloadBytes));
            }
            if (ContinuationMaxEnvelopesPerSession <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ContinuationMaxEnvelopesPerSession));
            }
            if (MaxValuesPerPage is 0 or > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxValuesPerPage));
            }
            if (Capabilities == null)
            {
                throw new ArgumentNullException(nameof(Capabilities));
            }
            foreach (SharedKeyValueStructuredHistorianNode node in
                StructuredNodes)
            {
                if (node == null || node.NodeId.IsNull || node.KeySelector == null)
                {
                    throw new ArgumentException(
                        "Every structured historian node requires a NodeId and key selector.",
                        nameof(StructuredNodes));
                }
            }
        }
    }

    /// <summary>
    /// Binds a structured-history node to its stable composite-key selector.
    /// </summary>
    public sealed class SharedKeyValueStructuredHistorianNode
    {
        /// <summary>
        /// The structured historizing variable.
        /// </summary>
        public required NodeId NodeId { get; init; }

        /// <summary>
        /// Selector for the structure's uniqueness fields.
        /// </summary>
        public required IHistorianStructuredDataKeySelector KeySelector { get; init; }
    }
}
