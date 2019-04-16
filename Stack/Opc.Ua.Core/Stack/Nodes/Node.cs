/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{    
    #region Node Class
    /// <summary>
    /// A node in the server address space.
    /// </summary>
    public partial class Node : IFormattable, ILocalNode
    {
        #region Constructors
        /// <summary>
        /// Creates a node from a reference desciption.
        /// </summary>
        /// <param name="reference">The reference.</param>
        public Node(ReferenceDescription reference)
        {
            Initialize();

            m_nodeId      = (NodeId)reference.NodeId;
            m_nodeClass   = reference.NodeClass;
            m_browseName  = reference.BrowseName;
            m_displayName = reference.DisplayName;
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
                this.NodeId        = source.NodeId;
                this.NodeClass     = source.NodeClass;
                this.BrowseName    = source.BrowseName;
                this.DisplayName   = source.DisplayName;
                this.Description   = source.Description;
                this.WriteMask     = (uint)source.WriteMask;
                this.UserWriteMask = (uint)source.UserWriteMask;
            }
        }

        /// <summary>
        /// Returns a copy of the node
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>A copy of the source node</returns>
        public static Node Copy(ILocalNode source)
        {
            if (source == null)
            {
                return null;
            }

            switch (source.NodeClass)
            {
                case NodeClass.Object: return new ObjectNode(source);
                case NodeClass.Variable: return new VariableNode(source);
                case NodeClass.ObjectType: return new ObjectTypeNode(source);
                case NodeClass.VariableType: return new VariableTypeNode(source);
                case NodeClass.DataType: return new DataTypeNode(source);
                case NodeClass.ReferenceType: return new ReferenceTypeNode(source);
                case NodeClass.Method: return new MethodNode(source);
                case NodeClass.View: return new ViewNode(source);
            }
       
            if (source is IObject) return new ObjectNode(source);
            if (source is IVariable) return new VariableNode(source);
            if (source is IObjectType) return new ObjectTypeNode(source);
            if (source is IVariableType) return new VariableTypeNode(source);
            if (source is IDataType) return new DataTypeNode(source);
            if (source is IReferenceType) return new ReferenceTypeNode(source);
            if (source is IMethod) return new MethodNode(source);
            if (source is IView) return new ViewNode(source);  

            return new Node(source);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// An opaque handle that can be assoociated with the node.
        /// </summary>
        /// <value>The handle.</value>
        public object Handle
        {
            get { return m_handle;  }
            set { m_handle = value; }
        }
        #endregion

        #region IFormattable Members
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="provider">The provider.</param>
        /// <returns>String representation of the object.</returns>
        public string ToString(string format, IFormatProvider provider)
        {
            if (format == null)
            {
                if (m_displayName != null && !String.IsNullOrEmpty(m_displayName.Text))
                {
                    return m_displayName.Text;
                }

                if (!QualifiedName.IsNull(m_browseName))
                {
                    return m_browseName.Name;
                }

                return Utils.Format("(unknown {0})", ((NodeClass)m_nodeClass).ToString().ToLower());
            }
        
            throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
        }
        
        /// <summary>
        /// Returns the string representation of the object.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }
        #endregion
               
        #region INode Methods
        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <value>The node identifier.</value>
        ExpandedNodeId Opc.Ua.INode.NodeId
        {
             get { return m_nodeId; }
        }

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
                    return m_referenceTable.FindTarget(ReferenceTypeIds.HasTypeDefinition, false, false, null, 0); 
                 }

                 return null;
             }
        }
        #endregion
        
        #region ILocalNode Methods
        /// <summary>
        /// A synchronization object that can be used to safely access the node.
        /// </summary>
        /// <value>The data lock.</value>
        public object DataLock
        {
             get { return this; }
        }

        /// <summary>
        /// A mask indicating which attributes are writeable.
        /// </summary>
        /// <value>The write mask.</value>
        AttributeWriteMask Opc.Ua.ILocalNode.WriteMask
        {
             get { return (AttributeWriteMask)m_writeMask;  }
             set { m_writeMask = (uint)value; }
        }

        /// <summary>
        /// A mask indicating which attributes that are writeable for the current user.
        /// </summary>
        /// <value>The user write mask.</value>
        AttributeWriteMask Opc.Ua.ILocalNode.UserWriteMask
        {
             get { return (AttributeWriteMask)m_userWriteMask;  }
             set { m_userWriteMask = (uint)value; }
        }

        /// <summary>
        /// The identifier for the ModellingRule node.
        /// </summary>
        /// <value>The modelling rule.</value>
        public NodeId ModellingRule 
        { 
            get
            {
                return (NodeId)ReferenceTable.FindTarget(ReferenceTypeIds.HasModellingRule, false, false, null, 0);
            }
        }

        /// <summary>
        /// The collection of references for the node.
        /// </summary>
        /// <value>The references.</value>
        IReferenceCollection ILocalNode.References
        {
            get { return ReferenceTable; }
        }

        /// <summary>
        /// Creates a copy of the node.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>Copy of the node</returns>
        public ILocalNode CreateCopy(NodeId nodeId)
        {
            Node node = Node.Copy(this);
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
                {
                    return true;
                }
            }

            return false;
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

            value.Value      = Read(attributeId);
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
                {
                    return StatusCodes.BadNotWritable;
                }
            }

            // check data type.
            if (attributeId != Attributes.Value)
            {
                if (Attributes.GetDataTypeId(attributeId) != TypeInfo.GetDataTypeId(value))
                {
                    return StatusCodes.BadTypeMismatch;
                }
            }

            return Write(attributeId, value.Value);
        }
        #endregion

        #region Public Members
        /// <summary>
        /// A searchable table of references for the node.
        /// </summary>
        /// <value>The reference table.</value>
        public ReferenceCollection ReferenceTable
        {
             get 
             { 
                 if (m_referenceTable == null)
                 {
                     m_referenceTable = new ReferenceCollection();
                 }

                 return m_referenceTable;  
             }
        }
                
        /// <summary>
        /// Returns true if the reference exist.
        /// </summary>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">if set to <c>true</c> [is inverse].</param>
        /// <param name="targetId">The target id.</param>
        /// <returns>True if the reference exist.</returns>
        public bool ReferenceExists(
            NodeId         referenceTypeId, 
            bool           isInverse, 
            ExpandedNodeId targetId)
        {
            return ReferenceTable.Exists(referenceTypeId, isInverse, targetId, false, null);
        }
                
        /// <summary>
        /// Returns all targets of the specified reference type.
        /// </summary>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">if set to <c>true</c> [is inverse].</param>
        /// <returns>All targets of the specified reference type.</returns>
        public IList<IReference> Find(
            NodeId referenceTypeId, 
            bool   isInverse)
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
        public ExpandedNodeId FindTarget(
            NodeId referenceTypeId, 
            bool   isInverse,
            int    index)
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
                 return m_referenceTable.FindTarget(ReferenceTypeIds.HasSubtype, true, typeTree != null, typeTree, 0);
             }

             return null; 
        }
        #endregion
        
        #region Protected Methods
        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected virtual object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.NodeId:              return m_nodeId;
                case Attributes.NodeClass:           return m_nodeClass;
                case Attributes.BrowseName:          return m_browseName;
                case Attributes.DisplayName:         return m_displayName;
                case Attributes.Description:         return m_description;
                case Attributes.WriteMask:           return m_writeMask;
                case Attributes.UserWriteMask:       return m_userWriteMask;
                case Attributes.RolePermissions:     return m_rolePermissions;
                case Attributes.UserRolePermissions: return m_userRolePermissions;
                case Attributes.AccessRestrictions:  return m_accessRestrictions;
            }

            return false;
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected virtual ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.BrowseName:          { m_browseName          = (QualifiedName)value; break; }
                case Attributes.DisplayName:         { m_displayName         = (LocalizedText)value; break; }
                case Attributes.Description:         { m_description         = (LocalizedText)value; break; }
                case Attributes.WriteMask:           { m_writeMask           = (uint)value; break; }
                case Attributes.UserWriteMask:       { m_userWriteMask       = (uint)value; break; }
                case Attributes.RolePermissions:     { m_rolePermissions     = (RolePermissionTypeCollection)value; break; }
                case Attributes.UserRolePermissions: { m_userRolePermissions = (RolePermissionTypeCollection)value; break; }
                case Attributes.AccessRestrictions:  { m_accessRestrictions  = (ushort)value; break; }

                default:
                {
                    return StatusCodes.BadAttributeIdInvalid;
                }
            }

            return ServiceResult.Good;
        }
        #endregion

        #region Private Fields
        private object m_handle = null;
        private ReferenceCollection m_referenceTable;
        #endregion
    }
    #endregion
    
    #region ReferenceNode Class
	/// <summary>
	/// A node in the server address space.
	/// </summary>
    public partial class ReferenceNode : IReference, IComparable, IFormattable
    {
        #region Constructors
        /// <summary>
        /// Initializes the reference.
        /// </summary>
        /// <param name="referenceTypeId">The reference type id.</param>
        /// <param name="isInverse">if set to <c>true</c> [is inverse].</param>
        /// <param name="targetId">The target id.</param>
        public ReferenceNode(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            m_referenceTypeId = referenceTypeId;
            m_isInverse       = isInverse;
            m_targetId        = targetId;
        }
        #endregion
        
        #region IFormattable Members
        /// <summary>
        /// Returns a string representation of the HierarchyBrowsePath.
        /// </summary>
        /// <returns>
        /// A <see cref="T:System.String"/> that represents the current <see cref="T:System.Object"/>.
        /// </returns>
        public override string ToString()
        {
            return ToString(null, null);
        }

        /// <summary>
        /// Returns a string representation of the HierarchyBrowsePath.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="provider">The provider.</param>
        /// <returns>A string representation of the HierarchyBrowsePath.</returns>
        public string ToString(string format, IFormatProvider provider)
        {
            if (format != null)
            {
                throw new FormatException(Utils.Format("Invalid format string: '{0}'.", format));
            }

            string referenceType = null;

            if (m_referenceTypeId != null && m_referenceTypeId.IdType == IdType.Numeric && m_referenceTypeId.NamespaceIndex == 0)
            {
                referenceType = ReferenceTypes.GetBrowseName((uint)m_referenceTypeId.Identifier);
            }
            
            if (referenceType == null)
            {
                referenceType = Utils.Format("{0}", m_referenceTypeId);
            }

            if (m_isInverse)
            {
                return Utils.Format("<!{0}>{1}", referenceType, m_targetId);
            }
            else
            {
                return Utils.Format("<{0}>{1}", referenceType, m_targetId);
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>.</param>
        /// <returns>
        /// true if the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>; otherwise, false.
        /// </returns>
        /// <exception cref="T:System.NullReferenceException">
        /// The <paramref name="obj"/> parameter is null.
        /// </exception>
        public override bool Equals(object obj)
        {
            return CompareTo(obj) == 0;
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"/>.
        /// </returns>
        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        /// <summary>
        /// Returns true if the objects are equal.
        /// </summary>
        /// <param name="a">ReferenceNode A.</param>
        /// <param name="b">The ReferenceNode B.</param>
        /// <returns>The result of the operator.Returns true if the objects are equal.</returns>
		public static bool operator==(ReferenceNode a, object b) 
		{
			if (Object.ReferenceEquals(a, null))
			{
				return Object.ReferenceEquals(b, null);
			}
            
			return a.CompareTo(b) == 0;
		}

        /// <summary>
        /// Returns true if the objects are not equal.
        /// </summary>
        /// <param name="a">ReferenceNode A.</param>
        /// <param name="b">The ReferenceNode B.</param>
        /// <returns>The result of the operator.Returns true if the objects are not equal.</returns>
		public static bool operator!=(ReferenceNode a, object b) 
		{
			if (Object.ReferenceEquals(a, null))
			{
				return !Object.ReferenceEquals(b, null);
			}
            
			return a.CompareTo(b) != 0;
		}		
        #endregion        
    
        #region IComparable Members
        /// <summary>
        /// Compares the current instance with another object of the same type and returns an integer that indicates whether the current instance precedes, follows, or occurs in the same position in the sort order as the other object.
        /// </summary>
        /// <param name="obj">An object to compare with this instance.</param>
        /// <returns>
        /// A 32-bit signed integer that indicates the relative order of the objects being compared. The return value has these meanings:
        /// Value
        /// Meaning
        /// Less than zero
        /// This instance is less than <paramref name="obj"/>.
        /// Zero
        /// This instance is equal to <paramref name="obj"/>.
        /// Greater than zero
        /// This instance is greater than <paramref name="obj"/>.
        /// </returns>
        /// <exception cref="T:System.ArgumentException">
        /// 	<paramref name="obj"/> is not the same type as this instance.
        /// </exception>
        public int CompareTo(object obj)
        {
            if (Object.ReferenceEquals(obj, null))
            {
                return +1;
            }

            if (Object.ReferenceEquals(obj, this))
            {
                return 0;
            }

            ReferenceNode reference = obj as ReferenceNode;

            if (reference == null)
            {
                return -1;
            }
            
            if (Object.ReferenceEquals(m_referenceTypeId, null))
            {
                return (Object.ReferenceEquals(reference.m_referenceTypeId, null))?0:-1;
            }

            int result = m_referenceTypeId.CompareTo(reference.m_referenceTypeId);

            if (result != 0)
            {
                return result;           
            }

            if (reference.m_isInverse != this.m_isInverse)
            {
                return (this.m_isInverse)?+1:-1;
            }
            
            if (Object.ReferenceEquals(m_targetId, null))
            {
                return (Object.ReferenceEquals(reference.m_targetId, null))?0:-1;
            }

            return m_targetId.CompareTo(reference.m_targetId);
        }
        #endregion
    }
    #endregion

    #region InstanceNode Class
    /// <summary>
    /// An instance node in the server address space.
    /// </summary>
    public partial class InstanceNode : Node
    {
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public InstanceNode(ILocalNode source) : base(source)
        {
        }
        #endregion
    }
    #endregion

    #region TypeNode Class
    /// <summary>
    /// An type node in the server address space.
    /// </summary>
    public partial class TypeNode : Node
    {
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public TypeNode(ILocalNode source) : base(source)
        {
        }
        #endregion
    }
    #endregion

    #region VariableNode Class
	/// <summary>
	/// A variable node in the server address space.
	/// </summary>
    public partial class VariableNode : IVariable
    {
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public VariableNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.Variable;

            IVariable variable = source as IVariable;

            if (variable != null)
            {
                this.DataType = variable.DataType;
                this.ValueRank = variable.ValueRank;
                this.AccessLevel = variable.AccessLevel;
                this.UserAccessLevel = variable.UserAccessLevel;
                this.MinimumSamplingInterval = variable.MinimumSamplingInterval;
                this.Historizing = variable.Historizing;

                object value = variable.Value;

                if (value == null)
                {
                    value = TypeInfo.GetDefaultValue(variable.DataType, variable.ValueRank);
                }

                this.Value = new Variant(value);

                if (variable.ArrayDimensions != null)
                {
                    this.ArrayDimensions = new UInt32Collection(variable.ArrayDimensions);
                }
            }
        }
        #endregion
        
        #region IVariableBase Members
        /// <summary>
        /// The value attribute.
        /// </summary>
        /// <value>The value.</value>
        object IVariableBase.Value
        {
            get
            {
                return m_value.Value;
            }

            set
            {
                m_value.Value = value;
            }
        }

        /// <summary>
        /// The number in each dimension of an array value.
        /// </summary>
        /// <value>The array dimensions.</value>
        IList<uint> IVariableBase.ArrayDimensions
        {
            get
            {
                return m_arrayDimensions;
            }

            set
            {
                if (value == null)
                {
                    m_arrayDimensions = new UInt32Collection();
                }
                else
                {
                    m_arrayDimensions = new UInt32Collection(value);
                }
            }
        }
        #endregion

        #region Protected Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>True if  the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Value:
                case Attributes.DataType:
                case Attributes.ValueRank:
                case Attributes.AccessLevel:
                case Attributes.AccessLevelEx:
                case Attributes.UserAccessLevel:
                case Attributes.MinimumSamplingInterval:
                case Attributes.Historizing:
                {
                    return true;
                }

                case Attributes.ArrayDimensions:
                {
                    if (m_arrayDimensions == null || m_arrayDimensions.Count == 0)
                    {
                        return false;
                    }

                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.DataType:                return m_dataType;
                case Attributes.ValueRank:               return m_valueRank;
                case Attributes.AccessLevel:             return m_accessLevel;
                case Attributes.UserAccessLevel:         return m_userAccessLevel;
                case Attributes.MinimumSamplingInterval: return m_minimumSamplingInterval;
                case Attributes.Historizing:             return m_historizing;
                case Attributes.AccessLevelEx:           return m_accessLevelEx;

                // values are copied when the are written so then can be safely returned.
                case Attributes.Value:
                {
                    return m_value.Value;
                }

                // array dimensions attribute is not support if it is empty.
                case Attributes.ArrayDimensions:
                {
                    if (m_arrayDimensions == null || m_arrayDimensions.Count == 0)
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }

                    return m_arrayDimensions.ToArray();
                }
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.AccessLevel:             { m_accessLevel             = (byte)value;   return ServiceResult.Good; }
                case Attributes.UserAccessLevel:         { m_userAccessLevel         = (byte)value;   return ServiceResult.Good; }
                case Attributes.AccessLevelEx:           { m_accessLevelEx           = (uint)value;   return ServiceResult.Good; }
                case Attributes.MinimumSamplingInterval: { m_minimumSamplingInterval = (int)value;    return ServiceResult.Good; }
                case Attributes.Historizing:             { m_historizing             = (bool)value;   return ServiceResult.Good; }

                // values are copied when the are written so then can be safely returned on read.
                case Attributes.Value:
                {
                    m_value.Value = Utils.Clone(value);
                    return ServiceResult.Good;
                }

                case Attributes.DataType: 
                { 
                    NodeId dataType = (NodeId)value;

                    // must ensure the value is of the correct datatype.
                    if (dataType != m_dataType)
                    {
                        m_value.Value = TypeInfo.GetDefaultValue(dataType, m_valueRank);
                    }

                    m_dataType = dataType;
                    return ServiceResult.Good;
                }
 
                case Attributes.ValueRank:
                {
                    int valueRank = (int)value;

                    if (valueRank != m_valueRank)
                    {
                        m_value.Value = TypeInfo.GetDefaultValue(m_dataType, valueRank);
                    }

                    m_valueRank = valueRank;
                    
                    return ServiceResult.Good;
                }
                    
                case Attributes.ArrayDimensions:
                {
                    m_arrayDimensions = new UInt32Collection((uint[])value);

                    // ensure number of dimensions is correct.
                    if (m_arrayDimensions.Count > 0)
                    {
                        if (m_arrayDimensions.Count != m_valueRank)
                        {
                            m_valueRank = m_arrayDimensions.Count;
                            m_value.Value = TypeInfo.GetDefaultValue(m_dataType, m_valueRank);
                        }
                    }

                    return ServiceResult.Good;
                }
            }

            return base.Write(attributeId, value);
        }        
        #endregion
    }
    #endregion

    #region ObjectNode Class
    /// <summary>
    /// An object node in the server address space.
    /// </summary>
    public partial class ObjectNode : IObject
    {        
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public ObjectNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.Object;

            IObject node = source as IObject;

            if (node != null)
            {
                this.EventNotifier = node.EventNotifier;
            }
        }
        #endregion
        
        #region Overridden Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>True if the value of an attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier:
                {
                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier: return m_eventNotifier;
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>Result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier: { m_eventNotifier = (byte)value; return ServiceResult.Good; }
            }

            return base.Write(attributeId, value);
        }
        #endregion
    }
    #endregion
        
    #region ObjectTypeNode Class
	/// <summary>
	/// An object type node in the server address space.
	/// </summary>
    public partial class ObjectTypeNode : IObjectType
    {    
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public ObjectTypeNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.ObjectType;

            IObjectType node = source as IObjectType;

            if (node != null)
            {
                this.IsAbstract = node.IsAbstract;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                {
                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>Tthe node supports the specified attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract: return m_isAbstract;
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract: { m_isAbstract = (bool)value; return ServiceResult.Good; }
            }

            return base.Write(attributeId, value);
        }
        #endregion
    }
    #endregion
            
    #region VariableTypeNode Class
    /// <summary>
    /// A variable type node in the server address space.
    /// </summary>
    public partial class VariableTypeNode : IVariableType
    {            
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public VariableTypeNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.VariableType;

            IVariableType node = source as IVariableType;

            if (node != null)
            {
                this.IsAbstract = node.IsAbstract;
                this.Value = new Variant(node.Value);
                this.DataType = node.DataType;
                this.ValueRank = node.ValueRank;

                if (node.ArrayDimensions != null)
                {
                    this.ArrayDimensions = new UInt32Collection(node.ArrayDimensions);
                }
            }
        }
        #endregion
     
        #region IVariableBase Members
        /// <summary>
        /// The value attribute.
        /// </summary>
        /// <value>The value.</value>
        object IVariableBase.Value
        {
            get
            {
                return m_value.Value;
            }

            set
            {
                m_value.Value = value;
            }
        }

        /// <summary>
        /// The number in each dimension of an array value.
        /// </summary>
        /// <value>The number in each dimension of an array value.</value>
        IList<uint> IVariableBase.ArrayDimensions
        {
            get
            {
                return m_arrayDimensions;
            }

            set
            {
                if (value == null)
                {
                    m_arrayDimensions = new UInt32Collection();
                }
                else
                {
                    m_arrayDimensions = new UInt32Collection(value);
                }
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Value:
                {
                    return (m_value.Value != null);
                }

                case Attributes.ValueRank:
                case Attributes.DataType:
                case Attributes.IsAbstract:
                {
                    return true;
                }

                case Attributes.ArrayDimensions:
                {
                    if (m_arrayDimensions == null || m_arrayDimensions.Count == 0)
                    {
                        return false;
                    }

                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.DataType:        return m_dataType;
                case Attributes.ValueRank:       return m_valueRank;
                    
                // values are copied when the are written so then can be safely returned.
                case Attributes.Value:
                {
                    return m_value.Value;
                }

                case Attributes.ArrayDimensions:
                {
                    if (m_arrayDimensions == null || m_arrayDimensions.Count == 0)
                    {
                        return StatusCodes.BadAttributeIdInvalid;
                    }

                    return m_arrayDimensions.ToArray();
                }
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {                    
                // values are copied when the are written so then can be safely returned on read.
                case Attributes.Value:
                {
                    m_value.Value = Utils.Clone(value);
                    return ServiceResult.Good;
                }

                case Attributes.DataType: 
                { 
                    NodeId dataType = (NodeId)value;

                    // must ensure the value is of the correct datatype.
                    if (dataType != m_dataType)
                    {
                        m_value.Value = null;
                    }

                    m_dataType = dataType;
                    return ServiceResult.Good;
                }
 
                case Attributes.ValueRank:
                {
                    int valueRank = (int)value;

                    if (valueRank != m_valueRank)
                    {
                        m_value.Value = null;
                    }

                    m_valueRank = valueRank;

                    return ServiceResult.Good;
                }
                    
                case Attributes.ArrayDimensions:
                {
                    m_arrayDimensions = new UInt32Collection((uint[])value);

                    // ensure number of dimensions is correct.
                    if (m_arrayDimensions.Count > 0)
                    {
                        if (m_valueRank != m_arrayDimensions.Count)
                        {                        
                            m_valueRank = m_arrayDimensions.Count;   
                            m_value.Value = null;
                        }
                    }

                    return ServiceResult.Good;
                }
            }

            return base.Write(attributeId, value);
        }
        #endregion
    }
    #endregion

    #region ReferenceTypeNode Class
	/// <summary>
	/// A reference type node in the server address space.
	/// </summary>
    public partial class ReferenceTypeNode :IReferenceType
    {
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public ReferenceTypeNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.ReferenceType;

            IReferenceType node = source as IReferenceType;

            if (node != null)
            {
                this.IsAbstract = node.IsAbstract;
                this.InverseName = node.InverseName;
                this.Symmetric = node.Symmetric;
            }
        }
        #endregion
     
        #region Overridden Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                case Attributes.InverseName:
                case Attributes.Symmetric:
                {
                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:  return m_isAbstract;
                case Attributes.InverseName: return m_inverseName; 
                case Attributes.Symmetric:   return m_symmetric; 
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:  { m_isAbstract  = (bool)value;          return ServiceResult.Good; }
                case Attributes.InverseName: { m_inverseName = (LocalizedText)value; return ServiceResult.Good; }
                case Attributes.Symmetric:   { m_symmetric   = (bool)value;          return ServiceResult.Good; }
            }

            return base.Write(attributeId, value);
        }
        #endregion
    }
    #endregion
    
    #region MethodNode Class
	/// <summary>
	/// A method node in the server address space.
	/// </summary>
    public partial class MethodNode : IMethod
    {
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public MethodNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.Method;

            IMethod node = source as IMethod;

            if (node != null)
            {
                this.Executable = node.Executable;
                this.UserExecutable = node.UserExecutable;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Executable:
                case Attributes.UserExecutable:
                {
                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.Executable:     return m_executable;
                case Attributes.UserExecutable: return m_userExecutable; 
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.Executable:     { m_executable     = (bool)value; return ServiceResult.Good; }
                case Attributes.UserExecutable: { m_userExecutable = (bool)value; return ServiceResult.Good; }
            }

            return base.Write(attributeId, value);
        }
        #endregion
    }
    #endregion
    
    #region ViewNode Class
	/// <summary>
	/// A view node in the server address space.
	/// </summary>
    public partial class ViewNode : IView
    {
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public ViewNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.View;

            IView node = source as IView;

            if (node != null)
            {
                this.EventNotifier = node.EventNotifier;
                this.ContainsNoLoops = node.ContainsNoLoops;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier:
                case Attributes.ContainsNoLoops:
                {
                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier:   return m_eventNotifier;
                case Attributes.ContainsNoLoops: return m_containsNoLoops; 
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="value">The value.</param>
        /// <returns>The write operation result.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.EventNotifier:   { m_eventNotifier   = (byte)value; return ServiceResult.Good; }
                case Attributes.ContainsNoLoops: { m_containsNoLoops = (bool)value; return ServiceResult.Good; }
            }

            return base.Write(attributeId, value);
        }
        #endregion
    }
    #endregion
        
    #region DataTypeNode Class
	/// <summary>
	/// A view node in the server address space.
	/// </summary>
    public partial class DataTypeNode : IDataType
    {
        #region Constructors
        /// <summary>
        /// Creates a node from another node (copies attributes - not references).
        /// </summary>
        /// <param name="source">The source.</param>
        public DataTypeNode(ILocalNode source) : base(source)
        {
            this.NodeClass = NodeClass.DataType;

            IDataType node = source as IDataType;

            if (node != null)
            {
                this.IsAbstract = node.IsAbstract;
            }
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Whether the node supports the specified attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>True if the node supports the specified attribute.</returns>
        public override bool SupportsAttribute(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract:
                case Attributes.DataTypeDefinition:
                {
                    return true;
                }
            }

            return base.SupportsAttribute(attributeId);
        }

        /// <summary>
        /// Reads the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <returns>The value of an attribute.</returns>
        protected override object Read(uint attributeId)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract: return m_isAbstract;
                case Attributes.DataTypeDefinition: return m_dataTypeDefinition;
            }

            return base.Read(attributeId);
        }

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of write operation.</returns>
        protected override ServiceResult Write(uint attributeId, object value)
        {
            switch (attributeId)
            {
                case Attributes.IsAbstract: { m_isAbstract = (bool)value; return ServiceResult.Good; }
                case Attributes.DataTypeDefinition: { m_dataTypeDefinition = (ExtensionObject)value; return ServiceResult.Good; }
            }

            return base.Write(attributeId, value);
        }
        #endregion
    }
    #endregion
}
