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
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A node in the server address space.
    /// </summary>
    [DataContract(Namespace = Namespaces.OpcUaXsd)]
    public class Node : IEncodeable, IJsonEncodeable, IFormattable, ILocalNode
    {
        /// <summary>
        /// Creates a node from a reference description.
        /// </summary>
        /// <param name="reference">The reference.</param>
        public Node(ReferenceDescription reference)
        {
            Initialize();

            NodeId = (NodeId)reference.NodeId;
            NodeClass = reference.NodeClass;
            BrowseName = reference.BrowseName;
            DisplayName = reference.DisplayName;
        }

        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public Node(ILocalNode source)
        {
            Initialize();

            if (source != null)
            {
                NodeId = source.NodeId;
                NodeClass = source.NodeClass;
                BrowseName = source.BrowseName;
                DisplayName = source.DisplayName;
                Description = source.Description;
                WriteMask = (uint)source.WriteMask;
                UserWriteMask = (uint)source.UserWriteMask;
            }
        }

        /// <summary>
        /// Create
        /// </summary>
        public Node()
        {
            Initialize();
        }

        [OnDeserializing]
        private void Initialize(StreamingContext context)
        {
            Initialize();
        }

        private void Initialize()
        {
            NodeId = default;
            NodeClass = NodeClass.Unspecified;
            BrowseName = default;
            DisplayName = default;
            Description = default;
            WriteMask = 0;
            UserWriteMask = 0;
            m_rolePermissions = [];
            m_userRolePermissions = [];
            AccessRestrictions = 0;
            m_references = [];
        }

        /// <summary>
        /// Node id
        /// </summary>
        [DataMember(Name = "NodeId", IsRequired = false, Order = 1)]
        public NodeId NodeId { get; set; }

        /// <summary>
        /// Node class
        /// </summary>
        [DataMember(Name = "NodeClass", IsRequired = false, Order = 2)]
        public NodeClass NodeClass { get; set; }

        /// <summary>
        /// Browse name
        /// </summary>
        [DataMember(Name = "BrowseName", IsRequired = false, Order = 3)]
        public QualifiedName BrowseName { get; set; }

        /// <summary>
        /// Display name
        /// </summary>
        [DataMember(Name = "DisplayName", IsRequired = false, Order = 4)]
        public LocalizedText DisplayName { get; set; }

        /// <summary>
        /// Description
        /// </summary>
        [DataMember(Name = "Description", IsRequired = false, Order = 5)]
        public LocalizedText Description { get; set; }

        /// <summary>
        /// Write mask
        /// </summary>
        [DataMember(Name = "WriteMask", IsRequired = false, Order = 6)]
        public uint WriteMask { get; set; }

        /// <summary>
        /// User write mask
        /// </summary>
        [DataMember(Name = "UserWriteMask", IsRequired = false, Order = 7)]
        public uint UserWriteMask { get; set; }

        /// <summary>
        /// Role permissions
        /// </summary>
        [DataMember(Name = "RolePermissions", IsRequired = false, Order = 8)]
        public RolePermissionTypeCollection RolePermissions
        {
            get => m_rolePermissions;

            set
            {
                m_rolePermissions = value;

                if (value == null)
                {
                    m_rolePermissions = [];
                }
            }
        }

        /// <summary>
        /// User role permissions
        /// </summary>
        /// <remarks />
        [DataMember(Name = "UserRolePermissions", IsRequired = false, Order = 9)]
        public RolePermissionTypeCollection UserRolePermissions
        {
            get => m_userRolePermissions;

            set
            {
                m_userRolePermissions = value;

                if (value == null)
                {
                    m_userRolePermissions = [];
                }
            }
        }

        /// <summary>
        /// Access restrictions
        /// </summary>
        /// <remarks />
        [DataMember(Name = "AccessRestrictions", IsRequired = false, Order = 10)]
        public ushort AccessRestrictions { get; set; }

        /// <summary>
        /// References
        /// </summary>
        /// <remarks />
        [DataMember(Name = "References", IsRequired = false, Order = 11)]
        public ReferenceNodeCollection References
        {
            get => m_references;
            set
            {
                m_references = value;

                if (value == null)
                {
                    m_references = [];
                }
            }
        }

        /// <inheritdoc/>
        public virtual ExpandedNodeId TypeId => DataTypeIds.Node;

        /// <inheritdoc/>
        public virtual ExpandedNodeId BinaryEncodingId => ObjectIds.Node_Encoding_DefaultBinary;

        /// <inheritdoc/>
        public virtual ExpandedNodeId XmlEncodingId => ObjectIds.Node_Encoding_DefaultXml;

        /// <inheritdoc/>
        public virtual ExpandedNodeId JsonEncodingId => ObjectIds.Node_Encoding_DefaultJson;

        /// <inheritdoc/>
        public virtual void Encode(IEncoder encoder)
        {
            encoder.PushNamespace(Namespaces.OpcUaXsd);

            encoder.WriteNodeId("NodeId", NodeId);
            encoder.WriteEnumerated("NodeClass", NodeClass);
            encoder.WriteQualifiedName("BrowseName", BrowseName);
            encoder.WriteLocalizedText("DisplayName", DisplayName);
            encoder.WriteLocalizedText("Description", Description);
            encoder.WriteUInt32("WriteMask", WriteMask);
            encoder.WriteUInt32("UserWriteMask", UserWriteMask);
            encoder.WriteEncodeableArray("RolePermissions", [.. RolePermissions]);
            encoder.WriteEncodeableArray("UserRolePermissions", [.. UserRolePermissions]);
            encoder.WriteUInt16("AccessRestrictions", AccessRestrictions);
            encoder.WriteEncodeableArray("References", [.. References]);

            encoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual void Decode(IDecoder decoder)
        {
            decoder.PushNamespace(Namespaces.OpcUaXsd);

            NodeId = decoder.ReadNodeId("NodeId");
            NodeClass = decoder.ReadEnumerated<NodeClass>("NodeClass");
            BrowseName = decoder.ReadQualifiedName("BrowseName");
            DisplayName = decoder.ReadLocalizedText("DisplayName");
            Description = decoder.ReadLocalizedText("Description");
            WriteMask = decoder.ReadUInt32("WriteMask");
            UserWriteMask = decoder.ReadUInt32("UserWriteMask");
            RolePermissions = decoder.ReadEncodeableArray<RolePermissionType>("RolePermissions");
            UserRolePermissions = decoder.ReadEncodeableArray<RolePermissionType>("UserRolePermissions");
            AccessRestrictions = decoder.ReadUInt16("AccessRestrictions");
            References = decoder.ReadEncodeableArray<ReferenceNode>("References");

            decoder.PopNamespace();
        }

        /// <inheritdoc/>
        public virtual bool IsEqual(IEncodeable encodeable)
        {
            if (ReferenceEquals(this, encodeable))
            {
                return true;
            }

            if (encodeable is not Node value)
            {
                return false;
            }

            if (!CoreUtils.IsEqual(NodeId, value.NodeId))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(NodeClass, value.NodeClass))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(BrowseName, value.BrowseName))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(DisplayName, value.DisplayName))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(Description, value.Description))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(WriteMask, value.WriteMask))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(UserWriteMask, value.UserWriteMask))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_rolePermissions, value.m_rolePermissions))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_userRolePermissions, value.m_userRolePermissions))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(AccessRestrictions, value.AccessRestrictions))
            {
                return false;
            }

            if (!CoreUtils.IsEqual(m_references, value.m_references))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        public virtual object Clone()
        {
            return (Node)MemberwiseClone();
        }

        /// <inheritdoc/>
        public new object MemberwiseClone()
        {
            var clone = (Node)base.MemberwiseClone();

            clone.NodeId = NodeId;
            clone.NodeClass = CoreUtils.Clone(NodeClass);
            clone.BrowseName = BrowseName;
            clone.DisplayName = CoreUtils.Clone(DisplayName);
            clone.Description = CoreUtils.Clone(Description);
            clone.WriteMask = CoreUtils.Clone(WriteMask);
            clone.UserWriteMask = CoreUtils.Clone(UserWriteMask);
            clone.m_rolePermissions = CoreUtils.Clone(m_rolePermissions);
            clone.m_userRolePermissions = CoreUtils.Clone(m_userRolePermissions);
            clone.AccessRestrictions = CoreUtils.Clone(AccessRestrictions);
            clone.m_references = CoreUtils.Clone(m_references);

            return clone;
        }

        /// <summary>
        /// Returns a copy of the node
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>A copy of the source node</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static Node Copy(ILocalNode source)
        {
            if (source == null)
            {
                return null;
            }

            switch (source.NodeClass)
            {
                case NodeClass.Object:
                    return new ObjectNode(source);
                case NodeClass.Variable:
                    return new VariableNode(source);
                case NodeClass.ObjectType:
                    return new ObjectTypeNode(source);
                case NodeClass.VariableType:
                    return new VariableTypeNode(source);
                case NodeClass.DataType:
                    return new DataTypeNode(source);
                case NodeClass.ReferenceType:
                    return new ReferenceTypeNode(source);
                case NodeClass.Method:
                    return new MethodNode(source);
                case NodeClass.View:
                    return new ViewNode(source);
                case NodeClass.Unspecified:
                    if (source is IObject)
                    {
                        return new ObjectNode(source);
                    }

                    if (source is IVariable)
                    {
                        return new VariableNode(source);
                    }

                    if (source is IObjectType)
                    {
                        return new ObjectTypeNode(source);
                    }

                    if (source is IVariableType)
                    {
                        return new VariableTypeNode(source);
                    }

                    if (source is IDataType)
                    {
                        return new DataTypeNode(source);
                    }

                    if (source is IReferenceType)
                    {
                        return new ReferenceTypeNode(source);
                    }

                    if (source is IMethod)
                    {
                        return new MethodNode(source);
                    }

                    if (source is IView)
                    {
                        return new ViewNode(source);
                    }

                    return new Node(source);
                default:
                    throw ServiceResultException.Unexpected(
                        $"Unexpected NodeClass {source.NodeClass}");
            }
        }

        /// <summary>
        /// An opaque handle that can be associated with the node.
        /// </summary>
        /// <value>The handle.</value>
        public object Handle { get; set; }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="formatProvider">The provider.</param>
        /// <returns>String representation of the object.</returns>
        /// <exception cref="FormatException"></exception>
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (format == null)
            {
                if (!string.IsNullOrEmpty(DisplayName.Text))
                {
                    return DisplayName.Text;
                }

                if (!BrowseName.IsNull)
                {
                    return BrowseName.Name;
                }

                return CoreUtils.Format(
                    "(unknown {0})",
                    NodeClass.ToString().ToLower(CultureInfo.InvariantCulture));
            }

            throw new FormatException(CoreUtils.Format("Invalid format string: '{0}'.", format));
        }

        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <returns>
        /// A <see cref="string"/> that represents the current <see cref="object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <value>The node identifier.</value>
        ExpandedNodeId INode.NodeId => NodeId;

        /// <summary>
        /// The identifier for the TypeDefinition node.
        /// </summary>
        /// <value>The type definition identifier.</value>
        public ExpandedNodeId TypeDefinitionId
        {
            get
            {
                if (m_referenceTable != null)
                {
                    return m_referenceTable.FindTarget(
                        ReferenceTypeIds.HasTypeDefinition,
                        false,
                        false,
                        null,
                        0);
                }

                return default;
            }
        }

        /// <summary>
        /// A synchronization object that can be used to safely access the node.
        /// </summary>
        /// <value>The data lock.</value>
        public object DataLock => this;

        /// <summary>
        /// A mask indicating which attributes are writeable.
        /// </summary>
        /// <value>The write mask.</value>
        AttributeWriteMask ILocalNode.WriteMask
        {
            get => (AttributeWriteMask)WriteMask;
            set => WriteMask = (uint)value;
        }

        /// <summary>
        /// A mask indicating which attributes that are writeable for the current user.
        /// </summary>
        /// <value>The user write mask.</value>
        AttributeWriteMask ILocalNode.UserWriteMask
        {
            get => (AttributeWriteMask)UserWriteMask;
            set => UserWriteMask = (uint)value;
        }

        /// <summary>
        /// The identifier for the ModellingRule node.
        /// </summary>
        /// <value>The modelling rule.</value>
        public NodeId ModellingRule =>
            (NodeId)ReferenceTable.FindTarget(
                ReferenceTypeIds.HasModellingRule,
                false,
                false,
                null,
                0);

        /// <summary>
        /// The collection of references for the node.
        /// </summary>
        /// <value>The references.</value>
        IReferenceCollection ILocalNode.References => ReferenceTable;

        /// <summary>
        /// Creates a copy of the node.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>Copy of the node</returns>
        public ILocalNode CreateCopy(NodeId nodeId)
        {
            Node node = Copy(this);
            node.NodeId = nodeId;
            return node;
        }

        /// <summary>
        /// Returns true if the node supports the attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>True if the node supports the attribute.</returns>
        public virtual bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.NodeId:
                case Attributes.NodeClass:
                case Attributes.BrowseName:
                case Attributes.DisplayName:
                case Attributes.Description:
                case Attributes.WriteMask:
                case Attributes.UserWriteMask:
                case Attributes.RolePermissions:
                case Attributes.UserRolePermissions:
                case Attributes.AccessRestrictions:
                    return true;
                default:
                    Attributes.ThrowIfOutOfRange(attributeId);
                    return false;
            }
        }

        /// <summary>
        /// Reads the value of a attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of read operation.</returns>
        public ServiceResult Read(IOperationContext context, uint attributeId, DataValue value)
        {
            if (!SupportsAttribute(attributeId))
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            value.WrappedValue = Read(attributeId);
            value.StatusCode = StatusCodes.Good;

            if (attributeId == Attributes.Value)
            {
                value.SourceTimestamp = DateTime.UtcNow;
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        public ServiceResult Write(uint attributeId, DataValue value)
        {
            if (!SupportsAttribute(attributeId))
            {
                return StatusCodes.BadAttributeIdInvalid;
            }

            // check for read only attributes.
            switch (attributeId)
            {
                case Attributes.NodeId:
                case Attributes.NodeClass:
                    return StatusCodes.BadNotWritable;
                default:
                    // check data type.
                    if (attributeId != Attributes.Value &&
                        Attributes.GetDataTypeId(attributeId) !=
                            TypeInfo.GetDataTypeId(value, null)) // TODO: Pass message context
                    {
                        return StatusCodes.BadTypeMismatch;
                    }
                    return Write(attributeId, value.WrappedValue);
            }
        }

        /// <summary>
        /// A searchable table of references for the node.
        /// </summary>
        /// <value>The reference table.</value>
        public ReferenceCollection ReferenceTable => m_referenceTable ??= [];

        /// <summary>
        /// Returns true if the reference exist.
        /// </summary>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">if set to <c>true</c> [is inverse].</param>
        /// <param name="targetId">The target id.</param>
        /// <returns>True if the reference exist.</returns>
        public bool ReferenceExists(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            return ReferenceTable.Exists(referenceTypeId, isInverse, targetId, false, null);
        }

        /// <summary>
        /// Returns all targets of the specified reference type.
        /// </summary>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">if set to <c>true</c> [is inverse].</param>
        /// <returns>All targets of the specified reference type.</returns>
        public IList<IReference> Find(NodeId referenceTypeId, bool isInverse)
        {
            return ReferenceTable.Find(referenceTypeId, isInverse, false, null);
        }

        /// <summary>
        /// Returns a target of the specified reference type.
        /// </summary>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">if set to <c>true</c> [is inverse].</param>
        /// <param name="index">The index.</param>
        /// <returns>A target of the specified reference type.</returns>
        public ExpandedNodeId FindTarget(NodeId referenceTypeId, bool isInverse, int index)
        {
            return ReferenceTable.FindTarget(referenceTypeId, isInverse, false, null, index);
        }

        /// <summary>
        /// Returns the supertype for the Node if one exists.
        /// </summary>
        /// <param name="typeTree">The type tree.</param>
        /// <returns>The supertype for the Node if one exists.</returns>
        /// <remarks>
        /// Includes subtypes of HasSubtype if typeTree != null.
        /// </remarks>
        public ExpandedNodeId GetSuperType(ITypeTable typeTree)
        {
            if (m_referenceTable != null)
            {
                return m_referenceTable.FindTarget(
                    ReferenceTypeIds.HasSubtype,
                    true,
                    typeTree != null,
                    typeTree,
                    0);
            }

            return default;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(NodeId);
            hash.Add(NodeClass);
            hash.Add(BrowseName);
            return hash.ToHashCode();
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected virtual Variant Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.NodeId:
                    return NodeId;
                case Attributes.NodeClass:
                    return Variant.From(NodeClass);
                case Attributes.BrowseName:
                    return BrowseName;
                case Attributes.DisplayName:
                    return DisplayName;
                case Attributes.Description:
                    return Description;
                case Attributes.WriteMask:
                    return WriteMask;
                case Attributes.UserWriteMask:
                    return UserWriteMask;
                case Attributes.RolePermissions:
                    return Variant.FromStructure(m_rolePermissions);
                case Attributes.UserRolePermissions:
                    return Variant.FromStructure(m_userRolePermissions);
                case Attributes.AccessRestrictions:
                    return AccessRestrictions;
                default:
                    Attributes.ThrowIfOutOfRange(attributeId);
                    return false;
            }
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected virtual ServiceResult Write(uint attributeId, Variant value)
        {
            switch (attributeId)
            {
                case Attributes.BrowseName:
                    BrowseName = (QualifiedName)value;
                    break;
                case Attributes.DisplayName:
                    DisplayName = (LocalizedText)value;
                    break;
                case Attributes.Description:
                    Description = (LocalizedText)value;
                    break;
                case Attributes.WriteMask:
                    WriteMask = (uint)value;
                    break;
                case Attributes.UserWriteMask:
                    UserWriteMask = (uint)value;
                    break;
                case Attributes.RolePermissions:
                    m_rolePermissions = value.GetStructureArray<RolePermissionType>();
                    break;
                case Attributes.UserRolePermissions:
                    m_userRolePermissions = value.GetStructureArray<RolePermissionType>();
                    break;
                case Attributes.AccessRestrictions:
                    AccessRestrictions = (ushort)value;
                    break;
                default:
                    Attributes.ThrowIfOutOfRange(attributeId);
                    return StatusCodes.BadAttributeIdInvalid;
            }

            return ServiceResult.Good;
        }

        private ReferenceCollection m_referenceTable;
        private ArrayOf<RolePermissionType> m_rolePermissions;
        private ArrayOf<RolePermissionType> m_userRolePermissions;
        private ReferenceNodeCollection m_references;
    }
}
