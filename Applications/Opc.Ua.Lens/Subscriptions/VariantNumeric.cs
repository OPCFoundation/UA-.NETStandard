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

using System;
using Opc.Ua;

namespace UaLens.Subscriptions;

/// <summary>
/// Numeric-conversion helper for <see cref="Variant"/>.
/// </summary>
/// <remarks>
/// The OPC UA stack only exposes the throwing
/// <see cref="Variant.ConvertToDouble"/>; there is no built-in Try
/// variant.  Centralising the try/catch here keeps the engine adapters
/// (and any future call sites) free of boilerplate exception handling.
/// </remarks>
internal static class VariantNumeric
{
    /// <summary>
    /// Attempts to convert <paramref name="v"/> to a finite
    /// <see cref="double"/>.  Returns <c>false</c> for null/array
    /// variants, non-numeric types (structures, byte strings, …), and
    /// any conversion failure (string parse error, overflow, …).
    /// </summary>
    public static bool TryToDouble(Variant v, out double d)
    {
        d = double.NaN;
        if (v.IsNull)
        {
            return false;
        }
        // Arrays and matrices are not single numeric samples — skip.
        if (v.TypeInfo.ValueRank != ValueRanks.Scalar)
        {
            return false;
        }
        try
        {
            Variant converted = v.ConvertToDouble();
            if (!converted.TryGetValue(out double parsed))
            {
                return false;
            }
            if (double.IsNaN(parsed) || double.IsInfinity(parsed))
            {
                return false;
            }
            d = parsed;
            return true;
        }
        catch (InvalidCastException) { return false; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (ServiceResultException) { return false; }
    }
}
