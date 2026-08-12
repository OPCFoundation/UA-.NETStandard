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

using Opc.Ua.Aas.V2;

namespace Opc.Ua.Aas.Client.V2
{
    /// <summary>
    /// Validates OPC UA Variant values against the AAS V2 ValueType enumeration.
    /// </summary>
    public static class AasV2ValueTypeMap
    {
        /// <summary>
        /// Returns <c>true</c> when <paramref name="value"/> is compatible with <paramref name="valueType"/>.
        /// </summary>
        public static bool IsCompatible(in Variant value, AASValueTypeDataType valueType)
        {
            switch (valueType)
            {
                case AASValueTypeDataType.Boolean:
                    return value.TryGetValue(out bool _);
                case AASValueTypeDataType.SByte:
                    return value.TryGetValue(out sbyte _);
                case AASValueTypeDataType.Byte:
                    return value.TryGetValue(out byte _);
                case AASValueTypeDataType.Int16:
                    return value.TryGetValue(out short _);
                case AASValueTypeDataType.UInt16:
                    return value.TryGetValue(out ushort _);
                case AASValueTypeDataType.Int32:
                    return value.TryGetValue(out int _);
                case AASValueTypeDataType.UInt32:
                    return value.TryGetValue(out uint _);
                case AASValueTypeDataType.Int64:
                    return value.TryGetValue(out long _);
                case AASValueTypeDataType.UInt64:
                    return value.TryGetValue(out ulong _);
                case AASValueTypeDataType.Float:
                    return value.TryGetValue(out float _);
                case AASValueTypeDataType.Double:
                    return value.TryGetValue(out double _);
                case AASValueTypeDataType.String:
                    return value.TryGetValue(out string _);
                case AASValueTypeDataType.DateTime:
                case AASValueTypeDataType.UtcTime:
                    return value.TryGetValue(out DateTimeUtc _);
                case AASValueTypeDataType.ByteString:
                    return value.TryGetValue(out ByteString _);
                case AASValueTypeDataType.LocalizedText:
                    return value.TryGetValue(out LocalizedText _);
                default:
                    return false;
            }
        }
    }
}
