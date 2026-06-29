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
using System.Buffers.Binary;
using System.IO;
using Opc.Ua.Server;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Extension beyond OPC 10000-4 §6.6: serializes a <see cref="NodeState"/> to a portable, self-describing
    /// payload and reconstructs it. The payload is framed as a 4-byte
    /// little-endian <see cref="NodeClass"/> followed by the standard
    /// <c>NodeState.SaveAsBinary</c> encoding, so a replica can reconstruct
    /// a generic node of the correct class without knowing the original
    /// concrete (possibly source-generated) type.
    /// </summary>
    /// <remarks>
    /// Reconstruction yields the matching generic base state
    /// (<see cref="BaseObjectState"/>, <see cref="BaseDataVariableState"/>,
    /// …). Type-specific behavior (method handlers, custom callbacks) is not
    /// carried in the payload — it is re-attached by the owning node manager
    /// on the active replica. This is sufficient for browse / read / value
    /// replication and active/passive failover.
    /// </remarks>
    public static class NodeStateSerializer
    {
        /// <summary>
        /// Serializes a node (and its children/references) to a framed
        /// binary payload.
        /// </summary>
        /// <param name="context">The system context for encoding.</param>
        /// <param name="node">The node to serialize.</param>
        public static ByteString Serialize(ISystemContext context, NodeState node)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            using var stream = new MemoryStream();
            byte[] header = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(header, (int)node.NodeClass);
            stream.Write(header, 0, 4);
            node.SaveAsBinary(context, stream);
            return new ByteString(stream.ToArray());
        }

        /// <summary>
        /// Reconstructs a node from a payload produced by
        /// <see cref="Serialize"/>.
        /// </summary>
        /// <param name="context">The system context for decoding.</param>
        /// <param name="payload">The framed binary payload.</param>
        public static NodeState Deserialize(ISystemContext context, ByteString payload)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            byte[] bytes = payload.ToArray();
            if (bytes.Length < 4)
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Distributed node payload is too short to contain a node class header.");
            }

            var nodeClass = (NodeClass)BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
            NodeState node = Create(nodeClass);
            using var stream = new MemoryStream(bytes, 4, bytes.Length - 4, writable: false);
            node.LoadAsBinary(context, stream);
            return node;
        }

        private static NodeState Create(NodeClass nodeClass)
        {
            return nodeClass switch
            {
                NodeClass.Object => new BaseObjectState(null),
                NodeClass.Variable => new BaseDataVariableState(null),
                NodeClass.Method => new MethodState(null),
                NodeClass.View => new ViewState(),
                NodeClass.ObjectType => new BaseObjectTypeState(),
                NodeClass.VariableType => new BaseDataVariableTypeState(),
                NodeClass.ReferenceType => new ReferenceTypeState(),
                NodeClass.DataType => new DataTypeState(),
                _ => throw new ServiceResultException(
                    StatusCodes.BadNodeClassInvalid,
                    $"Cannot reconstruct a node of class {nodeClass}.")
            };
        }
    }
}
