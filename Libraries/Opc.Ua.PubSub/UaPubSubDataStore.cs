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

using System;
using System.Collections.Generic;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// DataStore is a repository where Publisher applications will push data values for nodes + attributes published in data sets
    /// </summary>
    public class UaPubSubDataStore : IUaPubSubDataStore
    {
        #region Private Fields
        private readonly object m_lock = new object();
        private Dictionary<NodeId, Dictionary<uint, DataValue>> m_store;
        #endregion

        #region Constructor
        /// <summary>
        /// Create new instance of <see cref="UaPubSubDataStore"/>
        /// </summary>
        public UaPubSubDataStore()
        {
            m_store = new Dictionary<NodeId, Dictionary<uint, DataValue>>();
        }
        #endregion  

        #region Read/Write Public Methods
        /// <summary>
        /// Write a DataValue to the DataStore. 
        /// The DataValue is identified by node NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier for DataValue that will be stored</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/>.</param>
        /// <param name="dataValue">Default value is null. </param>
        public void WritePublishedDataItem(NodeId nodeId, uint attributeId = Attributes.Value, DataValue dataValue = null)
        {
            if (nodeId == null)
            {
                throw new ArgumentException(nameof(nodeId));
            }
            if (attributeId == 0)
            {
                attributeId = Attributes.Value;
            }
            if (!Attributes.IsValid(attributeId))
            {
                throw new ArgumentException(nameof(attributeId));
            }
            //copy instance of dataValue to be stored
            if (dataValue != null)
            {
                dataValue = Utils.Clone(dataValue) as DataValue;
            }
            lock (m_lock)
            {
                if (m_store.ContainsKey(nodeId))
                {
                    m_store[nodeId][attributeId] = dataValue;
                }
                else
                {
                    Dictionary<uint, DataValue> dictionary = new Dictionary<uint, DataValue>();
                    dictionary.Add(attributeId, dataValue);
                    m_store.Add(nodeId, dictionary);
                }
            }            
        }

        /// <summary>
        /// Read the DataValue stored for a specific NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier of node</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/></param>
        /// <returns></returns>
        public DataValue ReadPublishedDataItem(NodeId nodeId, uint attributeId = Attributes.Value)
        {
            if (nodeId == null)
            {
                throw new ArgumentException(nameof(nodeId));
            }
            if (attributeId == 0)
            {
                attributeId = Attributes.Value;
            }
            if (!Attributes.IsValid(attributeId))
            {
                throw new ArgumentException(nameof(attributeId));
            }
            lock (m_lock)
            {
                if (m_store.ContainsKey(nodeId))
                {
                    if (m_store[nodeId].ContainsKey(attributeId))
                    {
                        return m_store[nodeId][attributeId];
                    }
                }                
            }
            return null;
        }
        #endregion
    }
}
