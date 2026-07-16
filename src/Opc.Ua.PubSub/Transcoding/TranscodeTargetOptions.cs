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
