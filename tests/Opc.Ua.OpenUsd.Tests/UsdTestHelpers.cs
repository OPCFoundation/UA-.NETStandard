/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Small assertion helpers that resolve a required prim, attribute or relationship and fail the
    /// test with a readable message when it is missing.
    /// </summary>
    internal static class UsdTestHelpers
    {
        public static UsdPrim RequirePrim(UsdStage stage, string path)
        {
            UsdPrim? prim = stage.Find(path);
            Assert.That(prim, Is.Not.Null, "expected a prim at " + path);
            return prim!;
        }

        public static UsdAttribute RequireAttribute(UsdPrim prim, string name)
        {
            UsdAttribute? attr = prim.Attributes
                .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.Ordinal));
            Assert.That(attr, Is.Not.Null, "expected attribute " + name + " on " + prim.Path);
            return attr!;
        }

        public static UsdRelationship RequireRelationship(UsdPrim prim, string name)
        {
            UsdRelationship? rel = prim.Relationships
                .FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
            Assert.That(rel, Is.Not.Null, "expected relationship " + name + " on " + prim.Path);
            return rel!;
        }

        public static UsdValue Tuple(params UsdValue[] values)
        {
            return UsdValue.FromTuple(values.ToArrayOf());
        }

        public static UsdValue Array(params UsdValue[] values)
        {
            return UsdValue.FromArray(values.ToArrayOf());
        }

        public static UsdValue Dictionary(params KeyValuePair<string, UsdValue>[] entries)
        {
            // The IEnumerable<KeyValuePair<,>> constructor is not available on net48,
            // so fill the dictionary explicitly.
            var map = new Dictionary<string, UsdValue>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, UsdValue> entry in entries)
            {
                map[entry.Key] = entry.Value;
            }
            return UsdValue.FromDictionary(map);
        }

        public static UsdValue NumberArray(params double[] values)
        {
            return UsdValue.FromArray(values.Select(UsdValue.From).ToArrayOf());
        }

        public static UsdValue NumberTuple(params double[] values)
        {
            return UsdValue.FromTuple(values.Select(UsdValue.From).ToArrayOf());
        }

        public static UsdValue IntegerArray(params long[] values)
        {
            return UsdValue.FromArray(values.Select(UsdValue.From).ToArrayOf());
        }

        public static UsdValue IntegerTuple(params long[] values)
        {
            return UsdValue.FromTuple(values.Select(UsdValue.From).ToArrayOf());
        }

        public static UsdValue StringArray(params string[] values)
        {
            return UsdValue.FromArray(values.Select(UsdValue.FromString).ToArrayOf());
        }

        public static UsdValue TokenArray(params string[] values)
        {
            return UsdValue.FromArray(values.Select(UsdValue.FromToken).ToArrayOf());
        }

        public static UsdValue AssetArray(params string[] values)
        {
            return UsdValue.FromArray(values.Select(UsdValue.FromAssetPath).ToArrayOf());
        }

        public static void AssertDouble(UsdValue value, double expected)
        {
            Assert.That(value.TryGetDouble(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertInteger(UsdValue value, long expected)
        {
            Assert.That(value.TryGetInteger(out long actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertBoolean(UsdValue value, bool expected)
        {
            Assert.That(value.TryGetBoolean(out bool actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertString(UsdValue value, string expected)
        {
            Assert.That(value.TryGetString(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertToken(UsdValue value, string expected)
        {
            Assert.That(value.TryGetToken(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertAssetPath(UsdValue value, string expected)
        {
            Assert.That(value.TryGetAssetPath(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertPathReference(UsdValue value, string expected)
        {
            Assert.That(value.TryGetPathReference(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertText(UsdValue value, string expected)
        {
            Assert.That(value.TryGetText(out string actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }

        public static void AssertIntegerItems(UsdValue value, params long[] expected)
        {
            Assert.That(value.TryGetItems(out ArrayOf<UsdValue> items), Is.True);
            Assert.That(items.Count, Is.EqualTo(expected.Length));
            for (int ii = 0; ii < expected.Length; ii++)
            {
                AssertInteger(items[ii], expected[ii]);
            }
        }

        public static void AssertDoubleItems(UsdValue value, params double[] expected)
        {
            Assert.That(value.TryGetItems(out ArrayOf<UsdValue> items), Is.True);
            Assert.That(items.Count, Is.EqualTo(expected.Length));
            for (int ii = 0; ii < expected.Length; ii++)
            {
                AssertDouble(items[ii], expected[ii]);
            }
        }

        public static void AssertTextItems(UsdValue value, params string[] expected)
        {
            Assert.That(value.TryGetItems(out ArrayOf<UsdValue> items), Is.True);
            Assert.That(items.Count, Is.EqualTo(expected.Length));
            for (int ii = 0; ii < expected.Length; ii++)
            {
                AssertText(items[ii], expected[ii]);
            }
        }

        public static void AssertNestedIntegerItems(UsdValue value, params long[][] expected)
        {
            Assert.That(value.TryGetItems(out ArrayOf<UsdValue> rows), Is.True);
            Assert.That(rows.Count, Is.EqualTo(expected.Length));
            for (int ii = 0; ii < expected.Length; ii++)
            {
                AssertIntegerItems(rows[ii], expected[ii]);
            }
        }
    }
}
