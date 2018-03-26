using System;
using System.Collections.Generic;
using System.IO;
using Opc.Ua;

namespace MqttSamplePublisher
{
    public class JsonDataSetMessage : IEncodeable
    {
        public JsonDataSetMessage()
        {
            MessageContentMask =
                JsonDataSetMessageContentMask.DataSetWriterId |
                JsonDataSetMessageContentMask.MetaDataVersion |
                JsonDataSetMessageContentMask.SequenceNumber;

            FieldContentMask = (DataSetFieldContentMask)0;
        }

        public JsonDataSetMessageContentMask MessageContentMask { get; set; }

        public string DataSetWriterId { get; set; }

        public uint SequenceNumber { get; set; }

        public ConfigurationVersionDataType MetaDataVersion { get; set; }

        public DateTime Timestamp { get; set; }

        public StatusCode Status { get; set; }

        public DataSetFieldContentMask FieldContentMask { get; set; }

        public Dictionary<string,DataValue> Payload { get; set; }

        public ExpandedNodeId TypeId { get { return ExpandedNodeId.Null; } }

        public ExpandedNodeId BinaryEncodingId { get { return ExpandedNodeId.Null; } }

        public ExpandedNodeId XmlEncodingId { get { return ExpandedNodeId.Null; } }
        
