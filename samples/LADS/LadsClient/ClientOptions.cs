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

namespace LadsSample.Client
{
    /// <summary>
    /// Command line options of the client.
    /// </summary>
    internal sealed record ClientOptions
    {
        public string Endpoint { get; init; } = "opc.tcp://localhost:62565/LadsServer";

        public string? PkiRoot { get; init; }

        public bool AutoAccept { get; init; } = true;

        /// <summary>
        /// Parses <c>[--url endpoint] [--pki-root path] [--autoaccept true|false]</c>.
        /// </summary>
        public static ClientOptions Parse(string[] args)
        {
            var options = new ClientOptions();
            for (int ii = 0; ii < args.Length; ii++)
            {
                string argument = args[ii];
                switch (argument)
                {
                    case "--url" when ii + 1 < args.Length:
                        options = options with { Endpoint = args[++ii] };
                        break;
                    case "--pki-root" when ii + 1 < args.Length && Path.IsPathFullyQualified(args[ii + 1]):
                        options = options with { PkiRoot = args[++ii] };
                        break;
                    case "--autoaccept" when ii + 1 < args.Length && bool.TryParse(args[ii + 1], out bool accept):
                        ii++;
                        options = options with { AutoAccept = accept };
                        break;
                    default:
                        throw new ArgumentException($"Invalid option '{argument}'.", nameof(args));
                }
            }
            return options;
        }
    }
}
