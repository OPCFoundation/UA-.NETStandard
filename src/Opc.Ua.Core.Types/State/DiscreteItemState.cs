/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

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
        /// integers that fit in <see cref="long"/> so the caller can defer to the
        /// base class for non-integer or overflowing unsigned values.
        /// </summary>
        private protected static bool TryGetIntegerValue(in Variant value, out long number)
        {
            switch (value.TypeInfo.BuiltInType)
            {
                case BuiltInType.SByte:
                case BuiltInType.Byte:
                case BuiltInType.Int16:
                case BuiltInType.UInt16:
                case BuiltInType.Int32:
                case BuiltInType.UInt32:
                case BuiltInType.Int64:
                case BuiltInType.UInt64:
                    try
                    {
                        number = value.ConvertToInt64().GetInt64();
                        return true;
                    }
                    catch (OverflowException)
                    {
                        number = 0;
                        return false;
                    }
                default:
                    number = 0;
                    return false;
            }
        }
    }
}
