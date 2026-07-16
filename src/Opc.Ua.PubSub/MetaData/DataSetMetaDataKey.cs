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

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.MetaData
{
    /// <summary>
    /// Identity tuple used to look up a <see cref="DataSetMetaDataType"/>
    /// in an <see cref="IDataSetMetaDataRegistry"/>. Combines the
    /// PublisherId, WriterGroupId, DataSetWriterId, DataSetClassId, and
    /// metadata MajorVersion that together determine whether two
    /// metadata descriptions are interchangeable on the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implements the metadata-identity model from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.2.3">
    /// Part 14 §5.2.3 DataSetMetaData</see>. The
    /// <see cref="MajorVersion"/> is part of the key because a
    /// MajorVersion mismatch is non-negotiable: incoming
    /// DataSetMessages whose metadata MajorVersion does not match the
    /// registered version must be rejected per the same clause and the
    /// research §15 supplement.
    /// </para>
    /// <para>
    /// The MinorVersion is intentionally *not* part of this key: a
    /// MinorVersion-only change is treated as a backward-compatible
    /// update and reconciled by the registry, not by a separate lookup.
    /// </para>
    /// </remarks>
    public readonly record struct DataSetMetaDataKey
    {
        /// <summary>
        /// Initializes a new <see cref="DataSetMetaDataKey"/>.
        /// </summary>
        /// <param name="publisherId">Publisher identity (Part 14 §6.2.7.1).</param>
        /// <param name="writerGroupId">WriterGroupId within the publisher.</param>
        /// <param name="dataSetWriterId">DataSetWriterId within the writer group.</param>
        /// <param name="dataSetClassId">DataSetClassId from the published dataset.</param>
        /// <param name="majorVersion">MajorVersion of the metadata description.</param>
        public DataSetMetaDataKey(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId,
            Uuid dataSetClassId,
            uint majorVersion)
        {
            PublisherId = publisherId;
            WriterGroupId = writerGroupId;
            DataSetWriterId = dataSetWriterId;
            DataSetClassId = dataSetClassId;
            MajorVersion = majorVersion;
        }

        /// <summary>
        /// Publisher identity per Part 14 §6.2.7.1.
        /// </summary>
        public PublisherId PublisherId { get; init; }

        /// <summary>
        /// WriterGroupId within the publisher.
        /// </summary>
        public ushort WriterGroupId { get; init; }

        /// <summary>
        /// DataSetWriterId within the writer group.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// DataSetClassId from the published dataset. May be
        /// <see cref="Uuid.Empty"/> when the publisher does not assign one.
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// MajorVersion of the metadata description. A change in this
        /// value indicates a breaking schema update and must trigger
        /// rejection of in-flight messages bound to the prior version.
        /// </summary>
        public uint MajorVersion { get; init; }

        /// <summary>
        /// <see langword="true"/> when no PublisherId, WriterGroupId, or
        /// DataSetWriterId is set — the key is effectively unbound.
        /// </summary>
        public bool IsNull => PublisherId.IsNull &&
            WriterGroupId == 0 &&
            DataSetWriterId == 0 &&
            DataSetClassId == Uuid.Empty &&
            MajorVersion == 0;
    }
}
