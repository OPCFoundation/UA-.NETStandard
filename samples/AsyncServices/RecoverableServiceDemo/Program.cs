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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

[assembly: CLSCompliant(false)]

namespace RecoverableServiceDemo
{
    /// <summary>
    /// Runs a local long-running Method, optionally disconnecting its native transport.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Runs one bounded scenario and returns a nonzero exit code on an unexpected outcome.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--network-outage"))
            {
                await Console.Error.WriteLineAsync(
                    "Usage: RecoverableServiceDemo [--network-outage]").ConfigureAwait(false);
                return 2;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                _ = await RecoveryDemo.RunAsync(args.Length == 1, Console.Out, timeout.Token).ConfigureAwait(false);
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
