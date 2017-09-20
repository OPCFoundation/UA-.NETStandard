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

using System.ComponentModel;
using Opc.Ua;
using PubSubBase.Definitions;
using PubSubConfigurationUI.Views;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for field target variable edit view
    /// </summary>
    public class FieldTargetVariableEditViewModel : BaseViewModel
    {
        #region Private Fields 

        private uint m_attributeId = 13;
        private string m_dataSetFieldId = string.Empty;
        private FieldTargetVariableDefinition m_definition;
        private string m_name = string.Empty;
        private object m_overrideValue;
        private int m_overrideValueHandling = 1;
        private string m_receiverIndexRange = string.Empty;
        private NodeId m_targetFieldNodeId = string.Empty;
        private string m_targetNodeId = string.Empty;
        private string m_writeIndexRange = string.Empty;

        #endregion

        #region Private Methods

        private void GetDataSetFieldIdDialog_Closing( object sender, CancelEventArgs e )
        {
            var getDataSetFieldIdDialog = sender as GetDataSetFieldIdDialog;
            if (getDataSetFieldIdDialog._isApplied )
                if (getDataSetFieldIdDialog.FieldIdGrid.SelectedItem != null )
                {
                    var metadata = getDataSetFieldIdDialog.FieldIdGrid.SelectedItem as FieldMetaDataDefinition;
                    DataSetFieldId = metadata.DataSetFieldId;
                }
        }

        #endregion

        #region Constructors

        public FieldTargetVariableEditViewModel( )
        {
            Definition = new FieldTargetVariableDefinition( );
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// defines data set meta data type
        /// </summary>
        public DataSetMetaDataType DataSetMetaDataType { get; set; }

        /// <summary>
        /// defines definition of type field target variable
        /// </summary>
        public FieldTargetVariableDefinition Definition
        {
            get { return m_definition; }
            set
            {
                m_definition = value;
                OnPropertyChanged( "Definition" );
            }
        }

        /// <summary>
        /// defines name of target definition
        /// </summary>
        public string Name
        {
            get { return m_name; }
            set
            {
                m_name = value;
                OnPropertyChanged( "Name" );
            }
        }

        /// <summary>
        /// defines data set field ID
        /// </summary>
        public string DataSetFieldId
        {
            get { return m_dataSetFieldId; }
            set
            {
                m_dataSetFieldId = value;
                OnPropertyChanged( "DataSetFieldId" );
            }
        }

        /// <summary>
        /// defines receiver index range value
        /// </summary>
        public string ReceiverIndexRange
        {
            get { return m_receiverIndexRange; }
            set
            {
                m_receiverIndexRange = value;
                OnPropertyChanged( "ReceiverIndexRange" );
            }
        }

        /// <summary>
        /// defines target node ID
        /// </summary>
        public string TargetNodeId
        {
            get { return m_targetNodeId; }
            set
            {
                m_targetNodeId = value;
                OnPropertyChanged( "TargetNodeId" );
            }
        }

        /// <summary>
        /// defines Target Field Node ID
        /// </summary>
        public NodeId TargetFieldNodeId
        {
            get { return m_targetFieldNodeId; }
            set
            {
                m_targetFieldNodeId = value;
                OnPropertyChanged( "TargetFieldNodeId" );
            }
        }

        /// <summary>
        /// defines attribute ID of target variable
        /// </summary>
        public uint AttributeId
        {
            get { return m_attributeId; }
            set
            {
                m_attributeId = value;
                OnPropertyChanged( "AttributeId" );
            }
        }

        /// <summary>
        /// defines write index range of target definition
        /// </summary>
        public string WriteIndexRange
        {
            get { return m_writeIndexRange; }
            set
            {
                m_writeIndexRange = value;
                OnPropertyChanged( "WriteIndexRange" );
            }
        }

        /// <summary>
        /// defines overridevalue handling of target definition
        /// </summary>
        public int OverrideValueHandling
        {
            get { return m_overrideValueHandling; }
            set
            {
                m_overrideValueHandling = value;
                OnPropertyChanged( "OverrideValueHandling" );
            }
        }

        /// <summary>
        /// defines override value of target definition
        /// </summary>
        public object OverrideValue
        {
            get { return m_overrideValue; }
            set
            {
                m_overrideValue = value;
                OnPropertyChanged( "OverrideValue" );
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Method to get DatasetFieldID
        /// </summary>
        public void GetDataSetFieldId( )
        {
            var getDataSetFieldIdDialog = new GetDataSetFieldIdDialog( DataSetMetaDataType );
            getDataSetFieldIdDialog.Closing += GetDataSetFieldIdDialog_Closing;
            getDataSetFieldIdDialog.ShowInTaskbar = false;
            getDataSetFieldIdDialog.ShowDialog( );
        }
        /// <summary>
        /// Initializer method for FieldTargetVariable
        /// </summary>
        public void Initialize( )
        {
            Name = Definition.Name;
            DataSetFieldId = Definition.DataSetFieldId;
            ReceiverIndexRange = Definition.ReceiverIndexRange;
            TargetNodeId = Definition.TargetNodeId;
            TargetFieldNodeId = Definition.TargetNodeId;
            AttributeId = Definition.AttributeId;
            WriteIndexRange = Definition.WriteIndexRange;
            OverrideValueHandling = Definition.OverrideValueHandling;
            OverrideValue = Definition.OverrideValue;
        }

        #endregion
    }
}