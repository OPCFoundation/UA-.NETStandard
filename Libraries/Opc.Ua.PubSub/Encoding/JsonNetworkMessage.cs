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

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Json Network Message
    /// </summary>
    public class JsonNetworkMessage : UaNetworkMessage
    {
        #region Fields
        private const string kDataSetMessageType = "ua-data";
        private const string kMetaDataMessageType = "ua-metadata";
        private const string kFieldMessages = "Messages";
        private const string kFieldMetaData = "MetaData";
        private const string kFieldReplyTo = "ReplyTo";

        private JSONNetworkMessageType m_jsonNetworkMessageType;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/>
        /// </summary>
        public JsonNetworkMessage() : this(null, new List<JsonDataSetMessage>())
        {
        }

        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/> as a DataSet message
        /// </summary>
        /// <param name="writerGroupConfiguration">The <see cref="WriterGroupDataType"/> configuration object that produced this message.</param>
        /// <param name="jsonDataSetMessages"><see cref="JsonDataSetMessage"/> list as input</param>
        public JsonNetworkMessage(WriterGroupDataType writerGroupConfiguration, List<JsonDataSetMessage> jsonDataSetMessages)
            : base(writerGroupConfiguration, jsonDataSetMessages?.ConvertAll<UaDataSetMessage>(x => (UaDataSetMessage)x) ?? new List<UaDataSetMessage>())
        {
            MessageId = Guid.NewGuid().ToString();
            MessageType = kDataSetMessageType;
            DataSetClassId = string.Empty;

            m_jsonNetworkMessageType = JSONNetworkMessageType.DataSetMessage;
        }

        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/> as a DataSetMetadata message
        /// </summary>
        public JsonNetworkMessage(WriterGroupDataType writerGroupConfiguration, DataSetMetaDataType metadata)
            : base(writerGroupConfiguration, metadata)
        {
            MessageId = Guid.NewGuid().ToString();
            MessageType = kMetaDataMessageType;
            DataSetClassId = string.Empty;

            m_jsonNetworkMessageType = JSONNetworkMessageType.DataSetMetaData;
        }
        #endregion

        #region Properties
        /// <summary>
        /// NetworkMessageContentMask contains the mask that will be used to check NetworkMessage options selected for usage
        /// </summary>
        public JsonNetworkMessageContentMask NetworkMessageContentMask { get; private set; }

        /// <summary>
        /// Get flag that indicates if message has network message header
        /// </summary>
        public bool HasNetworkMessageHeader
        {
            get
            {
                return (NetworkMessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0;
            }
        }

        /// <summary>
        /// Flag that indicates if the Network message contains a single dataset message
        /// </summary>
        public bool HasSingleDataSetMessage
        {
            get
            {
                return (NetworkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0;
            }
        }

        /// <summary>
        /// Flag that indicates if the Network message dataSets have header
        /// </summary>
        public bool HasDataSetMessageHeader
        {
            get
            {
                return (NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0;
            }
        }

        #region NetworkMessage Header
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

        #endregion

        #endregion

        #region Public Methods

        /// <summary>
        /// Set network message content mask
        /// </summary>
        /// <param name="networkMessageContentMask"></param>
        public void SetNetworkMessageContentMask(JsonNetworkMessageContentMask networkMessageContentMask)
        {
            NetworkMessageContentMask = networkMessageContentMask;

            foreach (JsonDataSetMessage jsonDataSetMessage in DataSetMessages)
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
            using (MemoryStream stream = new MemoryStream())
            {
                Encode(messageContext, stream);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Encodes the object in the specified stream.
        /// </summary>
        /// <param name="messageContext">The context.</param>
        /// <param name="stream">The stream to use.</param>
        public override void Encode(IServiceMessageContext messageContext, Stream stream)
        {
            bool topLevelIsArray = !HasNetworkMessageHeader && !HasSingleDataSetMessage && !IsMetaDataMessage;

            using (JsonEncoder encoder = new JsonEncoder(messageContext, true, topLevelIsArray, stream))
            {
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
                        JsonDataSetMessage jsonDataSetMessage = DataSetMessages[0] as JsonDataSetMessage;

                        if (jsonDataSetMessage != null)
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
                        foreach (var message in DataSetMessages)
                        {
                            JsonDataSetMessage jsonDataSetMessage = message as JsonDataSetMessage;
                            if (jsonDataSetMessage != null)
                            {
                                jsonDataSetMessage.Encode(encoder);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Decodes the message
        /// </summary>
        /// <param name="context"></param>
        /// <param name="message"></param>
        /// <param name="dataSetReaders"></param>
        public override void Decode(IServiceMessageContext context, byte[] message, IList<DataSetReaderDataType> dataSetReaders)
        {
            string json = System.Text.Encoding.UTF8.GetString(message);  

            using (JsonDecoder jsonDecoder = new JsonDecoder(json, context))
            {
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
        }

        
        #endregion

        #region Private Methods - Encoding
        /// <summary>
        /// Encodes the object in a binary stream.
        /// </summary>
        private void Encode(JsonEncoder jsonEncoder)
        {
            if (jsonEncoder == null)
            {
                throw new ArgumentException(nameof(jsonEncoder));
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
        private void EncodeNetworkMessageHeader(IEncoder jsonEncoder)
        {
            jsonEncoder.WriteString(nameof(MessageId), MessageId);
            jsonEncoder.WriteString(nameof(MessageType), MessageType);

            if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMessage)
            {
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    jsonEncoder.WriteString(nameof(PublisherId), PublisherId);
                }

                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    if (HasSingleDataSetMessage)
                    {
                        JsonDataSetMessage jsonDataSetMessage = DataSetMessages[0] as JsonDataSetMessage;

                        if (jsonDataSetMessage?.DataSet?.DataSetMetaData?.DataSetClassId != null)
                        {
                            jsonEncoder.WriteString(nameof(DataSetClassId), jsonDataSetMessage.DataSet.DataSetMetaData.DataSetClassId.ToString());
                        }
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
                    Utils.Trace("The JSON MetaDataMessage cannot be encoded: The DataSetWriterId property is missing for MessageId:{0}.", MessageId);
                }
            }
        }
        
        /// <summary>
        /// Encode DataSetMessages
        /// </summary>
        private void EncodeMessages(JsonEncoder encoder)
        {
            if (DataSetMessages != null && DataSetMessages.Count > 0)
            {
                if (HasSingleDataSetMessage)
                {
                    // encode single dataset message
                    JsonDataSetMessage jsonDataSetMessage = DataSetMessages[0] as JsonDataSetMessage;
                    if (jsonDataSetMessage != null)
                    {
                        jsonDataSetMessage.Encode(encoder, kFieldMessages);
                    }
                }
                else
                {
                    encoder.PushArray(kFieldMessages);
                    foreach (var message in DataSetMessages)
                    {
                        JsonDataSetMessage jsonDataSetMessage = message as JsonDataSetMessage;
                        if (jsonDataSetMessage != null)
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

        #endregion

        #region Private Methods - Decoding 

        /// <summary>
        /// Encode Network Message Header
        /// </summary>
        /// <param name="jsonDecoder"></param>
        private void DecodeNetworkMessageHeader(JsonDecoder jsonDecoder)
        {
            object token = null;
            if (jsonDecoder.ReadField(nameof(MessageId), out token))
            {
                MessageId = jsonDecoder.ReadString(nameof(MessageId));
                NetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader;
            }

            if (jsonDecoder.ReadField(nameof(MessageType), out token))
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

                    Utils.Format("Invalid JSON MessageType: {0}. Supported values are {1} and {2}.",
                        MessageType, kDataSetMessageType, kMetaDataMessageType);
                }
            }

            if (jsonDecoder.ReadField(nameof(PublisherId), out token))
            {
                PublisherId = jsonDecoder.ReadString(nameof(PublisherId));
                if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMessage)
                {
                    // the NetworkMessageContentMask is set only for DataSet messages
                    NetworkMessageContentMask |= JsonNetworkMessageContentMask.PublisherId;
                }
            }

            if (jsonDecoder.ReadField(nameof(DataSetClassId), out token))
            {
                DataSetClassId = jsonDecoder.ReadString(nameof(DataSetClassId));
                NetworkMessageContentMask |= JsonNetworkMessageContentMask.DataSetClassId;
            }

            if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMetaData) 
            {
                // for metadata messages the DataSetWriterId field is mandatory
                if (jsonDecoder.ReadField(nameof(DataSetWriterId), out token))
                {
                    DataSetWriterId = jsonDecoder.ReadUInt16(nameof(DataSetWriterId));
                }
                else
                {
                    Utils.Trace("The JSON MetaDataMessage cannot be decoded: The DataSetWriterId property is missing for MessageId:{0}.", MessageId);
                }
            }
        }

        /// <summary>
        /// Decode the jsonDecoder content as a MetaData message
        /// </summary>
        /// <param name="jsonDecoder"></param>
        private void DecodeMetaDataMessage(JsonDecoder jsonDecoder)
        {
            try
            {
                m_metadata = jsonDecoder.ReadEncodeable(kFieldMetaData, typeof(DataSetMetaDataType)) as DataSetMetaDataType;
            }
            catch (Exception ex)
            {
                // Unexpected exception in DecodeMetaDataMessage
                Utils.Trace(ex, "JsonNetworkMessage.DecodeMetaDataMessage");
            }
        }

        /// <summary>
        /// Decode the stream from decoder parameter and produce a Dataset
        /// </summary>
        /// <param name="jsonDecoder"></param>
        /// <param name="dataSetReaders"></param>
        /// <returns></returns>
        private void DecodeSubscribedDataSets(JsonDecoder jsonDecoder, IList<DataSetReaderDataType> dataSetReaders)
        {
            if (dataSetReaders == null || dataSetReaders.Count == 0)
            {
                return;
            }
            try
            {
                List<DataSetReaderDataType> dataSetReadersFiltered = new List<DataSetReaderDataType>();

                // 1. decode network message header (PublisherId & DataSetClassId)
                DecodeNetworkMessageHeader(jsonDecoder);

                // handle metadata messages.
                if (m_jsonNetworkMessageType == JSONNetworkMessageType.DataSetMetaData)
                {
                    m_metadata = jsonDecoder.ReadEncodeable(kFieldMetaData, typeof(DataSetMetaDataType)) as DataSetMetaDataType;
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
                    else if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0
                        && PublisherId != null
                        && PublisherId.Equals(dataSetReader.PublisherId.Value.ToString()))
                    {
                        dataSetReadersFiltered.Add(dataSetReader);
                    }
                }
                if (dataSetReadersFiltered.Count == 0)
                {
                    return;
                }
                dataSetReaders = dataSetReadersFiltered;

                object messagesToken = null;
                List<object> messagesList = null;
                string messagesListName = string.Empty;
                if (jsonDecoder.ReadField(kFieldMessages, out messagesToken))
                {
                    messagesList = messagesToken as List<object>;
                    if (messagesList == null)
                    {
                        // this is a SingleDataSetMessage encoded as the content of Messages 
                        jsonDecoder.PushStructure(kFieldMessages);
                        messagesList = new List<object>();
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
                    messagesList = new List<object>();
                }
                if (messagesList != null)
                {
                    // atempt decoding for each data set reader
                    foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                    {
                        JsonDataSetReaderMessageDataType jsonMessageSettings = ExtensionObject.ToEncodeable(dataSetReader.MessageSettings)
                           as JsonDataSetReaderMessageDataType;
                        if (jsonMessageSettings == null)
                        {
                            // The reader MessageSettings is not set up correctly 
                            continue;
                        }
                        JsonNetworkMessageContentMask networkMessageContentMask =
                            (JsonNetworkMessageContentMask)jsonMessageSettings.NetworkMessageContentMask;
                        if ((networkMessageContentMask & NetworkMessageContentMask) != NetworkMessageContentMask)
                        {
                            // The reader MessageSettings.NetworkMessageContentMask is not set up correctly 
                            continue;
                        }

                        // initialize the dataset message
                        JsonDataSetMessage jsonDataSetMessage = new JsonDataSetMessage();
                        jsonDataSetMessage.DataSetMessageContentMask = (JsonDataSetMessageContentMask)jsonMessageSettings.DataSetMessageContentMask;
                        jsonDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetReader.DataSetFieldContentMask);
                        // set the flag that indicates if dataset message shall have a header
                        jsonDataSetMessage.HasDataSetMessageHeader = (networkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0;

                        jsonDataSetMessage.DecodePossibleDataSetReader(jsonDecoder, messagesList.Count, messagesListName, dataSetReader);
                        if (jsonDataSetMessage.DataSet != null)
                        {
                            m_uaDataSetMessages.Add(jsonDataSetMessage);
                        }
                        else if (jsonDataSetMessage.DecodeErrorReason == DataSetDecodeErrorReason.MetadataMajorVersion)
                        {
                            OnDataSetDecodeErrorOccurred(new DataSetDecodeErrorEventArgs(DataSetDecodeErrorReason.MetadataMajorVersion, this, dataSetReader));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Unexpected exception in DecodeSubscribedDataSets
                Utils.Trace(ex, "JsonNetworkMessage.DecodeSubscribedDataSets");
            }
        }
        #endregion
    }
}
