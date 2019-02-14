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
using OpcRcw.Da;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Stores the parameters and results for a COM DA read operation.
    /// </summary>
    public class ReadRequest
    {
        #region Private Feilds

        /// <summary>
        /// The BrowseName for the DefaultBinary component.
        /// </summary>
        private const string DefaultBinary = "Default Binary";

        /// <summary>
        /// The BrowseName for the DefaultXml component.
        /// </summary>
        private const string DefaultXml = "Default XML";

        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        public ReadRequest(string itemId)
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
        /// Gets the property ids to read.
        /// </summary>
        /// <value>The property ids.</value>
        public List<int> PropertyIds
        {
            get { return m_propertyIds; }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the item value must be read.
        /// </summary>
        /// <value><c>true</c> if the item value must be read; otherwise, <c>false</c>.</value>
        public bool ValueRequired
        {
            get { return m_valueRequired; }
            set { m_valueRequired = value; }
        }

        /// <summary>
        /// Gets or sets the item value returned from the server.
        /// </summary>
        /// <value>The value.</value>
        public DaValue Value
        {
            get { return m_value; }
            set { m_value = value; }
        }

        /// <summary>
        /// Gets or sets the property values returned from the server.
        /// </summary>
        /// <value>The property values.</value>
        public DaValue[] PropertyValues
        {
            get { return m_propertyValues; }
            set { m_propertyValues = value; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Adds the property to the request.
        /// </summary>
        /// <param name="propertyIds">The property ids.</param>
        public void AddProperty(params int[] propertyIds)
        {
            if (m_propertyIds == null)
            {
                m_propertyIds = new List<int>();
            }

            if (propertyIds != null)
            {
                for (int ii = 0; ii < propertyIds.Length; ii++)
                {
                    bool found = false;

                    for (int jj = 0; jj < PropertyIds.Count; jj++)
                    {
                        if (PropertyIds[jj] == propertyIds[ii])
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        PropertyIds.Add(propertyIds[ii]);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the item value.
        /// </summary>
        /// <param name="daValue">The da value.</param>
        /// <param name="uaValue">The ua value.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        /// <returns></returns>
        public static ServiceResult GetItemValue(DaValue daValue, DataValue uaValue, DiagnosticsMasks diagnosticsMasks)
        {
            ServiceResult result = null;

            uaValue.Value = null;

            if (daValue == null)
            {
                result = uaValue.StatusCode = StatusCodes.BadOutOfService;
                return result;
            }

            uaValue.SourceTimestamp = daValue.Timestamp;

            if (daValue.Error < 0)
            {
                result = MapReadErrorToServiceResult(daValue.Error, diagnosticsMasks);
                uaValue.StatusCode = result.StatusCode;
                return result;
            }

            if (daValue.Quality != OpcRcw.Da.Qualities.OPC_QUALITY_GOOD)
            {
                uaValue.StatusCode = ComUtils.GetQualityCode(daValue.Quality);
            }

            if (StatusCode.IsBad(uaValue.StatusCode))
            {
                result = uaValue.StatusCode;
                return result;
            }

            uaValue.Value = daValue.Value;

            return result;
        }

        /// <summary>
        /// Gets the property value.
        /// </summary>
        /// <typeparam name="T">The expected type for the property value.</typeparam>
        /// <param name="propertyId">The property id.</param>
        /// <param name="value">The value to update.</param>
        /// <param name="isValue">if set to <c>true</c> if the property is required for the value attribute of a node.</param>
        /// <returns>The value cast to the type T.</returns>
        /// <remarks>
        /// This method sets the StatusCode in the DataValue if an error occurs and returns default(T).
        /// The DataValue.Value property is set to the value cast to type T.
        /// </remarks>
        public T GetPropertyValue<T>(int propertyId, DataValue value, bool isValue)
        {
            value.Value = null;

            // find the property value.
            DaValue result = null;

            if (PropertyIds != null && PropertyValues != null)
            {
                for (int ii = 0; ii < PropertyIds.Count; ii++)
                {
                    if (PropertyIds[ii] == propertyId)
                    {
                        if (PropertyValues != null && PropertyValues.Length > ii)
                        {
                            result = PropertyValues[ii];
                        }

                        break;
                    }
                }
            }

            // set the appropriate error code if no value found.
            if (result == null || result.Error < 0)
            {
                value.StatusCode = StatusCodes.BadNotFound;
                return default(T);
            }

            // update the source timestamp if a value is being read.
            if (isValue)
            {
                value.SourceTimestamp = result.Timestamp;
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
            DaItemState item,
            ReadValueId nodeToRead,
            DataValue value,
            DiagnosticsMasks diagnosticsMasks)
        {
            if (nodeToRead.AttributeId == Attributes.Value)
            {
                ServiceResult result = GetItemValue(m_value, value, diagnosticsMasks);

                if (ServiceResult.IsBad(result))
                {
                    return result;
                }

                return ApplyIndexRangeAndDataEncoding(context, nodeToRead, value);
            }

            switch (nodeToRead.AttributeId)
            {
                case Attributes.Description:
                    {
                        string description = this.GetPropertyValue<string>(Opc.Ua.Com.PropertyIds.Description, value, false);

                        if (StatusCode.IsGood(value.StatusCode))
                        {
                            value.Value = new LocalizedText(description);
                        }

                        break;
                    }

                case Attributes.DataType:
                    {
                        short datatype = this.GetPropertyValue<short>(Opc.Ua.Com.PropertyIds.DataType, value, false);

                        if (StatusCode.IsGood(value.StatusCode))
                        {
                            value.Value = ComUtils.GetDataTypeId(datatype);
                        }

                        break;
                    }

                case Attributes.ValueRank:
                    {
                        short datatype = this.GetPropertyValue<short>(Opc.Ua.Com.PropertyIds.DataType, value, false);

                        if (StatusCode.IsGood(value.StatusCode))
                        {
                            value.Value = ComUtils.GetValueRank(datatype);
                        }

                        break;
                    }

                case Attributes.AccessLevel:
                case Attributes.UserAccessLevel:
                    {
                        int accessRights = this.GetPropertyValue<int>(Opc.Ua.Com.PropertyIds.AccessRights, value, false);

                        if (StatusCode.IsGood(value.StatusCode))
                        {
                            value.Value = (byte)accessRights;
                        }

                        break;
                    }

                case Attributes.MinimumSamplingInterval:
                    {
                        float scanRate = this.GetPropertyValue<float>(Opc.Ua.Com.PropertyIds.ScanRate, value, false);

                        if (StatusCode.IsGood(value.StatusCode))
                        {
                            value.Value = (double)scanRate;
                        }

                        break;
                    }

                default:
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }
            }

            // check if the property value is missing.
            if (value.StatusCode == StatusCodes.BadNotFound)
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            return ApplyIndexRangeAndDataEncoding(context, nodeToRead, value);
        }

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
                    bool useXml = nodeToRead.DataEncoding.Name == DefaultXml;

                    if (!useXml
                        && !string.IsNullOrEmpty(nodeToRead.DataEncoding.Name)
                        && nodeToRead.DataEncoding.Name != DefaultBinary)
                    {
                        result = StatusCodes.BadDataEncodingInvalid;
                    }

                    value.Value = null;
                    value.StatusCode = result.StatusCode;
                    return result;
                }

                value.Value = valueToUpdate;
            }

            return value.StatusCode;
        }

        /// <summary>
        /// Gets the result for the read operation.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="property">The property.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="value">The value.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        /// <returns></returns>
        public ServiceResult GetResult(
            ISystemContext context,
            DaPropertyState property,
            ReadValueId nodeToRead,
            DataValue value,
            DiagnosticsMasks diagnosticsMasks)
        {
            ServiceResult result = null;

            switch (nodeToRead.AttributeId)
            {
                case Attributes.Value:
                    {
                        if (!String.IsNullOrEmpty(property.Property.ItemId))
                        {
                            result = GetItemValue(m_value, value, diagnosticsMasks);
                        }
                        else
                        {
                            this.GetPropertyValue<object>(property.PropertyId, value, true);
                            result = value.StatusCode;
                        }

                        // check if the property value is missing.
                        if (value.StatusCode == StatusCodes.BadNotFound)
                        {
                            return StatusCodes.BadAttributeIdInvalid;
                        }

                        if (ServiceResult.IsBad(result))
                        {
                            return result;
                        }

                        return ApplyIndexRangeAndDataEncoding(context, nodeToRead, value);
                    }
            }

            return StatusCodes.BadAttributeIdInvalid;
        }

        /// <summary>
        /// Gets the result for the read operation.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="property">The property.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="value">The value.</param>
        /// <param name="diagnosticsMasks">The diagnostics masks.</param>
        /// <returns></returns>
        public ServiceResult GetResult(
            ISystemContext context,
            PropertyState property,
            ReadValueId nodeToRead,
            DataValue value,
            DiagnosticsMasks diagnosticsMasks)
        {
            DaItemState item = property.Parent as DaItemState;

            if (item == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (nodeToRead.AttributeId != Attributes.Value)
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            switch (property.SymbolicName)
            {
                case Opc.Ua.BrowseNames.EURange:
                    {
                        double high = this.GetPropertyValue<double>(Opc.Ua.Com.PropertyIds.HighEU, value, true);

                        if (StatusCode.IsBad(value.StatusCode))
                        {
                            return value.StatusCode;
                        }

                        double low = this.GetPropertyValue<double>(Opc.Ua.Com.PropertyIds.LowEU, value, true);

                        if (StatusCode.IsBad(value.StatusCode))
                        {
                            return value.StatusCode;
                        }

                        value.Value = new Range(high, low);
                        break;
                    }

                case Opc.Ua.BrowseNames.InstrumentRange:
                    {
                        double high = this.GetPropertyValue<double>(Opc.Ua.Com.PropertyIds.HighIR, value, true);

                        if (StatusCode.IsBad(value.StatusCode))
                        {
                            return value.StatusCode;
                        }

                        double low = this.GetPropertyValue<double>(Opc.Ua.Com.PropertyIds.LowIR, value, true);

                        if (StatusCode.IsBad(value.StatusCode))
                        {
                            return value.StatusCode;
                        }

                        value.Value = new Range(high, low);
                        break;
                    }

                case Opc.Ua.BrowseNames.EngineeringUnits:
                    {
                        string units = this.GetPropertyValue<string>(Opc.Ua.Com.PropertyIds.EngineeringUnits, value, true);

                        if (StatusCode.IsBad(value.StatusCode))
                        {
                            return value.StatusCode;
                        }

                        value.Value = new EUInformation(units, Namespaces.ComInterop);
                        break;
                    }

                case Opc.Ua.BrowseNames.EnumStrings:
                    {
                        string[] strings = this.GetPropertyValue<string[]>(Opc.Ua.Com.PropertyIds.EuInfo, value, true);

                        if (StatusCode.IsBad(value.StatusCode))
                        {
                            return value.StatusCode;
                        }

                        if (strings != null)
                        {
                            LocalizedText[] texts = new LocalizedText[strings.Length];

                            for (int ii = 0; ii < texts.Length; ii++)
                            {
                                texts[ii] = new LocalizedText(strings[ii]);
                            }

                            value.Value = texts;
                        }

                        break;
                    }

                case Opc.Ua.BrowseNames.LocalTime:
                    {
                        int timebias = this.GetPropertyValue<int>(Opc.Ua.Com.PropertyIds.TimeZone, value, true);

                        if (StatusCode.IsBad(value.StatusCode))
                        {
                            return value.StatusCode;
                        }

                        TimeZoneDataType timeZone = new TimeZoneDataType();
                        timeZone.Offset = (short)timebias;
                        timeZone.DaylightSavingInOffset = false;

                        value.Value = timeZone;
                        break;
                    }

                case Opc.Ua.BrowseNames.TrueState:
                    {
                        string description = this.GetPropertyValue<string>(Opc.Ua.Com.PropertyIds.CloseLabel, value, false);

                        if (StatusCode.IsGood(value.StatusCode))
                        {
                            value.Value = new LocalizedText(description);
                        }

                        break;
                    }

                case Opc.Ua.BrowseNames.FalseState:
                    {
                        string description = this.GetPropertyValue<string>(Opc.Ua.Com.PropertyIds.OpenLabel, value, false);

                        if (StatusCode.IsGood(value.StatusCode))
                        {
                            value.Value = new LocalizedText(description);
                        }

                        break;
                    }

                default:
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }
            }

            // check if the property value is missing.
            if (value.StatusCode == StatusCodes.BadNotFound)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            return ApplyIndexRangeAndDataEncoding(context, nodeToRead, value);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Maps a DA error code onto a service result.
        /// </summary>
        private static ServiceResult MapReadErrorToServiceResult(
            int error,
            DiagnosticsMasks diagnosticsMasks)
        {
            if (error == 0)
            {
                return ServiceResult.Good;
            }

            switch (error)
            {
                case ResultIds.E_OUTOFMEMORY: { return StatusCodes.BadOutOfMemory; }
                case ResultIds.E_INVALIDHANDLE: { return StatusCodes.BadNodeIdUnknown; }
                case ResultIds.E_BADRIGHTS: { return StatusCodes.BadNotReadable; }
                case ResultIds.E_UNKNOWNITEMID: { return StatusCodes.BadNodeIdUnknown; }
                case ResultIds.E_INVALIDITEMID: { return StatusCodes.BadNodeIdInvalid; }
                case ResultIds.E_INVALID_PID: { return StatusCodes.BadNodeIdInvalid; }
                case ResultIds.E_ACCESSDENIED: { return StatusCodes.BadOutOfService; }
            }

            return StatusCodes.BadUnexpectedError;
        }
        #endregion

        #region Private Fields
        private string m_itemId;
        private List<int> m_propertyIds;
        private bool m_valueRequired;
        private DaValue m_value;
        private DaValue[] m_propertyValues;
        #endregion
    }

    /// <summary>
    /// A collection of read requests.
    /// </summary>
    public class ReadRequestCollection : List<ReadRequest>
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
            return nodeToRead.Handle is ReadRequest;
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
            ReadRequest request = nodeToRead.Handle as ReadRequest;

            if (request == null)
            {
                return StatusCodes.Good;
            }

            // read item value.
            DaItemState item = source as DaItemState;

            if (item != null)
            {
                return request.GetResult(context, item, nodeToRead, value, diagnosticsMasks);
            }

            // read vendor defined property value.
            DaPropertyState daProperty = source as DaPropertyState;

            if (daProperty != null)
            {
                return request.GetResult(context, daProperty, nodeToRead, value, diagnosticsMasks);
            }

            // read UA defined property value.
            PropertyState uaProperty = source as PropertyState;

            if (uaProperty != null)
            {
                return request.GetResult(context, uaProperty, nodeToRead, value, diagnosticsMasks);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Adds a request for the specified source.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="value">The value.</param>
        /// <param name="queued">if set to <c>true</c> [queued].</param>
        /// <returns></returns>
        public StatusCode Add(NodeState source, ReadValueId nodeToRead, DataValue value, out bool queued)
        {
            queued = true;

            // read item value.
            DaItemState item = source as DaItemState;

            if (item != null)
            {
                return Add(item, nodeToRead, out queued);
            }

            // read vendor defined property value.
            DaPropertyState daProperty = source as DaPropertyState;

            if (daProperty != null)
            {
                return Add(daProperty, nodeToRead, out queued);
            }

            // read UA defined property value.
            PropertyState uaProperty = source as PropertyState;

            if (uaProperty != null)
            {
                return Add(uaProperty, nodeToRead, out queued);
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
        private ReadRequest Add(string itemId)
        {
            if (itemId == null)
            {
                this.Add((ReadRequest)null);
                return null;
            }

            if (m_index == null)
            {
                m_index = new Dictionary<string, ReadRequest>();
            }

            ReadRequest request = null;

            if (!m_index.TryGetValue(itemId, out request))
            {
                request = new ReadRequest(itemId);
                m_index.Add(itemId, request);
            }

            request.ValueRequired = true;
            this.Add(request);
            return request;
        }

        /// <summary>
        /// Adds a read request for the specified properties.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <returns>The new request.</returns>
        private ReadRequest Add(string itemId, params int[] propertyIds)
        {
            if (m_index == null)
            {
                m_index = new Dictionary<string, ReadRequest>();
            }

            ReadRequest request = null;

            if (!m_index.TryGetValue(itemId, out request))
            {
                request = new ReadRequest(itemId);
                m_index.Add(itemId, request);
                Add(request);
            }

            request.AddProperty(propertyIds);

            return request;
        }

        /// <summary>
        /// Adds a read request for the specified property.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="property">The property.</param>
        /// <returns>The new request.</returns>
        private ReadRequest Add(string itemId, DaProperty property)
        {
            if (m_index == null)
            {
                m_index = new Dictionary<string, ReadRequest>();
            }

            if (!String.IsNullOrEmpty(property.ItemId))
            {
                return Add(property.ItemId);
            }

            return Add(itemId, property.PropertyId);
        }

        /// <summary>
        /// Adds a read request for the specified item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="queued">if set to <c>true</c> if a request was created.</param>
        /// <returns>Any error.</returns>
        private StatusCode Add(DaItemState item, ReadValueId nodeToRead, out bool queued)
        {
            queued = true;

            switch (nodeToRead.AttributeId)
            {
                case Attributes.Value:
                    {
                        nodeToRead.Handle = Add(item.ItemId);
                        break;
                    }

                case Attributes.Description:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.Description);
                        break;
                    }

                case Attributes.DataType:
                case Attributes.ValueRank:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.DataType);
                        break;
                    }

                case Attributes.AccessLevel:
                case Attributes.UserAccessLevel:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.AccessRights);
                        break;
                    }

                case Attributes.MinimumSamplingInterval:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.ScanRate);
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
        /// Adds the specified property.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="queued">if set to <c>true</c> [queued].</param>
        /// <returns>Any error.</returns>
        private StatusCode Add(DaPropertyState property, ReadValueId nodeToRead, out bool queued)
        {
            queued = true;

            switch (nodeToRead.AttributeId)
            {
                case Attributes.Value:
                    {
                        nodeToRead.Handle = Add(property.ItemId, property.Property);
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
        /// Adds the specified property read to the request list.
        /// </summary>
        /// <param name="property">The property.</param>
        /// <param name="nodeToRead">The node to read.</param>
        /// <param name="queued">if set to <c>true</c> [queued].</param>
        /// <returns></returns>
        private StatusCode Add(PropertyState property, ReadValueId nodeToRead, out bool queued)
        {
            queued = false;

            DaItemState item = property.Parent as DaItemState;

            if (item == null)
            {
                return StatusCodes.Good;
            }

            if (nodeToRead.AttributeId != Attributes.Value)
            {
                return StatusCodes.Good;
            }

            queued = true;

            switch (property.SymbolicName)
            {
                case Opc.Ua.BrowseNames.EURange:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.HighEU, PropertyIds.LowEU);
                        break;
                    }

                case Opc.Ua.BrowseNames.InstrumentRange:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.HighIR, PropertyIds.LowIR);
                        break;
                    }

                case Opc.Ua.BrowseNames.EngineeringUnits:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.EngineeringUnits);
                        break;
                    }

                case Opc.Ua.BrowseNames.EnumStrings:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.EuInfo);
                        break;
                    }

                case Opc.Ua.BrowseNames.LocalTime:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.TimeZone);
                        break;
                    }

                case Opc.Ua.BrowseNames.TrueState:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.CloseLabel);
                        break;
                    }

                case Opc.Ua.BrowseNames.FalseState:
                    {
                        nodeToRead.Handle = Add(item.ItemId, PropertyIds.OpenLabel);
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
        #endregion

        #region Private Fields
        Dictionary<string, ReadRequest> m_index;
        #endregion
    }
}
