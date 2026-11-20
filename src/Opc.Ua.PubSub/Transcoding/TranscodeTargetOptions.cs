/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Format-level options controlling how the profile projector
    /// materialises the target concrete NetworkMessage record. These are
    /// wire-encoding choices only; identifier, field and value
    /// transformations are expressed as <see cref="IPubSubMessageTransform"/>
    /// steps rather than options here.
    /// </summary>
    public sealed record TranscodeTargetOptions
    {
        /// <summary>
        /// Overrides the per-DataSetMessage field encoding
        /// (Variant / RawData / DataValue) on the target. When
        /// <see langword="null"/> the source field encoding is preserved
        /// where the target mapping supports it, otherwise the mapping
        /// default is used.
        /// </summary>
        public PubSubFieldEncoding? FieldEncoding { get; init; }

        /// <summary>
        /// When the target encoding is JSON, selects the flat
        /// single-message layout of Part 14 Annex A.3.3 (no
        /// <c>Messages</c> wrapper). Ignored for UADP targets.
        /// </summary>
        public bool JsonSingleMessageMode { get; init; }

        /// <summary>
        /// When <see langword="true"/> (default) the target message keeps
        /// the source <c>MetaDataVersion</c> on every DataSetMessage so
        /// downstream readers can continue to validate their configured
        /// metadata major version.
        /// </summary>
        public bool PreserveMetaDataVersion { get; init; } = true;

        /// <summary>
        /// Default instance preserving the source characteristics as
        /// faithfully as the target mapping allows.
        /// </summary>
        public static TranscodeTargetOptions Default { get; } = new();
    }
}
