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
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub.Transcoding
{
    /// <summary>
    /// Fluent builder for a transcoding bridge: declares the source and
    /// target connections, the target encoding, the ordered transform
    /// pipeline, and the cross-encoding security policy.
    /// </summary>
    /// <remarks>
    /// Transforms are applied in the order the fluent methods are called.
    /// Repeated <see cref="RenameField(string, string)"/> calls accumulate
    /// into a single rename step.
    /// </remarks>
    public sealed class PubSubTranscoderBuilder
    {
        private readonly List<IPubSubMessageTransform> m_transforms = [];
        private readonly Dictionary<string, string> m_renameMap = new(StringComparer.Ordinal);
        private readonly List<string> m_promotedFields = [];
        private bool m_renameTransformAdded;
        private string? m_source;
        private string? m_target;
        private TranscodeEncoding m_targetEncoding = TranscodeEncoding.Uadp;
        private PubSubFieldEncoding? m_fieldEncoding;
        private bool m_jsonSingleMessage;
        private bool m_preserveMetaDataVersion = true;
        private bool m_allowInsecureCrossEncoding;
        private string? m_promotionPrefix;
        private Func<ReceivedNetworkMessage, string?>? m_topicSelector;

        /// <summary>
        /// Sets the name of the source connection to observe.
        /// </summary>
        /// <param name="sourceConnectionName">Source connection name.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscoderBuilder From(string sourceConnectionName)
        {
            m_source = sourceConnectionName
                ?? throw new ArgumentNullException(nameof(sourceConnectionName));
            return this;
        }

        /// <summary>
        /// Sets the target connection and encoding for the transcoded
        /// output.
        /// </summary>
        /// <param name="targetConnectionName">Target connection name.</param>
        /// <param name="encoding">Target NetworkMessage encoding.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscoderBuilder To(
            string targetConnectionName,
            TranscodeEncoding encoding)
        {
            m_target = targetConnectionName
                ?? throw new ArgumentNullException(nameof(targetConnectionName));
            m_targetEncoding = encoding;
            return this;
        }

        /// <summary>
        /// Adds an arbitrary transform to the pipeline.
        /// </summary>
        /// <param name="transform">Transform to add.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscoderBuilder AddTransform(IPubSubMessageTransform transform)
        {
            m_transforms.Add(transform ?? throw new ArgumentNullException(nameof(transform)));
            return this;
        }

        /// <summary>
        /// Adds an identifier-remapping step.
        /// </summary>
        /// <param name="publisherId">
        /// New PublisherId, or the null sentinel to preserve the source.
        /// </param>
        /// <param name="writerGroupId">New WriterGroupId, or unset.</param>
        /// <param name="dataSetClassId">New DataSetClassId, or unset.</param>
        /// <param name="dataSetWriterIds">Optional writer-id remap table.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder RemapIds(
            PublisherId publisherId = default,
            ushort? writerGroupId = null,
            Uuid? dataSetClassId = null,
            IReadOnlyDictionary<ushort, ushort>? dataSetWriterIds = null)
        {
            return AddTransform(new IdRemapTransform(
                publisherId, writerGroupId, dataSetClassId, dataSetWriterIds));
        }

        /// <summary>
        /// Renames a field. Repeated calls accumulate into one rename step.
        /// </summary>
        /// <param name="from">Source field name.</param>
        /// <param name="to">Target field name.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscoderBuilder RenameField(string from, string to)
        {
            if (from is null)
            {
                throw new ArgumentNullException(nameof(from));
            }
            if (to is null)
            {
                throw new ArgumentNullException(nameof(to));
            }
            m_renameMap[from] = to;
            if (!m_renameTransformAdded)
            {
                m_transforms.Add(new FieldRenameTransform(m_renameMap));
                m_renameTransformAdded = true;
            }
            return this;
        }

        /// <summary>
        /// Keeps only the named fields, in the given order.
        /// </summary>
        /// <param name="fieldNames">Field names to keep.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder SelectFields(params string[] fieldNames)
        {
            return AddTransform(new FieldProjectionTransform(fieldNames, exclude: false));
        }

        /// <summary>
        /// Drops the named fields, preserving the rest.
        /// </summary>
        /// <param name="fieldNames">Field names to drop.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder ExcludeFields(params string[] fieldNames)
        {
            return AddTransform(new FieldProjectionTransform(fieldNames, exclude: true));
        }

        /// <summary>
        /// Applies a value transformation to every field.
        /// </summary>
        /// <param name="transform">Value transform (field name, value).</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder TransformValue(Func<string, Variant, Variant> transform)
        {
            return AddTransform(new ValueTransform(transform));
        }

        /// <summary>
        /// Applies a transformation to the metadata of announcement
        /// messages.
        /// </summary>
        /// <param name="transform">Metadata transform.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder TransformMetaData(
            Func<DataSetMetaDataType, DataSetMetaDataType> transform)
        {
            return AddTransform(new MetaDataTransform(transform));
        }

        /// <summary>
        /// Filters DataSetMessages by message type, optionally forcing a
        /// replacement type on those kept.
        /// </summary>
        /// <param name="keep">Predicate selecting types to keep.</param>
        /// <param name="forceType">Optional replacement type.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder FilterMessageTypes(
            Func<PubSubDataSetMessageType, bool> keep,
            PubSubDataSetMessageType? forceType = null)
        {
            return AddTransform(new MessageTypeTransform(keep, forceType));
        }

        /// <summary>
        /// Drops KeepAlive DataSetMessages from the stream.
        /// </summary>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder DropKeepAlive()
        {
            return FilterMessageTypes(
                static type => type != PubSubDataSetMessageType.KeepAlive);
        }

        /// <summary>
        /// Promotes the named DataSet fields into target transport message
        /// properties (e.g. MQTT User Properties). Repeated calls
        /// accumulate. Promotion is <em>copy</em> semantics: the fields
        /// remain in the encoded payload. Ignored by transports without a
        /// header channel.
        /// </summary>
        /// <param name="fieldNames">Field BrowseNames to promote.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        public PubSubTranscoderBuilder PromoteFields(params string[] fieldNames)
        {
            if (fieldNames is null)
            {
                throw new ArgumentNullException(nameof(fieldNames));
            }
            foreach (string fieldName in fieldNames)
            {
                if (fieldName is null)
                {
                    throw new ArgumentException(
                        "Promoted field names must not be null.", nameof(fieldNames));
                }
                m_promotedFields.Add(fieldName);
            }
            return this;
        }

        /// <summary>
        /// Sets an optional prefix prepended to every promoted property key.
        /// </summary>
        /// <param name="prefix">Property key prefix.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscoderBuilder WithPromotedFieldPrefix(string prefix)
        {
            m_promotionPrefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
            return this;
        }

        /// <summary>
        /// Sets the target field encoding (Variant / RawData / DataValue).
        /// </summary>
        /// <param name="encoding">Target field encoding.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder WithFieldEncoding(PubSubFieldEncoding encoding)
        {
            m_fieldEncoding = encoding;
            return this;
        }

        /// <summary>
        /// Emits the flat JSON single-message layout for single-DataSet
        /// messages on a JSON target.
        /// </summary>
        /// <param name="enabled">Whether to enable single-message mode.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder AsJsonSingleMessage(bool enabled = true)
        {
            m_jsonSingleMessage = enabled;
            return this;
        }

        /// <summary>
        /// Controls whether the source MetaDataVersion is preserved on the
        /// target (default <see langword="true"/>).
        /// </summary>
        /// <param name="enabled">Whether to preserve the version.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder PreserveMetaDataVersion(bool enabled = true)
        {
            m_preserveMetaDataVersion = enabled;
            return this;
        }

        /// <summary>
        /// Allows lowering a secured source to an output without
        /// message-layer security (e.g. UADP to JSON).
        /// </summary>
        /// <param name="enabled">Whether to allow the downgrade.</param>
        /// <returns>This builder.</returns>
        public PubSubTranscoderBuilder AllowInsecureCrossEncoding(bool enabled = true)
        {
            m_allowInsecureCrossEncoding = enabled;
            return this;
        }

        /// <summary>
        /// Publishes the transcoded output to a fixed broker topic.
        /// </summary>
        /// <param name="topic">Target topic.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscoderBuilder ToTopic(string topic)
        {
            if (topic is null)
            {
                throw new ArgumentNullException(nameof(topic));
            }
            m_topicSelector = _ => topic;
            return this;
        }

        /// <summary>
        /// Computes the target broker topic per received message.
        /// </summary>
        /// <param name="selector">Topic selector.</param>
        /// <returns>This builder.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public PubSubTranscoderBuilder WithTopicSelector(
            Func<ReceivedNetworkMessage, string?> selector)
        {
            m_topicSelector = selector ?? throw new ArgumentNullException(nameof(selector));
            return this;
        }

        /// <summary>
        /// Builds the immutable <see cref="TranscodeSpec"/> for this route.
        /// </summary>
        /// <returns>The transcode specification.</returns>
        public TranscodeSpec BuildSpec()
        {
            return new TranscodeSpec
            {
                TargetEncoding = m_targetEncoding,
                Transforms = m_transforms,
                TargetOptions = new TranscodeTargetOptions
                {
                    FieldEncoding = m_fieldEncoding,
                    JsonSingleMessageMode = m_jsonSingleMessage,
                    PreserveMetaDataVersion = m_preserveMetaDataVersion
                },
                Promotion = m_promotedFields.Count == 0
                    ? null
                    : new TranscodePromotion
                    {
                        FieldNames = m_promotedFields,
                        PropertyKeyPrefix = m_promotionPrefix
                    }
            };
        }

        internal TranscodingBridgeDescriptor Build()
        {
            if (string.IsNullOrEmpty(m_source))
            {
                throw new InvalidOperationException(
                    "A source connection name is required; call From(...).");
            }
            if (string.IsNullOrEmpty(m_target))
            {
                throw new InvalidOperationException(
                    "A target connection name is required; call To(...).");
            }
            return new TranscodingBridgeDescriptor(
                m_source!,
                m_target!,
                BuildSpec(),
                m_allowInsecureCrossEncoding,
                m_topicSelector);
        }
    }

    /// <summary>
    /// Immutable description of a configured transcoding bridge, resolved
    /// and started by the hosted service.
    /// </summary>
    /// <param name="SourceConnectionName">Source connection name.</param>
    /// <param name="TargetConnectionName">Target connection name.</param>
    /// <param name="Spec">Transcode specification.</param>
    /// <param name="AllowInsecureCrossEncoding">Insecure downgrade policy.</param>
    /// <param name="TopicSelector">Optional target topic selector.</param>
    internal sealed record TranscodingBridgeDescriptor(
        string SourceConnectionName,
        string TargetConnectionName,
        TranscodeSpec Spec,
        bool AllowInsecureCrossEncoding,
        Func<ReceivedNetworkMessage, string?>? TopicSelector);
}
