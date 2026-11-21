// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Exception is thrown if the data type is not found.
    /// </summary>
    public class DataTypeNotFoundException : Exception
    {
        /// <summary>
        /// The nodeId of the data type.
        /// </summary>
        public ArrayOf<ExpandedNodeId> NodeIds { get; }

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
        public DataTypeNotFoundException(ArrayOf<ExpandedNodeId> nodeIds)
        {
            NodeIds = nodeIds;
        }

        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="nodeIds">The collection of nodeId of the data types not found.</param>
        /// <param name="message">The exception message.</param>
        public DataTypeNotFoundException(ArrayOf<ExpandedNodeId> nodeIds, string message)
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
            ArrayOf<ExpandedNodeId> nodeIds,
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
    public class DataTypeNotSupportedException : Exception
    {
        /// <summary>
        /// The nodeId of the data type.
        /// </summary>
        public ExpandedNodeId NodeId { get; }

        /// <summary>
        /// The name of the data type.
        /// </summary>
        public string? TypeName { get; }

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
