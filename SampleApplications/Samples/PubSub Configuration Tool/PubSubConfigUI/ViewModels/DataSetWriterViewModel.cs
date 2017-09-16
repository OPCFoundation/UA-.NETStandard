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

using System.Windows;
using Opc.Ua;
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// view model for data set writer view
    /// </summary>
    public class DataSetWriterViewModel : BaseViewModel
    {
        #region Private Fields 

        private int m_dataSetWriterId;
        private string m_dataSetWriterName;
        private Visibility m_isAMQP = Visibility.Collapsed;
        private Visibility m_isUADP = Visibility.Visible;
        private int m_keyFrameCount;
        private int m_maxMessageSize;
        private string m_metadataQueueName;
        private int m_metadataUpdateTime;
        private string m_publisherDataSetId;
        private NodeId m_publisherDataSetNodeId;
        private string m_queueName;

        #endregion

        #region Public Properties
        /// <summary>
        /// defines data set writer name
        /// </summary>
        public string DataSetWriterName
        {
            get { return m_dataSetWriterName; }
            set
            {
                m_dataSetWriterName = value;
                OnPropertyChanged( "DataSetWriterName" );
            }
        }

        /// <summary>
        /// defines publisher data set ID
        /// </summary>
        public string PublisherDataSetId
        {
            get { return m_publisherDataSetId; }
            set
            {
                m_publisherDataSetId = value;
                OnPropertyChanged( "PublisherDataSetId" );
            }
        }

        /// <summary>
        /// defines publisher data set node ID
        /// </summary>
        public NodeId PublisherDataSetNodeId
        {
            get { return m_publisherDataSetNodeId; }
            set
            {
                m_publisherDataSetNodeId = value;
                OnPropertyChanged( "PublisherDataSetNodeId" );
            }
        }

        /// <summary>
        /// defines key frame count
        /// </summary>
        public int KeyFrameCount
        {
            get { return m_keyFrameCount; }
            set
            {
                m_keyFrameCount = value;
                OnPropertyChanged( "KeyFrameCount" );
            }
        }

        /// <summary>
        /// defines data set writer ID
        /// </summary>
        public int DataSetWriterId
        {
            get { return m_dataSetWriterId; }
            set
            {
                m_dataSetWriterId = value;
                OnPropertyChanged( "DataSetWriterId" );
            }
        }
        /// <summary>
        /// defines queue name
        /// </summary>
        public string QueueName
        {
            get { return m_queueName; }
            set
            {
                m_queueName = value;
                OnPropertyChanged( "QueueName" );
            }
        }

        /// <summary>
        /// defines metadata queue name
        /// </summary>
        public string MetadataQueueName
        {
            get { return m_metadataQueueName; }
            set
            {
                m_metadataQueueName = value;
                OnPropertyChanged( "MetadataQueueName" );
            }
        }

        /// <summary>
        /// defines meta data updata time
        /// </summary>
        public int MetadataUpdataTime
        {
            get { return m_metadataUpdateTime; }
            set
            {
                m_metadataUpdateTime = value;
                OnPropertyChanged( "MetadataUpdataTime" );
            }
        }

        /// <summary>
        /// defines maximum message size
        /// </summary>
        public int MaxMessageSize
        {
            get { return m_maxMessageSize; }
            set
            {
                m_maxMessageSize = value;
                OnPropertyChanged( "MaxMessageSize" );
            }
        }
        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsUADP
        {
            get { return m_isUADP; }
            set
            {
                m_isUADP = value;
                OnPropertyChanged( "IsUADP" );
            }
        }
        /// <summary>
        /// defines visibility for context menu
        /// </summary>
        public Visibility IsAMQP
        {
            get { return m_isAMQP; }
            set
            {
                m_isAMQP = value;
                OnPropertyChanged( "IsAMQP" );
            }
        }

        #endregion
    }
}