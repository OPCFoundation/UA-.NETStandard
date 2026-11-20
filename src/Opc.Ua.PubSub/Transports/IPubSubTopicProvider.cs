/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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

        /// <summary>
        /// Builds the data topic for a writer-group publication.
        /// </summary>
        /// <param name="publisherId">Publisher identity.</param>
        /// <param name="writerGroup">WriterGroup configuration.</param>
        /// <param name="dataSetWriterId">Optional DataSetWriterId for single-message topics.</param>
        /// <returns>The constructed topic string.</returns>
        string BuildDataTopic(
            PublisherId publisherId,
            WriterGroupDataType writerGroup,
            ushort? dataSetWriterId);

        /// <summary>
        /// Builds a publisher-level discovery topic for MQTT message types such as
        /// status, connection, application, and endpoints.
        /// </summary>
        /// <param name="publisherId">Publisher identity.</param>
        /// <param name="messageTypeSegment">MQTT message type segment.</param>
        /// <returns>The constructed discovery topic string.</returns>
        string BuildDiscoveryTopic(
            PublisherId publisherId,
            string messageTypeSegment);
    }
}
