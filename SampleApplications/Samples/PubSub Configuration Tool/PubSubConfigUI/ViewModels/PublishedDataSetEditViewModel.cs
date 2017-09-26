
/* ========================================================================
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

using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for Published Data set edit view
    /// </summary>
    public class PublishedDataSetEditViewModel : BaseViewModel
    {
        #region Private Fields 

        private string m_dataSetNodeId = string.Empty;
        private PublishedDataSetDefinition m_definition;
        private uint m_maxVersion;
        private uint m_minVersion;

        #endregion

        #region Constructors

        public PublishedDataSetEditViewModel( )
        {
            Definition = new PublishedDataSetDefinition( );
            //PublishedDataSetCollection = new ObservableCollection<PublishedDataSetBase>();
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// defines definition of Published data set
        /// </summary>
        public PublishedDataSetDefinition Definition
        {
            get { return m_definition; }
            set
            {
                m_definition = value;
                OnPropertyChanged( "Definition" );
            }
        }

        /// <summary>
        /// defines data set node ID
        /// </summary>
        public string DataSetNodeId
        {
            get { return m_dataSetNodeId; }
            set
            {
                m_dataSetNodeId = value;
                OnPropertyChanged( "DataSetNodeId" );
            }
        }

        /// <summary>
        /// defines minimum version of published data set
        /// </summary>
        public uint MinVersion
        {
            get { return m_minVersion; }
            set
            {
                m_minVersion = value;
                OnPropertyChanged( "MinVersion" );
            }
        }

        /// <summary>
        /// defines maixmum version of published data set
        /// </summary>
        public uint MaxVersion
        {
            get { return m_maxVersion; }
            set
            {
                m_maxVersion = value;
                OnPropertyChanged( "MaxVersion" );
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Initialiser method for PublishedDataSetEdit
        /// </summary>
        public void Initialize( )
        {
            DataSetNodeId = Definition.DataSetNodeId;
            MinVersion = Definition.ConfigurationVersionDataType.MinorVersion;
            MaxVersion = Definition.ConfigurationVersionDataType.MajorVersion;
        }

        #endregion
    }
}