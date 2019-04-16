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
using System.Text;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Specifies the parameters used to create a DA group item.
    /// </summary>
    public class ComDaBrowseElement : IFormattable
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
        /// Gets or sets the name.
        /// </summary>
        /// <value>The name.</value>
        public string BrowseName
        {
            get { return m_browseName; }
            set { m_browseName = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is item.
        /// </summary>
        /// <value><c>true</c> if this instance is an item; otherwise, <c>false</c>.</value>
        public bool IsItem
        {
            get { return m_isItem; }
            set { m_isItem = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is an historical item.
        /// </summary>
        /// <value><c>true</c> if this instance is an historical item; otherwise, <c>false</c>.</value>
        public bool IsHistoricalItem
        {
            get { return m_isHistoricalItem; }
            set { m_isHistoricalItem = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether this instance has children.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance has children; otherwise, <c>false</c>.
        /// </value>
        public bool HasChildren
        {
            get { return m_hasChildren; }
            set { m_hasChildren = value; }
        }
        #endregion
        
        #region IFormattable Members
        /// <summary>
        /// Returns a <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </summary>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Formats the value of the current instance using the specified format.
        /// </summary>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            StringBuilder buffer = new StringBuilder();

            buffer.Append(m_browseName);
            buffer.Append('[');
            buffer.Append(m_itemId);
            buffer.Append(']');

            return buffer.ToString();
        }
        #endregion

        #region Private Fields
        private string m_itemId;
        private string m_browseName;
        private bool m_hasChildren;
        private bool m_isItem;
        private bool m_isHistoricalItem;
        #endregion
    }

    /// <summary>
    /// The filter which selectes the type of elements to return. 
    /// </summary>
    public enum BrowseElementFilter 
    {
        /// <summary>
        /// Returns all elements.
        /// </summary>
        All = 0x1,

        /// <summary>
        /// Returns elements with children.
        /// </summary>
        Branch = 0x2,

        /// <summary>
        /// Returns elements which are items.
        /// </summary>
        Item = 0x3
    }
}
