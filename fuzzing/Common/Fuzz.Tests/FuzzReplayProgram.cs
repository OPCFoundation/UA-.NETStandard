/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Hosts synchronous regression callbacks outside the NUnit process.
    /// </summary>
    internal static class FuzzReplayProgram
    {
        /// <summary>
        /// Replays one input through every fuzz target of the host.
        /// </summary>
        public const string ReplayAllOption = "--fuzz-replay-all";

        public static int Main(string[] args)
        {
            if (args.Length != 0 && args[0] == ReplayAllOption)
            {
                if (args.Length != 2)
                {
                    Console.Error.WriteLine($"Usage: {ReplayAllOption} <input-file>");
                    return 2;
                }
                byte[] input = File.ReadAllBytes(args[1]);
                int targets = 0;
                foreach (Type delegateType in FuzzMethods.Delegates)
                {
                    foreach (Delegate target in FuzzMethods.FindFuzzMethods(delegateType))
                    {
                        Console.WriteLine(target.Method.Name);
                        FuzzMethods.Replay(target, input);
                        targets++;
                    }
                }
                return targets == 0 ? 2 : 0;
            }

            if (args.Length != 0 && args[0] == "--fuzz-replay")
            {
                // A malformed replay request must fail loudly rather than fall through
                // to the benchmark host: the watchdog regressions assert a zero exit, so
                // exiting 0 here would report a pass for an input never replayed.
                if (args.Length != 3)
                {
                    Console.Error.WriteLine("Usage: --fuzz-replay <target> <input-file>");
                    return 2;
                }
                Delegate? target = FuzzMethods.FindFuzzMethod(Console.Error, args[1]);
                if (target == null)
                {
                    return 2;
                }
                FuzzMethods.Replay(target, File.ReadAllBytes(args[2]));
                return 0;
            }

            // The area test hosts link the BenchmarkDotNet entry point, whose Main
            // returns void, so there is no exit code to propagate here.
            Program.Main(args);
            return 0;
        }
    }
}
