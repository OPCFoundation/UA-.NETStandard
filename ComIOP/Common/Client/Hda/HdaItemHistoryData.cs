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
using System.Collections.Generic;
using Opc.Ua;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Stores the history of an HDA item.
    /// </summary>
    public class HdaItemHistoryData
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="HdaItemHistoryData"/> class.
        /// </summary>
        public HdaItemHistoryData()
        {
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the server handle.
        /// </summary>
        /// <value>The server handle.</value>
        public int ServerHandle
        {
            get { return m_serverHandle; }
            set { m_serverHandle = value; }
        }

        /// <summary>
        /// Gets or sets the error.
        /// </summary>
        /// <value>The error.</value>
        public int Error
        {
            get { return m_error; }
            set { m_error = value; }
        }

        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public object[] Values
        {
            get { return m_values; }
            set { m_values = value; }
        }

        /// <summary>
        /// Gets or sets the qualities.
        /// </summary>
        /// <value>The qualities.</value>
        public int[] Qualities
        {
            get { return m_qualities; }
            set { m_qualities = value; }
        }

        /// <summary>
        /// Gets or sets the timestamp.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime[] Timestamps
        {
            get { return m_timestamps; }
            set { m_timestamps = value; }
        }

        /// <summary>
        /// Gets or sets the modifications.
        /// </summary>
        /// <value>The modifications.</value>
        public ModificationInfo[] Modifications
        {
            get { return m_modifications; }
            set { m_modifications = value; }
        }
        #endregion

        #region Private Fields
        private int m_serverHandle;
        private object[] m_values;
        private int[] m_qualities;
        private DateTime[] m_timestamps;
        private ModificationInfo[] m_modifications;
        private int m_error;
        #endregion
    }
}
