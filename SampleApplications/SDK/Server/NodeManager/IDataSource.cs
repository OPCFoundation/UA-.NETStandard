/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Reflection;
using System.Collections.Generic;

namespace Opc.Ua.Server
{        
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// An interface to read the attribute values for a node.
    /// </summary>
    [Obsolete("The IReadMetadataSource interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IReadMetadataSource
    {        
        /// <summary>
        /// Reads the metadata for the node.
        /// </summary>
        /// <remarks>
        /// The caller fills in the default values.
        /// The implementor must not change a value if an an error occurs reading it.
        /// </remarks>
        void ReadMetadata(
            OperationContext context,
            object           targetId,
            BrowseResultMask resultMask,
            NodeMetadata     metadata);
    }

    /// <summary>
    /// An interface to read the attribute values for a node.
    /// </summary>
    [Obsolete("The IReadDataSource interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IReadDataSource : IReadMetadataSource
    {
        /// <summary>
        /// Reads the values of attributes.
        /// </summary>
        /// <remarks>
        /// The caller has already verified that:
        ///     - the AttributeId is valid for the Node.
        ///     - the IndexRange and DataEncoding are only specified for Value attributes.
        ///     - set the ServerTimestamp to UtcNow and the SourceTimestamp to MinValue. 
        /// 
        /// The caller also ensures that the handle passed in was a handle originally provided by
        /// the datasource. 
        /// 
        /// The caller also places the default value into the results. If the datasource does
        /// nothing the caller will use the default value.
        /// 
        /// The implementor must apply the IndexRange and DataEncoding to Value attributes. 
        /// </remarks>
        void Read(
            OperationContext     context,
            double               maxAge,
            IList<RequestHandle> handles,
            IList<ReadValueId>   nodesToRead, 
            IList<DataValue>     results, 
            IList<ServiceResult> errors);
    }

    /// <summary>
    /// An interface to write the attribute values for a node.
    /// </summary>
    [Obsolete("The IWriteDataSource interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IWriteDataSource
    {
        /// <summary>
        /// Writes a value of an attribute to the variable source.
        /// </summary>
        /// <remarks>
        /// The caller has already verified that:
        ///     - the AttributeId is valid for the Node.
        ///     - the IndexRange is only specified for Value attributes.
        ///     - the DataType of the value is valid for the Attribute (i.e. checks the DataType/ValueRank for Variable Values).
        ///     - the StatusCode and Timestamps are only specified for Value attributes.
        /// 
        /// The caller also ensures that the handle passed in was a handle originally provided by
        /// the datasource. 
        /// 
        /// The implementor must apply the IndexRange to Value attributes. 
        /// </remarks>
        void Write(
            OperationContext     context,
            IList<RequestHandle> handles,
            IList<WriteValue>    nodesToWrite, 
            IList<ServiceResult> errors);
    }


    /// <summary>
    /// An interface to access the attribute values for a node.
    /// </summary>
    /// <remarks>
    /// This interface allows objects to use the CoreNodeManager to manage the 
    /// references for a set of nodes in the address space but manage the real 
    /// values inside another object. During a Read or Write operation the CoreNodeManager
    /// will check to see if a node has an associated datasource. If no datasources then
    /// the CoreNodeManager uses the attribute values it stores internally.
    /// 
    /// Implementors of datasources must be careful about thread locks the CoreNodeManager
    /// will not release its internal lock when this interface is called. This is could create
    /// a deadlock if the datasource attempts to access other shared resources.
    /// </remarks>
    [Obsolete("The IDataSource interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public interface IDataSource : IReadDataSource, IWriteDataSource
    {
    }

    /// <summary>
    /// A handle that stores information used during a read history request.
    /// </summary>
    [Obsolete("The RequestHandle interface is obsolete and is not supported. See Opc.Ua.Server.CustomNodeManager for a replacement.")]
    public struct RequestHandle
    {
        #region Public Methods
        /// <summary>
        /// Creates a new handle.
        /// </summary>
        public RequestHandle(object handle, int index)
        {
            m_handle = handle;
            m_index  = index;
        }
        #endregion
        
        #region Overridden Methods
        /// <summary>
        /// Indicates whether this instance and a specified object are equal.
        /// </summary>
        /// <param name="obj">Another object to compare to.</param>
        /// <returns>
        /// true if <paramref name="obj"/> and this instance are the same type and represent the same value; otherwise, false.
        /// </returns>
        public override bool Equals(object obj)
        {         
            RequestHandle? handle = obj as RequestHandle?;

            if (handle != null)
            {
                return Object.ReferenceEquals(this.Handle, handle.Value.Handle);
            }

            return false;
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        /// <returns>
        /// A 32-bit signed integer that is the hash code for this instance.
        /// </returns>
        public override int GetHashCode()
        {
            if (this.Handle != null)
            {
                return this.Handle.GetHashCode();
            }

            return 0;
        }

        /// <summary>
        /// Implements the operator ==.
        /// </summary>
        /// <param name="value1">The value1.</param>
        /// <param name="value2">The value2.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator==(RequestHandle value1, RequestHandle value2)
        {
            return value1.Equals(value2);
        }

        /// <summary>
        /// Implements the operator !=.
        /// </summary>
        /// <param name="value1">The value1.</param>
        /// <param name="value2">The value2.</param>
        /// <returns>The result of the operator.</returns>
        public static bool operator!=(RequestHandle value1, RequestHandle value2)
        {
            return !value1.Equals(value2);
        }
        #endregion
                
        #region Public Properties
        /// <summary>
        /// The handle assigned by historian to the node.
        /// </summary>
        public object Handle
        {
            get { return m_handle; }
        }

        /// <summary>
        /// The position in the lists provided in the requests.
        /// </summary>
        public int Index
        {
            get { return m_index; }
        }
        #endregion
        
        #region Public Methods
        /// <summary>
        /// Returns true if the handle is valid and is of the specified type.
        /// </summary>
        public bool IsValid(Type expectedType)
        {
            if (m_handle != null)
            {
                return m_handle.GetType() == expectedType;
            }

            return false;
        }
        #endregion
        
        #region Private Fields
        private object m_handle;
        private int m_index;
        #endregion
    }
#endif
}
