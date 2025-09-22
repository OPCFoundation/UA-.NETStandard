/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Json Network Message
    /// </summary>
    public class JsonNetworkMessage : UaNetworkMessage
    {
        private const string kDataSetMessageType = "ua-data";
        private const string kMetaDataMessageType = "ua-metadata";
        private const string kFieldMessages = "Messages";
        private const string kFieldMetaData = "MetaData";
        private const string kFieldReplyTo = "ReplyTo";

        private JSONNetworkMessageType m_jsonNetworkMessageType;

        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/>
        /// </summary>
        public JsonNetworkMessage(ILogger logger = null)
            : this(null, [], logger)
        {
        }

        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/> as a DataSet message
        /// </summary>
        /// <param name="writerGroupConfiguration">The <see cref="WriterGroupDataType"/> configuration object that produced this message.</param>
        /// <param name="jsonDataSetMessages"><see cref="JsonDataSetMessage"/> list as input</param>
        /// <param name="logger">A contextual logger to log to</param>
        public JsonNetworkMessage(
            WriterGroupDataType writerGroupConfiguration,
            List<JsonDataSetMessage> jsonDataSetMessages,
            ILogger logger = null)
            : base(
                writerGroupConfiguration,
                jsonDataSetMessages?.ConvertAll<UaDataSetMessage>(x => x) ?? [],
                logger)
        {
            MessageId = Guid.NewGuid().ToString();
            MessageType = kDataSetMessageType;
            DataSetClassId = string.Empty;

            m_jsonNetworkMessageType = JSONNetworkMessageType.DataSetMessage;
        }

        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/> as a DataSetMetaData message
        /// </summary>
        public JsonNetworkMessage(
            WriterGroupDataType writerGroupConfiguration,
            DataSetMetaDataType metadata,
            ILogger logger = null)
            : base(writerGroupConfiguration, metadata, logger)
        {
            MessageId = Guid.NewGuid().ToString();
            MessageType = kMetaDataMessageType;
            DataSetClassId = string.Empty;

            m_jsonNetworkMessageType = JSONNetworkMessageType.DataSetMetaData;
        }

        /// <summary>
        /// NetworkMessageContentMask contains the mask that will be used to check
        /// NetworkMessage options selected for usage
        /// </summary>
        public JsonNetworkMessageContentMask NetworkMessageContentMask { get; private set; }

        /// <summary>
        /// Get flag that indicates if message has network message header
        /// </summary>
        public bool HasNetworkMessageHeader =>
            ((int)NetworkMessageContentMask &
                (int)JsonNetworkMessageContentMask.NetworkMessageHeader) != 0;

        /// <summary>
        /// Flag that indicates if the Network message contains a single dataset message
        /// </summary>
        public bool HasSingleDataSetMessage =>
            ((int)NetworkMessageContentMask &
                (int)JsonNetworkMessageContentMask.SingleDataSetMessage) != 0;

        /// <summary>
        /// Flag that indicates if the Network message dataSets have header
        /// </summary>
        public bool HasDataSetMessageHeader =>
            ((int)NetworkMessageContentMask &
                (int)JsonNetworkMessageContentMask.DataSetMessageHeader) != 0;

        /// <summary>
        /// A globally unique identifier for the message.
        /// This value is mandatory.
        /// </summary>
        public string MessageId { get; set; }

        /// <summary>
        /// This value shall be “ua-data” or "ua-metadata"
        /// This value is mandatory.
        /// </summary>
        public string MessageType { get; private set; }

        /// <summary>
        /// Get and Set PublisherId
        /// </summary>
        public string PublisherId { get; set; }

        /// <summary>
        /// Get and Set DataSetClassId
        /// </summary>
        public string DataSetClassId { get; set; }

        /// <summary>
        /// Get and Set ReplyTo
        /// </summary>
        public string ReplyTo { get; set; }

        /// <summary>
        /// Set network message content mask
        /// </summary>
        public void SetNetworkMessageContentMask(
            JsonNetworkMessageContentMask networkMessageContentMask)
        {
            NetworkMessageContentMask = networkMessageContentMask;

            foreach (JsonDataSetMessage jsonDataSetMessage in DataSetMessages
                .Cast<JsonDataSetMessage>())
            {
                jsonDataSetMessage.HasDataSetMessageHeader = HasDataSetMessageHeader;
            }
        }

        /// <summary>
        /// Encodes the object and returns the resulting byte array.
        /// </summary>
        /// <param name="messageContext">The context.</param>
        public override byte[] Encode(IServiceMessageContext messageContext)
        {
            using var stream = new MemoryStream();
            Encode(messageContext, stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Encodes the object in the specified stream.
        /// </summary>
        /// <param name="messageContext">The context.</param>
        /// <param name="stream">The stream to use.</param>
        public override void Encode(IServiceMessageContext messageContext, Stream stream)
        {
            bool topLevelIsArray = !HasNetworkMessageHeader &&
                !HasSingleDataSetMessage &&
                !IsMetaDataMessage;

            using var encoder = new JsonEncoder(messageContext, true, topLevelIsArray, stream);
            if (IsMetaDataMessage)
            {
                EncodeNetworkMessageHeader(encoder);

                encoder.WriteEncodeable(kFieldMetaData, m_metadata, null);

                return;
            }

            // handle no header
            if (HasNetworkMessageHeader)
            {
                Encode(encoder);
            }
            else if (DataSetMessages != null && DataSetMessages.Count > 0)
            {
                if (HasSingleDataSetMessage)
                {
                    // encode single dataset message

                    if (DataSetMessages[0] is JsonDataSetMessage jsonDataSetMessage)
                    {
                        if (!jsonDataSetMessage.HasDataSetMessageHeader)
                        {
                            // If the NetworkMessageHeader and the DataSetMessageHeader bits are not set
                            // and SingleDataSetMessage bit is set, the NetworkMessage is a JSON object
                            // containing the set of name/value pairs defined for a single DataSet.
                            jsonDataSetMessage.EncodePayload(encoder, false);
                        }
                        else
                        {
                            // If the SingleDataSetMessage bit of the NetworkMessageContentMask is set,
                            // the content of the Messages field is a JSON object containing a single DataSetMessage.
                            jsonDataSetMessage.Encode(encoder);
                        }
                    }
                }
                else
                {
                    // If the NetworkMessageHeader bit of the NetworkMessageContentMask is not set,
                    // the NetworkMessage is the contents of the Messages field (e.g. a JSON array of DataSetMessages).
                    foreach (UaDataSetMessage message in DataSetMessages)
                    {
                        if (message is JsonDataSetMessage jsonDataSetMessage)
                        {
                            jsonDataSetMessage.Encode(encoder);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Decodes the message
        /// </summary>
        public override void Decode(
            IServiceMessageContext context,
            byte[] message,
            IList<DataSetReaderDataType> dataSetReaders)
        {
            string json = System.Text.Encoding.UTF8.GetString(message);

            using var jsonDecoder = new JsonDecoder(json, context);
            // 1. decode network message header (PublisherId & DataSetClassId)
            DecodeNetworkMessageHeader(jsonDecoder);

            if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMetaData)
            {
                DecodeMetaDataMessage(jsonDecoder);
            }
            else if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMessage)
            {
                //decode bytes using dataset reader information
                DecodeSubscribedDataSets(jsonDecoder, dataSetReaders);
            }
        }

        /// <summary>
        /// Encodes the object in a binary stream.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="jsonEncoder"/></exception>
        private void Encode(IJsonEncoder jsonEncoder)
        {
            if (jsonEncoder == null)
            {
                throw new ArgumentException(null, nameof(jsonEncoder));
            }

            if (HasNetworkMessageHeader)
            {
                EncodeNetworkMessageHeader(jsonEncoder);
            }
            EncodeMessages(jsonEncoder);
            EncodeReplyTo(jsonEncoder);
        }

        /// <summary>
        ///  Encode Network Message Header
        /// </summary>
        private void EncodeNetworkMessageHeader(IJsonEncoder jsonEncoder)
        {
            jsonEncoder.WriteString(nameof(MessageId), MessageId);
            jsonEncoder.WriteString(nameof(MessageType), MessageType);

            if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMessage)
            {
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    jsonEncoder.WriteString(nameof(PublisherId), PublisherId);
                }

                if ((NetworkMessageContentMask &
                    JsonNetworkMessageContentMask.DataSetClassId) != 0 &&
                    HasSingleDataSetMessage)
                {
                    var jsonDataSetMessage = DataSetMessages[0] as JsonDataSetMessage;

                    if (jsonDataSetMessage?.DataSet?.DataSetMetaData?.DataSetClassId != null)
                    {
                        jsonEncoder.WriteString(
                            nameof(DataSetClassId),
                            jsonDataSetMessage.DataSet.DataSetMetaData.DataSetClassId.ToString());
                    }
                }
            }
            else if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMetaData)
            {
                jsonEncoder.WriteString(nameof(PublisherId), PublisherId);

                if (DataSetWriterId != null)
                {
                    jsonEncoder.WriteUInt16(nameof(DataSetWriterId), DataSetWriterId.Value);
                }
                else
                {
                    m_logger.LogInformation(
                        "The JSON MetaDataMessage cannot be encoded: The DataSetWriterId property is missing for MessageId:{MessageId}.",
                        MessageId);
                }
            }
        }

        /// <summary>
        /// Encode DataSetMessages
        /// </summary>
        private void EncodeMessages(IJsonEncoder encoder)
        {
            if (DataSetMessages != null && DataSetMessages.Count > 0)
            {
                if (HasSingleDataSetMessage)
                {
                    // encode single dataset message
                    if (DataSetMessages[0] is JsonDataSetMessage jsonDataSetMessage)
                    {
                        jsonDataSetMessage.Encode(encoder, kFieldMessages);
                    }
                }
                else
                {
                    encoder.PushArray(kFieldMessages);
                    foreach (UaDataSetMessage message in DataSetMessages)
                    {
                        if (message is JsonDataSetMessage jsonDataSetMessage)
                        {
                            jsonDataSetMessage.Encode(encoder);
                        }
                    }
                    encoder.PopArray();
                }
            }
        }

        /// <summary>
        /// Encode ReplyTo
        /// </summary>
        private void EncodeReplyTo(IEncoder jsonEncoder)
        {
            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.ReplyTo) != 0)
            {
                jsonEncoder.WriteString(kFieldReplyTo, ReplyTo);
            }
        }

        /// <summary>
        /// Encode Network Message Header
        /// </summary>
        private void DecodeNetworkMessageHeader(JsonDecoder jsonDecoder)
        {
            if (jsonDecoder.ReadField(nameof(MessageId), out _))
            {
                MessageId = jsonDecoder.ReadString(nameof(MessageId));
                NetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader;
            }

            if (jsonDecoder.ReadField(nameof(MessageType), out _))
            {
                MessageType = jsonDecoder.ReadString(nameof(MessageType));

                // detect the json network message type
                if (MessageType == kDataSetMessageType)
                {
                    m_jsonNetworkMessageType = JSONNetworkMessageType.DataSetMessage;
                }
                else if (MessageType == kMetaDataMessageType)
                {
                    m_jsonNetworkMessageType = JSONNetworkMessageType.DataSetMetaData;
                }
                else
                {
                    m_jsonNetworkMessageType = JSONNetworkMessageType.Invalid;

                    Utils.Format(
                        "Invalid JSON MessageType: {0}. Supported values are {1} and {2}.",
                        MessageType,
                        kDataSetMessageType,
                        kMetaDataMessageType);
                }
            }

            if (jsonDecoder.ReadField(nameof(PublisherId), out _))
            {
                PublisherId = jsonDecoder.ReadString(nameof(PublisherId));
                if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMessage)
                {
                    // the NetworkMessageContentMask is set only for DataSet messages
                    NetworkMessageContentMask |= JsonNetworkMessageContentMask.PublisherId;
                }
            }

            if (jsonDecoder.ReadField(nameof(DataSetClassId), out _))
            {
                DataSetClassId = jsonDecoder.ReadString(nameof(DataSetClassId));
                NetworkMessageContentMask |= JsonNetworkMessageContentMask.DataSetClassId;
            }

            if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMetaData)
            {
                // for metadata messages the DataSetWriterId field is mandatory
                if (jsonDecoder.ReadField(nameof(DataSetWriterId), out _))
                {
                    DataSetWriterId = jsonDecoder.ReadUInt16(nameof(DataSetWriterId));
                }
                else
                {
                    m_logger.LogInformation(
                        "The JSON MetaDataMessage cannot be decoded: The DataSetWriterId property is missing for MessageId:{MessageId}.",
                        MessageId);
                }
            }
        }

        /// <summary>
        /// Decode the jsonDecoder content as a MetaData message
        /// </summary>
        private void DecodeMetaDataMessage(JsonDecoder jsonDecoder)
        {
            try
            {
                m_metadata =
                    jsonDecoder.ReadEncodeable(
                        kFieldMetaData,
                        typeof(DataSetMetaDataType)) as DataSetMetaDataType;
            }
            catch (Exception ex)
            {
                // Unexpected exception in DecodeMetaDataMessage
                m_logger.LogError(ex, "JsonNetworkMessage.DecodeMetaDataMessage");
            }
        }

        /// <summary>
        /// Decode the stream from decoder parameter and produce a Dataset
        /// </summary>
        private void DecodeSubscribedDataSets(
            JsonDecoder jsonDecoder,
            IList<DataSetReaderDataType> dataSetReaders)
        {
            if (dataSetReaders == null || dataSetReaders.Count == 0)
            {
                return;
            }
            try
            {
                var dataSetReadersFiltered = new List<DataSetReaderDataType>();

                // 1. decode network message header (PublisherId & DataSetClassId)
                DecodeNetworkMessageHeader(jsonDecoder);

                // handle metadata messages.
                if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMetaData)
                {
                    m_metadata =
                        jsonDecoder.ReadEncodeable(
                            kFieldMetaData,
                            typeof(DataSetMetaDataType)) as DataSetMetaDataType;
                    return;
                }

                // ignore network messages that are not dataSet messages
                if (m_jsonNetworkMessageType != JSONNetworkMessageType.DataSetMessage)
                {
                    return;
                }

                //* 6.2.8.1 PublisherId
                // The parameter PublisherId defines the Publisher to receive NetworkMessages from.
                // If the value is null, the parameter shall be ignored and all received NetworkMessages pass the PublisherId filter. */
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    if (dataSetReader.PublisherId == Variant.Null)
                    {
                        dataSetReadersFiltered.Add(dataSetReader);
                    }
                    // publisher id
                    else if ((NetworkMessageContentMask &
                        JsonNetworkMessageContentMask.PublisherId) != 0 &&
                        PublisherId != null &&
                        PublisherId.Equals(
                            dataSetReader.PublisherId.Value.ToString(),
                            StringComparison.Ordinal))
                    {
                        dataSetReadersFiltered.Add(dataSetReader);
                    }
                }
                if (dataSetReadersFiltered.Count == 0)
                {
                    return;
                }
                dataSetReaders = dataSetReadersFiltered;

                List<object> messagesList = null;
                string messagesListName = string.Empty;
                if (jsonDecoder.ReadField(kFieldMessages, out object messagesToken))
                {
                    messagesList = messagesToken as List<object>;
                    if (messagesList == null)
                    {
                        // this is a SingleDataSetMessage encoded as the content of Messages
                        jsonDecoder.PushStructure(kFieldMessages);
                        messagesList = [];
                    }
                    else
                    {
                        messagesListName = kFieldMessages;
                    }
                }
                else if (jsonDecoder.ReadField(JsonDecoder.RootArrayName, out messagesToken))
                {
                    messagesList = messagesToken as List<object>;
                    messagesListName = JsonDecoder.RootArrayName;
                }
                else
                {
                    // this is a SingleDataSetMessage encoded as the content json
                    messagesList = [];
                }
                if (messagesList != null)
                {
                    // attempt decoding for each data set reader
                    foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                    {
                        if (ExtensionObject.ToEncodeable(dataSetReader.MessageSettings)
                            is not JsonDataSetReaderMessageDataType jsonMessageSettings)
                        {
                            // The reader MessageSettings is not set up correctly
                            continue;
                        }
                        var networkMessageContentMask = (JsonNetworkMessageContentMask)
                            jsonMessageSettings.NetworkMessageContentMask;
                        if ((networkMessageContentMask &
                            NetworkMessageContentMask) != NetworkMessageContentMask)
                        {
                            // The reader MessageSettings.NetworkMessageContentMask is not set up correctly
                            continue;
                        }

                        // initialize the dataset message
                        var jsonDataSetMessage = new JsonDataSetMessage(m_logger)
                        {
                            DataSetMessageContentMask = (JsonDataSetMessageContentMask)
                                jsonMessageSettings.DataSetMessageContentMask
                        };
                        jsonDataSetMessage.SetFieldContentMask(
                            (DataSetFieldContentMask)dataSetReader.DataSetFieldContentMask);
                        // set the flag that indicates if dataset message shall have a header
                        jsonDataSetMessage.HasDataSetMessageHeader =
                            (networkMessageContentMask &
                                JsonNetworkMessageContentMask.DataSetMessageHeader) != 0;

                        jsonDataSetMessage.DecodePossibleDataSetReader(
                            jsonDecoder,
                            messagesList.Count,
                            messagesListName,
                            dataSetReader);
                        if (jsonDataSetMessage.DataSet != null)
                        {
                            m_uaDataSetMessages.Add(jsonDataSetMessage);
                        }
                        else if (jsonDataSetMessage
                            .DecodeErrorReason == DataSetDecodeErrorReason.MetadataMajorVersion)
                        {
                            OnDataSetDecodeErrorOccurred(
                                new DataSetDecodeErrorEventArgs(
                                    DataSetDecodeErrorReason.MetadataMajorVersion,
                                    this,
                                    dataSetReader));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception in DecodeSubscribedDataSets
                m_logger.LogError(ex, "JsonNetworkMessage.DecodeSubscribedDataSets");
            }
        }
    }
}
