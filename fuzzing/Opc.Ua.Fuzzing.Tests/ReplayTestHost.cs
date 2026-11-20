// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;

namespace Opc.Ua.Fuzzing.Tests
{
    internal static class ReplayTestHost
    {
        public static int Main(string[] args)
        {
            // Keep expected negative cases out of OS crash reporting without converting a failure to success.
            try
            {
                return Program.Main(args);
            }
            catch (InvalidOperationException exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine(exception);
                return 1;
            }
        }
    }
}