        private void EncodeField(JsonEncoder encoder, string fieldName, DataValue value)
        {
            if (FieldContentMask == 0)
            {
                encoder.WriteVariant(fieldName, value.WrappedValue);
                return;
            }

            if ((FieldContentMask & DataSetFieldContentMask.RawDataEncoding) != 0)
            {
                var variant = value.WrappedValue;

                if (variant.TypeInfo == null || variant.TypeInfo.BuiltInType == BuiltInType.Null)
                {
                    return;
                }

                if (variant.TypeInfo.ValueRank == ValueRanks.Scalar)
                {
                    switch (variant.TypeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean: { encoder.WriteBoolean(fieldName, (bool)variant.Value); break; }
                        case BuiltInType.SByte: { encoder.WriteSByte(fieldName, (sbyte)variant.Value); break; }
                        case BuiltInType.Byte: { encoder.WriteByte(fieldName, (byte)variant.Value); break; }
                        case BuiltInType.Int16: { encoder.WriteInt16(fieldName, (short)variant.Value); break; }
                        case BuiltInType.UInt16: { encoder.WriteUInt16(fieldName, (ushort)variant.Value); break; }
                        case BuiltInType.Int32: { encoder.WriteInt32(fieldName, (int)variant.Value); break; }
                        case BuiltInType.UInt32: { encoder.WriteUInt32(fieldName, (uint)variant.Value); break; }
                        case BuiltInType.Int64: { encoder.WriteInt64(fieldName, (long)variant.Value); break; }
                        case BuiltInType.UInt64: { encoder.WriteUInt64(fieldName, (ulong)variant.Value); break; }
                        case BuiltInType.Float: { encoder.WriteFloat(fieldName, (float)variant.Value); break; }
                        case BuiltInType.Double: { encoder.WriteDouble(fieldName, (double)variant.Value); break; }
                        case BuiltInType.DateTime: { encoder.WriteDateTime(fieldName, (DateTime)variant.Value); break; }
                        case BuiltInType.Guid: { encoder.WriteGuid(fieldName, (Uuid)variant.Value); break; }
                        case BuiltInType.String: { encoder.WriteString(fieldName, (string)variant.Value); break; }
                        case BuiltInType.ByteString: { encoder.WriteByteString(fieldName, (byte[])variant.Value); break; }
                        case BuiltInType.QualifiedName: { encoder.WriteQualifiedName(fieldName, (QualifiedName)variant.Value); break; }
                        case BuiltInType.LocalizedText: { encoder.WriteLocalizedText(fieldName, (LocalizedText)variant.Value); break; }
                        case BuiltInType.NodeId: { encoder.WriteNodeId(fieldName, (NodeId)variant.Value); break; }
                        case BuiltInType.ExpandedNodeId: { encoder.WriteExpandedNodeId(fieldName, (ExpandedNodeId)variant.Value); break; }
                        case BuiltInType.StatusCode: { encoder.WriteStatusCode(fieldName, (StatusCode)variant.Value); break; }
                        case BuiltInType.XmlElement: { encoder.WriteXmlElement(fieldName, (System.Xml.XmlElement)variant.Value); break; }
                        case BuiltInType.ExtensionObject: { encoder.WriteExtensionObject(fieldName, (ExtensionObject)variant.Value); break; }
                    }
                }
                else
                {
                    switch (variant.TypeInfo.BuiltInType)
                    {
                        case BuiltInType.Boolean: { encoder.WriteBooleanArray(fieldName, (bool[])variant.Value); break; }
                        case BuiltInType.SByte: { encoder.WriteSByteArray(fieldName, (sbyte[])variant.Value); break; }
                        case BuiltInType.Byte: { encoder.WriteByteArray(fieldName, (byte[])variant.Value); break; }
                        case BuiltInType.Int16: { encoder.WriteInt16Array(fieldName, (short[])variant.Value); break; }
                        case BuiltInType.UInt16: { encoder.WriteUInt16Array(fieldName, (ushort[])variant.Value); break; }
                        case BuiltInType.Int32: { encoder.WriteInt32Array(fieldName, (int[])variant.Value); break; }
                        case BuiltInType.UInt32: { encoder.WriteUInt32Array(fieldName, (uint[])variant.Value); break; }
                        case BuiltInType.Int64: { encoder.WriteInt64Array(fieldName, (long[])variant.Value); break; }
                        case BuiltInType.UInt64: { encoder.WriteUInt64Array(fieldName, (ulong[])variant.Value); break; }
                        case BuiltInType.Float: { encoder.WriteFloatArray(fieldName, (float[])variant.Value); break; }
                        case BuiltInType.Double: { encoder.WriteDoubleArray(fieldName, (double[])variant.Value); break; }
                        case BuiltInType.DateTime: { encoder.WriteDateTimeArray(fieldName, (DateTime[])variant.Value); break; }
                        case BuiltInType.Guid: { encoder.WriteGuidArray(fieldName, (Uuid[])variant.Value); break; }
                        case BuiltInType.String: { encoder.WriteStringArray(fieldName, (string[])variant.Value); break; }
                        case BuiltInType.ByteString: { encoder.WriteByteStringArray(fieldName, (byte[][])variant.Value); break; }
                        case BuiltInType.QualifiedName: { encoder.WriteQualifiedNameArray(fieldName, (QualifiedName[])variant.Value); break; }
                        case BuiltInType.LocalizedText: { encoder.WriteLocalizedTextArray(fieldName, (LocalizedText[])variant.Value); break; }
                        case BuiltInType.NodeId: { encoder.WriteNodeIdArray(fieldName, (NodeId[])variant.Value); break; }
                        case BuiltInType.ExpandedNodeId: { encoder.WriteExpandedNodeIdArray(fieldName, (ExpandedNodeId[])variant.Value); break; }
                        case BuiltInType.StatusCode: { encoder.WriteStatusCodeArray(fieldName, (StatusCode[])variant.Value); break; }
                        case BuiltInType.XmlElement: { encoder.WriteXmlElementArray(fieldName, (System.Xml.XmlElement[])variant.Value); break; }
                        case BuiltInType.ExtensionObject: { encoder.WriteExtensionObjectArray(fieldName, (ExtensionObject[])variant.Value); break; }
                        case BuiltInType.Variant: { encoder.WriteVariantArray(fieldName, (Variant[])variant.Value); break; }
                    }
                }

                return;
            }

            DataValue dv = new DataValue();

            dv.WrappedValue = value.WrappedValue;

            if ((FieldContentMask & DataSetFieldContentMask.StatusCode) != 0)
            {
                dv.StatusCode = value.StatusCode;
            }

            if ((FieldContentMask & DataSetFieldContentMask.SourceTimestamp) != 0)
            {
                dv.SourceTimestamp = value.SourceTimestamp;
            }

            if ((FieldContentMask & DataSetFieldContentMask.SourcePicoSeconds) != 0)
            {
                dv.SourcePicoseconds = value.SourcePicoseconds;
            }

            if ((FieldContentMask & DataSetFieldContentMask.ServerTimestamp) != 0)
            {
                dv.ServerTimestamp = value.ServerTimestamp;
            }

            if ((FieldContentMask & DataSetFieldContentMask.ServerPicoSeconds) != 0)
            {
                dv.ServerPicoseconds = value.ServerPicoseconds;
            }

            encoder.WriteDataValue(fieldName, dv, true);
        }

