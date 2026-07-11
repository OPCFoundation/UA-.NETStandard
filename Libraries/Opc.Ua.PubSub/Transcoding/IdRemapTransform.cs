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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Encoding;
using JsonNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Json.JsonNetworkMessage;
using UadpNetworkMessageV2 = Opc.Ua.PubSub.Encoding.Uadp.UadpNetworkMessage;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Rewrites the identification of a NetworkMessage: the
    /// <see cref="PublisherId"/> and <c>WriterGroupId</c> at the
    /// NetworkMessage level, the <c>DataSetClassId</c> on the concrete
    /// record, and the per-DataSetMessage <c>DataSetWriterId</c> via a
    /// remap table. Only the parts supplied to the constructor are
    /// changed; everything else is preserved.
    /// </summary>
    /// <remarks>
    /// Supports re-addressing published streams as they cross the
    /// transcoding boundary (Part 14 §6.2.7 identification fields).
    /// </remarks>
    public sealed class IdRemapTransform : IPubSubMessageTransform
    {
        private readonly PublisherId m_publisherId;
        private readonly ushort? m_writerGroupId;
        private readonly Uuid? m_dataSetClassId;
        private readonly IReadOnlyDictionary<ushort, ushort>? m_dataSetWriterIds;

        /// <summary>
        /// Initializes a new <see cref="IdRemapTransform"/>.
        /// </summary>
        /// <param name="publisherId">
        /// New PublisherId. When <see cref="PublisherId.IsNull"/> the
        /// source PublisherId is preserved.
        /// </param>
        /// <param name="writerGroupId">
        /// New WriterGroupId, or <see langword="null"/> to preserve the
        /// source value.
        /// </param>
        /// <param name="dataSetClassId">
        /// New DataSetClassId, or <see langword="null"/> to preserve the
        /// source value.
        /// </param>
        /// <param name="dataSetWriterIds">
        /// Optional map of source-to-target DataSetWriterId. Writers not
        /// present in the map keep their original id.
        /// </param>
        public IdRemapTransform(
            PublisherId publisherId = default,
            ushort? writerGroupId = null,
            Uuid? dataSetClassId = null,
            IReadOnlyDictionary<ushort, ushort>? dataSetWriterIds = null)
        {
            m_publisherId = publisherId;
            m_writerGroupId = writerGroupId;
            m_dataSetClassId = dataSetClassId;
            m_dataSetWriterIds = dataSetWriterIds;
        }

        /// <inheritdoc/>
        public ValueTask<PubSubNetworkMessage?> TransformAsync(
            PubSubNetworkMessage message,
            TranscodeContext context,
            CancellationToken cancellationToken = default)
        {
            PubSubNetworkMessage result = message
                ?? throw new System.ArgumentNullException(nameof(message));

            if (!m_publisherId.IsNull && !m_publisherId.Equals(default))
            {
                result = result with { PublisherId = m_publisherId };
            }
            if (m_writerGroupId is ushort writerGroupId)
            {
                result = result with { WriterGroupId = writerGroupId };
            }
            if (m_dataSetClassId is Uuid classId)
            {
                result = ApplyDataSetClassId(result, classId);
            }
            if (m_dataSetWriterIds is { Count: > 0 } map && result.DataSetMessages.Count > 0)
            {
                result = result with
                {
                    DataSetMessages = RemapWriterIds(result.DataSetMessages, map)
                };
            }
            return new ValueTask<PubSubNetworkMessage?>(result);
        }

        private static PubSubNetworkMessage ApplyDataSetClassId(
            PubSubNetworkMessage message,
            Uuid classId)
        {
            return message switch
            {
                UadpNetworkMessageV2 uadp => uadp with { DataSetClassId = classId },
                JsonNetworkMessageV2 json => json with { DataSetClassId = classId },
                _ => message
            };
        }

        private static ArrayOf<PubSubDataSetMessage> RemapWriterIds(
            ArrayOf<PubSubDataSetMessage> messages,
            IReadOnlyDictionary<ushort, ushort> map)
        {
            var mapped = new List<PubSubDataSetMessage>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                PubSubDataSetMessage dsm = messages[i];
                mapped.Add(map.TryGetValue(dsm.DataSetWriterId, out ushort newId)
                    ? dsm with { DataSetWriterId = newId }
                    : dsm);
            }
            return mapped;
        }
    }
}
