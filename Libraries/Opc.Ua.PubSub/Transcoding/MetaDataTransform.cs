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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Rewrites the <see cref="DataSetMetaDataType"/> carried on a
    /// metadata-announcement NetworkMessage via a caller-supplied
    /// delegate. Data messages (those with no metadata) pass through
    /// unchanged.
    /// </summary>
    /// <remarks>
    /// Metadata transcoding per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6.4">
    /// Part 14 §7.2.4.6.4</see> (UADP) and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.5.2">
    /// §7.2.5.5.2</see> (JSON). Use to re-name the dataset, adjust the
    /// namespace mapping, or bump the metadata version when the field
    /// pipeline changes the schema.
    /// </remarks>
    public sealed class MetaDataTransform : IPubSubMessageTransform
    {
        private readonly Func<DataSetMetaDataType, DataSetMetaDataType> m_transform;

        /// <summary>
        /// Initializes a new <see cref="MetaDataTransform"/>.
        /// </summary>
        /// <param name="transform">
        /// Delegate invoked with the source metadata, returning the
        /// replacement metadata.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="transform"/> is
        /// <see langword="null"/>.
        /// </exception>
        public MetaDataTransform(Func<DataSetMetaDataType, DataSetMetaDataType> transform)
        {
            m_transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        /// <inheritdoc/>
        public ValueTask<PubSubNetworkMessage?> TransformAsync(
            PubSubNetworkMessage message,
            TranscodeContext context,
            CancellationToken cancellationToken = default)
        {
            if (message is null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (message.MetaData is null)
            {
                return new ValueTask<PubSubNetworkMessage?>(message);
            }
            DataSetMetaDataType transformed = m_transform(message.MetaData);
            return new ValueTask<PubSubNetworkMessage?>(
                message with { MetaData = transformed });
        }
    }
}
