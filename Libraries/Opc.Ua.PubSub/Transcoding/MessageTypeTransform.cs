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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Filters and/or relabels DataSetMessages by their
    /// <see cref="PubSubDataSetMessageType"/> (KeyFrame / DeltaFrame /
    /// Event / KeepAlive). DataSetMessages the <c>keep</c> predicate
    /// rejects are dropped; the remaining ones can be forced to a
    /// different message type. When no DataSetMessage survives and the
    /// message carries no metadata, the whole NetworkMessage is filtered
    /// out.
    /// </summary>
    /// <remarks>
    /// Message-type handling per
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/5.3.2">
    /// Part 14 §5.3.2</see>. A common use is dropping KeepAlive frames
    /// (<c>keep: t =&gt; t != PubSubDataSetMessageType.KeepAlive</c>) before
    /// re-publishing.
    /// </remarks>
    public sealed class MessageTypeTransform : IPubSubMessageTransform
    {
        private readonly Func<PubSubDataSetMessageType, bool>? m_keep;
        private readonly PubSubDataSetMessageType? m_forceType;

        /// <summary>
        /// Initializes a new <see cref="MessageTypeTransform"/>.
        /// </summary>
        /// <param name="keep">
        /// Predicate deciding which DataSetMessage types to keep. When
        /// <see langword="null"/> all are kept.
        /// </param>
        /// <param name="forceType">
        /// Message type to force on every kept DataSetMessage, or
        /// <see langword="null"/> to preserve the original type.
        /// </param>
        public MessageTypeTransform(
            Func<PubSubDataSetMessageType, bool>? keep = null,
            PubSubDataSetMessageType? forceType = null)
        {
            m_keep = keep;
            m_forceType = forceType;
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
            if (message.DataSetMessages.Count == 0)
            {
                return new ValueTask<PubSubNetworkMessage?>(message);
            }

            var kept = new List<PubSubDataSetMessage>(message.DataSetMessages.Count);
            for (int i = 0; i < message.DataSetMessages.Count; i++)
            {
                PubSubDataSetMessage dsm = message.DataSetMessages[i];
                if (m_keep is not null && !m_keep(dsm.MessageType))
                {
                    continue;
                }
                kept.Add(m_forceType is PubSubDataSetMessageType forced
                    ? dsm with { MessageType = forced }
                    : dsm);
            }

            if (kept.Count == 0 && message.MetaData is null)
            {
                return new ValueTask<PubSubNetworkMessage?>((PubSubNetworkMessage?)null);
            }
            return new ValueTask<PubSubNetworkMessage?>(
                message with { DataSetMessages = kept });
        }
    }
}
