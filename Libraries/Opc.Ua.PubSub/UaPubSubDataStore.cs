/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// DataStore is a repository where Publisher applications will push data values for nodes + attributes published in data sets
    /// </summary>
    public class UaPubSubDataStore : IUaPubSubDataStore
    {
        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, Dictionary<uint, DataValue>> m_store;

        /// <summary>
        /// Create new instance of <see cref="UaPubSubDataStore"/>
        /// </summary>
        public UaPubSubDataStore()
        {
            m_store = [];
        }

        /// <summary>
        /// Write a value to the DataStore.
        /// The value is identified by node NodeId.
        /// </summary>
        /// <param name="nodeId">NodeId identifier for value that will be stored.</param>
        /// <param name="value">The value to be store. The value is NOT copied.</param>
        /// <param name="status">The status associated with the value.</param>
        /// <param name="timestamp">The timestamp associated with the value.</param>
        /// <exception cref="ArgumentException"><paramref name="nodeId"/></exception>
        public void WritePublishedDataItem(
            NodeId nodeId,
            Variant value,
            StatusCode? status = null,
            DateTime? timestamp = null)
        {
            if (nodeId.IsNullNodeId)
            {
                throw new ArgumentException(null, nameof(nodeId));
            }

            lock (m_lock)
            {
                var dv = new DataValue
                {
                    WrappedValue = value,
                    StatusCode = status ?? StatusCodes.Good,
                    SourceTimestamp = timestamp ?? DateTime.UtcNow
                };

                if (!m_store.TryGetValue(nodeId, out Dictionary<uint, DataValue> dictionary))
                {
                    dictionary = [];
                    m_store.Add(nodeId, dictionary);
                }

                dictionary[Attributes.Value] = dv;
            }
        }

        /// <summary>
        /// Write a DataValue to the DataStore.
        /// The DataValue is identified by node NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier for DataValue that will be stored</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/>.</param>
        /// <param name="dataValue">Default value is null. </param>
        /// <exception cref="ArgumentException"><paramref name="nodeId"/></exception>
        public void WritePublishedDataItem(
            NodeId nodeId,
            uint attributeId = Attributes.Value,
            DataValue dataValue = null)
        {
            if (nodeId.IsNullNodeId)
            {
                throw new ArgumentException(null, nameof(nodeId));
            }
            if (attributeId == 0)
            {
                attributeId = Attributes.Value;
            }
            if (!Attributes.IsValid(attributeId))
            {
                throw new ArgumentException(null, nameof(attributeId));
            }
            lock (m_lock)
            {
                if (m_store.TryGetValue(nodeId, out Dictionary<uint, DataValue> value))
                {
                    value[attributeId] = dataValue;
                }
                else
                {
                    var dictionary = new Dictionary<uint, DataValue> { { attributeId, dataValue } };
                    m_store.Add(nodeId, dictionary);
                }
            }
        }

        /// <summary>
        /// Read the DataValue stored for a specific NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier of node</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/></param>
        /// <exception cref="ArgumentException"><paramref name="nodeId"/></exception>
        public DataValue ReadPublishedDataItem(NodeId nodeId, uint attributeId = Attributes.Value)
        {
            // todo find out why the deltaFrame parameter is not used
            if (nodeId.IsNullNodeId)
            {
                throw new ArgumentException(null, nameof(nodeId));
            }
            if (attributeId == 0)
            {
                attributeId = Attributes.Value;
            }
            if (!Attributes.IsValid(attributeId))
            {
                throw new ArgumentException(null, nameof(attributeId));
            }
            lock (m_lock)
            {
                if (m_store.TryGetValue(nodeId, out Dictionary<uint, DataValue> dictionary) &&
                    dictionary.TryGetValue(attributeId, out DataValue value))
                {
                    return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Updates the metadata.
        /// </summary>
        public void UpdateMetaData(PublishedDataSetDataType publishedDataSet)
        {
        }
    }
}
