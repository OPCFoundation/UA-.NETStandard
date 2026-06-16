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

namespace Opc.Ua.PubSub.Transports
{
    /// <summary>
    /// Optional capability implemented by transports that derive
    /// publish topics from a Part 14 §7.3.4.7 schema (e.g. MQTT).
    /// Datagram transports that ignore the <c>topic</c> argument of
    /// <see cref="IPubSubTransport.SendAsync"/> do not implement this
    /// interface; callers fall back to <see langword="null"/> in that
    /// case.
    /// </summary>
    /// <remarks>
    /// Implements the discovery/metadata topic lookup contract required
    /// by
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.7.4">
    /// Part 14 §7.3.4.7.4 Metadata topic</see> and
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.3.4.8">
    /// §7.3.4.8 Retained discovery messages</see>. Used by the
    /// application-level metadata publisher to derive a per-DataSetWriter
    /// metadata topic without taking a hard dependency on a specific
    /// transport library.
    /// </remarks>
    public interface IPubSubTopicProvider
    {
        /// <summary>
        /// Builds the per-DataSetWriter metadata topic for the supplied
        /// identity tuple. Implementations must follow the §7.3.4.7.4
        /// schema (e.g. <c>&lt;Prefix&gt;/&lt;Encoding&gt;/metadata/&lt;PublisherId&gt;/&lt;WriterGroup&gt;/&lt;DataSetWriter&gt;</c>).
        /// </summary>
        /// <param name="publisherId">Publisher identity (any Part 14 type).</param>
        /// <param name="writerGroupId">WriterGroupId.</param>
        /// <param name="dataSetWriterId">DataSetWriterId.</param>
        /// <returns>The constructed topic string.</returns>
        string BuildMetaDataTopic(
            PublisherId publisherId,
            ushort writerGroupId,
            ushort dataSetWriterId);
    }
}
