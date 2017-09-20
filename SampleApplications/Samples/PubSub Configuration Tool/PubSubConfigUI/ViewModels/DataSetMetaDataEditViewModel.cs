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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model definition for data set meta data view model
    /// </summary>
    public class DataSetMetaDataEditViewModel : BaseViewModel
    {
        #region Private Fields

        private string m_dataSetClassId;
        private DataSetMetaDataDefinition m_definition;
        private string m_description;

        private ObservableCollection< FieldMetaDataDefinition > m_fieldMetaDataDefinitionCollection =
        new ObservableCollection< FieldMetaDataDefinition >( );

        private uint m_majorVersion = 1;
        private uint m_minorVersion = 1;
        private ConfigurationVersionDataType m_configurationVersion;

        #endregion

        #region Private Methods

        /// <summary>
        /// Method to get Constants list from array
        /// </summary>
        /// <param name="type">Type of the contants</param>
        /// <returns></returns>
        private List< FieldInfo > GetConstants( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.Where( fi => fi.IsLiteral && !fi.IsInitOnly ).ToList( );
        }

        #endregion

        #region Constructors
        /// <summary>
        /// initialising  DataSetMetaDataEditViewModel with new definition
        /// </summary>
        public DataSetMetaDataEditViewModel( )
        {
            Definition = new DataSetMetaDataDefinition( null );
        }

        #endregion

        #region Public Properties
        /// <summary>
        /// defines Data Set Meta Data Definition
        /// </summary>
        public DataSetMetaDataDefinition Definition
        {
            get { return m_definition; }
            set
            {
                m_definition = value;
                OnPropertyChanged( "Definition" );
            }
        }
        /// <summary>
        /// defines description of the  definition
        /// </summary>
        public string Description
        {
            get { return m_description; }
            set
            {
                m_description = value;
                OnPropertyChanged( "Description" );
            }
        }

        /// <summary>
        /// defines data set class ID data set meta data
        /// </summary>
        public string DataSetClassId
        {
            get { return m_dataSetClassId; }
            set
            {
                m_dataSetClassId = value;
                OnPropertyChanged( "DataSetClassId" );
            }
        }

        /// <summary>
        /// defines configuration version of data set meta data
        /// </summary>
        public ConfigurationVersionDataType ConfigurationVersion
        {
            get { return m_configurationVersion; }
            set
            {
                m_configurationVersion = value;

                if ( value == null ) m_configurationVersion = new ConfigurationVersionDataType( );
            }
        }

        /// <summary>
        /// defines configuration minor version of data set meta data
        /// </summary>
        public uint MinorVersion
        {
            get { return m_minorVersion; }
            set
            {
                m_minorVersion = value;
                OnPropertyChanged( "MinorVersion" );
            }
        }
        /// <summary>
        /// defines configuration major version of data set meta data
        /// </summary>
        public uint MajorVersion
        {
            get { return m_majorVersion; }
            set
            {
                m_majorVersion = value;
                OnPropertyChanged( "MajorVersion" );
            }
        }

        /// <summary>
        /// defines collection of Field Meta Data Definition
        /// </summary>
        public ObservableCollection< FieldMetaDataDefinition > FieldMetaDataDefinitionCollection
        {
            get { return m_fieldMetaDataDefinitionCollection; }
            set
            {
                m_fieldMetaDataDefinitionCollection = value;
                OnPropertyChanged( "FieldMetaDataDefinitionCollection" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Initialise method with new field meta data definition
        /// </summary>
        public void Initialize( )
        {
            Description = Definition.DataSetMetaDataType.Description.Text;
            DataSetClassId = Definition.DataSetMetaDataType.DataSetClassId.GuidString;
            MinorVersion = Definition.DataSetMetaDataType.ConfigurationVersion.MinorVersion;
            MajorVersion = Definition.DataSetMetaDataType.ConfigurationVersion.MajorVersion;
            ConfigurationVersion = Definition.DataSetMetaDataType.ConfigurationVersion;
            FieldMetaDataDefinitionCollection.Clear( );
            foreach ( var fieldMetaData in Definition.DataSetMetaDataType.Fields )
            {
                var fieldMetaDataDefinition = new FieldMetaDataDefinition( );
                fieldMetaDataDefinition.ArrayDimensions = fieldMetaData.ArrayDimensions;
                fieldMetaDataDefinition.BuildInType = fieldMetaData.BuiltInType;
                fieldMetaDataDefinition.DataSetFieldFlags = fieldMetaData.FieldFlags;
                fieldMetaDataDefinition.DataSetFieldId = fieldMetaData.DataSetFieldId.ToString( );
                fieldMetaDataDefinition.Description = fieldMetaData.Description.Text;
                fieldMetaDataDefinition.Name = fieldMetaData.Name;
                fieldMetaDataDefinition.DataTypeId = fieldMetaData.DataType.ToString( );
                try
                {
                    fieldMetaDataDefinition.ValueRank = GetConstants( typeof( ValueRanks ) )
                    .Where( i => Convert.ToInt16( i.GetValue( i ) ) == fieldMetaData.ValueRank ).FirstOrDefault( )
                    .Name;
                }
                catch ( Exception ex )
                {
                    Utils.Trace( ex,"DataSetMetaDataEditViewModel:Initialize API", ex );
                }
                FieldMetaDataDefinitionCollection.Add( fieldMetaDataDefinition );
            }
        }

        #endregion
    }

    /// <summary>
    /// definition for field metadata definition
    /// </summary>
    public class FieldMetaDataDefinition : BaseViewModel
    {
        #region Private Fields 

        private int m_buildInType = 1;
        private ObservableCollection< DataItemBinding > m_buildInTypes = new ObservableCollection< DataItemBinding >( );
        private DataSetFieldFlags m_dataSetFieldFlags = DataSetFieldFlags.PromotedField;

        private ObservableCollection< DataItemBinding > m_dataTypesCollection =
        new ObservableCollection< DataItemBinding >( );

        private ObservableCollection< DataItemBinding > m_fieldsCollection =
        new ObservableCollection< DataItemBinding >( );

        private KeyValuePairDefinition m_keyValuePairDefinition = new KeyValuePairDefinition( );

        private ObservableCollection< DataItemBinding > m_valuerankCollection =
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
        /// <summary>
        ///  Method to get Constants list from array
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private List< FieldInfo > GetConstants( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.Where( fi => fi.IsLiteral && !fi.IsInitOnly ).ToList( );
        }

        /// <summary>
        ///  Method to get Field list
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
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

        #region Public Properties
        /// <summary>
        /// defines name of definition
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// defines description for target definition
        /// </summary>
        public string Description
        {
            get { return m_description; }
            set { m_description = value; }
        }

        public string DataType
        {
            get
            {
                if (DataTypesCollection != null)
                    try
                    {
                        return DataTypesCollection
                        .Where(i => i.Value.ToString() == DataTypeId).FirstOrDefault().DisplayName;
                    }
                    catch (Exception)
                    {
                        return DataTypeId;
                    }
                return m_dataType;
            }
            set { m_dataType = value; }
        }

        /// <summary>
        /// defines data type ID for target definition
        /// </summary>
        public string DataTypeId
        {
            get { return m_dataTypeId; }
            set { m_dataTypeId = value; }
        }

        /// <summary>
        /// defines value rank for target definition
        /// </summary>
        public string ValueRank
        {
            get { return m_ValueRank; }
            set { m_ValueRank = value; }
        }

        /// <summary>
        /// defines data set field flags for target definiton
        /// </summary>
        public DataSetFieldFlags DataSetFieldFlags
        {
            get { return m_dataSetFieldFlags; }
            set { m_dataSetFieldFlags = value; }
        }

        /// <summary>
        /// defines built in type for target definition
        /// </summary>
        public int BuildInType
        {
            get { return m_buildInType; }
            set { m_buildInType = value; }
        }

        /// <summary>
        /// defines Field collection of DataItemBinding
        /// </summary>
        public ObservableCollection< DataItemBinding > FieldsCollection
        {
            get { return m_fieldsCollection; }
            set
            {
                m_fieldsCollection = value;
                OnPropertyChanged( "FieldsCollection" );
            }
        }

        /// <summary>
        /// defines BuiltInType collection of type DataItemBinding
        /// </summary>
        public ObservableCollection< DataItemBinding > BuiltInTypeCollection
        {
            get { return m_buildInTypes; }
            set
            {
                m_buildInTypes = value;
                OnPropertyChanged( "BuiltInType" );
            }
        }

        /// <summary>
        /// defines array dimensions 
        /// </summary>
        public UInt32Collection ArrayDimensions
        {
            get { return m_arrayDimensions; }
            set
            {
                m_arrayDimensions = value;

                if ( value == null ) m_arrayDimensions = new UInt32Collection( );
            }
        }

        /// <summary>
        /// defines Data Set Field ID 
        /// </summary>
        public string DataSetFieldId
        {
            get { return m_dataSetFieldId; }
            set { m_dataSetFieldId = value; }
        }

        /// <summary>
        /// defines DataTypesCollection  of type DataItemBinding
        /// </summary>
        public ObservableCollection< DataItemBinding > DataTypesCollection
        {
            get { return m_dataTypesCollection; }
            set
            {
                m_dataTypesCollection = value;
                OnPropertyChanged( "DataTypesCollection" );
            }
        }

        /// <summary>
        /// defines valuerankcollection of type DataIteBinding
        /// </summary>
        public ObservableCollection< DataItemBinding > ValuerankCollection
        {
            get { return m_valuerankCollection; }
            set
            {
                m_valuerankCollection = value;
                OnPropertyChanged( "ValuerankCollection" );
            }
        }
        //public KeyValuePairDefinition KeyValuePairItem
        //{
        //    get { return m_KeyValuePairDefinition; }
        //    set
        //    {
        //        m_KeyValuePairDefinition = value;
        //        OnPropertyChanged("KeyValuePairItem");
        //    }
        //}
        #endregion
    }

    /// <summary>
    /// defines Data item binding definition
    /// </summary>
    public class DataItemBinding
    {
        #region Private Fields

        private string m_value;
        private string m_displayname;
        private string m_name;

        #endregion

        #region Public Properties
        /// <summary>
        /// defines name of target definition
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set { m_name = value; }
        }

        /// <summary>
        /// defines display name of target definition 
        /// </summary>
        public string DisplayName
        {
            get { return m_displayname; }
            set { m_displayname = value; }
        }

        /// <summary>
        /// defines value of target definition
        /// </summary>
        public string Value
        {
            get { return m_value; }
            set { m_value = value; }
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