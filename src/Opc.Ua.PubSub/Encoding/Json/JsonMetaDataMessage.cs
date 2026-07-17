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

namespace Opc.Ua.PubSub.Encoding.Json
{
    /// <summary>
    /// Concrete JSON metadata-announcement message
    /// (<c>ua-metadata</c> envelope) carrying a single
    /// <see cref="DataSetMetaDataType"/> for a specific
    /// DataSetWriter.
    /// </summary>
    /// <remarks>
    /// Implements
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.5">
    /// Part 14 §7.2.5.5</see> JsonDataSetMetaDataMessage layout. The
    /// metadata payload is exposed both on the base
    /// <see cref="PubSubNetworkMessage.MetaData"/> property and on
    /// <see cref="MetaDataPayload"/> so callers can use whichever
    /// accessor matches their fluent style.
    /// </remarks>
    public sealed record JsonMetaDataMessage : PubSubNetworkMessage
    {
        /// <summary>
        /// MessageId per Part 14 §7.2.5.3.
        /// </summary>
        public string MessageId { get; init; } = string.Empty;

        /// <summary>
        /// DataSetWriterId of the writer whose metadata is announced.
        /// </summary>
        public ushort DataSetWriterId { get; init; }

        /// <summary>
        /// DataSetClassId per Part 14 §7.2.5.3. Bound to
        /// <c>DataSetClassId</c> in the wire envelope.
        /// </summary>
        public Uuid DataSetClassId { get; init; }

        /// <summary>
        /// MetaData payload re-exposed for fluent access. When set,
        /// wins over <see cref="PubSubNetworkMessage.MetaData"/> at
        /// encode time.
        /// </summary>
        public DataSetMetaDataType? MetaDataPayload { get; init; }

        /// <inheritdoc/>
        public override string TransportProfileUri
            => Profiles.PubSubMqttJsonTransport;
    }
}
