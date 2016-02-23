/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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
        public DataTypeDefinition Definition { get; set; }
    }

    /// <summary>
    /// Defines an abstract description of a type.
    /// </summary>
    public class DataTypeDefinition
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
        public DataTypeDefinition Definition { get; set; }

        /// <summary>
        /// The value of an enumerated field.
        /// </summary>
        public int Value { get; set; }
    }
}
