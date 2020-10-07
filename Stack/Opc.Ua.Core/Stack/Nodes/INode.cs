/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that describes a node.
    /// </summary>
    public interface INode
    {
        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <value>The node identifier.</value>
        ExpandedNodeId NodeId { get; }

        /// <summary>
        /// The node class.
        /// </summary>
        /// <value>The node class.</value>
        NodeClass NodeClass { get; }
        
        /// <summary>
        /// The locale independent browse name.
        /// </summary>
        /// <value>The name of the browse.</value>
        QualifiedName BrowseName { get; }

        /// <summary>
        /// The localized display name.
        /// </summary>
        /// <value>The display name.</value>
        LocalizedText DisplayName { get; }

        /// <summary>
        /// The identifier for the TypeDefinition node.
        /// </summary>
        /// <value>The type definition identifier.</value>
        ExpandedNodeId TypeDefinitionId { get; }
    }
    
    /// <summary>
    /// An interface to an object that describes a node local to the server.
    /// </summary>
    public interface ILocalNode : INode
    {
        /// <summary>
        /// A synchronization object that can be used to safely access the node.
        /// </summary>
        /// <value>The data lock.</value>
        object DataLock { get; }

        /// <summary>
        /// A handle assigned to the node.
        /// </summary>
        /// <value>The handle.</value>
        object Handle { get; set; }

        /// <summary>
        /// The node identifier.
        /// </summary>
        /// <value>The node identifier.</value>
        new NodeId NodeId { get; }

        /// <summary>
        /// The locale independent browse name.
        /// </summary>
        /// <value>The name of the browse.</value>
        new QualifiedName BrowseName { get; set; }

        /// <summary>
        /// The localized display name.
        /// </summary>
        /// <value>The display name.</value>
        new LocalizedText DisplayName { get; set; }

        /// <summary>
        /// The localized description
        /// </summary>
        /// <value>The description.</value>
        LocalizedText Description { get; set; }

        /// <summary>
        /// A mask indicating which attributes are writeable.
        /// </summary>
        /// <value>The write mask.</value>
        AttributeWriteMask WriteMask { get; set; }

        /// <summary>
        /// A mask indicating which attributes that are writeable for the current user.
        /// </summary>
        /// <value>The user write mask.</value>
        AttributeWriteMask UserWriteMask { get; set; }

        /// <summary>
        /// The identifier for the ModellingRule node.
        /// </summary>
        /// <value>The modelling rule.</value>
        NodeId ModellingRule { get; }

        /// <summary>
        /// The collection of references for the node.
        /// </summary>
        /// <value>The references.</value>
        IReferenceCollection References { get; }

        /// <summary>
        /// Creates a copy of the node.
        /// </summary>
        /// <param name="nodeId">The node identifier.</param>
        /// <returns>Copy of the node.</returns>
        ILocalNode CreateCopy(NodeId nodeId);

        /// <summary>
        /// Returns true if the node supports the attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <returns>True if the node supports the attribute.</returns>
        bool SupportsAttribute(uint attributeId);

        /// <summary>
        /// Reads the value of a attribute.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of read operation.</returns>
        ServiceResult Read(IOperationContext context, uint attributeId, DataValue value);

        /// <summary>
        /// Writes the value of an attribute.
        /// </summary>
        /// <param name="attributeId">The attribute identifier.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result of Write operation</returns>
        ServiceResult Write(uint attributeId, DataValue value);
    }
        
    /// <summary>
    /// An interface to an object that describes an ObjectType node.
    /// </summary>
    public interface IObjectType : ILocalNode
    {
        /// <summary>
        /// Whether the type is an abstract type.
        /// </summary>
        bool IsAbstract { get; set; }
    }        

    /// <summary>
    /// An interface to an object that describes an Object node.
    /// </summary>
    public interface IObject : ILocalNode
    {
        /// <summary>
        /// Whether the object supports events.
        /// </summary>
        byte EventNotifier { get; set; }
    }

    /// <summary>
    /// An interface to an object that describes either a Variable or a VariableType node.
    /// </summary>
    public interface IVariableBase : ILocalNode
    {
        /// <summary>
        /// The value attribute.
        /// </summary>
        /// <value>The value.</value>
        object Value { get; set; }

        /// <summary>
        /// The data type for the value attribute.
        /// </summary>
        /// <value>The type of the data.</value>
        NodeId DataType { get; set; }

        /// <summary>
        /// Specifies whether the the value is an array or scalar.
        /// </summary>
        /// <value>The value rank.</value>
        int ValueRank { get; set; }

        /// <summary>
        /// The number in each dimension of an array value.
        /// </summary>
        /// <value>The array dimensions.</value>
        IList<uint> ArrayDimensions { get; set; }
    }

    /// <summary>
    /// An interface to an object that describes a VariableType node.
    /// </summary>
    public interface IVariableType : IVariableBase
    {
        /// <summary>
        /// Whether the type is an abstract type.
        /// </summary>
        bool IsAbstract { get; set; }
    }

    /// <summary>
    /// An interface to an object that describes a Variable node.
    /// </summary>
    public interface IVariable : IVariableBase
    {
        /// <summary>
        /// The type of access supported by variable.
        /// </summary>
        /// <value>The access level.</value>
        byte AccessLevel { get; set; }

        /// <summary>
        /// The type of access supported by variable for the current user.
        /// </summary>
        /// <value>The user access level.</value>
        byte UserAccessLevel { get; set; }

        /// <summary>
        /// The minimum sampling interval supported by the variable.
        /// </summary>
        /// <value>The minimum sampling interval.</value>
        double MinimumSamplingInterval { get; set; }

        /// <summary>
        /// Whether historical data is being archived for the variable.
        /// </summary>
        /// <value><c>true</c> if historizing; otherwise, <c>false</c>.</value>
        bool Historizing { get; set; }
    }

    /// <summary>
    /// An interface to an object that describes a DataType node.
    /// </summary>
    public interface IMethod : ILocalNode
    {
        /// <summary>
        /// Whether the method is currently executable.
        /// </summary>
        /// <value><c>true</c> if executable; otherwise, <c>false</c>.</value>
        bool Executable { get; set; }

        /// <summary>
        /// Whether the method is currently executable by the current user.
        /// </summary>
        /// <value><c>true</c> if executable by user; otherwise, <c>false</c>.</value>
        bool UserExecutable { get; set; }
    }

    /// <summary>
    /// An interface to an object that describes a DataType node.
    /// </summary>
    public interface IDataType : ILocalNode
    {
        /// <summary>
        /// Whether the type is an abstract type.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is abstract; otherwise, <c>false</c>.
        /// </value>
        bool IsAbstract { get; set; }
    }

    /// <summary>
    /// An interface to an object that describes a ReferenceType node.
    /// </summary>
    public interface IReferenceType : ILocalNode
    {
        /// <summary>
        /// Whether the type is an abstract type.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this instance is abstract; otherwise, <c>false</c>.
        /// </value>
        bool IsAbstract { get; set; }

        /// <summary>
        /// Whether the reference type has the same meaning in both directions.
        /// </summary>
        /// <value><c>true</c> if symmetric; otherwise, <c>false</c>.</value>
        bool Symmetric { get; set; }

        /// <summary>
        /// Whether the reference type has the same meaning in both directions.
        /// </summary>
        /// <value>The name of the inverse.</value>
        LocalizedText InverseName { get; set; }
    }

    /// <summary>
    /// An interface to an object that describes a View node.
    /// </summary>
    public interface IView : ILocalNode
    {
        /// <summary>
        /// Whether the view supports events.
        /// </summary>
        /// <value>The event notifier.</value>
        byte EventNotifier { get; set; }

        /// <summary>
        /// Whether the view contains no loops.
        /// </summary>
        /// <value><c>true</c> if contains no loops; otherwise, <c>false</c>.</value>
        bool ContainsNoLoops { get; set; }
    }
}
