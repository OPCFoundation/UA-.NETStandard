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
        #region Protected Fields
        /// <summary>
        /// list of DataSet messages
        /// </summary>
        protected readonly List<UaDataSetMessage> m_uaDataSetMessages;
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
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get and Set WriterGroupId
        /// </summary>
        public UInt16 WriterGroupId { get; set; }

        /// <summary>
        /// DataSet messages
        /// </summary>
        public ReadOnlyCollection<UaDataSetMessage> DataSetMessages
        {
            get
            {
                return new ReadOnlyCollection<UaDataSetMessage>(m_uaDataSetMessages);
            }
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
        /// <returns></returns>
        public abstract byte[] Encode();

        /// <summary>
        /// Decodes the message 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="dataSetReaders"></param>
        public abstract void Decode(byte[] message, IList<DataSetReaderDataType> dataSetReaders);
        #endregion

        #region Protectd Methods
        /// <summary>
        /// Read the bytes from a Stream
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        protected byte[] ReadBytes(Stream stream)
        {
            stream.Position = 0;
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }
        #endregion
    }
}
