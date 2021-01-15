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

namespace Opc.Ua.PubSub.PublishedData
{
    /// <summary>
    /// Entity that holds DataSet structure that is published/received bu the PubSub
    /// </summary>
    public class DataSet
    {
        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="DataSet"/>
        /// </summary>
        /// <param name="name"></param>
        public DataSet(string name = null)
        {
            Name = name;
        }
        #endregion

        #region Properties
        /// <summary>
        /// Get/Set data set name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Get/Set the DataSetWriterId that produced this DataSet
        /// </summary>
        public int DataSetWriterId { get; set; }

        /// <summary>
        /// Gets SequenceNumber - a strictly monotonically increasing sequence number assigned by the publisher to each DataSetMessage sent.
        /// </summary>
        public uint SequenceNumber { get; internal set; }

        /// <summary>
        /// Get/Set data set fields for this data set
        /// </summary>
        public Field[] Fields { get; set; }
        #endregion
    }
}
