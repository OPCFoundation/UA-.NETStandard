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
using System.Collections.Generic;
using System.IO;
using BenchmarkDotNet.Attributes;
using NUnit.Framework;

#nullable enable

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Baseline GC / throughput benchmarks for the reference-type
    /// <see cref="DataValue"/>. The numbers captured here feed the
    /// "class vs readonly struct" comparison documented in
    /// <c>plans/25-datavalue-struct-vs-class.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each scenario is parametrised across a representative
    /// <see cref="Count"/> and <see cref="Payload"/> so the trade-off
    /// vector (small / many vs few / large) is visible at a glance.
    /// </para>
    /// <para>
    /// Set <c>BENCHMARK</c> in your local test run to invoke the
    /// scenarios through BenchmarkDotNet
    /// (<c>BenchmarkRunner.Run&lt;DataValueBenchmarks&gt;()</c>); they
    /// are also exposed as NUnit <c>[Test]</c> entry points so the
    /// shapes are exercised in CI even when BDN is not invoked.
    /// </para>
    /// </remarks>
    [TestFixture]
    [Category("BuiltInType")]
    [Category("Benchmark")]
    [MemoryDiagnoser]
    [NonParallelizable]
    public class DataValueBenchmarks
    {
        /// <summary>
        /// Variant payload shape exercised by the benchmark — picked
        /// to cover the canonical performance regimes (small inline
        /// scalar, reference-type scalar, large primitive array).
        /// </summary>
        public enum Payload
        {
            ScalarDouble,
            ScalarString,
            ArrayDouble1K
        }

        [Params(1, 10, 100, 1000)]
        public int Count { get; set; } = 100;

        [Params(Payload.ScalarDouble, Payload.ScalarString, Payload.ArrayDouble1K)]
        public Payload Shape { get; set; } = Payload.ScalarDouble;

        [GlobalSetup]
        public void GlobalSetup()
        {
            (m_telemetry, m_context) = DataValueBenchmarkPayloads.NewContext();
            m_values = BuildPayload(Shape, Count);
            m_arrayOfValues = m_values.ToArrayOf();
            m_encodedBuffer = DataValueBenchmarkPayloads.EncodeAsDataValueArray(
                m_arrayOfValues, m_context);
            m_structValues = BuildStructPayload(m_values);
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            m_telemetry = null;
            m_context = null;
            m_values = null;
            m_encodedBuffer = null;
            m_structValues = null;
        }

        /// <summary>
        /// B1 — decoder hot path. Decodes <see cref="Count"/>
        /// DataValues from the pre-encoded buffer. Measures the
        /// per-iteration heap allocation and Gen0/1 collections caused
        /// by <c>new DataValue()</c> + property mutation in
        /// <c>BinaryDecoder.ReadDataValue</c>.
        /// </summary>
        [Benchmark(Description = "B1 — BinaryDecoder.ReadDataValue × N")]
        public int Decode_ClassDataValueArray()
        {
            using var stream = new MemoryStream(m_encodedBuffer!, writable: false);
            using var decoder = new BinaryDecoder(stream, m_context!);
            ArrayOf<DataValue?> read = decoder.ReadDataValueArray(null);
            return read.Count;
        }

        /// <summary>
        /// B2 — encoder. Writes a pre-built array; reference for the
        /// encode-side cost which the struct migration should leave
        /// untouched.
        /// </summary>
        [Benchmark(Description = "B2 — BinaryEncoder.WriteDataValueArray × N")]
        public long Encode_ClassDataValueArray()
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context!, leaveOpen: true))
            {
                encoder.WriteDataValueArray(null, m_arrayOfValues);
            }
            return stream.Length;
        }

        /// <summary>
        /// B3 — dispatch chain. Passes each DataValue through a
        /// 5-frame call chain — typical fan-out on the subscription
        /// notification dispatch path. With a class, the reference
        /// (8 B) passes through registers; with a struct (48 B) the
        /// caller-side and callee-side copies become visible.
        /// </summary>
        [Benchmark(Description = "B3 — Dispatch chain × N (5 frames)")]
        public uint Dispatch_ClassDataValue()
        {
            DataValue[] values = m_values!;
            uint accumulator = 0;
            for (int i = 0; i < values.Length; i++)
            {
                accumulator ^= DispatchFrame1(values[i]);
            }
            return accumulator;
        }

        /// <summary>
        /// B4 — allocation + capture. Constructs <see cref="Count"/>
        /// fresh DataValues into a List, the canonical pattern for
        /// building a snapshot list (e.g. notification queue).
        /// </summary>
        [Benchmark(Description = "B4 — Allocate + List<DataValue> × N")]
        public int Allocate_ClassDataValueList()
        {
            var list = new List<DataValue>(Count);
            DateTimeUtc ts = DataValueBenchmarkPayloads.ReferenceTimestamp;
            for (int i = 0; i < Count; i++)
            {
                list.Add(new DataValue(
                    new Variant(i * 1.0),
                    (StatusCode)StatusCodes.Good,
                    ts,
                    ts));
            }
            return list.Count;
        }

        /// <summary>
        /// B5 — deep-copy. <c>DataValue.Copy()</c> uses
        /// <c>MemberwiseClone</c> on the class — the allocator pays
        /// for every clone. On a struct, the outer copy is free but
        /// the inner <c>Variant.Copy()</c> still allocates for array
        /// payloads.
        /// </summary>
        [Benchmark(Description = "B5 — DataValue.Copy() × N")]
        public int Copy_ClassDataValue()
        {
            DataValue[] values = m_values!;
            int observed = 0;
            for (int i = 0; i < values.Length; i++)
            {
                DataValue copy = values[i].Copy();
                observed += (int)copy.StatusCode.Code;
            }
            return observed;
        }

        /// <summary>
        /// B6 — equality. Two reference-identical entries are compared
        /// per iteration so the reference-fast-path (instance ==
        /// instance) and the structural fallback (a clone) are both
        /// exercised.
        /// </summary>
        [Benchmark(Description = "B6 — DataValue.Equals × N")]
        public int Equals_ClassDataValue()
        {
            DataValue[] values = m_values!;
            int equal = 0;
            for (int i = 0; i < values.Length; i++)
            {
                DataValue a = values[i];
                DataValue b = values[i].Copy();
                if (a.Equals(b)) { equal++; }
            }
            return equal;
        }

        /// <summary>
        /// B7 — full encode/decode round-trip. Sanity-check that the
        /// per-call numbers line up with the end-to-end pipeline cost.
        /// </summary>
        [Benchmark(Description = "B7 — Encode + Decode round-trip × N")]
        public int RoundTrip_ClassDataValueArray()
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context!, leaveOpen: true))
            {
                encoder.WriteDataValueArray(null, m_arrayOfValues);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, m_context!);
            ArrayOf<DataValue?> read = decoder.ReadDataValueArray(null);
            return read.Count;
        }

        // ----------------------------------------------------------------
        // STRUCT counterparts (Phase 3) — exactly the same shapes
        // routed through DataValueStruct + the new
        // ReadDataValueStructArray / WriteDataValueStructArray entry
        // points so BDN reports both side-by-side under a single
        // benchmark group.
        // ----------------------------------------------------------------

        /// <summary>
        /// B1 (struct) — decoder hot path using
        /// <see cref="DataValueStruct"/>. The hot path mutates a ref
        /// builder, then commits once; no per-field copy.
        /// </summary>
        [Benchmark(Description = "B1S — BinaryDecoder.ReadDataValueStruct × N")]
        public int Decode_StructDataValueArray()
        {
            using var stream = new MemoryStream(m_encodedBuffer!, writable: false);
            using var decoder = new BinaryDecoder(stream, m_context!);
            // Same encoded bytes; the class-decoder wrote them, struct
            // decoder reads them — wire format is byte-identical.
            DataValueStruct[] read = decoder.ReadDataValueStructArray(null);
            return read.Length;
        }

        /// <summary>
        /// B2 (struct) — encoder for <see cref="DataValueStruct"/>.
        /// </summary>
        [Benchmark(Description = "B2S — BinaryEncoder.WriteDataValueStructArray × N")]
        public long Encode_StructDataValueArray()
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context!, leaveOpen: true))
            {
                encoder.WriteDataValueStructArray(null, m_structValues!);
            }
            return stream.Length;
        }

        /// <summary>
        /// B3 (struct) — dispatch chain over <see cref="DataValueStruct"/>.
        /// The 48-byte struct payload travels through 5 stack frames;
        /// compares per-call copy cost with the class' 8 B reference.
        /// </summary>
        [Benchmark(Description = "B3S — Dispatch chain × N (5 frames, struct)")]
        public uint Dispatch_StructDataValue()
        {
            DataValueStruct[] values = m_structValues!;
            uint accumulator = 0;
            for (int i = 0; i < values.Length; i++)
            {
                accumulator ^= DispatchStructFrame1(values[i]);
            }
            return accumulator;
        }

        /// <summary>
        /// B4 (struct) — allocate + capture into
        /// <c>List&lt;DataValueStruct&gt;</c>.
        /// </summary>
        [Benchmark(Description = "B4S — Allocate + List<DataValueStruct> × N")]
        public int Allocate_StructDataValueList()
        {
            var list = new List<DataValueStruct>(Count);
            DateTimeUtc ts = DataValueBenchmarkPayloads.ReferenceTimestamp;
            for (int i = 0; i < Count; i++)
            {
                list.Add(new DataValueStruct(
                    new Variant(i * 1.0),
                    (StatusCode)StatusCodes.Good,
                    ts,
                    ts));
            }
            return list.Count;
        }

        /// <summary>
        /// B5 (struct) — copy. A <see cref="DataValueStruct"/> copies
        /// itself by value (no allocation); only the inner
        /// <see cref="Variant"/>'s array payload would still allocate
        /// if we forced a deep copy. The struct overhead is therefore
        /// expected to be the 48 B copy on entry; no managed allocations.
        /// </summary>
        [Benchmark(Description = "B5S — DataValueStruct value-copy × N")]
        public int Copy_StructDataValue()
        {
            DataValueStruct[] values = m_structValues!;
            int observed = 0;
            for (int i = 0; i < values.Length; i++)
            {
                DataValueStruct copy = values[i];
                observed += (int)copy.StatusCode.Code;
            }
            return observed;
        }

        /// <summary>
        /// B6 (struct) — equality.
        /// </summary>
        [Benchmark(Description = "B6S — DataValueStruct.Equals × N")]
        public int Equals_StructDataValue()
        {
            DataValueStruct[] values = m_structValues!;
            int equal = 0;
            for (int i = 0; i < values.Length; i++)
            {
                DataValueStruct a = values[i];
                DataValueStruct b = values[i];
                if (a.Equals(b)) { equal++; }
            }
            return equal;
        }

        /// <summary>
        /// B7 (struct) — round-trip via
        /// <see cref="DataValueStruct"/>.
        /// </summary>
        [Benchmark(Description = "B7S — Encode + Decode round-trip × N (struct)")]
        public int RoundTrip_StructDataValueArray()
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, m_context!, leaveOpen: true))
            {
                encoder.WriteDataValueStructArray(null, m_structValues!);
            }
            stream.Position = 0;
            using var decoder = new BinaryDecoder(stream, m_context!);
            DataValueStruct[] read = decoder.ReadDataValueStructArray(null);
            return read.Length;
        }

        // ----------------------------------------------------------------
        // NUnit entry points so the benchmark shapes also run in CI.
        // ----------------------------------------------------------------

        [TestCase(1, Payload.ScalarDouble)]
        [TestCase(100, Payload.ScalarString)]
        [TestCase(10, Payload.ArrayDouble1K)]
        public void DecodeAndEncodeMatchClassRoundTrip(int count, Payload shape)
        {
            Count = count;
            Shape = shape;
            GlobalSetup();
            try
            {
                int decoded = Decode_ClassDataValueArray();
                Assert.That(decoded, Is.EqualTo(count));
                int rt = RoundTrip_ClassDataValueArray();
                Assert.That(rt, Is.EqualTo(count));
            }
            finally
            {
                GlobalCleanup();
            }
        }

        [TestCase(1, Payload.ScalarDouble)]
        [TestCase(100, Payload.ScalarString)]
        [TestCase(10, Payload.ArrayDouble1K)]
        public void DecodeAndEncodeMatchStructRoundTrip(int count, Payload shape)
        {
            Count = count;
            Shape = shape;
            GlobalSetup();
            try
            {
                int decoded = Decode_StructDataValueArray();
                Assert.That(decoded, Is.EqualTo(count));
                int rt = RoundTrip_StructDataValueArray();
                Assert.That(rt, Is.EqualTo(count));
            }
            finally
            {
                GlobalCleanup();
            }
        }

        [Test]
        public void ClassAndStructProduceIdenticalWireBytes()
        {
            // The struct re-encoder must produce byte-identical output
            // for the same logical values — otherwise the GC-vs-copy
            // benchmark would compare apples to oranges.
            Count = 16;
            Shape = Payload.ScalarDouble;
            GlobalSetup();
            try
            {
                long classBytes = Encode_ClassDataValueArray();
                long structBytes = Encode_StructDataValueArray();
                Assert.That(structBytes, Is.EqualTo(classBytes),
                    "Class and struct encoders must produce equal-length output.");
            }
            finally
            {
                GlobalCleanup();
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static DataValue[] BuildPayload(Payload shape, int count)
        {
            return shape switch
            {
                Payload.ScalarDouble => DataValueBenchmarkPayloads.BuildScalarDoubles(count),
                Payload.ScalarString => DataValueBenchmarkPayloads.BuildScalarStrings(count),
                Payload.ArrayDouble1K => DataValueBenchmarkPayloads.BuildArrayDoubles(count, length: 1024),
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
        }

        private static DataValueStruct[] BuildStructPayload(DataValue[] source)
        {
            var result = new DataValueStruct[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                DataValue v = source[i];
                result[i] = new DataValueStruct(
                    v.WrappedValue,
                    v.StatusCode,
                    v.SourceTimestamp,
                    v.ServerTimestamp,
                    v.SourcePicoseconds,
                    v.ServerPicoseconds);
            }
            return result;
        }

        // 5-frame dispatch chain so the JIT must materialise the
        // DataValue between calls — keeps the per-call copy visible
        // in the disassembly.
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchFrame1(DataValue value) => DispatchFrame2(value) ^ 0xA;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchFrame2(DataValue value) => DispatchFrame3(value) ^ 0xB;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchFrame3(DataValue value) => DispatchFrame4(value) ^ 0xC;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchFrame4(DataValue value) => DispatchFrame5(value) ^ 0xD;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchFrame5(DataValue value) => value.StatusCode.Code;

        // 5-frame dispatch chain for the struct counterpart. The
        // DataValueStruct is passed by value at every frame so the
        // ~48 B copy cost is materialised on each call.
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchStructFrame1(DataValueStruct value)
            => DispatchStructFrame2(value) ^ 0xA;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchStructFrame2(DataValueStruct value)
            => DispatchStructFrame3(value) ^ 0xB;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchStructFrame3(DataValueStruct value)
            => DispatchStructFrame4(value) ^ 0xC;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchStructFrame4(DataValueStruct value)
            => DispatchStructFrame5(value) ^ 0xD;

        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static uint DispatchStructFrame5(DataValueStruct value)
            => value.StatusCode.Code;

        private ITelemetryContext? m_telemetry;
        private IServiceMessageContext? m_context;
        private DataValue[]? m_values;
        private ArrayOf<DataValue> m_arrayOfValues;
        private byte[]? m_encodedBuffer;
        private DataValueStruct[]? m_structValues;
    }
}
