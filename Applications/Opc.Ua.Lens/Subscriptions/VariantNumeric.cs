/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
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
