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
    /// General-purpose transform backed by a caller-supplied delegate.
    /// Provides an escape hatch for transformations not covered by the
    /// built-in transforms. Returning <see langword="null"/> drops the
    /// message.
    /// </summary>
    public sealed class DelegateMessageTransform : IPubSubMessageTransform
    {
        private readonly Func<PubSubNetworkMessage, TranscodeContext, CancellationToken,
            ValueTask<PubSubNetworkMessage?>> m_transform;

        /// <summary>
        /// Initializes a new <see cref="DelegateMessageTransform"/> from an
        /// asynchronous delegate.
        /// </summary>
        /// <param name="transform">Asynchronous transform delegate.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="transform"/> is
        /// <see langword="null"/>.
        /// </exception>
        public DelegateMessageTransform(
            Func<PubSubNetworkMessage, TranscodeContext, CancellationToken,
                ValueTask<PubSubNetworkMessage?>> transform)
        {
            m_transform = transform ?? throw new ArgumentNullException(nameof(transform));
        }

        /// <summary>
        /// Initializes a new <see cref="DelegateMessageTransform"/> from a
        /// synchronous delegate.
        /// </summary>
        /// <param name="transform">Synchronous transform delegate.</param>
        /// <returns>The wrapping transform.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="transform"/> is
        /// <see langword="null"/>.
        /// </exception>
        public static DelegateMessageTransform FromSync(
            Func<PubSubNetworkMessage, PubSubNetworkMessage?> transform)
        {
            if (transform is null)
            {
                throw new ArgumentNullException(nameof(transform));
            }
            return new DelegateMessageTransform(
                (message, _, _) => new ValueTask<PubSubNetworkMessage?>(transform(message)));
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
            return m_transform(message, context, cancellationToken);
        }
    }
}
