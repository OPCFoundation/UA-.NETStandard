// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Single field within a <see cref="PubSubDataSetMessage"/>. Carries
    /// the field name, its Variant value, per-field status code (for
    /// DataValue field encoding), source timestamp, and the chosen
    /// field encoding so encoders can round-trip without consulting
    /// metadata for the encoding-mode bit alone.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.3.2">
    /// Part 14 §5.3.2 DataSetMessage</see>. A
    /// <see cref="StatusCode"/> of <see cref="StatusCodes.Good"/> with
    /// <see cref="PubSubFieldEncoding.Variant"/> or
    /// <see cref="PubSubFieldEncoding.RawData"/> indicates the encoder
    /// omitted the explicit DataValue wrapper.
    /// </remarks>
    public sealed record DataSetField
    {
        /// <summary>
        /// Field name as declared in the DataSetMetaData. Empty when the
        /// field is anonymous in RawData encoding.
        /// </summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>
        /// Field identifier resolved from the DataSetMetaData descriptor.
        /// Empty when no matching metadata is available; never inferred from <see cref="Name"/>.
        /// </summary>
        public Uuid DataSetFieldId { get; init; }

        /// <summary>
        /// Field value carried as a <see cref="Variant"/>; the inner
        /// Built-In type matches the metadata declaration.
        /// </summary>
        public Variant Value { get; init; }

        /// <summary>
        /// Metadata field index for UADP delta frames. A negative value
        /// means the field keeps its current list position.
        /// </summary>
        public int FieldIndex { get; init; } = -1;

        /// <summary>
        /// Per-field status code; meaningful only for
        /// <see cref="PubSubFieldEncoding.DataValue"/> encoding. Defaults
        /// to <see cref="StatusCodes.Good"/>.
        /// </summary>
        public StatusCode StatusCode { get; init; } = StatusCodes.Good;

        /// <summary>
        /// Per-field source timestamp; meaningful only for
        /// <see cref="PubSubFieldEncoding.DataValue"/> encoding when the
        /// writer's <c>DataSetFieldContentMask</c> includes
        /// <see cref="DataSetFieldContentMask.SourceTimestamp"/>.
        /// </summary>
        public DateTimeUtc SourceTimestamp { get; init; }

        /// <summary>
        /// Per-field source picoseconds; meaningful only for
        /// <see cref="PubSubFieldEncoding.DataValue"/> encoding when the
        /// writer's <c>DataSetFieldContentMask</c> includes
        /// <see cref="DataSetFieldContentMask.SourcePicoSeconds"/>.
        /// </summary>
        public ushort SourcePicoSeconds { get; init; }

        /// <summary>
        /// Per-field server timestamp; meaningful only for
        /// <see cref="PubSubFieldEncoding.DataValue"/> encoding when the
        /// writer's <c>DataSetFieldContentMask</c> includes
        /// <see cref="DataSetFieldContentMask.ServerTimestamp"/>.
        /// </summary>
        public DateTimeUtc ServerTimestamp { get; init; }

        /// <summary>
        /// Per-field server picoseconds; meaningful only for
        /// <see cref="PubSubFieldEncoding.DataValue"/> encoding when the
        /// writer's <c>DataSetFieldContentMask</c> includes
        /// <see cref="DataSetFieldContentMask.ServerPicoSeconds"/>.
        /// </summary>
        public ushort ServerPicoSeconds { get; init; }

        /// <summary>
        /// Field encoding chosen by the producing writer.
        /// </summary>
        public PubSubFieldEncoding Encoding { get; init; }
    }
}
