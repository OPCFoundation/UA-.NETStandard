/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;

namespace Opc.Ua.OneFuzz.TestTarget
{
    /// <summary>
    /// Deliberately inexact callback signatures: discovery must exclude every one.
    /// </summary>
    public sealed class CallbackLookalikes
    {
        /// <summary>An instance method is not a callback.</summary>
        public void Instance(ReadOnlySpan<byte> input)
        {
            Reject(input);
        }

        /// <summary>An array parameter is not a span callback.</summary>
        public static void Array(byte[] input)
        {
            Reject(input);
        }

        /// <summary>A mutable span is not a read-only span callback.</summary>
        public static void Mutable(Span<byte> input)
        {
            Reject(input);
        }

        /// <summary>A non-void return is not a callback.</summary>
        public static int Returning(ReadOnlySpan<byte> input)
        {
            return input.Length;
        }

        /// <summary>An open generic method is not a callback.</summary>
        public static void Generic<T>(ReadOnlySpan<byte> input)
        {
            throw new InvalidOperationException(typeof(T).Name + FixtureDependency.Hash(input));
        }

        /// <summary>Two parameters are not the exact callback signature.</summary>
        public static void ExtraParameter(ReadOnlySpan<byte> input, int count)
        {
            Reject(input[..count]);
        }

        internal static void Internal(ReadOnlySpan<byte> input)
        {
            Reject(input);
        }

        private static void Reject(ReadOnlySpan<byte> input)
        {
            throw new InvalidOperationException("Callback lookalike was invoked: " + FixtureDependency.Hash(input));
        }
    }

    internal static class HiddenCallbacks
    {
        public static void Hidden(ReadOnlySpan<byte> input)
        {
            throw new InvalidOperationException("Hidden callback was invoked: " + FixtureDependency.Hash(input));
        }
    }
}
