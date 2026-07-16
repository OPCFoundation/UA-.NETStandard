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
