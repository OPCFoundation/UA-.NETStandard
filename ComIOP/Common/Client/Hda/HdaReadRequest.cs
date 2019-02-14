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
using System.Text;
using System.Threading;
using OpcRcw.Comn;
using OpcRcw.Hda;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Stores the parameters and results for a COM DA read operation.
    /// </summary>
    public class HdaReadRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        public HdaReadRequest(string itemId)
        {
            m_itemId = itemId;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the item id to read.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
        }

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
        /// Gets the attribute ids to read.
        /// </summary>
        /// <value>The attribute ids.</value>
        public List<uint> AttributeIds
        {
            get { return m_attributeIds; }
        }

        /// <summary>
        /// Gets or sets the attribute values returned from the server.
        /// </summary>
        /// <value>The attribute values.</value>
        public HdaAttributeValue[] AttributeValues
        {
            get { return m_attributeValues; }
            set { m_attributeValues = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Sets the error for all attributes.
        /// </summary>
        /// <param name="error">The error.</param>
        public void SetError(int error)
        {
            if (m_attributeIds != null)
            {
                m_attributeValues = new HdaAttributeValue[m_attributeIds.Count];

                for (int ii = 0; ii < m_attributeValues.Length; ii++)
                {
                    m_attributeValues[ii] = new HdaAttributeValue();
                    m_attributeValues[ii].AttributeId = m_attributeIds[ii];
                    m_attributeValues[ii].Error = error;
                }
            }
        }

        /// <summary>
        /// Adds the attribute to the request.
        /// </summary>
        /// <param name="attributeIds">The attribute ids.</param>
        public void AddAttribute(params uint[] attributeIds)
        {
            if (m_attributeIds == null)
            {
                m_attributeIds = new List<uint>();
            }

            if (attributeIds != null)
            {
                for (int ii = 0; ii < attributeIds.Length; ii++)
                {
                    bool found = false;

                    for (int jj = 0; jj < AttributeIds.Count; jj++)
                    {
                        if (AttributeIds[jj] == attributeIds[ii])
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        AttributeIds.Add(attributeIds[ii]);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the attribute value.
        /// </summary>
        /// <typeparam name="T">The expected type for the attribute value.</typeparam>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value to update.</param>
        /// <param name="isValue">if set to <c>true</c> if the attribute is required for the value attribute of a node.</param>
        /// <returns>The value cast to the type T.</returns>
        /// <remarks>
        /// This method sets the StatusCode in the DataValue if an error occurs and returns default(T).
        /// The DataValue.Value attribute is set to the value cast to type T.
        /// </remarks>
        public T GetAttributeValue<T>(uint attributeId, DataValue value, bool isValue)
        {
            value.Value = null;

            // find the attribute value.
            HdaAttributeValue result = GetAttributeValue(attributeId);

            if (result == null)
            {
                value.StatusCode = StatusCodes.BadOutOfService;
                return default(T);
            }

            // set the appropriate error code if no value found.
            if (result == null || result.Error < 0 || result.Error == ResultIds.S_NODATA)
            {
                value.StatusCode = StatusCodes.BadNotFound;
                return default(T);
            }

            // update the source timestamp if a value is being read.
            if (isValue)
            {
                value.SourceTimestamp = result.Timestamp;
            }

            // check type conversion error.
            if (!typeof(T).IsInstanceOfType(result.Value))
            {
                value.StatusCode = StatusCodes.BadTypeMismatch;
                return default(T);
            }

            // save the value and return the result.
            value.Value = (T)result.Value;
            return (T)result.Value;
        }

        /// <summary>
        /// Gets the result for the read operayoin.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="item">The item.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="value">The value.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        /// <returns></returns>
        public ServiceResult GetResult(
            ISystemContext context,
            HdaItemState item,
            ReadValueId nodeToRead,
            DataValue value,
            DiagnosticsMasks diagnosticsMasks)
        {
            switch (nodeToRead.AttributeId)
            {
                case Attributes.Description:
                {
                    string description = this.GetAttributeValue<string>(Constants.OPCHDA_DESCRIPTION, value, false);

                    if (StatusCode.IsGood(value.StatusCode))
                    {
                        value.Value = new LocalizedText(description);
                    }

                    break;
                }

                case Attributes.DataType:
                {
                    short datatype = this.GetAttributeValue<short>(Constants.OPCHDA_DATA_TYPE, value, false);

                    if (StatusCode.IsGood(value.StatusCode))
                    {
                        value.Value = ComUtils.GetDataTypeId(datatype);
                    }
                    else
                    {
                        value.Value = DataTypeIds.BaseDataType;
                        value.StatusCode = StatusCodes.GoodLocalOverride;
                    }

                    break;
                }

                case Attributes.ValueRank:
                {
                    short datatype = this.GetAttributeValue<short>(Constants.OPCHDA_DATA_TYPE, value, false);

                    if (StatusCode.IsGood(value.StatusCode))
                    {
                        value.Value = ComUtils.GetValueRank(datatype);
                    }
                    else
                    {
                        value.Value = ValueRanks.Any;
                        value.StatusCode = StatusCodes.GoodLocalOverride;
                    }

                    break;
                }

                case Attributes.Historizing:
                {
                    bool archiving = this.GetAttributeValue<bool>(Constants.OPCHDA_ARCHIVING, value, false);

                    if (StatusCode.IsGood(value.StatusCode))
                    {
                        value.Value = archiving;
                    }
                    else
                    {
                        value.Value = false;
                        value.StatusCode = StatusCodes.GoodLocalOverride;
                    }

                    break;
                }

                default:
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }
            }

            // check if the attribute value is missing.
            if (value.StatusCode == StatusCodes.BadNotFound)
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            return ApplyIndexRangeAndDataEncoding(context, nodeToRead, value);
        }

        /// <summary>
        /// Gets the result for the read operation.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="value">The value.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        /// <returns></returns>
        public ServiceResult GetResult(
            ISystemContext context,
            HdaAttributeState attribute,
            ReadValueId nodeToRead,
            DataValue value,
            DiagnosticsMasks diagnosticsMasks)
        {
            if (nodeToRead.AttributeId != Attributes.Value)
            {
                // check if reading access level.
                if (nodeToRead.AttributeId == Attributes.AccessLevel || nodeToRead.AttributeId == Attributes.UserAccessLevel)
                {
                    HdaAttributeValue result = GetAttributeValue(attribute.Attribute.Id);

                    if (result == null || result.Error < 0 || result.Error == ResultIds.S_NODATA)
                    {
                        value.StatusCode = StatusCodes.BadNotFound;
                        return value.StatusCode;
                    }
                    
                    value.Value = AccessLevels.CurrentRead;

                    if (result.Error != ResultIds.S_CURRENTVALUE)
                    {
                        value.Value = (byte)(AccessLevels.CurrentRead | AccessLevels.HistoryRead);
                    }

                    return value.StatusCode;
                }

                return StatusCodes.BadAttributeIdInvalid;
            }
            
            // convert values when required.
            switch (attribute.Attribute.Id)
            {
                case Constants.OPCHDA_NORMAL_MAXIMUM:
                {
                    double high = this.GetAttributeValue<double>(Constants.OPCHDA_NORMAL_MAXIMUM, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    double low = this.GetAttributeValue<double>(Constants.OPCHDA_NORMAL_MINIMUM, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    value.Value = new Range(high, low);
                    break;
                }

                case Constants.OPCHDA_HIGH_ENTRY_LIMIT:
                {
                    double high = this.GetAttributeValue<double>(Constants.OPCHDA_HIGH_ENTRY_LIMIT, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    double low = this.GetAttributeValue<double>(Constants.OPCHDA_LOW_ENTRY_LIMIT, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    value.Value = new Range(high, low);
                    break;
                }

                case Constants.OPCHDA_ENG_UNITS:
                {
                    string units = this.GetAttributeValue<string>(Constants.OPCHDA_ENG_UNITS, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    value.Value = new EUInformation(units, Namespaces.ComInterop);
                    break;
                }

                case Constants.OPCHDA_MAX_TIME_INT:
                case Constants.OPCHDA_MIN_TIME_INT:
                {
                    string number = this.GetAttributeValue<string>(attribute.Attribute.Id, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    try
                    {
                        value.Value = Convert.ToDouble(number);
                    }
                    catch (Exception)
                    {
                        value.StatusCode = StatusCodes.BadTypeMismatch;
                        return value.StatusCode;
                    }

                    break;
                }

                case Constants.OPCHDA_EXCEPTION_DEV_TYPE:
                {
                    short number = this.GetAttributeValue<short>(attribute.Attribute.Id, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    value.Value = (int)number;
                    break;
                }

                default:
                {
                    object result = this.GetAttributeValue<object>(attribute.Attribute.Id, value, true);

                    if (StatusCode.IsBad(value.StatusCode))
                    {
                        return value.StatusCode;
                    }

                    value.Value = result;
                    break;
                }

            }

            // check if the attribute value is missing.
            if (value.StatusCode == StatusCodes.BadNotFound)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            return ApplyIndexRangeAndDataEncoding(context, nodeToRead, value);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the attribute value.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        private HdaAttributeValue GetAttributeValue(uint attributeId)
        {
            if (m_attributeIds != null)
            {
                for (int ii = 0; ii < m_attributeIds.Count; ii++)
                {
                    if (m_attributeIds[ii] == attributeId)
                    {
                        if (m_attributeValues != null && ii < m_attributeValues.Length)
                        {
                            return m_attributeValues[ii];
                        }

                        break;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Applies the index range and data encoding to the value.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="value">The value.</param>
        /// <returns></returns>
        private ServiceResult ApplyIndexRangeAndDataEncoding(
            ISystemContext context,
            ReadValueId nodeToRead,
            DataValue value)
        {
            if (StatusCode.IsBad(value.StatusCode))
            {
                return value.StatusCode;
            }

            if (nodeToRead.ParsedIndexRange != NumericRange.Empty || !QualifiedName.IsNull(nodeToRead.DataEncoding))
            {
                if (nodeToRead.AttributeId != Attributes.Value && !QualifiedName.IsNull(nodeToRead.DataEncoding))
                {
                    return StatusCodes.BadDataEncodingInvalid;
                }

                object valueToUpdate = value.Value;

                ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                    context,
                    nodeToRead.ParsedIndexRange,
                    nodeToRead.DataEncoding,
                    ref valueToUpdate);

                if (ServiceResult.IsBad(result))
                {
                    value.Value = null;
                    value.StatusCode = result.StatusCode;
                    return result;
                }

                value.Value = valueToUpdate;
            }

            return value.StatusCode;
        }
        #endregion

        #region Private Fields
        private string m_itemId;
        private int m_serverHandle;
        private List<uint> m_attributeIds;
        private HdaAttributeValue[] m_attributeValues;
        #endregion
    }

    /// <summary>
    /// A collection of read requests.
    /// </summary>
    public class HdaReadRequestCollection : List<HdaReadRequest>
    {
        #region Public Methods
        /// <summary>
        /// Determines whether the specified operation has a read request result.
        /// </summary>
        /// <param name="nodeToRead">The read operation parameters.</param>
        /// <returns>
        /// 	<c>true</c> if the specified operation has a read request result; otherwise, <c>false</c>.
        /// </returns>
        public bool HasResult(ReadValueId nodeToRead)
        {
            return nodeToRead.Handle is HdaReadRequest;
        }

        /// <summary>
        /// Gets the result.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="source">The source.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="value">The value.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        /// <returns></returns>
        public ServiceResult GetResult(
            ISystemContext context,
            NodeState source,
            ReadValueId nodeToRead,
            DataValue value,
            DiagnosticsMasks diagnosticsMasks)
        {
            HdaReadRequest request = nodeToRead.Handle as HdaReadRequest;

            if (request == null)
            {
                return StatusCodes.Good;
            }

            // read item value.
            HdaItemState item = source as HdaItemState;

            if (item != null)
            {
                return request.GetResult(context, item, nodeToRead, value, diagnosticsMasks);
            }

            // read vendor defined attribute value.
            HdaAttributeState attribute = source as HdaAttributeState;

            if (attribute != null)
            {
                return request.GetResult(context, attribute, nodeToRead, value, diagnosticsMasks);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Adds a request for the specified source.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="queued">if set to <c>true</c> [queued].</param>
        /// <returns></returns>
        public StatusCode Add(NodeState source, ReadValueId nodeToRead, out bool queued)
        {
            queued = true;

            // read item attributes.
            HdaItemState item = source as HdaItemState;

            if (item != null)
            {
                return Add(item, nodeToRead, out queued);
            }

            // read HDA attribute value.
            HdaAttributeState attribute = source as HdaAttributeState;

            if (attribute != null)
            {
                return Add(attribute, nodeToRead, out queued);
            }

            queued = false;
            return StatusCodes.Good;
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Adds a request for the specified item id.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns>The new request.</returns>
        private HdaReadRequest Add(string itemId)
        {
            if (m_index == null)
            {
                m_index = new Dictionary<string, HdaReadRequest>();
            }

            HdaReadRequest request = null;

            if (!m_index.TryGetValue(itemId, out request))
            {
                request = new HdaReadRequest(itemId);
                m_index.Add(itemId, request);
            }

            this.Add(request);
            return request;
        }

        /// <summary>
        /// Adds a read request for the specified properties.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="attributeIds">The attribute ids.</param>
        /// <returns>The new request.</returns>
        private HdaReadRequest Add(string itemId, params uint[] attributeIds)
        {
            if (m_index == null)
            {
                m_index = new Dictionary<string, HdaReadRequest>();
            }

            HdaReadRequest request = null;

            if (!m_index.TryGetValue(itemId, out request))
            {
                request = new HdaReadRequest(itemId);
                m_index.Add(itemId, request);
                this.Add(request);
            }

            request.AddAttribute(attributeIds);
            return request;
        }

        /// <summary>
        /// Adds a read request for the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="queued">if set to <c>true</c> if a request was created.</param>
        /// <returns>Any error.</returns>
        private StatusCode Add(HdaItemState item, ReadValueId nodeToRead, out bool queued)
        {
            queued = true;

            switch (nodeToRead.AttributeId)
            {
                case Attributes.Description:
                {
                    nodeToRead.Handle = Add(item.ItemId, Constants.OPCHDA_DESCRIPTION);
                    break;
                }

                case Attributes.DataType:
                case Attributes.ValueRank:
                {
                    nodeToRead.Handle = Add(item.ItemId, Constants.OPCHDA_DATA_TYPE);
                    break;
                }

                case Attributes.Historizing:
                {
                    nodeToRead.Handle = Add(item.ItemId, Constants.OPCHDA_ARCHIVING);
                    break;
                }

                default:
                {
                    queued = false;
                    break;
                }
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Adds the specified attribute read to the request list.
        /// </summary>
        /// <param name="attribute">The attribute.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="queued">if set to <c>true</c> [queued].</param>
        /// <returns></returns>
        private StatusCode Add(HdaAttributeState attribute, ReadValueId nodeToRead, out bool queued)
        {
            queued = false;

            if (nodeToRead.AttributeId != Attributes.Value && nodeToRead.AttributeId != Attributes.AccessLevel && nodeToRead.AttributeId != Attributes.UserAccessLevel)
            {
                return StatusCodes.Good;
            }

            queued = true;

            switch (attribute.Attribute.Id)
            {
                case Constants.OPCHDA_NORMAL_MAXIMUM:
                {
                    nodeToRead.Handle = Add(attribute.ItemId, Constants.OPCHDA_NORMAL_MAXIMUM, Constants.OPCHDA_NORMAL_MINIMUM);
                    break;
                }

                case Constants.OPCHDA_HIGH_ENTRY_LIMIT:
                {
                    nodeToRead.Handle = Add(attribute.ItemId, Constants.OPCHDA_HIGH_ENTRY_LIMIT, Constants.OPCHDA_LOW_ENTRY_LIMIT);
                    break;
                }

                default:
                {
                    nodeToRead.Handle = Add(attribute.ItemId, attribute.Attribute.Id);
                    break;
                }
            }

            return StatusCodes.Good;
        }
        #endregion

        #region Private Fields
        Dictionary<string,HdaReadRequest> m_index;
        #endregion
    }
}
