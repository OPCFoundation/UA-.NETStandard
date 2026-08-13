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

namespace Opc.Ua
{
    /// <summary>
    /// Shared behavior for the concrete DiscreteItemType subtypes (Part 8 section
    /// 5.3.3). The written value can arrive as any of the integer built-in types, so
    /// the helper normalizes a scalar numeric variant to a 64-bit integer without
    /// relying on the exact-type <see cref="Variant"/> accessors.
    /// </summary>
    public partial class DiscreteItemState
    {
        /// <summary>
        /// Converts a scalar integer variant to a <see cref="long"/> regardless of its
        /// concrete built-in type. Returns false for values that are not scalar
        /// integers so the caller can defer to the base class.
        /// </summary>
        private protected static bool TryGetIntegerValue(in Variant value, out long number)
        {
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.SByte:
                    number = value.GetSByte();
                    return true;
                case BuiltInType.Byte:
                    number = value.GetByte();
                    return true;
                case BuiltInType.Int16:
                    number = value.GetInt16();
                    return true;
                case BuiltInType.UInt16:
                    number = value.GetUInt16();
                    return true;
                case BuiltInType.Int32:
                    number = value.GetInt32();
                    return true;
                case BuiltInType.UInt32:
                    number = value.GetUInt32();
                    return true;
                case BuiltInType.Int64:
                    number = value.GetInt64();
                    return true;
                case BuiltInType.UInt64:
                    number = unchecked((long)value.GetUInt64());
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }
    }
}
