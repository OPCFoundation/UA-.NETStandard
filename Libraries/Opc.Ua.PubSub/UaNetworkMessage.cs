/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Abstract class for an UA network message
    /// </summary>
    public abstract class UaNetworkMessage
    {
        private ushort m_dataSetWriterId;

        #region Public Events

        /// <summary>
        /// The Default event for an error encountered during decoding the dataset messages
        /// </summary>
        public event EventHandler<DataSetDecodeErrorEventArgs> DataSetDecodeErrorOccurred;

        #endregion

        #region Protected Fields
        /// <summary>
        /// The DataSetMetaData
        /// </summary>
        protected DataSetMetaDataType m_metadata;

        /// <summary>
        /// List of DataSet messages
        /// </summary>
        protected List<UaDataSetMessage> m_uaDataSetMessages;
        #endregion

        #region Constructor
        /// <summary>
        /// Create instance of <see cref="UaNetworkMessage"/>.
        /// </summary>
        /// <param name="writerGroupConfiguration">The <see cref="WriterGroupDataType"/> confguration object that produced this message.</param>
        /// <param name="uaDataSetMessages">The containing data set messages.</param>
        protected UaNetworkMessage(WriterGroupDataType writerGroupConfiguration, List<UaDataSetMessage> uaDataSetMessages)
        {
            WriterGroupConfiguration = writerGroupConfiguration;
            m_uaDataSetMessages = uaDataSetMessages;
            m_metadata = null;
        }

        /// <summary>
        /// Create instance of <see cref="UaNetworkMessage"/>.
        /// </summary>
        protected UaNetworkMessage(WriterGroupDataType writerGroupConfiguration, DataSetMetaDataType metadata)
        {
            WriterGroupConfiguration = writerGroupConfiguration;
            m_uaDataSetMessages = new List<UaDataSetMessage>();
            m_metadata = metadata;
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get and Set WriterGroupId
        /// </summary>
        public UInt16 WriterGroupId { get; set; }

        /// <summary>
        /// Get and Set DataSetWriterId if a single value exists for the message.
        /// </summary>
        public UInt16? DataSetWriterId
        {
            get
            {
                if (m_dataSetWriterId == 0)
                {
                    if (m_uaDataSetMessages != null && m_uaDataSetMessages.Count == 1)
                    {
                        return m_uaDataSetMessages[0].DataSetWriterId;
                    }

                    return null;
                }

                return ((m_dataSetWriterId != 0) ? m_dataSetWriterId : (UInt16?)null);
            }

            set
            {
                m_dataSetWriterId = (value != null) ? value.Value : (ushort)0;
            }
        }
    
        /// <summary>
        /// DataSet messages
        /// </summary>
        public List<UaDataSetMessage> DataSetMessages
        {
            get
            {
                return m_uaDataSetMessages;
            }
        }

        /// <summary>
        /// DataSetMetaData messages
        /// </summary>
        public DataSetMetaDataType DataSetMetaData
        {
            get
            {
                return m_metadata;
            }
        }

        /// <summary>
        /// TRUE if it is a metadata message.
        /// </summary>
        public bool IsMetaDataMessage
        {
            get { return m_metadata != null; }
        }

        /// <summary>
        /// Get the writer group configuration for this network message
        /// </summary>
        internal WriterGroupDataType WriterGroupConfiguration { get; set; }
        #endregion

        #region Public Methods
        /// <summary>
        /// Encodes the object and returns the resulting byte array.
        /// </summary>
        /// <param name="messageContext">The context.</param>
        public abstract byte[] Encode(IServiceMessageContext messageContext);

        /// <summary>
        /// Encodes the object in the specified stream.
        /// </summary>
        /// <param name="messageContext">The context.</param>
        /// <param name="stream">The stream to use.</param>
        public abstract void Encode(IServiceMessageContext messageContext, Stream stream);

        /// <summary>
        /// Decodes the message
        /// </summary>
        /// <param name="messageContext"></param>
        /// <param name="message"></param>
        /// <param name="dataSetReaders"></param>
        public abstract void Decode(IServiceMessageContext messageContext, byte[] message, IList<DataSetReaderDataType> dataSetReaders);
        #endregion

        #region Protected Methods
        /// <summary>
        /// The DataSetDecodeErrorOccurred event handler
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnDataSetDecodeErrorOccurred(DataSetDecodeErrorEventArgs e)
        {
            DataSetDecodeErrorOccurred?.Invoke(this, e);
        }
        #endregion

    }
}
