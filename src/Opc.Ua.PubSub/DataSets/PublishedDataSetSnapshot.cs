// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.DataSets
{
    /// <summary>
    /// Immutable snapshot of one <see cref="IPublishedDataSet"/>
    /// sample: the metadata version the snapshot was produced under,
    /// the materialised field values, and the sample timestamp.
    /// </summary>
    /// <remarks>
    /// Implements the DataSet sampling result described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.3">
    /// Part 14 §6.2.3 PublishedDataSet</see>.
    /// </remarks>
    public sealed record PublishedDataSetSnapshot
    {
        /// <summary>
        /// Initializes a new <see cref="PublishedDataSetSnapshot"/>.
        /// </summary>
        /// <param name="metaDataVersion">
        /// MetaData version active at sample time. Subscribers
        /// compare this against their registered version to detect
        /// drift.
        /// </param>
        /// <param name="fields">Field values in MetaData order.</param>
        /// <param name="sampledAt">Wall-clock time of the sample.</param>
        /// <param name="messageType">
        /// Kind of message the sample represents, or <see langword="null"/>
        /// when the writer decides. See <see cref="MessageType"/>.
        /// </param>
        public PublishedDataSetSnapshot(
            ConfigurationVersionDataType metaDataVersion,
            ArrayOf<DataSetField> fields,
            DateTimeUtc sampledAt,
            PubSubDataSetMessageType? messageType = null)
        {
            if (metaDataVersion is null)
            {
                throw new ArgumentNullException(nameof(metaDataVersion));
            }

            MetaDataVersion = metaDataVersion;
            Fields = fields;
            SampledAt = sampledAt;
            MessageType = messageType;
        }

        /// <summary>
        /// MetaData version active at sample time.
        /// </summary>
        public ConfigurationVersionDataType MetaDataVersion { get; init; }

        /// <summary>
        /// Field values in MetaData order.
        /// </summary>
        public ArrayOf<DataSetField> Fields { get; init; }

        /// <summary>
        /// Wall-clock time of the sample.
        /// </summary>
        public DateTimeUtc SampledAt { get; init; }

        /// <summary>
        /// Kind of message this sample represents, or <see langword="null"/>
        /// when the <see cref="Groups.DataSetWriter"/> decides.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A source that returns its complete current state every cycle leaves
        /// this <see langword="null"/>: the writer then applies
        /// <see cref="Groups.DataSetWriter.KeyFrameCount"/> and derives delta
        /// frames by comparing successive snapshots, as described in
        /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.5">
        /// Part 14 §6.2.5 DataSetWriter</see>.
        /// </para>
        /// <para>
        /// That derivation assumes successive snapshots describe the same
        /// fields in the same order. A source whose samples are occurrences -
        /// an event, a condition snapshot, or a delta it has already computed -
        /// cannot satisfy that assumption, because consecutive samples carry
        /// different fields. Such a source sets this property, and the writer
        /// then publishes exactly the fields supplied under the declared type
        /// and leaves its key-frame state untouched.
        /// </para>
        /// </remarks>
        public PubSubDataSetMessageType? MessageType { get; init; }
    }
}
