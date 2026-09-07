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
using System.Threading;
using Opc.Ua;

namespace UaLens.Plugins.Performance;

// CA5394: this file uses System.Random for synthetic benchmark values —
// the security analyzer flags every `rng.Next(...)` site as if it were
// generating cryptographic material.  For a perf benchmark that just needs
// non-deterministic ops, System.Random is the correct choice (a CSRNG would
// distort latency measurements).  Suppress the rule for this file only.
#pragma warning disable CA5394 // Random is an insecure RNG.  Benchmark-only use; not security-sensitive.

/// <summary>
/// Helper for generating per-op synthetic values matched to the target
/// <see cref="BuiltInType"/>, driven by the user's
/// <see cref="ValueGenerator"/> choice.  Designed to be cheap to call
/// in tight loops — uses a thread-local <see cref="Random"/> for the
/// <see cref="ValueGenerator.Random"/> mode and an int cast of the op
/// index for the others.  Falls back to a Variant.Null on unsupported
/// data types so the runner can still issue ops and surface the
/// resulting Bad_TypeMismatch status code to the user.
/// </summary>
internal static class ValueFactory
{
    private static readonly ThreadLocal<Random> s_rand =
        new(() => new Random(unchecked(Environment.TickCount * 31 + Environment.CurrentManagedThreadId)));

    /// <summary>
    /// Maps an OPC UA <see cref="Argument"/> to a <see cref="BuiltInType"/>
    /// suitable for value synthesis.  Falls back to <c>Int32</c> for
    /// unrecognised data types (caller will get a Bad_TypeMismatch from
    /// the server, which is the correct way to surface this).
    /// </summary>
    public static BuiltInType BuiltInForArgument(Argument arg)
    {
        if (arg is null || arg.DataType.IsNull)
        {
            return BuiltInType.Int32;
        }

        if (arg.DataType.IdType != IdType.Numeric)
        {
            return BuiltInType.Int32;
        }

        if (arg.DataType.NamespaceIndex != 0)
        {
            return BuiltInType.Int32;
        }

        uint id = (uint)arg.DataType.Identifier;
        BuiltInType bi = (BuiltInType)id;
        return Enum.IsDefined(bi) ? bi : BuiltInType.Int32;
    }

    /// <summary>
    /// Build a synthetic scalar <see cref="Variant"/> of the requested
    /// built-in type using the given strategy.
    /// </summary>
    public static Variant BuildScalar(BuiltInType bi, ValueGenerator generator, long opIndex)
    {
        // s_rand uses a non-null factory delegate; ThreadLocal<T>.Value
        // returns T? to model the case where the factory itself returns null,
        // which never happens here.
        Random rng = s_rand.Value!;
        switch (bi)
        {
            case BuiltInType.Boolean:
                return Variant.From(generator switch
                {
                    ValueGenerator.Random => rng.Next(2) == 1,
                    ValueGenerator.Sequential => (opIndex & 1) == 1,
                    _ => true
                });
            case BuiltInType.SByte:
                return Variant.From((sbyte)Pick(generator, opIndex, () => rng.Next(sbyte.MinValue, sbyte.MaxValue + 1)));
            case BuiltInType.Byte:
                return Variant.From((byte)Pick(generator, opIndex, () => rng.Next(byte.MaxValue + 1)));
            case BuiltInType.Int16:
                return Variant.From((short)Pick(generator, opIndex, () => rng.Next(short.MinValue, short.MaxValue + 1)));
            case BuiltInType.UInt16:
                return Variant.From((ushort)Pick(generator, opIndex, () => rng.Next(ushort.MaxValue + 1)));
            case BuiltInType.Int32:
                return Variant.From(Pick(generator, opIndex, () => rng.Next()));
            case BuiltInType.UInt32:
                return Variant.From((uint)Pick(generator, opIndex, () => rng.Next()));
            case BuiltInType.Int64:
                return Variant.From(PickLong(generator, opIndex, () => ((long)rng.Next() << 32) | (uint)rng.Next()));
            case BuiltInType.UInt64:
                return Variant.From((ulong)PickLong(generator, opIndex, () => ((long)rng.Next() << 32) | (uint)rng.Next()));
            case BuiltInType.Float:
                return Variant.From((float)PickDouble(generator, opIndex, () => rng.NextDouble() * 1000.0));
            case BuiltInType.Double:
                return Variant.From(PickDouble(generator, opIndex, () => rng.NextDouble() * 1000.0));
            case BuiltInType.String:
                return Variant.From(generator switch
                {
                    ValueGenerator.Random => "v" + rng.Next().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ValueGenerator.Sequential => "v" + opIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => "ualens-bench"
                });
            case BuiltInType.DateTime:
                return Variant.From(new DateTimeUtc(DateTime.UtcNow));
            case BuiltInType.Guid:
                return Variant.From(new Uuid(generator == ValueGenerator.Fixed
                    ? Guid.Empty
                    : Guid.NewGuid()));
            case BuiltInType.ByteString:
                byte[] bytes = new byte[8];
                if (generator == ValueGenerator.Random)
                {
                    rng.NextBytes(bytes);
                }
                else if (generator == ValueGenerator.Sequential)
                {
                    BitConverter.TryWriteBytes(bytes, opIndex);
                }
                return Variant.From(new ByteString(bytes));
            default:
                // Unsupported — return a default Int32 so the server can
                // tell us off with a clear Bad_TypeMismatch / Bad_TypeUnsupported.
                return Variant.From(Pick(generator, opIndex, () => rng.Next()));
        }
    }

    private static int Pick(ValueGenerator gen, long idx, Func<int> randFunc) =>
        gen switch
        {
            ValueGenerator.Random => randFunc(),
            ValueGenerator.Sequential => unchecked((int)idx),
            _ => 42
        };

    private static long PickLong(ValueGenerator gen, long idx, Func<long> randFunc) =>
        gen switch
        {
            ValueGenerator.Random => randFunc(),
            ValueGenerator.Sequential => idx,
            _ => 42L
        };

    private static double PickDouble(ValueGenerator gen, long idx, Func<double> randFunc) =>
        gen switch
        {
            ValueGenerator.Random => randFunc(),
            ValueGenerator.Sequential => idx,
            _ => 1.0
        };
}
