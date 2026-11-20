/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
