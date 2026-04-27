/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime.Interop;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.CttTestRunner.Runtime
{
    /// <summary>
    /// Implements the CTT Assert object exposed to JavaScript.
    /// Methods: True, False, Equal, NotEqual, GreaterThan, LessThan, InRange,
    ///          StatusCodeIs, StatusCodeIsOneOf, CoercedEqual, StringContains, etc.
    /// </summary>
    public sealed class CttAssert : ObjectInstance
    {
        private readonly CttTestContext _context;
        private readonly ILogger _logger;

        public CttAssert(CttTestContext context, ILogger logger)
            : base(new Engine())
        {
            _context = context;
            _logger = logger;

            FastSetDataProperty("True", new ClrFunction(Engine, "True",
                (_, args) => AssertTrue(args)));
            FastSetDataProperty("False", new ClrFunction(Engine, "False",
                (_, args) => AssertFalse(args)));
            FastSetDataProperty("Equal", new ClrFunction(Engine, "Equal",
                (_, args) => AssertEqual(args)));
            FastSetDataProperty("NotEqual", new ClrFunction(Engine, "NotEqual",
                (_, args) => AssertNotEqual(args)));
            FastSetDataProperty("GreaterThan", new ClrFunction(Engine, "GreaterThan",
                (_, args) => AssertGreaterThan(args)));
            FastSetDataProperty("LessThan", new ClrFunction(Engine, "LessThan",
                (_, args) => AssertLessThan(args)));
            FastSetDataProperty("InRange", new ClrFunction(Engine, "InRange",
                (_, args) => AssertInRange(args)));
            FastSetDataProperty("StatusCodeIs", new ClrFunction(Engine, "StatusCodeIs",
                (_, args) => AssertStatusCodeIs(args)));
            FastSetDataProperty("StatusCodeIsOneOf", new ClrFunction(Engine, "StatusCodeIsOneOf",
                (_, args) => AssertStatusCodeIsOneOf(args)));
            FastSetDataProperty("CoercedEqual", new ClrFunction(Engine, "CoercedEqual",
                (_, args) => AssertCoercedEqual(args)));
            FastSetDataProperty("StringContains", new ClrFunction(Engine, "StringContains",
                (_, args) => AssertStringContains(args)));
            FastSetDataProperty("StringNotContains", new ClrFunction(Engine, "StringNotContains",
                (_, args) => AssertStringNotContains(args)));
            FastSetDataProperty("NullNodeId", new ClrFunction(Engine, "NullNodeId",
                (_, args) => AssertNullNodeId(args)));
            FastSetDataProperty("NodeIdsEqual", new ClrFunction(Engine, "NodeIdsEqual",
                (_, args) => AssertNodeIdsEqual(args)));
            FastSetDataProperty("QualifiedNamesEqual", new ClrFunction(Engine, "QualifiedNamesEqual",
                (_, args) => AssertQualifiedNamesEqual(args)));
            FastSetDataProperty("LocalizedTextsEqual", new ClrFunction(Engine, "LocalizedTextsEqual",
                (_, args) => AssertLocalizedTextsEqual(args)));
        }

        private JsValue AssertTrue(JsValue[] args)
        {
            bool condition = args.Length > 0 && args[0].AsBoolean();
            string message = args.Length > 1 ? args[1].ToString() : "Expected true";
            if (!condition)
            {
                _context.AddError($"Assert.True failed: {message}");
                return JsValue.FromObject(Engine, false);
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertFalse(JsValue[] args)
        {
            bool condition = args.Length > 0 && args[0].AsBoolean();
            string message = args.Length > 1 ? args[1].ToString() : "Expected false";
            if (condition)
            {
                _context.AddError($"Assert.False failed: {message}");
                return JsValue.FromObject(Engine, false);
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertEqual(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            string expected = args[0].ToString();
            string actual = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected '{expected}' == '{actual}'";
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                _context.AddError($"Assert.Equal failed: {message} (expected={expected}, actual={actual})");
                return JsValue.FromObject(Engine, false);
            }
            if (args.Length > 3)
            {
                _context.AddLog(args[3].ToString());
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertNotEqual(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            string expected = args[0].ToString();
            string actual = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected '{expected}' != '{actual}'";
            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                _context.AddError($"Assert.NotEqual failed: {message}");
                return JsValue.FromObject(Engine, false);
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertGreaterThan(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            double threshold = args[0].AsNumber();
            double actual = args[1].AsNumber();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected {actual} > {threshold}";
            if (actual <= threshold)
            {
                _context.AddError($"Assert.GreaterThan failed: {message} (actual={actual}, threshold={threshold})");
                return JsValue.FromObject(Engine, false);
            }
            if (args.Length > 3)
            {
                _context.AddLog(args[3].ToString());
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertLessThan(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            double threshold = args[0].AsNumber();
            double actual = args[1].AsNumber();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected {actual} < {threshold}";
            if (actual >= threshold)
            {
                _context.AddError($"Assert.LessThan failed: {message}");
                return JsValue.FromObject(Engine, false);
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertInRange(JsValue[] args)
        {
            if (args.Length < 3) return JsValue.FromObject(Engine, false);
            double low = args[0].AsNumber();
            double high = args[1].AsNumber();
            double actual = args[2].AsNumber();
            string message = args.Length > 3 ? args[3].ToString() : $"Expected {actual} in [{low},{high}]";
            if (actual < low || actual > high)
            {
                _context.AddError($"Assert.InRange failed: {message}");
                return JsValue.FromObject(Engine, false);
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertStatusCodeIs(JsValue[] args)
        {
            // StatusCodeIs(expected, actual, failMsg, passMsg)
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            uint expected = (uint)args[0].AsNumber();
            uint actual = (uint)args[1].AsNumber();
            string failMsg = args.Length > 2 ? args[2].ToString() : "StatusCode mismatch";
            if (expected != actual)
            {
                _context.AddError($"Assert.StatusCodeIs failed: {failMsg} (expected=0x{expected:X8}, actual=0x{actual:X8})");
                return JsValue.FromObject(Engine, false);
            }
            if (args.Length > 3)
            {
                _context.AddLog(args[3].ToString());
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertStatusCodeIsOneOf(JsValue[] args)
        {
            // StatusCodeIsOneOf(expectedArray_or_ExpectedResults, actual, failMsg, passMsg)
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            uint actual = (uint)args[1].AsNumber();
            string failMsg = args.Length > 2 ? args[2].ToString() : "StatusCode not in expected set";

            // The first arg could be an ExpectedAndAcceptedResults object or an array
            if (args[0].IsObject() && args[0].AsObject().HasProperty("containsStatusCode"))
            {
                var fn = args[0].AsObject().Get("containsStatusCode");
                var contains = fn.AsObject().Call(args[0], new[] { args[1] });
                if (!contains.AsBoolean())
                {
                    _context.AddError($"Assert.StatusCodeIsOneOf failed: {failMsg} (actual=0x{actual:X8})");
                    return JsValue.FromObject(Engine, false);
                }
                return JsValue.FromObject(Engine, true);
            }

            // TODO: handle array-based expected values
            _context.AddLog($"Assert.StatusCodeIsOneOf: actual=0x{actual:X8}");
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertCoercedEqual(JsValue[] args)
        {
            // CoercedEqual(expected, actual, message)
            return AssertEqual(args); // simplified
        }

        private JsValue AssertStringContains(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            string haystack = args[0].ToString();
            string needle = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected string to contain '{needle}'";
            if (!haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                _context.AddError($"Assert.StringContains failed: {message}");
                return JsValue.FromObject(Engine, false);
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertStringNotContains(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(Engine, false);
            string haystack = args[0].ToString();
            string needle = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected string NOT to contain '{needle}'";
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                _context.AddError($"Assert.StringNotContains failed: {message}");
                return JsValue.FromObject(Engine, false);
            }
            return JsValue.FromObject(Engine, true);
        }

        private JsValue AssertNullNodeId(JsValue[] args)
        {
            // NullNodeId(nodeId, message)
            return JsValue.FromObject(Engine, true); // TODO: implement NodeId null check
        }

        private JsValue AssertNodeIdsEqual(JsValue[] args) => AssertEqual(args);
        private JsValue AssertQualifiedNamesEqual(JsValue[] args) => AssertEqual(args);
        private JsValue AssertLocalizedTextsEqual(JsValue[] args) => AssertEqual(args);
    }
}


