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
using System.Globalization;
using System.IO;
using Opc.Ua.Tests;

#nullable enable

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Reusable Variant / DataValue fixtures and pre-encoded byte buffers
    /// for the <see cref="DataValueBenchmarks"/> suite. Building the
    /// payloads once in the fixture (rather than per-iteration) keeps
    /// the benchmarks focused on the hot path under measurement (read /
    /// write / dispatch) and avoids charging fixture cost to a single
    /// scenario.
    /// </summary>
    /// <remarks>
    /// DataValue is now a <c>readonly struct</c>; the benchmark
    /// suite focuses on encode/decode and dispatch throughput on
    /// the unified type.
    /// </remarks>
    public static class DataValueBenchmarkPayloads
    {
        /// <summary>
        /// The fixed UTC reference timestamp used by every synthesized
        /// DataValue. Holding the timestamp constant keeps the
        /// generated payloads byte-deterministic across runs.
        /// </summary>
        public static readonly DateTimeUtc ReferenceTimestamp = new(
            new DateTime(2024, 03, 01, 06, 05, 59, DateTimeKind.Utc).Ticks +
            456789);

        /// <summary>
        /// Construct a representative scalar-<see cref="double"/>
        /// <see cref="DataValue"/> array of the requested size. ~10% of
        /// entries carry <c>BadDataLost</c> to exercise both the Good
        /// and Bad encoding paths.
        /// </summary>
        public static DataValue[] BuildScalarDoubles(int count, int seed = 42)
        {
            UnsecureRandom rand = new UnsecureRandom(seed);
            var result = new DataValue[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = new DataValue(
                    new Variant((rand.NextDouble() - 0.5) * 1000.0),
                    rand.NextDouble() > 0.1
                        ? (StatusCode)StatusCodes.Good
                        : (StatusCode)StatusCodes.BadDataLost,
                    ReferenceTimestamp,
                    ReferenceTimestamp);
            }
            return result;
        }

        /// <summary>
        /// Construct a representative scalar-<see cref="string"/>
        /// payload — exercises the reference-type Variant path.
        /// </summary>
        public static DataValue[] BuildScalarStrings(int count, int seed = 42)
        {
            UnsecureRandom rand = new UnsecureRandom(seed);
            var result = new DataValue[count];
            for (int i = 0; i < count; i++)
            {
                string s = "value-" + rand.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture);
                result[i] = new DataValue(
                    new Variant(s),
                    (StatusCode)StatusCodes.Good,
                    ReferenceTimestamp,
                    ReferenceTimestamp);
            }
            return result;
        }

        /// <summary>
        /// Construct a payload where every Variant carries an
        /// <c>ArrayOf&lt;double&gt;[length]</c>. Exercises the
        /// array-payload path which dominates GC for high-rate
        /// telemetry assets.
        /// </summary>
        public static DataValue[] BuildArrayDoubles(
            int count,
            int length = 1024,
            int seed = 42)
        {
            UnsecureRandom rand = new UnsecureRandom(seed);
            var result = new DataValue[count];
            for (int i = 0; i < count; i++)
            {
                double[] payload = new double[length];
                for (int j = 0; j < length; j++)
                {
                    payload[j] = (rand.NextDouble() - 0.5) * 1000.0;
                }
                result[i] = new DataValue(
                    new Variant(payload.ToArrayOf()),
                    (StatusCode)StatusCodes.Good,
                    ReferenceTimestamp,
                    ReferenceTimestamp);
            }
            return result;
        }

        /// <summary>
        /// Build a binary-encoded blob containing
        /// <paramref name="values"/> as a length-prefixed DataValue
        /// array, ready for repeated decoder benchmarks against a
        /// <see cref="MemoryStream"/> reset to position 0.
        /// </summary>
        public static byte[] EncodeAsDataValueArray(
            ArrayOf<DataValue> values,
            IServiceMessageContext context)
        {
            using var stream = new MemoryStream();
            using (var encoder = new BinaryEncoder(stream, context, leaveOpen: true))
            {
                encoder.WriteDataValueArray(null, values);
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Create a fresh telemetry context + service-message context
        /// for the benchmark fixture. The context is intentionally
        /// re-created per benchmark instance so namespace tables and
        /// encodeable factories are not shared between scenarios.
        /// </summary>
        public static (ITelemetryContext Telemetry, IServiceMessageContext Context) NewContext()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.Create(telemetry);
            return (telemetry, context);
        }
    }
}
