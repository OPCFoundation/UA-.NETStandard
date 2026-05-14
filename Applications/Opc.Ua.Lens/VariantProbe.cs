/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Opc.Ua;
using UaLens.Subscriptions;

namespace UaLens;

/// <summary>
/// Headless validator for <see cref="VariantParser"/> + <see cref="VariantNumeric.TryToDouble"/>
/// exception paths.  Asserts that unsupported types, NaN/Infinity, and
/// parse failures all return <c>false</c> instead of throwing.
/// </summary>
internal static class VariantProbe
{
    public static int Run()
    {
        Console.WriteLine("== Variant probe ==");
        int rc = 0;
        // 1. Plain double — true.
        if (!VariantNumeric.TryToDouble(Variant.From(42.0), out double d) || d != 42.0)
        { Fail("scalar double=42 expected true/42, got false/" + d); rc = 1; }
        // 2. Int32 — true.
        if (!VariantNumeric.TryToDouble(Variant.From(7), out d) || d != 7.0)
        { Fail("scalar Int32=7 expected true/7, got false/" + d); rc = 1; }
        // 3. ByteString — false (not numeric).
        if (VariantNumeric.TryToDouble(Variant.From(new ByteString(new byte[] { 1, 2, 3 })), out _))
        { Fail("ByteString expected false, got true"); rc = 1; }
        // 4. Array — false (not scalar).
#pragma warning disable CA1861 // Test fixture: literal arrays are the test input.
        if (VariantNumeric.TryToDouble(Variant.From((ArrayOf<int>)new int[] { 1, 2, 3 }), out _))
        { Fail("Int32[] array expected false, got true"); rc = 1; }
#pragma warning restore CA1861
        // 5. NaN — false.
        if (VariantNumeric.TryToDouble(Variant.From(double.NaN), out _))
        { Fail("NaN expected false, got true"); rc = 1; }
        // 6. Infinity — false.
        if (VariantNumeric.TryToDouble(Variant.From(double.PositiveInfinity), out _))
        { Fail("+inf expected false, got true"); rc = 1; }
        // 7. Null Variant — false.
        if (VariantNumeric.TryToDouble(Variant.Null, out _))
        { Fail("Variant.Null expected false, got true"); rc = 1; }
        // 8. String that doesn't parse — false (FormatException caught).
        if (VariantNumeric.TryToDouble(Variant.From("not-a-number"), out _))
        { Fail("'not-a-number' expected false, got true"); rc = 1; }
        // 9. String overflow — false (OverflowException caught).
        if (VariantNumeric.TryToDouble(Variant.From("9e9999"), out _))
        { Fail("'9e9999' expected false, got true"); rc = 1; }
        // 10. Bool — true (1.0/0.0).
        if (!VariantNumeric.TryToDouble(Variant.From(true), out d) || d != 1.0)
        { Fail("Boolean=true expected true/1.0, got false/" + d); rc = 1; }

        // VariantParser scalar round-trip.
        if (!VariantParser.TryParse(DataTypeIds.Int32, ValueRanks.Scalar, "42",
            out Variant pv, out string? perr) || !pv.TryGetValue(out int parsedI) || parsedI != 42)
        { Fail("VariantParser Int32 '42' failed: " + perr); rc = 1; }
        // VariantParser unsupported (Structure) → false.
        if (VariantParser.TryParse(DataTypeIds.Structure, ValueRanks.Scalar, "x",
            out _, out _))
        { Fail("VariantParser Structure expected false"); rc = 1; }

        Console.WriteLine(rc == 0 ? "VARIANT PROBE PASS" : "VARIANT PROBE FAIL");
        return rc;
    }

    private static void Fail(string msg) => Console.WriteLine("FAIL: " + msg);
}
