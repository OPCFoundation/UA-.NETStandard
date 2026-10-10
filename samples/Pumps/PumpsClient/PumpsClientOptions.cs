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

namespace PumpsSample.Client
{
    /// <summary>
    /// Command-line options of the sample client.
    /// </summary>
    internal sealed record PumpsClientOptions
    {
        /// <summary>
        /// The endpoint to connect to.
        /// </summary>
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62547/PumpsServer";

        /// <summary>
        /// Accepts any server certificate. Demo only.
        /// </summary>
        public bool Insecure { get; init; }

        /// <summary>
        /// How long to stream live values, in seconds. 0 skips it.
        /// </summary>
        public int WatchSeconds { get; init; } = 20;

        public static PumpsClientOptions Parse(string[] args)
        {
            var options = new PumpsClientOptions();
            for (int ii = 0; ii < args.Length; ii++)
            {
                string argument = args[ii];
                switch (argument)
                {
                    case "--insecure":
                        options = options with { Insecure = true };
                        break;
                    case "--url" when ii + 1 < args.Length:
                        options = options with { ServerUrl = args[++ii] };
                        break;
                    case "--seconds" when ii + 1 < args.Length:
                        options = options with
                        {
                            WatchSeconds = int.Parse(args[++ii], CultureInfo.InvariantCulture)
                        };
                        break;
                    default:
                        if (argument.StartsWith("--", StringComparison.Ordinal))
                        {
                            throw new ArgumentException($"Unknown option '{argument}'.", nameof(args));
                        }
                        options = options with { ServerUrl = argument };
                        break;
                }
            }
            return options;
        }
    }
}
