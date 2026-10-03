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

namespace AmbSample.Client
{
    /// <summary>
    /// The command line of the sample client.
    /// </summary>
    internal sealed record AmbClientOptions
    {
        /// <summary>Gets the endpoint of the server.</summary>
        public string ServerUrl { get; init; } = "opc.tcp://localhost:62553/AmbServer";

        /// <summary>Gets whether any server certificate is accepted; demo only.</summary>
        public bool Insecure { get; init; }

        /// <summary>Gets how long alarms and maintenance are streamed; 0 skips it.</summary>
        public int WatchSeconds { get; init; } = 30;

        /// <summary>Gets an AssetId to write to the first configurable asset, if any.</summary>
        public string? AssetId { get; init; }

        public static AmbClientOptions Parse(string[] args)
        {
            var options = new AmbClientOptions();
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
                    case "--asset-id" when ii + 1 < args.Length:
                        options = options with { AssetId = args[++ii] };
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
