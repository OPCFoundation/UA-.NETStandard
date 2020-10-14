/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
    /// The known base complex types.
    /// </summary>
    public enum StructureBaseDataType
    {
        /// <summary>
        /// The type is a structure.
        /// </summary>
        Structure,

        /// <summary>
        /// The type is an OptionSet.
        /// </summary>
        OptionSet,

        /// <summary>
        /// The type is a Union.
        /// </summary>
        Union
    }

    /// <summary>
    /// Attribute for a base complex type structure definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class StructureDefinitionAttribute : Attribute
    {
        #region Constructors
        /// <summary>
        /// Create the attribute for a structure definition.
        /// </summary>
        public StructureDefinitionAttribute()
        {
            StructureType = StructureType.Structure;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Convert the base type node id to a <see cref="StructureBaseDataType"/>.
        /// </summary>
        /// <param name="baseTypeId">The base type nodeId.</param>
        public static StructureBaseDataType FromBaseType(NodeId baseTypeId)
        {
            if (baseTypeId == DataTypeIds.Union)
            {
                return StructureBaseDataType.Union;
            }
            if (baseTypeId == DataTypeIds.OptionSet)
            {
                return StructureBaseDataType.OptionSet;
            }
            return StructureBaseDataType.Structure;
        }
        #endregion

        #region  Public Properties
        /// <summary>
        /// The default encoding Id.
        /// </summary>
        public string DefaultEncodingId { get; set; }
        /// <summary>
        /// The base DataType.
        /// </summary>
        public StructureBaseDataType BaseDataType { get; set; }
        /// <summary>
        /// The structure type.
        /// </summary>
        public StructureType StructureType { get; set; }
        #endregion
    }
}//namespace
