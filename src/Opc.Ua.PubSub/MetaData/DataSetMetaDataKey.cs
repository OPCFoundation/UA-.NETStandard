/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
