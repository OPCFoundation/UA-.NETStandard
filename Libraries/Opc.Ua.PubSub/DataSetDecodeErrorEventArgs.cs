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
using Opc.Ua.PubSub.Encoding;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Class that contains data related to DataSetDecodeErrorOccurred event 
    /// </summary>
    public class DataSetDecodeErrorEventArgs : EventArgs
    {
        #region Private members
        private DataSetDecodeErrorReason m_dataSetDecodeErrorReason;
        private UaNetworkMessage m_networkMessage;
        private DataSetReaderDataType m_dataSetReader;
        #endregion

        #region Constructor
        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="dataSetDecodeErrorReason"></param>
        /// <param name="networkMessage"></param>
        /// <param name="dataSetReader"></param>
        public DataSetDecodeErrorEventArgs(DataSetDecodeErrorReason dataSetDecodeErrorReason, UaNetworkMessage networkMessage, DataSetReaderDataType dataSetReader)
        {
            m_dataSetDecodeErrorReason = dataSetDecodeErrorReason;
            m_networkMessage = networkMessage;
            m_dataSetReader = dataSetReader;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The reason for triggering the DataSetDecodeErrorOccurred event
        /// </summary>
        public DataSetDecodeErrorReason DecodeErrorReason
        {
            get
            {
                return m_dataSetDecodeErrorReason;
            }
            set
            {
                m_dataSetDecodeErrorReason = value;
            }
        }

        /// <summary>
        /// The DataSetMessage on which the decoding operated
        /// </summary>
        public UaNetworkMessage UaNetworkMessage
        {
            get
            {
                return m_networkMessage;
            }
            set
            {
                m_networkMessage = value;
            }
        }
        /// <summary>
        /// The DataSetReader used by the decoding operation
        /// </summary>
        public DataSetReaderDataType DataSetReader
        {
            get
            {
                return m_dataSetReader;
            }
            set
            {
                m_dataSetReader = value;
            }
        }
        #endregion
    }
}
