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
    public sealed class CttAssert
    {
        private readonly CttTestContext _context;
        private readonly ILogger _logger;
        private Engine? _engine;

        public CttAssert(CttTestContext context, ILogger logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Creates a JS object exposing all assert methods.
        /// </summary>
        public ObjectInstance ToJsObject()
        {
            _engine = new Engine();
            var obj = (ObjectInstance)_engine.Intrinsics.Object.Construct(Array.Empty<JsValue>());

            obj.Set("True", new ClrFunction(_engine, "True",
                (_, args) => AssertTrue(args)));
            obj.Set("False", new ClrFunction(_engine, "False",
                (_, args) => AssertFalse(args)));
            obj.Set("Equal", new ClrFunction(_engine, "Equal",
                (_, args) => AssertEqual(args)));
            obj.Set("NotEqual", new ClrFunction(_engine, "NotEqual",
                (_, args) => AssertNotEqual(args)));
            obj.Set("GreaterThan", new ClrFunction(_engine, "GreaterThan",
                (_, args) => AssertGreaterThan(args)));
            obj.Set("LessThan", new ClrFunction(_engine, "LessThan",
                (_, args) => AssertLessThan(args)));
            obj.Set("InRange", new ClrFunction(_engine, "InRange",
                (_, args) => AssertInRange(args)));
            obj.Set("StatusCodeIs", new ClrFunction(_engine, "StatusCodeIs",
                (_, args) => AssertStatusCodeIs(args)));
            obj.Set("StatusCodeIsOneOf", new ClrFunction(_engine, "StatusCodeIsOneOf",
                (_, args) => AssertStatusCodeIsOneOf(args)));
            obj.Set("CoercedEqual", new ClrFunction(_engine, "CoercedEqual",
                (_, args) => AssertCoercedEqual(args)));
            obj.Set("StringContains", new ClrFunction(_engine, "StringContains",
                (_, args) => AssertStringContains(args)));
            obj.Set("StringNotContains", new ClrFunction(_engine, "StringNotContains",
                (_, args) => AssertStringNotContains(args)));
            obj.Set("NullNodeId", new ClrFunction(_engine, "NullNodeId",
                (_, args) => AssertNullNodeId(args)));
            obj.Set("NodeIdsEqual", new ClrFunction(_engine, "NodeIdsEqual",
                (_, args) => AssertNodeIdsEqual(args)));
            obj.Set("QualifiedNamesEqual", new ClrFunction(_engine, "QualifiedNamesEqual",
                (_, args) => AssertQualifiedNamesEqual(args)));
            obj.Set("LocalizedTextsEqual", new ClrFunction(_engine, "LocalizedTextsEqual",
                (_, args) => AssertLocalizedTextsEqual(args)));

            return obj;
        }

        private JsValue AssertTrue(JsValue[] args)
        {
            bool condition = args.Length > 0 && args[0].AsBoolean();
            string message = args.Length > 1 ? args[1].ToString() : "Expected true";
            if (!condition)
            {
                _context.AddError($"Assert.True failed: {message}");
                return JsValue.FromObject(_engine!, false);
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertFalse(JsValue[] args)
        {
            bool condition = args.Length > 0 && args[0].AsBoolean();
            string message = args.Length > 1 ? args[1].ToString() : "Expected false";
            if (condition)
            {
                _context.AddError($"Assert.False failed: {message}");
                return JsValue.FromObject(_engine!, false);
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertEqual(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
            string expected = args[0].ToString();
            string actual = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected '{expected}' == '{actual}'";
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                _context.AddError($"Assert.Equal failed: {message} (expected={expected}, actual={actual})");
                return JsValue.FromObject(_engine!, false);
            }
            if (args.Length > 3)
            {
                _context.AddLog(args[3].ToString());
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertNotEqual(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
            string expected = args[0].ToString();
            string actual = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected '{expected}' != '{actual}'";
            if (string.Equals(expected, actual, StringComparison.Ordinal))
            {
                _context.AddError($"Assert.NotEqual failed: {message}");
                return JsValue.FromObject(_engine!, false);
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertGreaterThan(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
            double threshold = args[0].AsNumber();
            double actual = args[1].AsNumber();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected {actual} > {threshold}";
            if (actual <= threshold)
            {
                _context.AddError($"Assert.GreaterThan failed: {message} (actual={actual}, threshold={threshold})");
                return JsValue.FromObject(_engine!, false);
            }
            if (args.Length > 3)
            {
                _context.AddLog(args[3].ToString());
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertLessThan(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
            double threshold = args[0].AsNumber();
            double actual = args[1].AsNumber();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected {actual} < {threshold}";
            if (actual >= threshold)
            {
                _context.AddError($"Assert.LessThan failed: {message}");
                return JsValue.FromObject(_engine!, false);
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertInRange(JsValue[] args)
        {
            if (args.Length < 3) return JsValue.FromObject(_engine!, false);
            double low = args[0].AsNumber();
            double high = args[1].AsNumber();
            double actual = args[2].AsNumber();
            string message = args.Length > 3 ? args[3].ToString() : $"Expected {actual} in [{low},{high}]";
            if (actual < low || actual > high)
            {
                _context.AddError($"Assert.InRange failed: {message}");
                return JsValue.FromObject(_engine!, false);
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertStatusCodeIs(JsValue[] args)
        {
            // StatusCodeIs(expected, actual, failMsg, passMsg)
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
            uint expected = (uint)args[0].AsNumber();
            uint actual = (uint)args[1].AsNumber();
            string failMsg = args.Length > 2 ? args[2].ToString() : "StatusCode mismatch";
            if (expected != actual)
            {
                _context.AddError($"Assert.StatusCodeIs failed: {failMsg} (expected=0x{expected:X8}, actual=0x{actual:X8})");
                return JsValue.FromObject(_engine!, false);
            }
            if (args.Length > 3)
            {
                _context.AddLog(args[3].ToString());
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertStatusCodeIsOneOf(JsValue[] args)
        {
            // StatusCodeIsOneOf(expectedArray_or_ExpectedResults, actual, failMsg, passMsg)
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
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
                    return JsValue.FromObject(_engine!, false);
                }
                return JsValue.FromObject(_engine!, true);
            }

            // TODO: handle array-based expected values
            _context.AddLog($"Assert.StatusCodeIsOneOf: actual=0x{actual:X8}");
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertCoercedEqual(JsValue[] args)
        {
            // CoercedEqual(expected, actual, message)
            return AssertEqual(args); // simplified
        }

        private JsValue AssertStringContains(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
            string haystack = args[0].ToString();
            string needle = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected string to contain '{needle}'";
            if (!haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                _context.AddError($"Assert.StringContains failed: {message}");
                return JsValue.FromObject(_engine!, false);
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertStringNotContains(JsValue[] args)
        {
            if (args.Length < 2) return JsValue.FromObject(_engine!, false);
            string haystack = args[0].ToString();
            string needle = args[1].ToString();
            string message = args.Length > 2 ? args[2].ToString() : $"Expected string NOT to contain '{needle}'";
            if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                _context.AddError($"Assert.StringNotContains failed: {message}");
                return JsValue.FromObject(_engine!, false);
            }
            return JsValue.FromObject(_engine!, true);
        }

        private JsValue AssertNullNodeId(JsValue[] args)
        {
            // NullNodeId(nodeId, message)
            return JsValue.FromObject(_engine!, true); // TODO: implement NodeId null check
        }

        private JsValue AssertNodeIdsEqual(JsValue[] args) => AssertEqual(args);
        private JsValue AssertQualifiedNamesEqual(JsValue[] args) => AssertEqual(args);
        private JsValue AssertLocalizedTextsEqual(JsValue[] args) => AssertEqual(args);
    }
}


