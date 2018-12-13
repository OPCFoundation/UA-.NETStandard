using Newtonsoft.Json.Linq;
using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SubscriberDataSource
{
    public interface IUASubscriberDecoder
    {
        UInt64 DataSetWriterId { get; set; }
        DataSetMetaDataType DataSetMetaDataType { get; set; }
        void UpdateTargetVariables(Dictionary<string, Object> dic_NetworkMessage);
        void UpdateFieldTargetDataType(FieldTargetDataType[] fieldTargetDataTypes);
        void RemoveFieldTargetDataType();
    }
   public class UASubscriberJsonDecoder: IUASubscriberDecoder
    {
        public UInt64 DataSetWriterId { get; set; }
        public DataSetMetaDataType DataSetMetaDataType { get; set; }
        DataSetReaderState m_dataSetReaderState = null;
        Opc.Ua.Core.SubscriberDelegate m_subscriberDelegate;
        FieldTargetDataType[] m_fieldTargetDataTypes;
        object _lock = new object();

        #region Public Methods
        public UASubscriberJsonDecoder(DataSetReaderState dataSetReaderState, Opc.Ua.Core.SubscriberDelegate subscriberDelegate)
        {
            m_dataSetReaderState = dataSetReaderState;
            m_subscriberDelegate = subscriberDelegate;
            DataSetWriterId = dataSetReaderState.DataSetWriterId.Value;
            DataSetMetaDataType = dataSetReaderState.DataSetMetaData.Value;

        }
        public void UpdateFieldTargetDataType(FieldTargetDataType[] fieldTargetDataTypes)
        {
            m_fieldTargetDataTypes = fieldTargetDataTypes;
        }
        public void RemoveFieldTargetDataType()
        {
            m_fieldTargetDataTypes = null;
        }

        public void UpdateTargetVariables(Dictionary<string, Object> dic_NetworkMessage)
        {
            //lock (_lock)
            //{
            try
            {
                if (m_dataSetReaderState.Status.State.Value != PubSubState.Operational || (m_dataSetReaderState.Parent as ReaderGroupState).Status.State.Value != PubSubState.Operational)
                {
                    return;
                }
                if (dic_NetworkMessage.ContainsKey("PublisherId"))
                {
                    object publisherId = dic_NetworkMessage["PublisherId"];
                    Type publisherIDType = publisherId.GetType();
                    bool IsvalidPublisher = false;
                    switch (publisherIDType.FullName)
                    {
                        case "System.String":
                            if (m_dataSetReaderState.PublisherId.Value.ToString() == publisherId.ToString())
                            {
                                IsvalidPublisher = true;
                            }
                            break;
                        case "System.Byte":
                            if ((byte)m_dataSetReaderState.PublisherId.Value == (byte)publisherId)
                            {
                                IsvalidPublisher = true;
                            }
                            break;
                        case "System.UInt16":
                            if ((UInt16)m_dataSetReaderState.PublisherId.Value == (UInt16)publisherId)
                            {
                                IsvalidPublisher = true;
                            }
                            break;
                        case "System.UInt32":
                            if ((UInt32)m_dataSetReaderState.PublisherId.Value == (UInt32)publisherId)
                            {
                                IsvalidPublisher = true;
                            }
                            break;
                        case "System.UInt64":
                            if ((UInt64)m_dataSetReaderState.PublisherId.Value == (UInt64)publisherId)
                            {
                                IsvalidPublisher = true;
                            }
                            break;
                        case "System.Guid":
                            if ((Guid)m_dataSetReaderState.PublisherId.Value == (Guid)publisherId)
                            {
                                IsvalidPublisher = true;
                            }
                            break;
                    }
                    if (IsvalidPublisher)
                    {
                        if (dic_NetworkMessage.ContainsKey("Messages"))
                        {


                            var mes = dic_NetworkMessage["Messages"];

                            Type type = mes.GetType();
                            if (type.Name == "JArray")
                            {
                                JArray Jarray = dic_NetworkMessage["Messages"] as JArray;
                                foreach (JObject _JObject in Jarray)
                                {
                                    UpdateTargetVariables(_JObject);
                                }
                            }
                            else
                            {
                                JObject Jobject = dic_NetworkMessage["Messages"] as JObject;
                                UpdateTargetVariables(Jobject);
                            }



                        }
                    }
                }
            }
            catch (Exception ex)
            {

            }
            //}
        }
        public void UpdateTargetVariables(JObject jObject)
        {
            try
            {
                JsonDataSetMessage _JsonDataSetMessage = new JsonDataSetMessage();
                JToken token = null;
                if (jObject.TryGetValue("DataSetWriterId", out token))
                {
                    _JsonDataSetMessage.DataSetWriterId = (string)jObject["DataSetWriterId"];
                }
                if (jObject.TryGetValue("SequenceNumber", out token))
                {
                    _JsonDataSetMessage.SequenceNumber = (uint)jObject["SequenceNumber"];
                }
                if (jObject.TryGetValue("MetaDataVersion", out token))
                {
                    //_JsonDataSetMessage.MetaDataVersion = token["MetaDataVersion"];
                    JToken jtoken = jObject["MetaDataVersion"];
                    string js = jtoken.ToString();
                    _JsonDataSetMessage.MetaDataVersion = Newtonsoft.Json.JsonConvert.DeserializeObject<ConfigurationVersionDataType>(jtoken.ToString());

                }
                if (jObject.TryGetValue("Payload", out token))
                {
                    JToken jtoken = jObject["Payload"];
                    Dictionary<string, object> payloads = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(jtoken.ToString());
                    _JsonDataSetMessage.Payload = new Dictionary<string, DataValue>();
                    if ((m_dataSetReaderState.DataSetFieldContentMask.Value & (uint)DataSetFieldContentMask.RawDataEncoding) != 0)
                    {
                        foreach (string key in payloads.Keys)
                        {
                            DataValue dataValue = new DataValue();
                            dataValue.SourceTimestamp = DateTime.UtcNow;
                            dataValue.Value = payloads[key];
                            _JsonDataSetMessage.Payload[key] = dataValue;
                        }
                    }
                    else
                    {

                        foreach (string key in payloads.Keys)
                        {
                            DataValue dataValue = new DataValue();
                            JToken dvalue = (JToken)payloads[key];
                            JToken fieldValue = dvalue["Value"];
                            string fieldBodyValue = (string)fieldValue["Body"];

                            byte fieldTypeValue = (byte)fieldValue["Type"];
                            object DatafieldValue = ConvertValue(fieldTypeValue, fieldBodyValue);
                            DateTime sourceTime = (DateTime)dvalue["SourceTimestamp"];
                            DateTime ServerTime = (DateTime)dvalue["ServerTimestamp"];
                            dataValue.Value = DatafieldValue;
                            dataValue.SourceTimestamp = sourceTime;
                            dataValue.ServerTimestamp = ServerTime;
                            _JsonDataSetMessage.Payload[key] = dataValue;
                        }
                    }
                }

                if (_JsonDataSetMessage != null)
                {
                    UpdateTargetVariables(_JsonDataSetMessage);
                }
            }
            catch (Exception ex)
            {

            }
        }

        #endregion


        #region Private Method
        private void UpdateTargetVariables(JsonDataSetMessage _jsonDataSetMessage)
        {
            if (_jsonDataSetMessage != null && !string.IsNullOrWhiteSpace(_jsonDataSetMessage.DataSetWriterId))
            {
                if (_jsonDataSetMessage.DataSetWriterId == m_dataSetReaderState.DataSetWriterId.Value.ToString())
                {
                    foreach (string key in _jsonDataSetMessage.Payload.Keys)
                    {
                        DataSetMetaDataType _DataSetMetaDataType = m_dataSetReaderState.DataSetMetaData.Value as DataSetMetaDataType;
                        FieldMetaData metadata = _DataSetMetaDataType.Fields.Where(i => i.Name == key).FirstOrDefault();
                        if (metadata != null)
                        {
                              if (m_fieldTargetDataTypes != null)
                            {
                                  
                                FieldTargetDataType fieldTargetDataType = m_fieldTargetDataTypes.Where(i => i.DataSetFieldId == metadata.DataSetFieldId).FirstOrDefault();
                                DataValue dataValue = _jsonDataSetMessage.Payload[key];
                                m_subscriberDelegate(fieldTargetDataType.TargetNodeId, dataValue);
                            }

                        }


                    }
                }
            }
        }
        #endregion
        object ConvertValue(int type,object body)
        {
            BuiltInType builtInType;
            Enum.TryParse<BuiltInType>(type.ToString(), out builtInType);
            switch (builtInType)
            {
                case BuiltInType.Boolean: return Convert.ToBoolean(body);
                case BuiltInType.SByte: return Convert.ToSByte(body);
                case BuiltInType.Byte: return Convert.ToByte(body);
                case BuiltInType.Int16: return Convert.ToInt16(body);
                case BuiltInType.UInt16: return Convert.ToUInt16(body);
                case BuiltInType.Int32: return Convert.ToInt32(body);
                case BuiltInType.UInt32: return Convert.ToUInt32(body);
                case BuiltInType.Int64: return Convert.ToInt64(body);
                case BuiltInType.UInt64: return Convert.ToUInt64(body);
                case BuiltInType.Float: return Convert.ToSingle(body);
                case BuiltInType.Double: return Convert.ToDouble(body);
                case BuiltInType.DateTime: return Convert.ToDateTime(body);
                case BuiltInType.Guid: return (Guid)body;
                case BuiltInType.String: return Convert.ToString(body);
                case BuiltInType.ByteString: return (byte[])body;
                case BuiltInType.QualifiedName: return (QualifiedName)body;
                case BuiltInType.LocalizedText: return (LocalizedText)body;
                case BuiltInType.NodeId: return (NodeId)body;
                case BuiltInType.ExpandedNodeId: return (ExpandedNodeId)body;
                case BuiltInType.StatusCode: return (StatusCode)body;
                case BuiltInType.XmlElement: return (System.Xml.XmlElement)body;
                case BuiltInType.ExtensionObject: return (ExtensionObject)body;
            }
            return null;
        }
    }

    public class UASubscriberUADPDecoder: IUASubscriberDecoder
    {
        public UInt64 DataSetWriterId { get; set; }
        public DataSetMetaDataType DataSetMetaDataType { get; set; }
        DataSetReaderState m_dataSetReaderState = null;
        Opc.Ua.Core.SubscriberDelegate m_subscriberDelegate;
        FieldTargetDataType[] m_fieldTargetDataTypes; 

        #region Public Methods
        public UASubscriberUADPDecoder(DataSetReaderState dataSetReaderState, Opc.Ua.Core.SubscriberDelegate subscriberDelegate)
        {
            m_dataSetReaderState = dataSetReaderState;
            m_subscriberDelegate = subscriberDelegate;
            DataSetWriterId = dataSetReaderState.DataSetWriterId.Value;
            DataSetMetaDataType = dataSetReaderState.DataSetMetaData.Value;
        }

        public void UpdateTargetVariables(Dictionary<string, object> dic_NetworkMessage)
        {
            if (m_dataSetReaderState.Status.State.Value != PubSubState.Operational || (m_dataSetReaderState.Parent as ReaderGroupState).Status.State.Value != PubSubState.Operational)
            {
                return;
            }

            UadpNetworkMessageDecoder uadpNetworkMessage= dic_NetworkMessage["NetworkMessage"] as UadpNetworkMessageDecoder;
            bool IsvalidPublisher = false;
            if (!uadpNetworkMessage.IsPublishedEnabled)
            {
                IsvalidPublisher = true;
            }
            else
            { 
                object publisherId = uadpNetworkMessage.PublisherId;
                Type publisherIDType = publisherId.GetType();

                switch (publisherIDType.FullName)
                {
                    case "System.String":
                        if (m_dataSetReaderState.PublisherId.Value.ToString() == publisherId.ToString())
                        {
                            IsvalidPublisher = true;
                        }
                        break;
                    case "System.Byte":
                        if ((byte)m_dataSetReaderState.PublisherId.Value == (byte)publisherId)
                        {
                            IsvalidPublisher = true;
                        }
                        break;
                    case "System.UInt16":
                        try
                        {
                            if (Convert.ToUInt16(m_dataSetReaderState.PublisherId.Value.ToString()) == Convert.ToUInt16(publisherId))
                            {
                                IsvalidPublisher = true;
                            }
                        }
                        catch(Exception ex)
                        {

                        }
                        break;
                    case "System.UInt32":
                        if (Convert.ToUInt32(m_dataSetReaderState.PublisherId.Value.ToString()) == Convert.ToUInt32(publisherId))
                        {
                            IsvalidPublisher = true;
                        }
                        break;
                    case "System.UInt64":
                        if (Convert.ToUInt64(m_dataSetReaderState.PublisherId.Value.ToString()) == Convert.ToUInt64(publisherId))
                        {
                            IsvalidPublisher = true;
                        }
                        break;
                    case "System.Guid":
                        if (new Guid(m_dataSetReaderState.PublisherId.Value.ToString()) == (Guid)publisherId)
                        {
                            IsvalidPublisher = true;
                        }
                        break;
                }
            }
            if(!IsvalidPublisher)
            {
                return;
            }
            foreach (UInt16 DS_WriterId in uadpNetworkMessage.DicDataSetWiter_Message.Keys)
            {
                if(DS_WriterId== DataSetWriterId)
                {
                  UadpDataSetMessageDecoder uadpDataSetMessageDecoder=  uadpNetworkMessage.DicDataSetWiter_Message[DS_WriterId];
                     
                        DataSetMetaDataType _DataSetMetaDataType = m_dataSetReaderState.DataSetMetaData.Value as DataSetMetaDataType;
                    for (int i=0;i< _DataSetMetaDataType.Fields.Count;i++)
                    {
                        FieldMetaData fieldMetaData = _DataSetMetaDataType.Fields[i];
                        if (m_fieldTargetDataTypes != null)
                        {

                            FieldTargetDataType fieldTargetDataType = m_fieldTargetDataTypes.Where(j => j.DataSetFieldId == fieldMetaData.DataSetFieldId).FirstOrDefault();
                            if (fieldTargetDataType != null)
                            {
                                DataValue dataValue = uadpDataSetMessageDecoder.LstFieldMessageData[i];
                                m_subscriberDelegate(fieldTargetDataType.TargetNodeId, dataValue);
                            }
                        }
                             
                        
                    }
                    break;
                }
            }
        }

        public void UpdateFieldTargetDataType(FieldTargetDataType[] fieldTargetDataTypes)
        {
            m_fieldTargetDataTypes = fieldTargetDataTypes;
        }

        public void RemoveFieldTargetDataType()
        {
            m_fieldTargetDataTypes = null;
        }
        #endregion
    }
}
