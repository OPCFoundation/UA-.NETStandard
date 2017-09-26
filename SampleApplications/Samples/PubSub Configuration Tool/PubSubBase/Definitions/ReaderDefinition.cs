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
    /// definition of Data set reader
    /// </summary>
    public class DataSetReaderDefinition : PubSubConfiguationBase
    {
        #region Private Fields

        private string m_dataSetReaderName;
        private object m_publisherId;
        private int m_dataSetWriterId;
        private DataSetMetaDataType m_dataSetMetaDataType = new DataSetMetaDataType();
        private double m_messageReceiveTimeOut;
        private int m_dataSetContentMask;
        private int m_networkMessageContentMask;
        private double m_publishingInterval;
        private NodeId m_dataSetReaderNodeId;

        #endregion

        #region Public Properties
        /// <summary>
        /// defines data set reader name 
        /// </summary>
        public string DataSetReaderName
        {
            get
            {
                return m_dataSetReaderName;
            }
            set
            {
                Name = m_dataSetReaderName = value;
                OnPropertyChanged("DataSetReaderName");
            }
        }

        /// <summary>
        /// defines pulisher ID
        /// </summary>
        public object PublisherId
        {
            get
            {
                return m_publisherId;
            }
            set
            {
                m_publisherId = value;
                OnPropertyChanged("PublisherId");
            }
        }

        /// <summary>
        /// defines data set writer id
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
        /// defines data set metadata type
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

        /// <summary>
        /// defines message received timeout of Data set reader
        /// </summary>
        public double MessageReceiveTimeOut
        {
            get
            {
                return m_messageReceiveTimeOut;
            }
            set
            {
                m_messageReceiveTimeOut = value;
                OnPropertyChanged("MessageReceiveTimeOut");
            }
        }

        /// <summary>
        /// defines data set content mask
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

        /// <summary>
        /// defines network message content mask
        /// </summary>
        public int NetworkMessageContentMask
        {
            get { return m_networkMessageContentMask; }
            set
            {
                m_networkMessageContentMask = value;
                OnPropertyChanged("NetworkMessageContentMask");
            }
        }

        /// <summary>
        /// defines publishing interval for data set reader
        /// </summary>
        public double PublishingInterval
        {
            get
            {
                return m_publishingInterval;
            }
            set
            {
                m_publishingInterval = value;
                OnPropertyChanged("PublishingInterval");
            }
        }

        /// <summary>
        /// defines data set reader node ID
        /// </summary>
        public NodeId DataSetReaderNodeId
        {
            get
            {
                return m_dataSetReaderNodeId;
            }
            set
            {
                m_dataSetReaderNodeId = value;
                OnPropertyChanged("DataSetReaderNodeId");
            }
        }

        #endregion
    }
}
