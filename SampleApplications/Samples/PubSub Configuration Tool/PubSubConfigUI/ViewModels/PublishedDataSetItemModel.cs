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
using System.Linq;
using System.Reflection;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// defines published data item set view
    /// </summary>
    public class PublishedDataItemSetModel : BaseViewModel
    {
        #region Private Fields 

        private string m_attribute = "Value";
        private string m_deadbandType = "0";
        private double m_deadbandValue;
        private PublishedDataSetItemDefinition m_definition;
        private QualifiedNameCollection m_fieldMetaDataProperties = new QualifiedNameCollection( );
        private string m_indexrange = string.Empty;
        private bool m_isEnabled;
        private double m_samplingInterval = -1;
        private string m_substituteValue = "0";

        #endregion

        #region Private Properties

        private string _PublishVariable { get; set; }

        private string _PublishVariableNodeId { get; set; }

        #endregion

        #region Private Methods

        /// <summary>
        /// Method to get List of field info based on type specified.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private List< FieldInfo > GetFields( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.ToList( );
        }

        /// <summary>
        /// Method to get constant List of fieldInfo based on type specified.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private List< FieldInfo > GetConstants( Type type )
        {
            var fieldInfos = type.GetFields( BindingFlags.Public | BindingFlags.Static |
                                             BindingFlags.FlattenHierarchy );

            return fieldInfos.Where( fi => fi.IsLiteral && !fi.IsInitOnly ).ToList( );
        }

        #endregion

        #region Constructors

        public PublishedDataItemSetModel( )
        {
            Definition = new PublishedDataSetItemDefinition( null );
            //PublishedDataSetCollection = new ObservableCollection<PublishedDataSetBase>();
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// defines definition of published data set item
        /// </summary>
        public PublishedDataSetItemDefinition Definition
        {
            get { return m_definition; }
            set
            {
                m_definition = value;
                OnPropertyChanged( "Definition" );
            }
        }

        /// <summary>
        /// defines condition whether item should be enabled or not
        /// </summary>
        public bool IsEnabled
        {
            get { return m_isEnabled; }
            set
            {
                m_isEnabled = value;
                OnPropertyChanged( "IsEnabled" );
            }
        }

        /// <summary>
        /// defines publish variable
        /// </summary>
        public string PublishVariable
        {
            get { return _PublishVariable; }
            set
            {
                _PublishVariable = value;
                OnPropertyChanged( "PublishVariable" );
            }
        }

        /// <summary>
        /// defines publish variable node ID
        /// </summary>
        public string PublishVariableNodeId
        {
            get { return _PublishVariableNodeId; }
            set
            {
                _PublishVariableNodeId = value;
                OnPropertyChanged( "PublishVariableNodeId" );
            }
        }

        /// <summary>
        /// defines attribute of target definition 
        /// </summary>
        public string Attribute
        {
            get { return m_attribute; }
            set
            {
                m_attribute = value;
                OnPropertyChanged( "Attribute" );
            }
        }

        /// <summary>
        /// defines sampling interval for target definition
        /// </summary>
        public double SamplingInterval
        {
            get { return m_samplingInterval; }
            set
            {
                m_samplingInterval = value;
                OnPropertyChanged( "SamplingInterval" );
            }
        }

        /// <summary>
        /// defines deadbandtype for target definition
        /// </summary>
        public string DeadbandType
        {
            get { return m_deadbandType; }
            set
            {
                m_deadbandType = value;
                OnPropertyChanged( "DeadbandType" );
            }
        }

        /// <summary>
        /// defines deadband value for target definition
        /// </summary>
        public double DeadbandValue
        {
            get { return m_deadbandValue; }
            set
            {
                m_deadbandValue = value;
                OnPropertyChanged( "DeadbandValue" );
            }
        }

        /// <summary>
        /// defines index range for target definition
        /// </summary>
        public string Indexrange
        {
            get { return m_indexrange; }
            set
            {
                m_indexrange = value;
                OnPropertyChanged( "Indexrange" );
            }
        }

        /// <summary>
        /// defines substitute value of target definition
        /// </summary>
        public string SubstituteValue
        {
            get { return m_substituteValue; }
            set
            {
                m_substituteValue = value;
                OnPropertyChanged( "SubstituteValue" );
            }
        }

        /// <summary>
        /// defines properties of target definition
        /// </summary>
        public QualifiedNameCollection FieldMetaDataProperties
        {
            get { return m_fieldMetaDataProperties; }
            set
            {
                m_fieldMetaDataProperties = value;
                OnPropertyChanged( "FieldMetaDataProperties" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Initialiser method for PubishedDataItemSet
        /// </summary>
        public void Initialize( )
        {
            PublishVariable = Definition.Name;
            PublishVariableNodeId = Definition.PublishVariableNodeId.ToString( );
            Attribute = GetFields( typeof( Attributes ) )
            .Where( i => Convert.ToUInt16( i.GetValue( i ) ) == Definition.Attribute ).FirstOrDefault( ).Name;
            SamplingInterval = Definition.SamplingInterval;
            DeadbandType = GetConstants( typeof( DeadbandType ) )
            .Where( i => Convert.ToInt16( i.GetValue( i ) ) == Definition.DeadbandType ).FirstOrDefault( ).Name;
            DeadbandValue = Definition.DeadbandValue;
            Indexrange = Definition.Indexrange;
            SubstituteValue = Definition.SubstituteValue.Value != null
                ? Convert.ToString( Definition.SubstituteValue.Value ) : string.Empty;
            FieldMetaDataProperties = Definition.FieldMetaDataProperties;
        }

        #endregion
    }
}