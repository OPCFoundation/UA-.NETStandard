/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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
    /// Attribute for a base complex type field definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class StructureFieldAttribute : Attribute
    {
        #region Constructors
        /// <summary>
        /// Initialize a field attribute with defaults.
        /// </summary>
        public StructureFieldAttribute()
        {
            ValueRank = -1;
            MaxStringLength = 0;
            IsOptional = false;
            BuiltInType = 0;
        }
        #endregion Constructors

        #region  Public Properties
        /// <summary>
        /// The value rank of the field.
        /// </summary>
        public Int32 ValueRank { get; set; }

        /// <summary>
        /// The maximum string length of the field.
        /// </summary>
        public UInt32 MaxStringLength { get; set; }

        /// <summary>
        /// If the field is optional.
        /// </summary>
        public bool IsOptional { get; set; }

        /// <summary>
        /// The datatype of a field as BuiltInType.
        /// </summary>
        public Int32 BuiltInType { get; set; }
        #endregion Public Properties
    }
}//namespace
