// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Declarative selection of DataSet fields to promote into transport
    /// message properties on the target side (e.g. MQTT User Properties).
    /// Promotion is <em>copy</em> semantics per Part 14 PromotedFields: the
    /// selected fields remain in the encoded payload and are additionally
    /// surfaced as broker message properties, so consumers can filter on
    /// them without decoding the body.
    /// </summary>
    /// <remarks>
    /// Only meaningful when the target transport implements
    /// <see cref="Transports.IPubSubHeaderTransport"/>; other
    /// transports ignore the promoted properties. Field values are formatted
    /// as invariant strings.
    /// </remarks>
    public sealed record TranscodePromotion
    {
        /// <summary>
        /// BrowseNames of the DataSet fields to promote, matched against
        /// <see cref="Encoding.DataSetField.Name"/>. The first
        /// matching field across the message's DataSetMessages wins.
        /// </summary>
        public ArrayOf<string> FieldNames { get; init; } = [];

        /// <summary>
        /// Optional prefix prepended to every promoted property key. When
        /// <see langword="null"/> or empty the field name is used verbatim.
        /// </summary>
        public string? PropertyKeyPrefix { get; init; }

        /// <summary>
        /// Returns <see langword="true"/> when at least one field is
        /// selected for promotion.
        /// </summary>
        public bool HasFields => FieldNames.Count > 0;

        /// <summary>
        /// An empty promotion selecting no fields.
        /// </summary>
        public static TranscodePromotion None { get; } = new();
    }
}
