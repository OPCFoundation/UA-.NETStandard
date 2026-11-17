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
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Opc.Ua.Server;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Perf.ServerLoadHarness
{
    /// <summary>
    /// Simulated regressions for validating the load floors (<c>perf/regression</c>): extra
    /// allocation and CPU time per written value and per reported event, added in the server
    /// child by <c>--inject-alloc</c> (bytes) and <c>--inject-spin-us</c> (microseconds).
    /// Off by default; the product code is unchanged.
    /// </summary>
    internal static class FaultInjection
    {
        /// <summary>
        /// Bytes allocated per written value and per reported event.
        /// </summary>
        public static int AllocBytes { get; set; }

        /// <summary>
        /// Busy-wait per written value and per reported event, in microseconds.
        /// </summary>
        public static int SpinMicroseconds { get; set; }

        public static bool Enabled => AllocBytes > 0 || SpinMicroseconds > 0;

        /// <summary>
        /// Applies the configured cost <paramref name="count"/> times.
        /// </summary>
        public static void Apply(int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (AllocBytes > 0)
                {
                    Consume(new byte[AllocBytes]);
                }
                if (SpinMicroseconds > 0)
                {
                    long until = Stopwatch.GetTimestamp() + (SpinMicroseconds * Stopwatch.Frequency / 1_000_000);
                    while (Stopwatch.GetTimestamp() < until)
                    {
                        // burn CPU like a per-item hook would
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Consume(byte[] buffer)
        {
            GC.KeepAlive(buffer);
        }
    }

    /// <summary>
    /// The reference server with <see cref="FaultInjection"/> applied per written value.
    /// </summary>
    internal sealed class FaultInjectingReferenceServer : ReferenceServer
    {
        public FaultInjectingReferenceServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
        }

        public override ValueTask<WriteResponse> WriteAsync(
            SecureChannelContext secureChannelContext,
            RequestHeader? requestHeader,
            ArrayOf<WriteValue> nodesToWrite,
            RequestLifetime requestLifetime)
        {
            FaultInjection.Apply(nodesToWrite.Count);
            return base.WriteAsync(secureChannelContext, requestHeader, nodesToWrite, requestLifetime);
        }
    }
}
