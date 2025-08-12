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

namespace Opc.Ua.Client.ComplexTypes
{
    /// <summary>
    /// Exception is thrown if the data type is not found.
    /// </summary>
    [Serializable]
    public class DataTypeNotFoundException : Exception
    {
        /// <summary>
        /// The nodeId of the data type.
        /// </summary>
        public ExpandedNodeIdCollection NodeIds { get; }

        /// <summary>
        /// Create exception
        /// </summary>
        public DataTypeNotFoundException()
        {
            NodeIds = [];
        }

        /// <summary>
        /// Create exception with message.
        /// </summary>
        public DataTypeNotFoundException(string message)
            : base(message)
        {
            NodeIds = [];
        }

        /// <summary>
        /// Create exception with message and inner exception.
        /// </summary>
        public DataTypeNotFoundException(string message, Exception innerException)
            : base(message, innerException)
        {
            NodeIds = [];
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="nodeIds">The collection of nodeId of the data types not found.</param>
        public DataTypeNotFoundException(ExpandedNodeIdCollection nodeIds)
        {
            NodeIds = nodeIds;
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="nodeIds">The collection of nodeId of the data types not found.</param>
        /// <param name="message">The exception message.</param>
        public DataTypeNotFoundException(ExpandedNodeIdCollection nodeIds, string message)
            : base(message)
        {
            NodeIds = nodeIds;
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="nodeIds">The collection of nodeId of the data types not found.</param>
        /// <param name="message">The exception message.</param>
        /// <param name="inner">The inner exception.</param>
        public DataTypeNotFoundException(
            ExpandedNodeIdCollection nodeIds,
            string message,
            Exception inner)
            : base(message, inner)
        {
            NodeIds = nodeIds;
        }
    }

    /// <summary>
    /// DataType is not supported due to structure or value rank.
    /// </summary>
    [Serializable]
    public class DataTypeNotSupportedException : Exception
    {
        /// <summary>
        /// The nodeId of the data type.
        /// </summary>
        public ExpandedNodeId NodeId { get; }

        /// <summary>
        /// The name of the data type.
        /// </summary>
        public string TypeName { get; }

        /// <summary>
        /// Create exception
        /// </summary>
        public DataTypeNotSupportedException()
        {
            NodeId = ExpandedNodeId.Null;
        }

        /// <summary>
        /// Create exception with message.
        /// </summary>
        public DataTypeNotSupportedException(string message)
            : base(message)
        {
            NodeId = ExpandedNodeId.Null;
        }

        /// <summary>
        /// Create exception with message and inner exception.
        /// </summary>
        public DataTypeNotSupportedException(string message, Exception innerException)
            : base(message, innerException)
        {
            NodeId = ExpandedNodeId.Null;
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="nodeId">The nodeId of the data type.</param>
        public DataTypeNotSupportedException(ExpandedNodeId nodeId)
        {
            NodeId = nodeId;
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="typeName">The name of the type.</param>
        /// <param name="message">The exception message.</param>
        public DataTypeNotSupportedException(string typeName, string message)
            : base(message)
        {
            NodeId = ExpandedNodeId.Null;
            TypeName = typeName;
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="nodeId">The nodeId of the data type.</param>
        /// <param name="message">The exception message.</param>
        public DataTypeNotSupportedException(ExpandedNodeId nodeId, string message)
            : base(message)
        {
            NodeId = nodeId;
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="nodeId">The nodeId of the data type.</param>
        /// <param name="message">The exception message.</param>
        /// <param name="inner">The inner exception.</param>
        public DataTypeNotSupportedException(ExpandedNodeId nodeId, string message, Exception inner)
            : base(message, inner)
        {
            NodeId = nodeId;
        }
    }
}