        public void Encode(JsonEncoder encoder, JsonNetworkMessageContentMask messageContentMask)
        {
            if ((messageContentMask & JsonNetworkMessageContentMask.DataSetMessageHeader) != 0)
            {
                Encode(encoder);
                return;
            }

            if (Payload != null)
            {
                foreach (var ii in Payload)
                {
                    EncodeField(encoder, ii.Key, ii.Value);
                }
            }
        }

        void IEncodeable.Encode(IEncoder encoder)
        {
            Encode((JsonEncoder)encoder);
        }

        public void Encode(JsonEncoder encoder)
        {
            if ((MessageContentMask & JsonDataSetMessageContentMask.DataSetWriterId) != 0)
            {
                encoder.WriteString("DataSetWriterId", DataSetWriterId);
            }
            else
            {
                encoder.WriteString("DataSetWriterId", null);
            }

            if ((MessageContentMask & JsonDataSetMessageContentMask.SequenceNumber) != 0)
            {
                encoder.WriteUInt32("SequenceNumber", SequenceNumber);
            }
            else
            {
                encoder.WriteUInt32("SequenceNumber", 0);
            }

            if ((MessageContentMask & JsonDataSetMessageContentMask.MetaDataVersion) != 0)
            {
                encoder.WriteEncodeable("MetaDataVersion", MetaDataVersion, typeof(ConfigurationVersionDataType));
            }
            else
            {
                encoder.WriteEncodeable("MetaDataVersion", null, typeof(ConfigurationVersionDataType));
            }

            if ((MessageContentMask & JsonDataSetMessageContentMask.Timestamp) != 0)
            {
                encoder.WriteDateTime("Timestamp", Timestamp);
            }
            else
            {
                encoder.WriteDateTime("Timestamp", DateTime.MinValue);
            }

            if ((MessageContentMask & JsonDataSetMessageContentMask.Status) != 0)
            {
                encoder.WriteStatusCode("Status", Status);
            }
            else
            {
                encoder.WriteStatusCode("Status", StatusCodes.Good);
            }

            if (Payload != null)
            {
                ((JsonEncoder)encoder).PushStructure("Payload");

                foreach (var ii in Payload)
                {
                    EncodeField(encoder, ii.Key, ii.Value);
                }

                ((JsonEncoder)encoder).PopStructure();
            }
        }

        public void Decode(IDecoder decoder)
        {
            throw new NotImplementedException();
        }

        public bool IsEqual(IEncodeable encodeable)
        {
            if (Object.ReferenceEquals(this, encodeable))
            {
                return true;
            }

            return false;
        }
    }

    public class JsonNetworkMessage
    {
        public JsonNetworkMessage()
        {
            MessageContentMask = 
                JsonNetworkMessageContentMask.NetworkMessageHeader | 
                JsonNetworkMessageContentMask.DataSetMessageHeader | 
                JsonNetworkMessageContentMask.PublisherId |
                JsonNetworkMessageContentMask.DataSetClassId;
        }

        public JsonNetworkMessageContentMask MessageContentMask { get; set; }

        public string MessageId { get; set; }

