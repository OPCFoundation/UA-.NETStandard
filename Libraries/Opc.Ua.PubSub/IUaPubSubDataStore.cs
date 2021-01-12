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

namespace Opc.Ua.PubSub
{
    /// <summary>
    /// Interface for a data store component responsible to store/get data to and from Ua publisher
    /// </summary>
    public interface IUaPubSubDataStore
    {
        /// <summary>
        /// Write a DataValue to the DataStore. 
        /// The DataValue is identified by node NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier for DataValue that will be stored</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/>.</param>
        /// <param name="dataValue">Default value is null. </param>
        void WritePublishedDataItem(NodeId nodeId, uint attributeId = Attributes.Value, DataValue dataValue = null);

        /// <summary>
        /// Read the DataValue stored for a specific NodeId and Attribute.
        /// </summary>
        /// <param name="nodeId">NodeId identifier of node</param>
        /// <param name="attributeId">Default value is <see cref="Attributes.Value"/></param>
        /// <returns></returns>
        DataValue ReadPublishedDataItem(NodeId nodeId, uint attributeId = Attributes.Value);
    }
}
