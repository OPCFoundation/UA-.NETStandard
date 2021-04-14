/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

using Opc.Ua.PubSub.PublishedData;
using System;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Base class for a DataSet message implementation
    /// </summary>
    public abstract class UaDataSetMessage
    {
        #region Fields
        // Configuration Major and Major current version (VersionTime)
        /// <summary>
        /// Default value for Configured MetaDataVersion.MajorVersion
        /// </summary>
        protected const UInt32 ConfigMajorVersion = 1;
        /// <summary>
        /// Default value for Configured MetaDataVersion.MinorVersion
        /// </summary>
        protected const UInt32 ConfigMinorVersion = 1;

        private DataSet m_dataSet;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="UaDataSetMessage"/>
        /// </summary>
        public UaDataSetMessage()
        {
            Timestamp = DateTime.UtcNow;
            MetaDataVersion = new ConfigurationVersionDataType() {
                MajorVersion = ConfigMajorVersion,
                MinorVersion = ConfigMinorVersion
            };
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get DataSet
        /// </summary>
        public DataSet DataSet
        {
            get
            {
                return m_dataSet;
            }
            internal set
            {
                m_dataSet = value;
            }
        }

        /// <summary>
        /// Get and Set corresponding DataSetWriterId
        /// </summary>
        public ushort DataSetWriterId { get; set; }

        /// <summary>
        /// Get DataSetFieldContentMask
        /// This DataType defines flags to include DataSet field related information like status and 
        /// timestamp in addition to the value in the DataSetMessage.
        /// </summary>
        public DataSetFieldContentMask FieldContentMask { get; protected set; }

        /// <summary>
        /// The version of the DataSetMetaData which describes the contents of the Payload.
        /// </summary>
        public ConfigurationVersionDataType MetaDataVersion { get; set; }

        /// <summary>
        /// Get and Set SequenceNumber
        /// A strictly monotonically increasing sequence number assigned by the publisher to each DataSetMessage sent.
        /// </summary>
        public uint SequenceNumber { get; set; }

        /// <summary>
        /// Get and Set Timestamp
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Get and Set Status
        /// </summary>
        public StatusCode Status { get; set; }

        #endregion

        #region Methods
        /// <summary>
        /// Set DataSetFieldContentMask 
        /// </summary>
        /// <param name="fieldContentMask">The new <see cref="DataSetFieldContentMask"/> for this dataset</param>
        public abstract void SetFieldContentMask(DataSetFieldContentMask fieldContentMask);
        #endregion
    }
}
