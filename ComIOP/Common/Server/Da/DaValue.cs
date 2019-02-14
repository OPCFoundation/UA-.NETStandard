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

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Stores information an element in the DA server address space.
    /// </summary>
    public class DaValue
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="DaValue"/> class.
        /// </summary>
        public DaValue()
        {
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the value.
        /// </summary>
        /// <value>The value.</value>
        public object Value
        {
            get { return m_value; }
            set { m_value = value; }
        }

        /// <summary>
        /// Gets or sets the timestamp.
        /// </summary>
        /// <value>The timestamp.</value>
        public DateTime Timestamp
        {
            get { return m_timestamp; }
            set { m_timestamp = value; }
        }

        /// <summary>
        /// Gets or sets the quality.
        /// </summary>
        /// <value>The quality.</value>
        public short Quality
        {
            get { return (short)(m_quality & 0xFFFF); }
            set { m_quality = Utils.ToUInt32((int)value); }
        }

        /// <summary>
        /// Gets or sets the quality.
        /// </summary>
        /// <value>The quality.</value>
        public uint HdaQuality
        {
            get { return m_quality; }
            set { m_quality = value; }
        }

        /// <summary>
        /// Gets or sets the COM error.
        /// </summary>
        /// <value>The COM error.</value>
        public int Error
        {
            get { return m_error; }
            set { m_error = value; }
        }

        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <typeparam name="T">The type of value to return.</typeparam>
        /// <returns>The value if no error and a valid value exists. The default value for the type otherwise.</returns>
        public T GetValue<T>()
        {
            if (m_error < 0)
            {
                return default(T);
            }

            if (typeof(T).IsInstanceOfType(m_value))
            {
                return (T)m_value;
            }

            return default(T);
        }
        #endregion

        #region Private Fields
        private object m_value;
        private uint m_quality;
        private DateTime m_timestamp;
        private int m_error;
        #endregion
    }

    /// <summary>
    /// Stores information an element in the DA server address space.
    /// </summary>
    public class DaCacheValue : DaValue
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="DaValue"/> class.
        /// </summary>
        public DaCacheValue()
        {
        }
        #endregion

        #region Public Members
        /// <summary>
        /// Gets or sets the cache timestamp.
        /// </summary>
        /// <value>The cache timestamp.</value>
        public DateTime CacheTimestamp
        {
            get { return m_cacheTimestamp; }
            set { m_cacheTimestamp = value; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the value has changed since the last update.
        /// </summary>
        /// <value><c>true</c> if the value has changed; otherwise, <c>false</c>.</value>
        public bool Changed
        {
            get { return m_changed; }
            set { m_changed = value; }
        }

        /// <summary>
        /// Gets or sets the next entry in the cache.
        /// </summary>
        /// <value>The next entry.</value>
        public DaCacheValue NextEntry
        {
            get { return m_nextEntry; }
            set { m_nextEntry = value; }
        }

        /// <summary>
        /// Gets the latest value in the cache.
        /// </summary>
        /// <param name="value">The value.</param>
        public void GetLatest(DaValue value)
        {
            value.Value = this.Value;
            value.Quality = this.Quality;
            value.Timestamp = this.Timestamp;
            value.Error = this.Error;
        }
        #endregion

        #region Private Fields
        private DateTime m_cacheTimestamp;
        private DaCacheValue m_nextEntry;
        private bool m_changed;
        #endregion
    }
}
