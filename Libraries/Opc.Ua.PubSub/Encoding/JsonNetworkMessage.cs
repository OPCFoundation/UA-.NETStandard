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
            MessageId = Guid.NewGuid().ToString();
            MessageType = DefaultMessageType;

            DataSetClassId = string.Empty;
            //Timestamp = DateTime.UtcNow;
        }
        #endregion

        #region Properties
        /// <summary>
        /// NetworkMessageContentMask contains the mask that will be used to check NetworkMessage options selected for usage  
        /// </summary>
        public JsonNetworkMessageContentMask NetworkMessageContentMask { get; private set; }

        #region NetworkMessage Header

        /// <summary>
        /// Get Json Flags
        /// </summary>
        public JSONFlagsEncodingMask JSONFlags { get; private set; }

        /// <summary>
        /// A globally unique identifier for the message.
        /// This value is mandatory.
        /// </summary>
        public string MessageId { get; private set; }

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

            SetFlags();
        }

        /// <summary>
        /// Encodes the object and returns the resulting byte array.
        /// </summary>
        /// <returns></returns>
        public override byte[] Encode()
        {
            ServiceMessageContext messageContext = new ServiceMessageContext();
            byte[] bytes = null;
            using (JsonEncoder encoder = new JsonEncoder(messageContext, true))
            {
                Encode(encoder);
                bytes = System.Text.Encoding.ASCII.GetBytes(encoder.CloseAndReturnText());

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

            EncodeNetworkMessageHeader(jsonEncoder);
            EncodePayload(jsonEncoder);
            EncodeReplyTo(jsonEncoder);
        }


        /// <summary>
        /// Set All flags before encode/decode
        /// </summary>
        private void SetFlags()
        {
            JSONFlags = 0;

            #region Network Message Header

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                // Enable NetworkMessageHeader usage
                JSONFlags |= JSONFlagsEncodingMask.NetworkMessageHeader;

                #region SingleDataSetMessage 
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    JSONFlags |= JSONFlagsEncodingMask.SingleDataSetMessage;
                }
                #endregion

                #region PublisherId in network message
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    JSONFlags |= JSONFlagsEncodingMask.PublishedId;
                }
                #endregion

                #region DataSetClassId in network message
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    JSONFlags |= JSONFlagsEncodingMask.DataSetClassId;
                }
                #endregion

            }
            else
            {
                // Enable ExtendedFlags1 usage
                JSONFlags &= ~JSONFlagsEncodingMask.NetworkMessageHeader;

                #region SingleDataSetMessage 
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    JSONFlags &= ~JSONFlagsEncodingMask.SingleDataSetMessage;
                }
                #endregion

                #region PublisherId in network message
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    JSONFlags &= ~JSONFlagsEncodingMask.PublishedId;
                }
                #endregion

                #region DataSetClassId in network message
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    JSONFlags &= ~JSONFlagsEncodingMask.DataSetClassId;
                }
                #endregion
            }
            #endregion

            #region DataSet Message Header
            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0)
            {
                JSONFlags |= JSONFlagsEncodingMask.DataSetMessageHeader;
            }
            #endregion

            #region DataSetClassId in network message
            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.ReplyTo) != 0)
            {
                JSONFlags |= JSONFlagsEncodingMask.ReplyTo;
            }
            #endregion

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
                if (m_jsonNetworkMessageType != JSONNetworkMessageType.DataSetMessage
                    || PublisherId == null)
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

                //continue filtering
                dataSetReadersFiltered = new List<DataSetReaderDataType>();

                //// 2. decode WriterGroupId
                //DecodeGroupMessageHeader(jsonDecoder);
                ///* 6.2.8.2 WriterGroupId
                //The parameter WriterGroupId with DataType UInt16 defines the identifier of the corresponding WriterGroup.
                //The default value 0 is defined as null value, and means this parameter shall be ignored.*/
                //foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                //{
                //    //check WriterGroupId id
                //    if (dataSetReader.WriterGroupId == 0 || dataSetReader.WriterGroupId == WriterGroupId)
                //    {
                //        dataSetReadersFiltered.Add(dataSetReader);
                //    }
                //}
                //if (dataSetReadersFiltered.Count == 0)
                //{
                //    return subscribedDataSets;
                //}
                //dataSetReaders = dataSetReadersFiltered;

                //// 3. decode payload header
                DecodePayloadHeader(jsonDecoder);
                //// 4.
                //DecodeExtendedNetworkMessageHeader(jsonDecoder);
                //// 5.
                //DecodeSecurityHeader(jsonDecoder);

                ////6.1
                DecodePayloadSize(jsonDecoder);

                /* 6.2.8.3 DataSetWriterId
                The parameter DataSetWriterId with DataType UInt16 defines the DataSet selected in the Publisher for the DataSetReader.
                If the value is 0 (null), the parameter shall be ignored and all received DataSetMessages pass the DataSetWriterId filter.*/
                foreach (DataSetReaderDataType dataSetReader in dataSetReaders)
                {
                    List<UaDataSetMessage> jsonDataSetMessages = new List<UaDataSetMessage>(DataSetMessages);
                    //if there is no information regarding dataSet in network message, add dummy datasetMessage to try decoding
                    if (jsonDataSetMessages.Count == 0)
                    {
                        jsonDataSetMessages.Add(new JsonDataSetMessage());
                    }
                    // 6.2 Decode payload into DataSets 
                    // Restore the encoded fields (into dataset for now) for each possible dataset reader
                    foreach (JsonDataSetMessage jsonDataSetMessage in jsonDataSetMessages)
                    {
                        if (dataSetReader.DataSetWriterId == 0 || jsonDataSetMessage.DataSetWriterId == dataSetReader.DataSetWriterId)
                        {
                            //decode dataset message using the reader
                            DataSet dataSet = jsonDataSetMessage.DecodePossibleDataSetReader(jsonDecoder, dataSetReader);
                            if (dataSet != null)
                            {
                                ReceivedDataSets.Add(dataSet);
                            }
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
            // temporary save the mask
            jsonEncoder.WriteByte("NetworkMessageContentMask", (byte)JSONFlags);

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                jsonEncoder.WriteString("MessageId", MessageId);
                jsonEncoder.WriteString("MessageType", MessageType);

                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    jsonEncoder.WriteString("PublisherId", PublisherId);
                }

                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    jsonEncoder.WriteString("DataSetClassId", DataSetClassId);
                }
            }
            else
            {
                // If the NetworkMessageHeader bit of the NetworkMessageContentMask is not set,
                // the NetworkMessage is the contents of the Messages field (e.g. a JSON array of DataSetMessages).

                //jsonEncoder.WriteString("MessageId", null);
                //jsonEncoder.WriteString("MessageType", null);
                //jsonEncoder.WriteString("PublisherId", null);
                //jsonEncoder.WriteString("DataSetClassId", null);
            }
        }

        /// <summary>
        /// Encode Payload Header
        /// </summary>
        /// <param name="encoder"></param>
        //private void EncodePayloadHeader(JsonEncoder encoder)
        //{
        //    if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PayloadHeader) != 0)
        //    {
        //    //    encoder.WriteByte("Count", (byte) m_uadpDataSetMessages.Count);

        //    //    // Collect DataSetSetMessages headers
        //    //    for (int index = 0; index < m_uadpDataSetMessages.Count; index++)
        //    //    {
        //    //        UadpDataSetMessage uadpDataSetMessage = m_uadpDataSetMessages[index];
        //    //        if (uadpDataSetMessage.DataSet != null)
        //    //        {
        //    //            encoder.WriteUInt16("DataSetWriterId", uadpDataSetMessage.DataSetWriterId);
        //    //        }
        //    //    }
        //    }
        //}

        /// <summary>
        /// Encode payload
        /// </summary>
        /// <param name="encoder"></param>
        private void EncodePayload(JsonEncoder encoder)
        {
            //int payloadStartPositionInStream = encoder.Position;
            //if (m_uadpDataSetMessages.Count > 1
            //    && (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            //{                
            //    //skip 2 * dataset count for each dataset payload size 
            //    encoder.Position = encoder.Position + 2 * m_uadpDataSetMessages.Count;               
            //}
            //encode dataset message payload
            encoder.PushStructure("DataSet Payload");
            if ((NetworkMessageContentMask &
                    (JsonNetworkMessageContentMask.NetworkMessageHeader &
                    JsonNetworkMessageContentMask.SingleDataSetMessage)) != 0)
            {
                if (DataSetMessages.Count > 0)
                {
                    encoder.PushStructure("DataSetMessage");
                    JsonDataSetMessage jsonDataSetMessage = DataSetMessages[0] as JsonDataSetMessage;
                    encoder.PopStructure();
                }
                encoder.PopStructure();
            }
            else
            {
                foreach (JsonDataSetMessage jsonDataSetMessage in DataSetMessages)
                {
                    encoder.PushStructure("DataSetMessage");
                    jsonDataSetMessage.Encode(encoder);
                    encoder.PopStructure();
                }
            }
            encoder.PopStructure();

            //if (m_uadpDataSetMessages.Count > 1
            //    && (NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            //{
            //    int payloadEndPositionInStream = encoder.Position;
            //    encoder.Position = payloadStartPositionInStream;
            //    foreach (UadpDataSetMessage uadpDataSetMessage in m_uadpDataSetMessages)
            //    {                   
            //        encoder.WriteUInt16("Size", uadpDataSetMessage.PayloadSizeInStream);
            //    }
            //    encoder.Position = payloadEndPositionInStream;
            //}
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
            // temporary restore mask
            byte networkMessageContentMask = jsonDecoder.ReadByte("NetworkMessageContentMask");
            JSONFlags = (JSONFlagsEncodingMask)(networkMessageContentMask & 0x3F);

            if ((JSONFlags & JSONFlagsEncodingMask.NetworkMessageHeader) != 0)
            {
                jsonDecoder.ReadString("MessageId");
                jsonDecoder.ReadString("MessageType");

                // Decode PublisherId
                if ((JSONFlags & JSONFlagsEncodingMask.PublishedId) != 0)
                {
                    PublisherId = jsonDecoder.ReadString("PublisherId");
                }

                // Decode DataSetClassId
                if ((JSONFlags & JSONFlagsEncodingMask.DataSetClassId) != 0)
                {
                    DataSetClassId = jsonDecoder.ReadString("DataSetClassId");
                }
            }
        }

        /// <summary>
        /// Decode Payload Header
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodePayloadHeader(JsonDecoder decoder)
        {
            // Decode PayloadHeader
            if ((JSONFlags & JSONFlagsEncodingMask.DataSetMessageHeader) != 0)
            {
                byte count = decoder.ReadByte("Count");
                for (int idx = 0; idx < count; idx++)
                {
                    m_uaDataSetMessages.Add(new JsonDataSetMessage());
                }

                // collect DataSetSetMessages headers
                foreach (JsonDataSetMessage uadpDataSetMessage in m_uaDataSetMessages)
                {
                    uadpDataSetMessage.DataSetWriterId = decoder.ReadUInt16("DataSetWriterId");
                }
            }
        }

        /// <summary>
        /// Decode  payload size and prepare for decoding payload
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodePayloadSize(JsonDecoder decoder)
        {
            if (m_uaDataSetMessages.Count > 1)
            {
                // Decode PayloadHeader Size
                if ((JSONFlags & JSONFlagsEncodingMask.DataSetMessageHeader) != 0)
                {
                    foreach (UadpDataSetMessage uadpDataSetMessage in m_uaDataSetMessages)
                    {
                        // Save the size
                        uadpDataSetMessage.PayloadSizeInStream = decoder.ReadUInt16("Size");
                    }
                }
            }
            JsonDecoder jsonDecoder = decoder as JsonDecoder;
            if (jsonDecoder != null)
            {
                int offset = 0;
                // set start position of dataset message in binary stream 
                foreach (JsonDataSetMessage jsonDataSetMessage in m_uaDataSetMessages)
                {
                    //jsonDataSetMessage.StartPositionInStream = jsonDecoder.Position + offset;
                    offset += jsonDataSetMessage.PayloadSizeInStream;
                }
            }
        }

        #endregion
    }
}
