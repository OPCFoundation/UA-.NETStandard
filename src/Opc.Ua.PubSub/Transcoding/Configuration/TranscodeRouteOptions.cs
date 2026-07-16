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

using System.Collections.Generic;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Declarative description of one transcoding route bound from
    /// configuration. Covers the declarative transforms only; delegate-based
    /// transforms (custom value / metadata callbacks) remain available on the
    /// fluent <see cref="PubSubTranscoderBuilder"/>.
    /// </summary>
    public sealed class TranscodeRouteOptions
    {
        /// <summary>
        /// Unique route key. Reloads diff routes by this name, so only
        /// changed / added / removed routes are reconfigured.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Name of the source connection whose received messages are
        /// transcoded.
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// Name of the target connection the transcoded messages are
        /// published on.
        /// </summary>
        public string? Target { get; set; }

        /// <summary>
        /// Target NetworkMessage encoding. Defaults to
        /// <see cref="TranscodeEncoding.Uadp"/>.
        /// </summary>
        public TranscodeEncoding TargetEncoding { get; set; } = TranscodeEncoding.Uadp;

        /// <summary>
        /// Fixed broker topic for the transcoded output (MQTT). Ignored by
        /// datagram transports.
        /// </summary>
        public string? Topic { get; set; }

        /// <summary>
        /// Overrides the target field encoding (Variant / RawData /
        /// DataValue). When <see langword="null"/> the source encoding is
        /// preserved where the mapping supports it.
        /// </summary>
        public PubSubFieldEncoding? FieldEncoding { get; set; }

        /// <summary>
        /// Emits the flat JSON single-message layout on a JSON target.
        /// </summary>
        public bool JsonSingleMessage { get; set; }

        /// <summary>
        /// Preserves the source MetaDataVersion on the target (default
        /// <see langword="true"/>).
        /// </summary>
        public bool PreserveMetaDataVersion { get; set; } = true;

        /// <summary>
        /// Allows lowering a secured source to an output without
        /// message-layer security (e.g. UADP to JSON).
        /// </summary>
        public bool AllowInsecureCrossEncoding { get; set; }

        /// <summary>
        /// Optional identifier remapping.
        /// </summary>
        public TranscodeIdRemapOptions? RemapIds { get; set; }

        /// <summary>
        /// Field rename map (source name to target name).
        /// </summary>
        public IDictionary<string, string>? RenameFields { get; set; }

        /// <summary>
        /// Keeps only the named fields, in order. Mutually exclusive with
        /// <see cref="ExcludeFields"/>.
        /// </summary>
        public IList<string>? SelectFields { get; set; }

        /// <summary>
        /// Drops the named fields, preserving the rest.
        /// </summary>
        public IList<string>? ExcludeFields { get; set; }

        /// <summary>
        /// Keeps only DataSetMessages of these message types. When empty all
        /// types are kept.
        /// </summary>
        public IList<PubSubDataSetMessageType>? KeepMessageTypes { get; set; }

        /// <summary>
        /// Drops KeepAlive DataSetMessages from the stream.
        /// </summary>
        public bool DropKeepAlive { get; set; }

        /// <summary>
        /// DataSet field names promoted into target transport message
        /// properties (e.g. MQTT User Properties).
        /// </summary>
        public IList<string>? PromoteFields { get; set; }

        /// <summary>
        /// Optional prefix prepended to every promoted property key.
        /// </summary>
        public string? PromotedFieldPrefix { get; set; }
    }

    /// <summary>
    /// Declarative identifier-remapping options for a transcoding route.
    /// </summary>
    public sealed class TranscodeIdRemapOptions
    {
        /// <summary>
        /// Replacement string PublisherId. Takes precedence over
        /// <see cref="PublisherIdNumber"/> when both are set.
        /// </summary>
        public string? PublisherId { get; set; }

        /// <summary>
        /// Replacement numeric PublisherId (encoded as UInt64).
        /// </summary>
        public ulong? PublisherIdNumber { get; set; }

        /// <summary>
        /// Replacement WriterGroupId.
        /// </summary>
        public ushort? WriterGroupId { get; set; }

        /// <summary>
        /// Replacement DataSetClassId (GUID string).
        /// </summary>
        public string? DataSetClassId { get; set; }

        /// <summary>
        /// Per-writer DataSetWriterId remap table (source id to target id).
        /// </summary>
        public IDictionary<ushort, ushort>? DataSetWriterIds { get; set; }
    }
}