        public string MessageType { get; set; }

        public string PublisherId { get; set; }

        public string DataSetClassId { get; set; }

        public List<JsonDataSetMessage> Messages { get; set; }

        public void Encode(ServiceMessageContext context, bool useReversibleEncoding, StreamWriter writer)
        {
            bool topLevelIsArray = false;

            if ((MessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) == 0 && (MessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) == 0)
            {
                topLevelIsArray = true;
            }

            using (JsonEncoder encoder = new JsonEncoder(context, writer, useReversibleEncoding, topLevelIsArray))
            {
                if ((MessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
                {
                    Encode(encoder);
                }
                else if (Messages != null && Messages.Count > 0)
                {
                    if ((MessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                    {
                        encoder.PushStructure(null);
                        Messages[0].Encode(encoder, MessageContentMask);
                        encoder.PopStructure();
                    }
                    else
                    {
                        foreach (var message in Messages)
                        {
                            message.Encode(encoder, MessageContentMask);
                        }
                    }
                }

                encoder.Close();
            }
        }

        public void Encode(JsonEncoder encoder)
        {
            encoder.PushStructure(null);

            if ((MessageContentMask & JsonNetworkMessageContentMask.NetworkMessageHeader) != 0)
            {
                encoder.WriteString("MessageId", MessageId);
                encoder.WriteString("MessageType", "ua-data");

                if ((MessageContentMask & JsonNetworkMessageContentMask.PublisherId) != 0)
                {
                    encoder.WriteString("PublisherId", PublisherId);
                }
                else
                {
                    encoder.WriteString("PublisherId", null);
                }

                if ((MessageContentMask & JsonNetworkMessageContentMask.DataSetClassId) != 0)
                {
                    encoder.WriteString("DataSetClassId", DataSetClassId);
                }
                else
                {
                    encoder.WriteString("DataSetClassId", null);
                }

                if (Messages != null && Messages.Count > 0)
                {
                    if ((MessageContentMask & JsonNetworkMessageContentMask.SingleDataSetMessage) != 0)
                    {
                        encoder.WriteEncodeable("Messages", Messages[0], typeof(JsonDataSetMessage));
                    }
                    else
                    {
                        encoder.WriteEncodeableArray("Messages", Messages.ToArray(), typeof(JsonDataSetMessage));
                    }
                }
            }

            encoder.PopStructure();
        }
    }

    public class JsonDataSetMetaData
    {
        public string MessageId { get; set; }

        public string MessageType { get; set; }

        public string PublisherId { get; set; }

        public string DataSetClassId { get; set; }

        public DataSetMetaDataType MetaData { get; set; }

        public void Encode(ServiceMessageContext context, bool useReversibleEncoding, StreamWriter writer)
        {
            using (JsonEncoder encoder = new JsonEncoder(context, writer, useReversibleEncoding, false))
            {
                encoder.WriteString("MessageId", MessageId);
                encoder.WriteString("MessageType", "ua-metadata");
                encoder.WriteString("PublisherId", PublisherId);
                encoder.WriteString("DataSetClassId", DataSetClassId);
                encoder.WriteEncodeable("MetaData", MetaData, typeof(DataSetMetaDataType));

                encoder.Close();
            }
        }

        public static JsonDataSetMetaData Decode(ServiceMessageContext context, StreamReader reader)
        {
            var json = reader.ReadToEnd();

            JsonDataSetMetaData output = new JsonDataSetMetaData();

            using (JsonDecoder decoder = new JsonDecoder(json, context))
            {
                output.MessageId = decoder.ReadString("MessageId");
                output.MessageType = decoder.ReadString("MessageType");
                output.PublisherId = decoder.ReadString("PublisherId");
                output.DataSetClassId = decoder.ReadString("DataSetClassId");
                output.MetaData = (DataSetMetaDataType)decoder.ReadEncodeable("MetaData", typeof(DataSetMetaDataType));

                decoder.Close();
            }

            return output;
        }
    }
}
