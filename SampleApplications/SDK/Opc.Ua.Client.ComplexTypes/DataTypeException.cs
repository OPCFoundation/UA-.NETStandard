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

namespace Opc.Ua.Client.ComplexTypes
{

    /// <summary>
    /// Exception is thrown if data type is not found.
    /// </summary>
    public class DataTypeNotFoundException : Exception
    {
        public ExpandedNodeId nodeId;
        public string typeName;

        public DataTypeNotFoundException(ExpandedNodeId nodeId)
        {
            this.nodeId = nodeId;
        }

        public DataTypeNotFoundException(string typeName, string message)
            : base(message)
        {
            this.nodeId = NodeId.Null;
            this.typeName = typeName;
        }

        public DataTypeNotFoundException(ExpandedNodeId nodeId, string message)
            : base(message)
        {
            this.nodeId = nodeId;
        }

        public DataTypeNotFoundException(ExpandedNodeId nodeId, string message, Exception inner)
            : base(message, inner)
        {
            this.nodeId = nodeId;
        }
    }

    /// <summary>
    /// DataType is not supported due to structure or value rank.
    /// </summary>
    public class DataTypeNotSupportedException : Exception
    {
        public ExpandedNodeId nodeId;
        public string typeName;

        public DataTypeNotSupportedException(ExpandedNodeId nodeId)
        {
            this.nodeId = nodeId;
        }

        public DataTypeNotSupportedException(string typeName, string message)
            : base(message)
        {
            this.nodeId = NodeId.Null;
            this.typeName = typeName;
        }

        public DataTypeNotSupportedException(ExpandedNodeId nodeId, string message)
            : base(message)
        {
            this.nodeId = nodeId;
        }

        public DataTypeNotSupportedException(ExpandedNodeId nodeId, string message, Exception inner)
            : base(message, inner)
        {
            this.nodeId = nodeId;
        }
    }

}//namespace
