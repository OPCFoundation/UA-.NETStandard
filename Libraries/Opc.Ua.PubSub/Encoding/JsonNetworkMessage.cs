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

        // The UADPVersion for this specification version is 1.
        private const byte JsonVersion = 1;
        private const byte PublishedIdTypeUsedBits = 0x07;
        //private const byte JSONVersionBitMask = 0x0F;
        private const byte PublishedIdResetMask = 0xFC;
        private const byte JSONMessageTypeMask = 0x3F;

        //private byte m_uadpVersion;
        private object m_publisherId;
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
        /// NetworkMessageContentMask contains the mask that will be used to check NetworkMessage options selected for usage  
        /// </summary>
        public JsonNetworkMessageContentMask NetworkMessageContentMask { get; private set; }
        
        #region NetworkMessage Header
        /// <summary>
        /// Get Uadp Flags
        /// </summary>
        public JSONFlagsEncodingMask JSONFlags { get; private set; }

        /// <summary>
        /// Get ExtendedFlags1
        /// </summary>
        public ExtendedFlags1EncodingMask ExtendedFlags1 { get; private set; }

        /// <summary>
        /// Get ExtendedFlags2
        /// </summary>
        public ExtendedFlags2EncodingMask ExtendedFlags2 { get; private set; }

        /// <summary>
        /// Get and Set DataSetClassId
        /// </summary>
        public string DataSetClassId { get; set; }

        /// <summary>
        /// Get and Set SingleDataSetMessage
        /// </summary>
        public string SingleDataSetMessage { get; set; }
        
        #endregion

        #region Group Header

        /// <summary>
        /// Get and Set GroupFlags
        /// </summary>
        public GroupFlagsEncodingMask GroupFlags { get; private set; }

        /// <summary>
        /// Get and Set VersionTime type: it represents the time in seconds since the year 2000
        /// </summary>
        public UInt32 GroupVersion { get; set; }

        /// <summary>
        /// Get and Set NetworkMessageNumber
        /// </summary>
        public UInt16 NetworkMessageNumber { get; set; }

        #endregion

        #region NetworkMessage Header Extended (ExtendedNetwork Header)

        /// <summary>
        /// Get and Set Timestamp
        /// </summary>
        //public DateTime Timestamp { get; set; }

        /// <summary>
        /// PicoSeconds
        /// </summary>
        //public UInt16 PicoSeconds { get; set; }

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

            jsonEncoder.WriteString("MessageId", MessageId);
            jsonEncoder.WriteString("MessageType", MessageType);

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                EncodeNetworkMessageHeader(jsonEncoder);
            }

            //EncodeGroupMessageHeader(jsonEncoder);
            //EncodePayloadHeader(jsonEncoder);
            //EncodeExtendedNetworkMessageHeader(jsonEncoder);
            
            EncodePayload(jsonEncoder);
        }


        /// <summary>
        /// Set All flags before encode/decode
        /// </summary>
        private void SetFlags()
        {
            JSONFlags = 0;
            //ExtendedFlags1 &= (ExtendedFlags1EncodingMask)PublishedIdTypeUsedBits;
            //ExtendedFlags2 = 0;
            GroupFlags = 0;

            // bit 0    1-included in network message header
            //2-0 - SingleDataSetMessage not included
            //4-0 - DataSetClassId not included
            #region Network Message Header
            // 1 included in network message
            // 2-0 - json fla =0
            // 4-0 dataset class id 0 nu e in network message

            //if ((NetworkMessageContentMask & (UadpNetworkMessageContentMask.PublisherId |
            //                                  UadpNetworkMessageContentMask.DataSetClassId)) != 0)
            //{
            //    //  UADPFlags: The ExtendedFlags1 shall be omitted if bit 7 of the UADPFlags is false.
            //    // Enable ExtendedFlags1 usage
            //    UADPFlags |= UADPFlagsEncodingMask.ExtendedFlags1;
            //}
            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                // Enable ExtendedFlags1 usage
                JSONFlags |= JSONFlagsEncodingMask.NetworkMessageHeader;

                #region SingleDataSetMessage 
                if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                {
                    JSONFlags |= JSONFlagsEncodingMask.SingleDataSetMessage;
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

            #region PublisherId in network message
            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
            {
                JSONFlags |= JSONFlagsEncodingMask.PublishedId;
            }
            #endregion

            #region DataSetClassId in network message
            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.ReplyTo) != 0)
            {
                JSONFlags |= JSONFlagsEncodingMask.ReplyTo;
            }
            #endregion

            //#region Group Message Header

            //if ((NetworkMessageContentMask & (UadpNetworkMessageContentMask.GroupHeader |
            //                                  UadpNetworkMessageContentMask.WriterGroupId |
            //                                  UadpNetworkMessageContentMask.GroupVersion |
            //                                  UadpNetworkMessageContentMask.NetworkMessageNumber |
            //                                  UadpNetworkMessageContentMask.SequenceNumber)) !=
            //    UadpNetworkMessageContentMask.None)
            //{
            //    // UADPFlags: Bit 5: GroupHeader enabled
            //    UADPFlags |= UADPFlagsEncodingMask.GroupHeader;
            //}

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            //{
            //    // GroupFlags: Bit 0: WriterGroupId enabled
            //    GroupFlags |= GroupFlagsEncodingMask.WriterGroupId;
            //}

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            //{
            //    // GroupFlags: Bit 1: GroupVersion enabled
            //    GroupFlags |= GroupFlagsEncodingMask.GroupVersion;
            //}

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            //{
            //    // GroupFlags: Bit 2: NetworkMessageNumber enabled
            //    GroupFlags |= GroupFlagsEncodingMask.NetworkMessageNumber;
            //}

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            //{
            //    // GroupFlags: Bit 3: SequenceNumber enabled
            //    GroupFlags |= GroupFlagsEncodingMask.SequenceNumber;
            //}

            //#endregion

            //#region Extended network message header

            //if ((NetworkMessageContentMask & (UadpNetworkMessageContentMask.Timestamp |
            //                                  UadpNetworkMessageContentMask.PicoSeconds |
            //                                  UadpNetworkMessageContentMask.PromotedFields)) != 0)
            //{
            //    // Enable ExtendedFlags1 usage
            //    UADPFlags |= UADPFlagsEncodingMask.ExtendedFlags1;
            //}

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
            //{
            //    // ExtendedFlags1: Bit 5: Timestamp enabled
            //    ExtendedFlags1 |= ExtendedFlags1EncodingMask.Timestamp;
            //}

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
            //{
            //    // ExtendedFlags1: Bit 6: PicoSeconds enabled
            //    ExtendedFlags1 |= ExtendedFlags1EncodingMask.PicoSeconds;
            //}

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PromotedFields) != 0)
            //{
            //    // todo:
            //    // ExtendedFlags1: Bit 7: ExtendedFlags2 enabled
            //    ExtendedFlags1 |= ExtendedFlags1EncodingMask.ExtendedFlags2;

            //    // The PromotedFields shall be omitted if bit 4 of the ExtendedFlags2 is false.
            //    // ExtendedFlags2: Bit 1: PromotedFields enabled
            //    // Wireshark: PromotedFields; omitted if bit 1 of ExtendedFlags2 is false
            //    ExtendedFlags2 |= ExtendedFlags2EncodingMask.PromotedFields;

            //    // Bit range 2-4: UADP NetworkMessage type
            //    // 000 NetworkMessage with DataSetMessage payload for now
            //}

            //#endregion

            //#region PayLoad Header

            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PayloadHeader) != 0)
            //{
            //    // UADPFlag: Bit 6: PayloadHeader enabled
            //    UADPFlags |= UADPFlagsEncodingMask.PayloadHeader;
            //}

            //#endregion

            //#region Security footer (not implemented yet)

            //// ExtendedFlags1: Bit 4: Security enabled
            //// Disable security for now
            //ExtendedFlags1 &= ~(ExtendedFlags1EncodingMask.Security);

            //// The security footer size shall be omitted if bit 2 of the SecurityFlags is false.
            //SecurityFlags &= ~(SecurityFlagsEncodingMask.SecurityFooter);

            //#endregion
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
                    if (PublisherId.Equals(dataSetReader.PublisherId.Value))
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
        /// <param name="encoder"></param>
        private void EncodeNetworkMessageHeader(JsonEncoder encoder)
        {
            //// byte[0..3] UADPVersion value 1 (for now)
            //// byte[4..7] UADPFlags
            //encoder.WriteByte("VersionFlags", (byte) (UADPVersion | (byte)UADPFlags));

            //if((UADPFlags & UADPFlagsEncodingMask.ExtendedFlags1) !=0)
            //{
            //    encoder.WriteByte("ExtendedFlags1", (byte)ExtendedFlags1);
            //}

            //if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.ExtendedFlags2) != 0)
            //{
            //    encoder.WriteByte("ExtendedFlags2", (byte)ExtendedFlags2);
            //}

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
            {
                PublisherIdTypeEncodingMask publisherIdType = (PublisherIdTypeEncodingMask)((byte)ExtendedFlags1 & PublishedIdTypeUsedBits);
                switch (publisherIdType)
                {
                    case PublisherIdTypeEncodingMask.Byte:
                        encoder.WriteByte("PublisherId", Convert.ToByte(PublisherId));
                        break;
                    case PublisherIdTypeEncodingMask.UInt16:
                        encoder.WriteUInt16("PublisherId", Convert.ToUInt16(PublisherId));
                        break;
                    case PublisherIdTypeEncodingMask.UInt32:
                        encoder.WriteUInt32("PublisherId", Convert.ToUInt32(PublisherId));
                        break;
                    case PublisherIdTypeEncodingMask.UInt64:
                        encoder.WriteUInt64("PublisherId", Convert.ToUInt64(PublisherId));
                        break;
                    case PublisherIdTypeEncodingMask.String:
                        encoder.WriteString("PublisherId", Convert.ToString(PublisherId));
                        break;
                    default:
                        // Reserved - no type provided
                        break;
                }
            }

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
            {
                encoder.WriteString("SingleDataSetMessage", SingleDataSetMessage);
            }

            if ((NetworkMessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
            {
                encoder.WriteString("DataSetClassId", DataSetClassId);
            }
        }

        /// <summary>
        /// Encode Group Message Header
        /// </summary>
        /// <param name="encoder"></param>
        //private void EncodeGroupMessageHeader(BinaryEncoder encoder)
        //{
            //if ((NetworkMessageContentMask & (JsonNetworkMessageContentMask.GroupHeader |
            //                                  JsonNetworkMessageContentMask.WriterGroupId |
            //                                  JsonNetworkMessageContentMask.GroupVersion |
            //                                  JsonNetworkMessageContentMask.NetworkMessageNumber |
            //                                  JsonNetworkMessageContentMask.SequenceNumber)) != JsonNetworkMessageContentMask.None)
            //{
            //    encoder.WriteByte("GroupFlags", (byte)GroupFlags);
            //}
            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.WriterGroupId) != 0)
            //{
            //    encoder.WriteUInt16("WriterGroupId", WriterGroupId);
            //}
            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.GroupVersion) != 0)
            //{
            //    encoder.WriteUInt32("GroupVersion", GroupVersion);
            //}
            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.NetworkMessageNumber) != 0)
            //{
            //    encoder.WriteUInt16("NetworkMessageNumber", NetworkMessageNumber);
            //}
            //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.SequenceNumber) != 0)
            //{
            //    encoder.WriteUInt16("SequenceNumber", SequenceNumber);
            //}
        //}

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
        ///  Encode Extended network message header
        /// </summary>
        /// <param name="encoder"></param>
        //private void EncodeExtendedNetworkMessageHeader(BinaryEncoder encoder)
        //{
        //    //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.Timestamp) != 0)
        //    //{
        //    //    encoder.WriteDateTime("Timestamp", Timestamp);
        //    //}

        //    //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PicoSeconds) != 0)
        //    //{
        //    //    encoder.WriteUInt16("PicoSeconds", PicoSeconds);
        //    //}

        //    //if ((NetworkMessageContentMask & UadpNetworkMessageContentMask.PromotedFields) != 0)
        //    //{
        //    //    EncodePromotedFields(encoder);
        //    //}
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

        

        #endregion

        #region Private Methods - Decoding 

        /// <summary>
        /// Encode Network Message Header
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodeNetworkMessageHeader(JsonDecoder decoder)
        {
            // byte[0..3] UADPVersion value 1 (for now)
            // byte[4..7] UADPFlags
            //byte versionFlags = decoder.ReadByte("VersionFlags");
            //UADPVersion = (byte)(versionFlags & UADPVersionBitMask);
            //// Decode UADPFlags
            //JSONFlags = (JSONFlagsEncodingMask)(versionFlags & 0xF0);

            //// Decode the ExtendedFlags1
            //if ((UADPFlags & UADPFlagsEncodingMask.ExtendedFlags1) != 0)
            //{
            //    ExtendedFlags1 = (ExtendedFlags1EncodingMask)decoder.ReadByte("ExtendedFlags1");
            //}

            //// Decode the ExtendedFlags2
            //if ((ExtendedFlags1 & ExtendedFlags1EncodingMask.ExtendedFlags2) != 0)
            //{
            //    ExtendedFlags2 = (ExtendedFlags2EncodingMask)decoder.ReadByte("ExtendedFlags2");
            //}
            //// calculate UADPNetworkMessageType
            //if ((ExtendedFlags2 & ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryRequest) != 0)
            //{
            //    m_uadpNetworkMessageType = UADPNetworkMessageType.DiscoveryRequest;
            //}
            //else if ((ExtendedFlags2 & ExtendedFlags2EncodingMask.NetworkMessageWithDiscoveryResponse) != 0)
            //{
            //    m_uadpNetworkMessageType = UADPNetworkMessageType.DiscoveryResponse;
            //}
            //else
            //{
            //    m_uadpNetworkMessageType = UADPNetworkMessageType.DataSetMessage;
            //}

            // Decode PublisherId
            if ((JSONFlags & JSONFlagsEncodingMask.PublishedId) != 0)
            {
                PublisherIdTypeEncodingMask publishedIdTypeType = (PublisherIdTypeEncodingMask)((byte)ExtendedFlags1 & PublishedIdTypeUsedBits);

                switch (publishedIdTypeType)
                {
                    case PublisherIdTypeEncodingMask.UInt16:
                        m_publisherId = decoder.ReadUInt16("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.UInt32:
                        m_publisherId = decoder.ReadUInt32("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.UInt64:
                        m_publisherId = decoder.ReadUInt64("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.String:
                        m_publisherId = decoder.ReadString("PublisherId");
                        break;
                    case PublisherIdTypeEncodingMask.Byte:
                    default:
                        // 000 The PublisherId is of DataType Byte
                        // This is the default value if ExtendedFlags1 is omitted
                        m_publisherId = decoder.ReadByte("PublisherId");
                        break;
                }
            }

            // Decode DataSetClassId
            if ((JSONFlags & JSONFlagsEncodingMask.DataSetClassId) != 0)
            {
                DataSetClassId = decoder.ReadString("DataSetClassId");
            }
        }

        /// <summary>
        /// Decode Group Message Header
        /// </summary>
        /// <param name="decoder"></param>
        private void DecodeGroupMessageHeader(BinaryDecoder decoder)
        {
            // Decode GroupHeader (that holds GroupFlags)
            //if ((JSONFlags & JSONFlagsEncodingMask.GroupHeader) != 0)
            //{
            //    GroupFlags = (GroupFlagsEncodingMask)decoder.ReadByte("GroupFlags");
            //}

            // Decode WriterGroupId
            //if ((GroupFlags & GroupFlagsEncodingMask.WriterGroupId) != 0)
            //{
            //    WriterGroupId = decoder.ReadUInt16("WriterGroupId");
            //}

            //// Decode GroupVersion
            //if ((GroupFlags & GroupFlagsEncodingMask.GroupVersion) != 0)
            //{
            //    GroupVersion = decoder.ReadUInt32("GroupVersion");
            //}

            //// Decode NetworkMessageNumber
            //if ((GroupFlags & GroupFlagsEncodingMask.NetworkMessageNumber) != 0)
            //{
            //    NetworkMessageNumber = decoder.ReadUInt16("NetworkMessageNumber");
            //}

            //// Decode SequenceNumber
            //if ((GroupFlags & GroupFlagsEncodingMask.SequenceNumber) != 0)
            //{
            //    SequenceNumber = decoder.ReadUInt16("SequenceNumber");
            //}
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
