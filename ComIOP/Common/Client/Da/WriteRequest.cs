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
using System.Runtime.InteropServices;
using OpcRcw.Comn;
using OpcRcw.Da;
using Opc.Ua;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Stores the parameters and results for a COM DA write operation.
    /// </summary>
    public class WriteRequest
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteRequest"/> class.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="index">The index associated with the request.</param>
        public WriteRequest(string itemId, DataValue value, int index)
        {
            m_itemId = itemId;
            m_value = value;
            m_index = index;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets the result fo the write opeartion.
        /// </summary>
        /// <returns>The result code.</returns>
        public StatusCode GetResult()
        {
            return MapWriteErrorToServiceResult(Error);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the item id.
        /// </summary>
        /// <value>The item id.</value>
        public string ItemId
        {
            get { return m_itemId; }
        }

        /// <summary>
        /// Gets the value to write.
        /// </summary>
        /// <value>The value to write.</value>
        public DataValue Value
        {
            get { return m_value; }
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
        /// Gets or sets the index.
        /// </summary>
        /// <value>The index.</value>
        public int Index
        {
            get { return m_index; }
            set {  m_index = value; }
        }
        #endregion
        
        #region Private Methods
        /// <summary>
        /// Maps a DA error code onto a service result.
        /// </summary>
        private StatusCode MapWriteErrorToServiceResult(int error)
        {
            if (error == 0)
            {
                return StatusCodes.Good;
            }

            switch (error)
            {
                case ResultIds.E_BADRIGHTS: { return StatusCodes.BadUserAccessDenied; }
                case ResultIds.DISP_E_TYPEMISMATCH: { return StatusCodes.BadTypeMismatch; }
                case ResultIds.E_BADTYPE: { return StatusCodes.BadTypeMismatch; }
                case ResultIds.E_RANGE: { return StatusCodes.BadOutOfRange; }
                case ResultIds.DISP_E_OVERFLOW: { return StatusCodes.BadOutOfRange; }
                case ResultIds.E_OUTOFMEMORY: { return StatusCodes.BadOutOfMemory; }
                case ResultIds.E_INVALIDHANDLE: { return StatusCodes.BadNodeIdUnknown; }
                case ResultIds.E_UNKNOWNITEMID: { return StatusCodes.BadNodeIdUnknown; }
                case ResultIds.E_INVALIDITEMID: { return StatusCodes.BadNodeIdInvalid; }
                case ResultIds.E_INVALID_PID: { return StatusCodes.BadNodeIdInvalid; }
                case ResultIds.E_NOTSUPPORTED: { return StatusCodes.BadWriteNotSupported; }
                case ResultIds.S_CLAMP: { return StatusCodes.GoodClamped; }
                case ResultIds.E_FILTER_DUPLICATE: { return StatusCodes.BadTypeMismatch; }
                case ResultIds.E_FILTER_INVALID: { return StatusCodes.BadTypeMismatch; }
                case ResultIds.E_FILTER_ERROR: { return StatusCodes.BadTypeMismatch; }
            }
            
            return StatusCodes.BadUnexpectedError;
        }
        #endregion

        #region Private Fields
        private string m_itemId;
        private DataValue m_value;
        private int m_error;
        private int m_index;
        #endregion
    }

    /// <summary>
    /// Stores a collection of write requests.
    /// </summary>
    public class WriteRequestCollection : List<WriteRequest>
    {
        #region Public Methods
        /// <summary>
        /// Adds a write request for a DA item.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <param name="nodeToWrite">The node to write.</param>
        /// <param name="index">The index associated with the value.</param>
        /// <param name="queued">If set to <c>true</c> then the a request was added.</param>
        /// <returns>
        /// StatusCode.Good if the write is allowed. An error code otherwise.
        /// </returns>
        public StatusCode Add(NodeState source, WriteValue nodeToWrite, int index, out bool queued)
        {
            queued = true;

            DaItemState item = source as DaItemState;

            if (item != null)
            {
                return Add(item, nodeToWrite, index);
            }

            DaPropertyState property = source as DaPropertyState;

            if (property != null)
            {
                return Add(property, nodeToWrite, index);
            }

            queued = false;
            return StatusCodes.Good;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Adds a write request for a DA item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="nodeToWrite">The node to write.</param>
        /// <param name="index">The index associated with the value.</param>
        /// <returns>StatusCode.Good if the write is allowed. An error code otherwise.</returns>
        private StatusCode Add(DaItemState item, WriteValue nodeToWrite, int index)
        {
            if (nodeToWrite.AttributeId != Attributes.Value)
            {
                return StatusCodes.BadNotWritable;
            }

            //if (nodeToWrite.ParsedIndexRange != NumericRange.Empty)
            //{
            //    return StatusCodes.BadIndexRangeInvalid;
            //}

            if (nodeToWrite.Value.ServerTimestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            if (item.Element.DataType != 0)
            {
                if (!CheckDataType(item.Element.DataType, nodeToWrite.Value.WrappedValue))
                {
                    return StatusCodes.BadTypeMismatch;
                }
            }

            WriteRequest request = new WriteRequest(item.ItemId, nodeToWrite.Value, index);
            Add(request);
            return StatusCodes.Good;
        }

        /// <summary>
        /// Adds a write request for a DA property value.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="nodeToWrite">The node to write.</param>
        /// <param name="index">The index associated with the value.</param>
        /// <returns>
        /// StatusCode.Good if the write is allowed. An error code otherwise.
        /// </returns>
        private StatusCode Add(DaPropertyState item, WriteValue nodeToWrite, int index)
        {
            if (nodeToWrite.AttributeId != Attributes.Value)
            {
                return StatusCodes.BadNotWritable;
            }

            if (String.IsNullOrEmpty(item.Property.ItemId))
            {
                return StatusCodes.BadNotWritable;
            }

            if (nodeToWrite.ParsedIndexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            if (nodeToWrite.Value.ServerTimestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            if (item.Property.DataType != 0)
            {
                if (!CheckDataType(item.Property.DataType, nodeToWrite.Value.WrappedValue))
                {
                    return StatusCodes.BadTypeMismatch;
                }
            }

            WriteRequest request = new WriteRequest(item.Property.ItemId, nodeToWrite.Value, index);
            Add(request);
            nodeToWrite.Handle = request;
            return StatusCodes.Good;
        }

        /// <summary>
        /// Checks the type of the data.
        /// </summary>
        /// <param name="expectedType">The expected type.</param>
        /// <param name="value">The value.</param>
        /// <returns>True if the value can be written to the server.</returns>
        private bool CheckDataType(short expectedType, Variant value)
        {
            if (value.Value == null)
            {
                return true;
            }

            if (expectedType == (short)VarEnum.VT_CY)
            {
                expectedType = (short)VarEnum.VT_BSTR;
            }

            if (expectedType == (short)(VarEnum.VT_ARRAY | VarEnum.VT_CY))
            {
                expectedType = (short)(VarEnum.VT_ARRAY | VarEnum.VT_BSTR);
            }

            TypeInfo typeInfo = value.TypeInfo;
            VarEnum vtType = ComUtils.GetVarType(typeInfo);

            if (vtType == VarEnum.VT_EMPTY)
            {
                return false;
            }

            if (expectedType == (short)(VarEnum.VT_ARRAY | VarEnum.VT_VARIANT))
            {
                // must be an array.
                Array array = value.Value as Array;

                if (array == null || (typeInfo.BuiltInType == BuiltInType.ByteString && typeInfo.ValueRank == ValueRanks.Scalar))
                {
                    return false;
                }

                // nothing more to for fixed type arrays.
                if (typeInfo.BuiltInType != BuiltInType.Variant)
                {
                    return true;
                }

                // must check each element.
                for (int ii = 0; ii < array.GetLength(0); ii++)
                {
                    object element = array.GetValue(ii);

                    if (!CheckDataType((short)VarEnum.VT_VARIANT, new Variant(element)))
                    {
                        return false;
                    }
                }
            }

            if (expectedType == (short)vtType || expectedType == (short)VarEnum.VT_VARIANT)
            {
                return true;
            }

            return false;
        }
        #endregion
    }
}
