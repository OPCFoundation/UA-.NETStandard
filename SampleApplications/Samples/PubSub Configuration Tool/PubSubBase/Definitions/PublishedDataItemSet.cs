using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PubSubBase.Definitions
{
    public class PublishedDataDefinition: PublishedDataSetBase
    {
        

        public PublishedDataDefinition(PublishedDataSetBase publishedDataSetBase)
        {
            ParentNode = publishedDataSetBase;
        }

    }
   public class PublishedDataSetItemDefinition : PublishedDataSetBase
    {
        string _PublishVariable { get; set; }
        public string PublishVariable
        {
            get
            {
                return _PublishVariable;
            }
            set
            {
              Name=  _PublishVariable = value;
                OnPropertyChanged("PublishVariable");
            }
        }
        NodeId _PublishVariableNodeId = new NodeId("");
        public NodeId PublishVariableNodeId
        {
            get
            {
                return _PublishVariableNodeId;
            }
            set
            {
                _PublishVariableNodeId = value;
                OnPropertyChanged("PublishVariableNodeId");
            }
        }

        uint _Attribute = 13;
        public uint Attribute
        {
            get
            {
                return _Attribute;
            }
            set
            {
                _Attribute = value;
                OnPropertyChanged("Attribute");
            }
        }

        double _SamplingInterval = -1;
        public double SamplingInterval
        {
            get
            {
                return _SamplingInterval;
            }
            set
            {
                _SamplingInterval = value;
                OnPropertyChanged("SamplingInterval");
            }
        }

        private uint _DeadbandType = (uint)Opc.Ua.DeadbandType.None;
        public uint DeadbandType
        {
            get
            {
                return _DeadbandType;
            }
            set
            {
                 
                   _DeadbandType = value;
                OnPropertyChanged("DeadbandType");
            }
        }

        private double _DeadbandValue = 0;
        public double DeadbandValue
        {
            get
            {
                return _DeadbandValue;
            }
            set
            {
                _DeadbandValue = value;
                OnPropertyChanged("DeadbandValue");
            }
        }
        private string _Indexrange = "0";
        public string Indexrange
        {
            get
            {
                return _Indexrange;
            }
            set
            {
                _Indexrange = value;
                OnPropertyChanged("Indexrange");
            }
        }
        Variant _SubstituteValue="0";
        public Variant SubstituteValue
        {
            get
            {
                return _SubstituteValue;
            }
            set
            {
                _SubstituteValue = value;
                OnPropertyChanged("SubstituteValue");
            }
        }
        QualifiedNameCollection _FieldMetaDataProperties = new QualifiedNameCollection();
       public QualifiedNameCollection FieldMetaDataProperties
        {
            get
            {
                return _FieldMetaDataProperties;
            }
            set
            {
                _FieldMetaDataProperties = value;
                OnPropertyChanged("FieldMetaDataProperties");
            }
        }
        public PublishedVariableDataType PublishedVariableDataType { get; set; }
        public PublishedDataSetItemDefinition(PublishedDataSetBase _PublishedDataSetBase)
        {
            ParentNode = _PublishedDataSetBase;
        }
    }

    public class DataSetMetaDataDefinition: PublishedDataSetBase
    {

        DataSetMetaDataType _DataSetMetaDataType;
       public  DataSetMetaDataType DataSetMetaDataType
        {
            get
            {
                return _DataSetMetaDataType;
            }
            set
            {
                _DataSetMetaDataType = value;
                OnPropertyChanged("DataSetMetaDataType"); 
            }
        }
        public DataSetMetaDataDefinition(PublishedDataSetDefinition _PublishedDataSetDefinition)
        {
            ParentNode = _PublishedDataSetDefinition;
             
        }
    }
    
    public class PublishedDataSetDefinition : PublishedDataSetBase
    {
        public NodeId PublishedDataSetNodeId { get; set; }
        string _DataSetNodeId = string.Empty;
        public string DataSetNodeId
        {
            get
            {
                if(PublishedDataSetNodeId!=null)
                {
                    return PublishedDataSetNodeId.ToString();
                }
                return _DataSetNodeId;
            }
            set
            {
                _DataSetNodeId = value;
                OnPropertyChanged("DataSetNodeId");
            }
        }

        ConfigurationVersionDataType _ConfigurationVersionDataType=new ConfigurationVersionDataType();
        public ConfigurationVersionDataType ConfigurationVersionDataType
        {
            get
            { 
                return _ConfigurationVersionDataType;
            }
            set
            {
                _ConfigurationVersionDataType = value;
                OnPropertyChanged("ConfigurationVersionDataType");
            }
        }

         
        //public DataSetMetaDataDefinition DataSetMetaDataDefinition { get; set; }
        //public PublishedDataDefinition PublishedDataDefinition { get; set; }

        public PublishedDataSetDefinition()
        {
            //DataSetMetaDataDefinition = new DataSetMetaDataDefinition(this);
            //PublishedDataDefinition = new PublishedDataDefinition(this);
        }
    }


    public class FieldTargetVariableDefinition : PubSubConfiguationBase
    {
        public FieldTargetDataType FieldTargetDataType { get; set; }
        private string _DataSetFieldId = string.Empty;
        public string DataSetFieldId
        {
            get
            {
                return _DataSetFieldId;
            }
            set
            {
                _DataSetFieldId = value;
                OnPropertyChanged("DataSetFieldId");
            }
        }
        private string _ReceiverIndexRange = string.Empty;
        public string ReceiverIndexRange
        {
            get
            {
                return _ReceiverIndexRange;
            }
            set
            {
                _ReceiverIndexRange = value;
                OnPropertyChanged("ReceiverIndexRange");
            }
        }
        private string _TargetNodeId = string.Empty;
        public string TargetNodeId
        {
            get
            {
                return _TargetNodeId;
            }
            set
            {
                _TargetNodeId = value;
                OnPropertyChanged("TargetNodeId");
            }
        }
        private NodeId _TargetFieldNodeId = string.Empty;
        public NodeId TargetFieldNodeId
        {
            get
            {
                return _TargetFieldNodeId;
            }
            set
            {
                _TargetFieldNodeId = value;
                OnPropertyChanged("TargetFieldNodeId");
            }
        }
        private uint _AttributeId = 13;
        public uint AttributeId
        {
            get
            {
                return _AttributeId;
            }
            set
            {
                _AttributeId = value;
                OnPropertyChanged("AttributeId");
            }
        }
        private string _WriteIndexRange = string.Empty;
        public string WriteIndexRange
        {
            get
            {
                return _WriteIndexRange;
            }
            set
            {
                _WriteIndexRange = value;
                OnPropertyChanged("WriteIndexRange");
            }
        }
        private int _OverrideValueHandling = 1;
        public int OverrideValueHandling
        {
            get
            {
                return _OverrideValueHandling;
            }
            set
            {
                _OverrideValueHandling = value;
                OnPropertyChanged("OverrideValueHandling");
            }
        }
        object _OverrideValue;
        public object OverrideValue
        {
            get
            {
                return _OverrideValue;
            }
            set
            {
                _OverrideValue = value;
                OnPropertyChanged("OverrideValue");
            }
        }
    }

    public class SubscribedDataSetDefinition: PubSubConfiguationBase
    {
        ObservableCollection<FieldTargetVariableDefinition> _FieldTargetVariableDefinitionCollection = new ObservableCollection<Definitions.FieldTargetVariableDefinition>();
        public ObservableCollection<FieldTargetVariableDefinition> FieldTargetVariableDefinitionCollection
        {
            get
            {
                return _FieldTargetVariableDefinitionCollection;
            }
            set
            {
                _FieldTargetVariableDefinitionCollection = value;

            }
        }

        public ConfigurationVersionDataType ConfigurationVersionDataType { get; set; }
      
    }

    public class MirrorSubscribedDataSetDefinition : PubSubConfiguationBase
    {
        ObservableCollection<MirrorVariableDefinition> _MirrorVariableDefinitionCollection = new ObservableCollection<MirrorVariableDefinition>();
        public ObservableCollection<MirrorVariableDefinition> MirrorVariableDefinitionCollection
        {
            get
            {
                return _MirrorVariableDefinitionCollection;
            }
            set
            {
                _MirrorVariableDefinitionCollection = value;

            }
        }
         

    }

    public class MirrorVariableDefinition: PubSubConfiguationBase
    {

    }



}
