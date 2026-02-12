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

using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for all object type nodes.
    /// </summary>
    public class BaseObjectTypeState : BaseTypeState
    {
        /// <summary>
        /// Initializes the instance with its default attribute values.
        /// </summary>
        public BaseObjectTypeState()
            : base(NodeClass.ObjectType)
        {
        }

        /// <summary>
        /// Initializes the instance with the default values.
        /// </summary>
        protected override void Initialize(ISystemContext context)
        {
            SuperTypeId = NodeId.Create(
                ObjectTypes.BaseObjectType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            NodeId = NodeId.Create(
                ObjectTypes.BaseObjectType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            BrowseName = QualifiedName.Create(
                BrowseNames.BaseObjectType,
                Namespaces.OpcUa,
                context.NamespaceUris);
            DisplayName = new LocalizedText(
                BrowseNames.BaseObjectType,
                string.Empty,
                BrowseNames.BaseObjectType);
            Description = default;
            WriteMask = AttributeWriteMask.None;
            UserWriteMask = AttributeWriteMask.None;
            IsAbstract = false;
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new BaseObjectTypeState();
        }

        /// <inheritdoc/>
        public override object Clone()
        {
            var clone = new BaseObjectTypeState();
            CopyTo(clone);
            return clone;
        }
    }
}
