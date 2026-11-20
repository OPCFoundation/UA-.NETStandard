// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
