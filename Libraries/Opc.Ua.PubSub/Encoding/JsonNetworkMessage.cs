/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.PubSub.PublishedData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Opc.Ua.PubSub.Encoding
{
    /// <summary>
    /// Json Network Message
    /// </summary>
    internal class JsonNetworkMessage : UaNetworkMessage
    {
        #region Fields
        private const string DefaultMessageType = "ua-data";
        private const string FieldMessageId = "MessageId";
        private const string FieldMessageType = "MessageType";
        private const string FieldPublisherId = "PublisherId";
        private const string FieldDataSetClassId = "DataSetClassId";
        private const string FieldMessages = "Messages";

        private JSONNetworkMessageType m_jsonNetworkMessageType;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/>
        /// </summary>
        public JsonNetworkMessage() : this(null, new List<UaDataSetMessage>())
        {

        }

        /// <summary>
        /// Create new instance of <see cref="JsonNetworkMessage"/>
        /// </summary>
        /// <param name="writerGroupConfiguration">The <see cref="WriterGroupDataType"/> confguration object that produced this message.</param>  
        /// <param name="jsonDataSetMessages"><see cref="JsonDataSetMessage"/> list as input</param>
        public JsonNetworkMessage(WriterGroupDataType writerGroupConfiguration, List<UaDataSetMessage> jsonDataSetMessages) : base(writerGroupConfiguration, jsonDataSetMessages)
        {           
            MessageType = DefaultMessageType;
            DataSetClassId = string.Empty;
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
        /// This value shall be “ua-data”.
        /// This value is mandatory.
        /// </summary>
        public string MessageType { get; private set; }

        /// <summary>
        /// Get and Set PublisherId type
        /// </summary>
        public string PublisherId { get; set; }

        /// <summary>
        /// Get and Set DataSetClassId
        /// </summary>
        public string DataSetClassId { get; set; }

        /// <summary>
        /// Get and Set SingleDataSetMessage
        /// </summary>
        public string SingleDataSetMessage { get; set; }

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

            foreach(JsonDataSetMessage jsonDataSetMessage in DataSetMessages)
            {
                jsonDataSetMessage.HasDataSetMessageHeader = HasDataSetMessageHeader;
            }
        }

        /// <summary>
        /// Encodes the object and returns the resulting byte array.
        /// </summary>
        /// <returns></returns>
        public override byte[] Encode()
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();
            bool topLevelIsArray = !HasNetworkMessageHeader && !HasSingleDataSetMessage;

            using (JsonEncoder encoder = new JsonEncoder(messageContext, false, null, topLevelIsArray))
            {
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
                        encoder.PushStructure(null);
                        JsonDataSetMessage jsonDataSetMessage = DataSetMessages[0] as JsonDataSetMessage;
                        if (jsonDataSetMessage != null)
                        {
                            jsonDataSetMessage.Encode(encoder);
                        }                        
                        encoder.PopStructure();
                    }
                    else
                    {
                        foreach (var message in DataSetMessages)
                        {
                            JsonDataSetMessage jsonDataSetMessage = message as JsonDataSetMessage;
                            if (jsonDataSetMessage != null)
                            {
                                encoder.PushStructure(null);
                                jsonDataSetMessage.Encode(encoder);
                                encoder.PopStructure();
                            }                            
                        }
                    }
                }
                
                byte[] bytes = System.Text.Encoding.ASCII.GetBytes(encoder.CloseAndReturnText());

                return bytes;
            }
        }

        /// <summary>
        /// Decodes the message 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="message"></param>
        /// <param name="dataSetReaders"></param>
        public override void Decode(string source, byte[] message, IList<DataSetReaderDataType> dataSetReaders)
        {
            if (dataSetReaders == null || dataSetReaders.Count == 0)
            {
                return;
            }

            ServiceMessageContext messageContext = new ServiceMessageContext();
            string json = System.Text.Encoding.ASCII.GetString(message);

            // TODO temporary display message until decoding is in place
            Console.WriteLine("Decoding from source: {0}; json\n{1}", source, json);

            using (JsonDecoder decoder = new JsonDecoder(json, messageContext))
            {                
                //decode bytes using dataset reader information
                DecodeSubscribedDataSets(decoder, dataSetReaders);                
            }
        }
        #endregion

        #region Private Methods - Encoding
        /// <summary>
        /// Encodes the object in a binary stream.
        /// </summary>
        /// <param name="jsonEncoder"></param>
        private void Encode(JsonEncoder jsonEncoder)
        {
            if (jsonEncoder == null)
            {
                throw new ArgumentException(nameof(jsonEncoder));
            }
            // temporary save the mask
            jsonEncoder.WriteUInt32("NetworkMessageContentMask", (UInt32)NetworkMessageContentMask);

            if (HasNetworkMessageHeader)
            {
                EncodeNetworkMessageHeader(jsonEncoder);
            }
            EncodeMessages(jsonEncoder);
            EncodeReplyTo(jsonEncoder);
        }


        /// <summary>
        /// Decode the stream from decoder parameter and produce a Dataset 
        /// </summary> 
        /// <param name="jsonDecoder"></param>
        /// <param name="dataSetReaders"></param>
        /// <returns></returns>
        public void DecodeSubscribedDataSets(JsonDecoder jsonDecoder, IEnumerable<DataSetReaderDataType> dataSetReaders)
        {
            ReceivedDataSets = new List<DataSet>();
            try
            {
                List<DataSetReaderDataType> dataSetReadersFiltered = new List<DataSetReaderDataType>();

                // 1. decode network message header (PublisherId & DataSetClassId)
                DecodeNetworkMessageHeader(jsonDecoder);

                ////ignore network messages that are not dataSet messages
                if (m_jsonNetworkMessageType != JSONNetworkMessageType.DataSetMessage)
                {
                   return;
                }

                //* 6.2.8.1 PublisherId
                // The parameter PublisherId defines the Publisher to receive NetworkMessages from.
                // If the value is null, the parameter shall be ignored and all received NetworkMessages pass the PublisherId filter. */
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    //check Enabled & publisher id
                    if (PublisherId.Equals(dataSetReader.PublisherId.Value.ToString()))
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
                if (jsonDecoder.ReadField(FieldMessages, out messagesToken))
                {
                    messagesList = messagesToken as List<object>;
                }

                if (messagesList != null && messagesList.Count > 0)
                { 
                    // atempt decoding for each data set reader
                    foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                    {
                        JsonDataSetReaderMessageDataType jsonMessageSettings = ExtensionObject.ToEncodeable(dataSetReader.MessageSettings)
                           as JsonDataSetReaderMessageDataType;
                        if (jsonMessageSettings == null)
                        {
                            // The reader MessageSettings is not set up corectly 
                            continue;
                        }
                        JsonNetworkMessageContentMask networkMessageContentMask =
                            (JsonNetworkMessageContentMask)jsonMessageSettings.NetworkMessageContentMask;
                        if ((networkMessageContentMask & NetworkMessageContentMask) != NetworkMessageContentMask)
                        {
                            // The reader MessageSettings.NetworkMessageContentMask is not set up corectly 
                            continue;
                        }

                        // initialize the dataset message
                        JsonDataSetMessage jsonDataSetMessage = new JsonDataSetMessage();
                        jsonDataSetMessage.SetMessageContentMask((JsonDataSetMessageContentMask)jsonMessageSettings.DataSetMessageContentMask);
                        jsonDataSetMessage.SetFieldContentMask((DataSetFieldContentMask)dataSetReader.DataSetFieldContentMask);





                        DataSet dataSet = jsonDataSetMessage.DecodePossibleDataSetReader(messagesList, dataSetReader);
                        if (dataSet != null)
                        {
                            ReceivedDataSets.Add(dataSet);
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

        /// <summary>
        ///  Encode Network Message Header
        /// </summary>
        /// <param name="jsonEncoder"></param>
        private void EncodeNetworkMessageHeader(JsonEncoder jsonEncoder)
        {
            jsonEncoder.WriteString(FieldMessageId, MessageId);
            jsonEncoder.WriteString(FieldMessageType, MessageType);

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
            {
                jsonEncoder.WriteString(FieldPublisherId, PublisherId);
            }
            //else
            //{
            //    jsonEncoder.WriteString(FieldPublisherId, null);
            //}

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
            {
                jsonEncoder.WriteString(FieldDataSetClassId, DataSetClassId);
            }
            //else
            //{
            //    jsonEncoder.WriteString(FieldDataSetClassId, DataSetClassId);
            //}
        }

        /// <summary>
        /// Encode DataSetMessages
        /// </summary>
        /// <param name="encoder"></param>
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
                        // (UA Specs:) If the NetworkMessageHeader and the DataSetMessageHeader bits are not set
                        // and SingleDataSetMessage bit is set, the NetworkMessage
                        // is a JSON object containing the set of name/value pairs defined for a single DataSet.
                        if (!jsonDataSetMessage.HasDataSetMessageHeader && !HasDataSetMessageHeader)
                        {
                            jsonDataSetMessage.EncodePayload(encoder, false);
                        }
                        else
                        {
                            encoder.PushArray(FieldMessages);
                            jsonDataSetMessage.Encode(encoder);
                            encoder.PopArray();
                        }
                    }                   
                }
                else
                {
                    encoder.PushArray(FieldMessages);
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
        /// <param name="jsonEncoder"></param>
        private void EncodeReplyTo(JsonEncoder jsonEncoder)
        {
            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.ReplyTo) != 0)
            {
                jsonEncoder.WriteString("ReplyTo", ReplyTo);
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
            if (jsonDecoder.ReadField(FieldMessageId, out token))
            {
                MessageId = jsonDecoder.ReadString(FieldMessageId);
                NetworkMessageContentMask = JsonNetworkMessageContentMask.NetworkMessageHeader;
            }

            if (jsonDecoder.ReadField(FieldMessageType, out token))
            {
                MessageType = jsonDecoder.ReadString(FieldMessageType);
            }

            if (jsonDecoder.ReadField(FieldPublisherId, out token))
            {
                PublisherId = jsonDecoder.ReadString(FieldPublisherId);
                NetworkMessageContentMask = NetworkMessageContentMask | JsonNetworkMessageContentMask.PublisherId;
            }

            if (jsonDecoder.ReadField(FieldPublisherId, out token))
            {
                DataSetClassId = jsonDecoder.ReadString(FieldDataSetClassId);
                NetworkMessageContentMask = NetworkMessageContentMask | JsonNetworkMessageContentMask.DataSetClassId;
            }
        }

        #endregion
    }
}
