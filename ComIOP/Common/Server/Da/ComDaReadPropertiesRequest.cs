/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Specifies the parameters used to create a DA group item.
    /// </summary>
    public class ComDaReadPropertiesRequest
    {
        #region Public Properties
        /// <summary>
        /// Gets or sets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
            set { m_itemId = value; }
        }

        /// <summary>
        /// Gets or sets the values.
        /// </summary>
        /// <value>The values.</value>
        public DaValue[] Values
        {
            get { return m_values; }
            set { m_values = value; }
        }

        /// <summary>
        /// Gets or sets the indexes used to look up the results.
        /// </summary>
        /// <value>The indexes.</value>
        public int[] Indexes
        {
            get { return m_indexes; }
            set { m_indexes = value; }
        }

        /// <summary>
        /// Gets or sets the error associated with the operation.
        /// </summary>
        /// <value>The error.</value>
        public int Error
        {
            get { return m_error; }
            set { m_error = value; }
        }
        #endregion

        #region Private Fields
        private string m_itemId;
        private DaValue[] m_values;
        private int[] m_indexes;
        private int m_error;
        #endregion
    }
}
