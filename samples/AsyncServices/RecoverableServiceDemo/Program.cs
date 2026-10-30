/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;

[assembly: CLSCompliant(false)]

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Runs a local long-running Method or an explicitly supervised child-process restart TEST.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Runs one bounded scenario and returns a nonzero exit code on an unexpected outcome.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            bool child = args.Length == 3 && args[0] == "--process-restart-child";
            bool restart = args.Length == 1 && args[0] == "--process-restart-test";
            bool outage = args.Length == 1 && args[0] == "--network-outage";
            int supervisorPid = 0;
            if ((args.Length != 0 && !outage && !restart && !child) ||
                (child &&
                    (!args[1].StartsWith("ua-rse-restart-test-", StringComparison.Ordinal) ||
                        !Guid.TryParseExact(args[1]["ua-rse-restart-test-".Length..], "N", out _) ||
                        !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out supervisorPid) ||
                        supervisorPid <= 0 ||
                        supervisorPid == Environment.ProcessId)))
            {
                await Console.Error.WriteLineAsync(
                    "Usage: RecoverableServiceDemo [--network-outage | --process-restart-test]").ConfigureAwait(false);
                return 2;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                if (child || restart)
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        // Main catches this and returns exit code 1; it is not an escaping API exception.
                        // TODO: Remove when RCS1140 recognizes locally handled exceptions in Main.
#pragma warning disable RCS1140
                        throw new PlatformNotSupportedException(
                            "The process-restart TEST uses current-user-only Windows pipes " +
                            "with OS peer-PID verification.");
#pragma warning restore RCS1140
                    }
                    if (child)
                    {
                        await ProcessRestartHost.RunAsync(args[1], supervisorPid, timeout.Token).ConfigureAwait(false);
                    }
                    else
                    {
                        _ = await ProcessRestartDemo.RunTestAsync(Console.Out, timeout.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    _ = await RecoveryDemo.RunAsync(outage, Console.Out, timeout.Token).ConfigureAwait(false);
                }
                return 0;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(ex.ToString()).ConfigureAwait(false);
                return 1;
            }
        }
    }
}
