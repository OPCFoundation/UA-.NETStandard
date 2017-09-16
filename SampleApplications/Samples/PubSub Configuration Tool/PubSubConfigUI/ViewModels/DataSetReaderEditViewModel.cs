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
using PubSubBase.Definitions;

namespace PubSubConfigurationUI.ViewModels
{
    /// <summary>
    /// View model definition for Data set reader
    /// </summary>
    public class DataSetReaderEditViewModel : BaseViewModel
    {
        #region Private Fields

        private string m_connectionPublisherId;
        private int m_dataSetContentMask;
        private DataSetMetaDataType m_dataSetMetaDataType = new DataSetMetaDataType();
        private string m_dataSetReaderName;
        private int m_dataSetWriterId;
        private double m_messageReceiveTimeOut;
        private int m_networkMessageContentMask;
        private object m_publisherId;
        private double m_publishingInterval;

        #endregion

        #region Public Properties
        /// <summary>
        /// defines data set reader name
        /// </summary>
        public string DataSetReaderName
        {
            get { return m_dataSetReaderName; }
            set
            {
                m_dataSetReaderName = value;
                OnPropertyChanged("DataSetReaderName");
            }
        }

        /// <summary>
        /// defines publisher id for Data ser reader
        /// </summary>
        public object PublisherId
        {
            get { return m_publisherId; }
            set
            {
                m_publisherId = value;
                OnPropertyChanged("PublisherId");
            }
        }

        /// <summary>
        /// defines connection publisher id for data set reader
        /// </summary>
        public string ConnectionPublisherId
        {
            get
            {
                if (PublisherId != null) PublisherId.ToString();
                return m_connectionPublisherId;
            }
            set
            {
                m_connectionPublisherId = value;
                OnPropertyChanged("ConnectionPublisherId");
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
                OnPropertyChanged("DataSetWriterId");
            }
        }

        /// <summary>
        /// defines dataSet meta data type
        /// </summary>
        public DataSetMetaDataType DataSetMetaDataType
        {
            get { return m_dataSetMetaDataType; }
            set
            {
                m_dataSetMetaDataType = value;
                OnPropertyChanged("DataSetMetaDataType");
            }
        }

        /// <summary>
        /// defines message receive timeout
        /// </summary>
        public double MessageReceiveTimeOut
        {
            get { return m_messageReceiveTimeOut; }
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
            get { return m_publishingInterval; }
            set
            {
                m_publishingInterval = value;
                OnPropertyChanged("PublishingInterval");
            }
        }

        #endregion

        #region Public Methods
        /// <summary>
        /// Initialise method
        /// </summary>
        public void Initialize()
        {
            DataSetReaderName = ReaderDefinition.DataSetReaderName;
            PublisherId = ReaderDefinition.PublisherId;
            ConnectionPublisherId = PublisherId.ToString();
            DataSetWriterId = ReaderDefinition.DataSetWriterId;
            DataSetMetaDataType = ReaderDefinition.DataSetMetaDataType;
            MessageReceiveTimeOut = ReaderDefinition.MessageReceiveTimeOut;
            DataSetContentMask = ReaderDefinition.DataSetContentMask;
            PublishingInterval = ReaderDefinition.PublishingInterval;
            NetworkMessageContentMask = ReaderDefinition.NetworkMessageContentMask;
        }

        #endregion

        #region Public Fields
        public DataSetReaderDefinition ReaderDefinition;
        #endregion
    }
}