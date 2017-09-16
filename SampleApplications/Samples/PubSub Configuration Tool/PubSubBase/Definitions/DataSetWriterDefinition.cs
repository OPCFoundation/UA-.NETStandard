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

namespace PubSubBase.Definitions
{
    /// <summary>
    /// Defines DataSet Writer for publisher 
    /// </summary>
    public class DataSetWriterDefinition : PubSubConfiguationBase
    {
        #region Private Fields
        private string m_dataSetWriterName;
        private NodeId m_publisherDataSetNodeId = new NodeId("0", 1);
        private string m_publisherDataSetId;
        private int m_keyFrameCount;
        private int m_dataSetWriterId;
        private string m_queueName;
        private string m_metadataQueueName;
        private int m_metadataUpdateTime;
        private int m_maxMessageSize;
        private NodeId m_writerNodeId;
        private int m_revisedKeyFrameCount;
        private int m_revisedMaxMessageSize;
        private int m_dataSetContentMask;

        #endregion

        #region Public Properties
        /// <summary>
        /// defines name of DataSetWriter Name
        /// </summary>
        public string DataSetWriterName
        {
            get
            {
                return m_dataSetWriterName;
            }
            set
            {
                Name = m_dataSetWriterName = value;
                OnPropertyChanged("DataSetWriterName");
            }
        }

        /// <summary>
        /// Defines Pulisher DataSet Node ID
        /// </summary>
        public NodeId PublisherDataSetNodeId
        {
            get
            {
                return m_publisherDataSetNodeId;
            }
            set
            {
                m_publisherDataSetNodeId = value;
                OnPropertyChanged("PublisherDataSetNodeId");
            }
        }

        /// <summary>
        /// Defines Publisher DataSet ID
        /// </summary>
        public string PublisherDataSetId
        {
            get
            {
                return m_publisherDataSetId;
            }
            set
            {
                m_publisherDataSetId = value;
                OnPropertyChanged("PublisherDataSetId");
            }
        }

        /// <summary>
        /// Defines the KeyFrame Count of DataSet Writer
        /// </summary>
        public int KeyFrameCount
        {
            get
            {
                return m_keyFrameCount;
            }
            set
            {
                m_keyFrameCount = value;
                OnPropertyChanged("KeyFrameCount");
            }
        }

        /// <summary>
        /// Defines the DataSet Writer ID
        /// </summary>
        public int DataSetWriterId
        {
            get
            {
                return m_dataSetWriterId;
            }
            set
            {
                m_dataSetWriterId = value;
                OnPropertyChanged("DataSetWriterId");
            }
        }

        /// <summary>
        /// Defines the  Data Writer Queue Name
        /// </summary>
        public string QueueName
        {
            get
            {
                return m_queueName;
            }
            set
            {
                m_queueName = value;
                OnPropertyChanged("QueueName");
            }
        }
        /// <summary>
        /// Defines the MetaDataQueue Name
        /// </summary>
        public string MetadataQueueName
        {
            get
            {
                return m_metadataQueueName;
            }
            set
            {
                m_metadataQueueName = value;
                OnPropertyChanged("MetadataQueueName");
            }
        }

        /// <summary>
        /// Defines the MetaData update time 
        /// </summary>
        public int MetadataUpdataTime
        {
            get
            {
                return m_metadataUpdateTime;
            }
            set
            {
                m_metadataUpdateTime = value;
                OnPropertyChanged("MetadataUpdataTime");
            }
        }

        /// <summary>
        /// defines the max size of the messgae queue
        /// </summary>
        public int MaxMessageSize
        {
            get
            {
                return m_maxMessageSize;
            }
            set
            {
                m_maxMessageSize = value;
                OnPropertyChanged("MaxMessageSize");
            }
        }

        /// <summary>
        /// Defines the Writer Node ID
        /// </summary>
        public NodeId WriterNodeId
        {
            get
            {
                return m_writerNodeId;

            }
            set
            {
                m_writerNodeId = value;
                OnPropertyChanged("WriterNodeId");
            }
        }

        /// <summary>
        /// Defines the revised key frame count
        /// </summary>
        public int RevisedKeyFrameCount
        {
            get
            {
                return m_revisedKeyFrameCount;

            }
            set
            {
                m_revisedKeyFrameCount = value;
                OnPropertyChanged("RevisedKeyFrameCount");
            }
        }
        /// <summary>
        /// Defines Revised maximum message size code
        /// </summary>
        public int RevisedMaxMessageSize
        {
            get
            {
                return m_revisedMaxMessageSize;

            }
            set
            {
                m_revisedMaxMessageSize = value;
                OnPropertyChanged("RevisedMaxMessageSize");
            }
        }
        /// <summary>
        /// defines the DataSet Content Mask
        /// </summary>
        public int DataSetContentMask
        {
            get { return m_dataSetContentMask; }
            set
            {
                m_dataSetContentMask = value;
                OnPropertyChanged("DataSetContentMask");
            }
        }

        #endregion

    }
}
