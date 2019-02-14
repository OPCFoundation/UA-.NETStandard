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
    /// <summary> 
    /// The base class for all reference type nodes.
    /// </summary>
    public class DataTypeState : BaseTypeState
    {
        #region Constructors
        /// <summary>
        /// Initializes the instance with its defalt attribute values.
        /// </summary>
        public DataTypeState() : base(NodeClass.DataType)
        {
        }

        /// <summary>
        /// Constructs an instance of a node.
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <returns>The new node.</returns>
        public static NodeState Construct(NodeState parent)
        {
            return new DataTypeState();
        }
        #endregion

        /// <summary>
        /// The abstract definition of the data type.
        /// </summary>
        public UADataTypeDefinition Definition { get; set; }
    }

    /// <summary>
    /// Defines an abstract description of a type.
    /// </summary>
    public class UADataTypeDefinition
    {
        /// <summary>
        /// The name of the type.
        /// </summary>
        public QualifiedName Name { get; set; }

        /// <summary>
        /// The symbolic name (if the name can't be used as a program symbol).
        /// </summary>
        public string SymbolicName { get; set; }

        /// <summary>
        /// The qualified name of the base type.
        /// </summary>
        public QualifiedName BaseType { get; set; }

        /// <summary>
        /// The description of the data type.
        /// </summary>
        public LocalizedText Description { get; set; }

        /// <summary>
        /// The fields in structure.
        /// </summary>
        public List<DataTypeDefinitionField> Fields { get; set; }
    }

    /// <summary>
    /// Defines a field within an abstract definition of a data type.
    /// </summary>
    public class DataTypeDefinitionField
    {
        /// <summary>
        /// The name of the field.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The symbolic name (if the name can't be used as a program symbol).
        /// </summary>
        public string SymbolicName { get; set; }

        /// <summary>
        /// The data type of the field.
        /// </summary>
        public NodeId DataType { get; set; }

        /// <summary>
        /// The value rank for the field.
        /// </summary>
        public int ValueRank { get; set; }

        /// <summary>
        /// The description of the field.
        /// </summary>
        public LocalizedText Description { get; set; }

        /// <summary>
        /// A nested description of a structured field.
        /// </summary>
        public UADataTypeDefinition Definition { get; set; }

        /// <summary>
        /// The value of an enumerated field.
        /// </summary>
        public int Value { get; set; }
    }
}
