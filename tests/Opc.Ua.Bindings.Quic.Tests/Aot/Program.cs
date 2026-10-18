// Copyright (c) OPC Foundation. Licensed under the OPC Foundation MIT License 1.00.

using System;
using System.Threading.Tasks;
using Opc.Ua.Native.QuicGo;

namespace Opc.Ua.Bindings.Quic.Tests
{
    /// <summary>
    /// Runs the same framework-independent real native flow checks in a NativeAOT executable.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Accepts the reviewed native library path, keeping asset selection explicit.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Length != 1)
                {
                    throw new ArgumentException("Pass the reviewed native QUIC library path.");
                }
                INativeQuicBackend backend = await NativeFlowTestEnvironment.LoadBackendAsync(args[0]).ConfigureAwait(false);
                ArrayOf<string> checks = await NativeFlowLiveScenario.RunAsync(backend).ConfigureAwait(false);
                foreach (string check in checks)
                {
                    Console.WriteLine("PASS " + check);
                }
                Console.WriteLine($"Passed {checks.Count} real native flow checks.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
    }
}
