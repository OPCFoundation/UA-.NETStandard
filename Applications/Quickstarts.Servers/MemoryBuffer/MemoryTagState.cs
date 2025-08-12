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

using Opc.Ua;

namespace MemoryBuffer
{
    public partial class MemoryTagState
    {
        /// <summary>
        /// Initializes a memory tag for a buffer.
        /// </summary>
        /// <param name="parent">The buffer that owns the tag.</param>
        /// <param name="offet">The offset of the tag address in the memory buffer.</param>
        public MemoryTagState(MemoryBufferState parent, uint offet)
            : base(parent)
        {
            // these objects are created an discarded during each operation.
            // the metadata is derived from the parameters passed to constructors.
            NodeId = new NodeId(
                Utils.Format("{0}[{1}]", parent.SymbolicName, offet),
                parent.NodeId.NamespaceIndex);
            BrowseName = new QualifiedName(
                Utils.Format("{1:X8}", parent.SymbolicName, offet),
                parent.TypeDefinitionId.NamespaceIndex
            );
            DisplayName = BrowseName.Name;
            Description = null;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            ReferenceTypeId = ReferenceTypeIds.HasComponent;
            TypeDefinitionId = new NodeId(
                VariableTypes.MemoryTagType,
                parent.TypeDefinitionId.NamespaceIndex);
            ModellingRuleId = null;
            NumericId = offet;
            DataType = new NodeId((uint)parent.ElementType);
            ValueRank = ValueRanks.Scalar;
            ArrayDimensions = null;
            AccessLevel = AccessLevels.CurrentReadOrWrite;
            UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            MinimumSamplingInterval = parent.MaximumScanRate;
            Historizing = false;

            // re-direct read and write operations to the parent.
            OnReadValue = parent.ReadTagValue;
            OnWriteValue = parent.WriteTagValue;

            Offset = offet;
        }

        /// <summary>
        /// The offset of the tag address in the memory buffer.
        /// </summary>
        public uint Offset { get; }
    }
}
