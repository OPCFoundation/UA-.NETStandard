/* Copyright (c) 1996-2017, OPC Foundation. All rights reserved.

   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else

   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/

   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2

   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using Opc.Ua;
using System.Collections.ObjectModel;

namespace PubSubBase.Definitions
{
    /// <summary>
    /// Published Data definition
    /// </summary>
    public class PublishedDataDefinition : PublishedDataSetBase
    {
        #region Costructors
        /// <summary>
        /// Initialise PublishedDataSetBase 
        /// </summary>
        /// <param name="publishedDataSetBase"></param>
        public PublishedDataDefinition(PublishedDataSetBase publishedDataSetBase)
        {
            ParentNode = publishedDataSetBase;
        }
        #endregion
    }

    /// <summary>
    /// Published Data Set Item definition
    /// </summary>
    public class PublishedDataSetItemDefinition : PublishedDataSetBase
    {
        #region Private Fields

        private string m_publishVariable;
        uint m_attribute = 13;
        double m_samplingInterval = -1;
        private uint m_deadbandType = (uint)Opc.Ua.DeadbandType.None;
        private double m_deadbandValue = 0;
        private string m_indexrange = "0";
        Variant m_substituteValue = "0";
        QualifiedNameCollection m_fieldMetaDataProperties = new QualifiedNameCollection();
        NodeId m_publishVariableNodeId = new NodeId("");


        #endregion

        #region Constructors
        /// <summary>
        /// Initialising baseclass object
        /// </summary>
        /// <param name="_PublishedDataSetBase"></param>
        public PublishedDataSetItemDefinition(PublishedDataSetBase _PublishedDataSetBase)
        {
            ParentNode = _PublishedDataSetBase;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Publishing Variable name of the definition
        /// </summary>
        public string PublishVariable
        {
            get
            {
                return m_publishVariable;
            }
            set
            {
                Name = m_publishVariable = value;
                OnPropertyChanged("PublishVariable");
            }
        }

        /// <summary>
        /// Publish Variable Node ID of the definition
        /// </summary>
        public NodeId PublishVariableNodeId
        {
            get
            {
                return m_publishVariableNodeId;
            }
            set
            {
                m_publishVariableNodeId = value;
                OnPropertyChanged("PublishVariableNodeId");
            }
        }

        /// <summary>
        /// defines attribute of the target definition
        /// </summary>
        public uint Attribute
        {
            get
            {
                return m_attribute;
            }
            set
            {
                m_attribute = value;
                OnPropertyChanged("Attribute");
            }
        }

        /// <summary>
        /// defines Interval for the variable definition
        /// </summary>
        public double SamplingInterval
        {
            get
            {
                return m_samplingInterval;
            }
            set
            {
                m_samplingInterval = value;
                OnPropertyChanged("SamplingInterval");
            }
        }

        /// <summary>
        /// defines dead band type of the definition
        /// </summary>
        public uint DeadbandType
        {
            get
            {
                return m_deadbandType;
            }
            set
            {

                m_deadbandType = value;
                OnPropertyChanged("DeadbandType");
            }
        }

        /// <summary>
        /// defines dead band value of the definition
        /// </summary>
        public double DeadbandValue
        {
            get
            {
                return m_deadbandValue;
            }
            set
            {
                m_deadbandValue = value;
                OnPropertyChanged("DeadbandValue");
            }
        }

        /// <summary>
        /// defines Index range of the target definition
        /// </summary>
        public string Indexrange
        {
            get
            {
                return m_indexrange;
            }
            set
            {
                m_indexrange = value;
                OnPropertyChanged("Indexrange");
            }
        }

        /// <summary>
        /// defines Substitute value of the target variable
        /// </summary>
        public Variant SubstituteValue
        {
            get
            {
                return m_substituteValue;
            }
            set
            {
                m_substituteValue = value;
                OnPropertyChanged("SubstituteValue");
            }
        }

        /// <summary>
        /// dfeines meta data properties of the target variable
        /// </summary>
        public QualifiedNameCollection FieldMetaDataProperties
        {
            get
            {
                return m_fieldMetaDataProperties;
            }
            set
            {
                m_fieldMetaDataProperties = value;
                OnPropertyChanged("FieldMetaDataProperties");
            }
        }

        /// <summary>
        /// defines data type of the target variable
        /// </summary>
        public PublishedVariableDataType PublishedVariableDataType { get; set; }
        #endregion
    }

    /// <summary>
    /// Definition of Data Set Meta Data
    /// </summary>
    public class DataSetMetaDataDefinition : PublishedDataSetBase
    {
        #region Private Fields
        DataSetMetaDataType m_dataSetMetaDataType;
        #endregion

        #region Public Properties
        /// <summary>
        /// defines the type of DataSet meta data
        /// </summary>
        public DataSetMetaDataType DataSetMetaDataType
        {
            get
            {
                return m_dataSetMetaDataType;
            }
            set
            {
                m_dataSetMetaDataType = value;
                OnPropertyChanged("DataSetMetaDataType");
            }
        }
        #endregion

        #region Constructors
        /// <summary>
        /// initialising base class object
        /// </summary>
        /// <param name="_PublishedDataSetDefinition"></param>
        public DataSetMetaDataDefinition(PublishedDataSetDefinition _PublishedDataSetDefinition)
        {
            ParentNode = _PublishedDataSetDefinition;

        }
        #endregion
    }

    /// <summary>
    /// defines Data Set definition
    /// </summary>
    public class PublishedDataSetDefinition : PublishedDataSetBase
    {
        #region Private Fields
        string m_dataSetNodeId = string.Empty;
        ConfigurationVersionDataType m_configurationVersionDataType = new ConfigurationVersionDataType();
        #endregion

        #region Public Properties
        /// <summary>
        /// defines node ID of Published Data Set
        /// </summary>
        public NodeId PublishedDataSetNodeId { get; set; }
        /// <summary>
        /// defines node ID of Data Set
        /// </summary>
        public string DataSetNodeId
        {
            get
            {
                if (PublishedDataSetNodeId != null)
                {
                    return PublishedDataSetNodeId.ToString();
                }
                return m_dataSetNodeId;
            }
            set
            {
                m_dataSetNodeId = value;
                OnPropertyChanged("DataSetNodeId");
            }
        }

        /// <summary>
        /// defines data type of the target definition
        /// </summary>
        public ConfigurationVersionDataType ConfigurationVersionDataType
        {
            get
            {
                return m_configurationVersionDataType;
            }
            set
            {
                m_configurationVersionDataType = value;
                OnPropertyChanged("ConfigurationVersionDataType");
            }
        }

        #endregion

        #region Constructors
        public PublishedDataSetDefinition()
        {
        }
        #endregion
    }

    /// <summary>
    /// defines Field Target Variable definition
    /// </summary>
    public class FieldTargetVariableDefinition : PubSubConfiguationBase
    {
        #region Private Fields

        private string m_dataSetFieldId = string.Empty;
        private string m_receiverIndexRange = string.Empty;
        private string m_targetNodeId = string.Empty;
        private NodeId m_targetFieldNodeId = string.Empty;
        private uint m_attributeId = 13;
        private string m_writeIndexRange = string.Empty;
        private int m_overrideValueHandling = 1;
        object m_overrideValue;

        #endregion

        #region Public Properties

        /// <summary>
        /// defines target data type of the definition
        /// </summary>
        public FieldTargetDataType FieldTargetDataType { get; set; }
        /// <summary>
        /// defines Data Set Field ID
        /// </summary>
        public string DataSetFieldId
        {
            get
            {
                return m_dataSetFieldId;
            }
            set
            {
                m_dataSetFieldId = value;
                OnPropertyChanged("DataSetFieldId");
            }
        }
        /// <summary>
        /// defines receiver index range for target definition
        /// </summary>
        public string ReceiverIndexRange
        {
            get
            {
                return m_receiverIndexRange;
            }
            set
            {
                m_receiverIndexRange = value;
                OnPropertyChanged("ReceiverIndexRange");
            }
        }
        /// <summary>
        /// defines Target node ID
        /// </summary>
        public string TargetNodeId
        {
            get
            {
                return m_targetNodeId;
            }
            set
            {
                m_targetNodeId = value;
                OnPropertyChanged("TargetNodeId");
            }
        }
        /// <summary>
        /// defines Target field Node ID
        /// </summary>
        public NodeId TargetFieldNodeId
        {
            get
            {
                return m_targetFieldNodeId;
            }
            set
            {
                m_targetFieldNodeId = value;
                OnPropertyChanged("TargetFieldNodeId");
            }
        }
        /// <summary>
        /// defines attribute ID of the Target Node
        /// </summary>
        public uint AttributeId
        {
            get
            {
                return m_attributeId;
            }
            set
            {
                m_attributeId = value;
                OnPropertyChanged("AttributeId");
            }
        }
        /// <summary>
        /// defines write Index range of the target Node
        /// </summary>
        public string WriteIndexRange
        {
            get
            {
                return m_writeIndexRange;
            }
            set
            {
                m_writeIndexRange = value;
                OnPropertyChanged("WriteIndexRange");
            }
        }
        /// <summary>
        /// defines handling of the definition
        /// </summary>
        public int OverrideValueHandling
        {
            get
            {
                return m_overrideValueHandling;
            }
            set
            {
                m_overrideValueHandling = value;
                OnPropertyChanged("OverrideValueHandling");
            }
        }
        /// <summary>
        /// defines override value of the definition
        /// </summary>
        public object OverrideValue
        {
            get
            {
                return m_overrideValue;
            }
            set
            {
                m_overrideValue = value;
                OnPropertyChanged("OverrideValue");
            }
        }

        #endregion
    }

    /// <summary>
    /// definition of Subscribed data set 
    /// </summary>
    public class SubscribedDataSetDefinition : PubSubConfiguationBase
    {
        #region Private Fields
        ObservableCollection<FieldTargetVariableDefinition> m_fieldTargetVariableDefinitionCollection = new ObservableCollection<Definitions.FieldTargetVariableDefinition>();
        #endregion

        #region Public Properties
        /// <summary>
        /// defines collection of FieldTargetVariable definition 
        /// </summary>
        public ObservableCollection<FieldTargetVariableDefinition> FieldTargetVariableDefinitionCollection
        {
            get
            {
                return m_fieldTargetVariableDefinitionCollection;
            }
            set
            {
                m_fieldTargetVariableDefinitionCollection = value;
            }
        }

        /// <summary>
        /// defines data type of configuration version
        /// </summary>
        public ConfigurationVersionDataType ConfigurationVersionDataType { get; set; }

        #endregion

    }

    /// <summary>
    /// definition of Mirror subscribed data set
    /// </summary>
    public class MirrorSubscribedDataSetDefinition : PubSubConfiguationBase
    {
        #region Private Fields
        ObservableCollection<MirrorVariableDefinition> m_mirrorVariableDefinitionCollection = new ObservableCollection<MirrorVariableDefinition>();
        #endregion

        #region Public Properties
        /// <summary>
        /// collection of mirror variable definition 
        /// </summary>
        public ObservableCollection<MirrorVariableDefinition> MirrorVariableDefinitionCollection
        {
            get
            {
                return m_mirrorVariableDefinitionCollection;
            }
            set
            {
                m_mirrorVariableDefinitionCollection = value;

            }
        }
        #endregion

    }

    /// <summary>
    /// definition Mirror variable 
    /// </summary>
    public class MirrorVariableDefinition : PubSubConfiguationBase
    {

    }

}
