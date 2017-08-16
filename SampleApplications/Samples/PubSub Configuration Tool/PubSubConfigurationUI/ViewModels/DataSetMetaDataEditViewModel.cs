using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using NLogManager;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    public class DataSetMetaDataEditViewModel : BaseViewModel
    {
        #region Private Member 

        private string _dataSetClassId;
        private DataSetMetaDataDefinition _Definition;
        private string _Description;

        private ObservableCollection< FieldMetaDataDefinition > _FieldMetaDataDefinitionCollection =
        new ObservableCollection< FieldMetaDataDefinition >( );

        private uint _MajorVersion = 1;
        private uint _MinorVersion = 1;
        private ConfigurationVersionDataType m_configurationVersion;
        private StringCollection m_namespaces;

        #endregion

        #region Private Methods

        private List< FieldInfo > GetConstants( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.Where( fi => fi.IsLiteral && !fi.IsInitOnly ).ToList( );
        }

        #endregion

        #region Constructors

        public DataSetMetaDataEditViewModel( )
        {
            Definition = new DataSetMetaDataDefinition( null );
        }

        #endregion

        #region Public Property

        public DataSetMetaDataDefinition Definition
        {
            get { return _Definition; }
            set
            {
                _Definition = value;
                OnPropertyChanged( "Definition" );
            }
        }

        public string Description
        {
            get { return _Description; }
            set
            {
                _Description = value;
                OnPropertyChanged( "Description" );
            }
        }

        public string DataSetClassId
        {
            get { return _dataSetClassId; }
            set
            {
                _dataSetClassId = value;
                OnPropertyChanged( "DataSetClassId" );
            }
        }

        public StringCollection Namespaces
        {
            get { return m_namespaces; }
            set
            {
                m_namespaces = value;

                if ( value == null ) m_namespaces = new StringCollection( );
            }
        }

        public ConfigurationVersionDataType ConfigurationVersion
        {
            get { return m_configurationVersion; }
            set
            {
                m_configurationVersion = value;

                if ( value == null ) m_configurationVersion = new ConfigurationVersionDataType( );
            }
        }

        public uint MinorVersion
        {
            get { return _MinorVersion; }
            set
            {
                _MinorVersion = value;
                OnPropertyChanged( "MinorVersion" );
            }
        }

        public uint MajorVersion
        {
            get { return _MajorVersion; }
            set
            {
                _MajorVersion = value;
                OnPropertyChanged( "MajorVersion" );
            }
        }

        public ObservableCollection< FieldMetaDataDefinition > FieldMetaDataDefinitionCollection
        {
            get { return _FieldMetaDataDefinitionCollection; }
            set
            {
                _FieldMetaDataDefinitionCollection = value;
                OnPropertyChanged( "FieldMetaDataDefinitionCollection" );
            }
        }

        #endregion

        #region Public Methods

        public void Initialize( )
        {
            Description = Definition.DataSetMetaDataType.Description.Text;
            DataSetClassId = Definition.DataSetMetaDataType.DataSetClassId.GuidString;
            MinorVersion = Definition.DataSetMetaDataType.ConfigurationVersion.MinorVersion;
            MajorVersion = Definition.DataSetMetaDataType.ConfigurationVersion.MajorVersion;
            ConfigurationVersion = Definition.DataSetMetaDataType.ConfigurationVersion;
            FieldMetaDataDefinitionCollection.Clear( );
            foreach ( var _FieldMetaData in Definition.DataSetMetaDataType.Fields )
            {
                var _FieldMetaDataDefinition = new FieldMetaDataDefinition( );
                _FieldMetaDataDefinition.ArrayDimensions = _FieldMetaData.ArrayDimensions;
                _FieldMetaDataDefinition.BuildInType = _FieldMetaData.BuiltInType;
                _FieldMetaDataDefinition.DataSetFieldFlags = _FieldMetaData.FieldFlags;
                _FieldMetaDataDefinition.DataSetFieldId = _FieldMetaData.DataSetFieldId.ToString( );
                _FieldMetaDataDefinition.Description = _FieldMetaData.Description.Text;
                _FieldMetaDataDefinition.Name = _FieldMetaData.Name;
                _FieldMetaDataDefinition.DataTypeId = _FieldMetaData.DataType.ToString( );
                try
                {
                    _FieldMetaDataDefinition.ValueRank = GetConstants( typeof( ValueRanks ) )
                    .Where( i => Convert.ToInt16( i.GetValue( i ) ) == _FieldMetaData.ValueRank ).FirstOrDefault( )
                    .Name;
                }
                catch ( Exception ex )
                {
                    Log.ErrorException( "DataSetMetaDataEditViewModel:Initialize API", ex );
                }

                FieldMetaDataDefinitionCollection.Add( _FieldMetaDataDefinition );
            }
        }

        #endregion
    }

    public class FieldMetaDataDefinition : BaseViewModel
    {
        #region Private Member 

        private int _BuildInType = 1;
        private ObservableCollection< DataItemBinding > _BuildInTypes = new ObservableCollection< DataItemBinding >( );
        private DataSetFieldFlags _DataSetFieldFlags = DataSetFieldFlags.PromotedField;

        private ObservableCollection< DataItemBinding > _DataTypesCollection =
        new ObservableCollection< DataItemBinding >( );

        private ObservableCollection< DataItemBinding > _FieldsCollection =
        new ObservableCollection< DataItemBinding >( );

        private KeyValuePairDefinition _KeyValuePairDefinition = new KeyValuePairDefinition( );

        private ObservableCollection< DataItemBinding > _ValuerankCollection =
        new ObservableCollection< DataItemBinding >( );

        private UInt32Collection m_arrayDimensions = new UInt32Collection( );
        private string m_dataSetFieldId;
        private string m_dataType;
        private string m_dataTypeId;
        private string m_description;
        private string m_name;
        private string m_ValueRank;

        #endregion

        #region Private Methods

        private List< FieldInfo > GetConstants( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.Where( fi => fi.IsLiteral && !fi.IsInitOnly ).ToList( );
        }

        private List< FieldInfo > GetFields( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.ToList( );
        }

        #endregion

        #region Constructors

        public FieldMetaDataDefinition( )
        {
            FieldsCollection.Add( new DataItemBinding
                                  {
                                      Name = "PromotedField",
                                      DisplayName = "Promoted Field",
                                      Value = "1"
                                  } );

            foreach ( BuiltInType types in Enum.GetValues( typeof( BuiltInType ) ) )
                BuiltInTypeCollection.Add(
                    new DataItemBinding
                    {
                        Name = types.ToString( ),
                        DisplayName = types.ToString( ),
                        Value = Convert.ToString( ( uint ) types )
                    } );
            var lstDataTypesFiledInfo = GetFields( typeof( DataTypeIds ) );
            foreach ( var _FieldInfo in lstDataTypesFiledInfo )
                DataTypesCollection.Add(
                    new DataItemBinding
                    {
                        Name = _FieldInfo.Name,
                        DisplayName = _FieldInfo.Name,
                        Value = Convert.ToString( _FieldInfo.GetValue( _FieldInfo.Name ) )
                    } );
            var lstValueRanksFiledInfo = GetConstants( typeof( ValueRanks ) );
            foreach ( var _FieldInfo in lstValueRanksFiledInfo )
                ValuerankCollection.Add(
                    new DataItemBinding
                    {
                        Name = _FieldInfo.Name,
                        DisplayName = _FieldInfo.Name,
                        Value = Convert.ToString( _FieldInfo.GetValue( _FieldInfo.Name ) )
                    } );
        }

        #endregion

        #region Public Property

        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        public string Description
        {
            get { return m_description; }
            set { m_description = value; }
        }

        public string DataType
        {
            get
            {
                if ( DataTypesCollection != null )
                    try
                    {
                        return DataTypesCollection
                        .Where( i => i.Value.ToString( ) == DataTypeId ).FirstOrDefault( ).DisplayName;
                    }
                    catch ( Exception ex )
                    {
                        return DataTypeId;
                    }
                return m_dataType;
            }
            set { m_dataType = value; }
        }

        public string DataTypeId
        {
            get { return m_dataTypeId; }
            set { m_dataTypeId = value; }
        }

        public string ValueRank
        {
            get { return m_ValueRank; }
            set { m_ValueRank = value; }
        }

        public DataSetFieldFlags DataSetFieldFlags
        {
            get { return _DataSetFieldFlags; }
            set { _DataSetFieldFlags = value; }
        }

        public int BuildInType
        {
            get { return _BuildInType; }
            set { _BuildInType = value; }
        }

        public ObservableCollection< DataItemBinding > FieldsCollection
        {
            get { return _FieldsCollection; }
            set
            {
                _FieldsCollection = value;
                OnPropertyChanged( "FieldsCollection" );
            }
        }

        public ObservableCollection< DataItemBinding > BuiltInTypeCollection
        {
            get { return _BuildInTypes; }
            set
            {
                _BuildInTypes = value;
                OnPropertyChanged( "BuiltInType" );
            }
        }

        public UInt32Collection ArrayDimensions
        {
            get { return m_arrayDimensions; }
            set
            {
                m_arrayDimensions = value;

                if ( value == null ) m_arrayDimensions = new UInt32Collection( );
            }
        }

        public string DataSetFieldId
        {
            get { return m_dataSetFieldId; }
            set { m_dataSetFieldId = value; }
        }

        public ObservableCollection< DataItemBinding > DataTypesCollection
        {
            get { return _DataTypesCollection; }
            set
            {
                _DataTypesCollection = value;
                OnPropertyChanged( "DataTypesCollection" );
            }
        }

        public ObservableCollection< DataItemBinding > ValuerankCollection
        {
            get { return _ValuerankCollection; }
            set
            {
                _ValuerankCollection = value;
                OnPropertyChanged( "ValuerankCollection" );
            }
        }

        public KeyValuePairDefinition KeyValuePairItem
        {
            get { return _KeyValuePairDefinition; }
            set
            {
                _KeyValuePairDefinition = value;
                OnPropertyChanged( "KeyValuePairItem" );
            }
        }

        #endregion
    }

    public class DataItemBinding
    {
        #region Private Member 

        private string _Value;
        private string m_displayname;
        private string m_name;

        #endregion

        #region Public Property

        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        public string DisplayName
        {
            get { return m_displayname; }
            set { m_displayname = value; }
        }

        public string Value
        {
            get { return _Value; }
            set { _Value = value; }
        }

        #endregion
    }

    public class KeyValuePairDefinition
    {
        #region Public Property

        public string key { get; set; }

        public string value { get; set; }

        #endregion
    }
}