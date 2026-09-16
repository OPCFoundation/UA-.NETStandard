/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Schema
{
    /// <summary>
    /// Identifies standard wire encodings used by structure-field schemas.
    /// </summary>
    public static class SchemaTypeInfo
    {
        /// <summary>
        /// Returns the built-in wire encoding of a standard structure field.
        /// Number, Integer and UInteger fields use Variant encoding as specified
        /// by OPC 10000-6, 5.1.6.
        /// </summary>
        /// <param name="dataType">The field's declared data type.</param>
        /// <returns>
        /// The built-in encoding, or <see cref="BuiltInType.Null"/> when the
        /// data type requires definition resolution.
        /// </returns>
        public static BuiltInType GetFieldEncodingType(NodeId dataType)
        {
            if (dataType == DataTypeIds.Number ||
                dataType == DataTypeIds.Integer ||
                dataType == DataTypeIds.UInteger)
            {
                return BuiltInType.Variant;
            }
            return TypeInfo.GetBuiltInType(dataType);
        }
    }
}
