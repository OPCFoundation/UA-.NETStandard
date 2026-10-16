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
using Opc.Ua;

namespace GmsSample.Server
{
    /// <summary>
    /// Selects a finite, loopback-only simulation without equipment or secret configuration.
    /// </summary>
    internal sealed record GmsServerOptions
    {
        /// <summary>
        /// Gets the loopback listen port; zero selects an available port.
        /// </summary>
        public int Port { get; private init; } = 62558;

        /// <summary>
        /// Gets the running window after actual OPC UA readiness.
        /// </summary>
        public int Seconds { get; private init; } = 60;

        /// <summary>
        /// Gets the number of independent provider/store/instance triples.
        /// </summary>
        public int Instances { get; private init; } = 1;

        /// <summary>
        /// Gets whether native optional observations and result transfers are selected.
        /// </summary>
        public bool Advanced { get; private init; }

        /// <summary>
        /// Gets explicit consent to accept unknown certificates on the loopback listener.
        /// </summary>
        public bool DemoTrust { get; private init; }

        /// <summary>
        /// Gets the persistent certificate-store root.
        /// </summary>
        public string PkiRoot { get; private init; } = Path.Combine("artifacts", "gms-pki", "server");

        /// <summary>
        /// Validates all flags before allocating a host, socket or certificate store.
        /// </summary>
        /// <exception cref="ArgumentException">An argument is unsupported or outside its finite bound.</exception>
        internal static GmsServerOptions Parse(ArrayOf<string> arguments)
        {
            var options = new GmsServerOptions();
            for (int i = 0; i < arguments.Count; i++)
            {
                options = arguments[i] switch
                {
                    "--port" => options with { Port = SampleArguments.ReadInteger(arguments, ref i, 0, 65535) },
                    "--seconds" => options with { Seconds = SampleArguments.ReadInteger(arguments, ref i, 1, 120) },
                    "--instances" => options with { Instances = SampleArguments.ReadInteger(arguments, ref i, 1, 2) },
                    "--pki" => options with { PkiRoot = SampleArguments.ReadValue(arguments, ref i) },
                    "--advanced" => options with { Advanced = true },
                    "--demo-trust" => options with { DemoTrust = true },
                    _ => throw new ArgumentException($"Unknown option '{arguments[i]}'.", nameof(arguments))
                };
            }
            return options;
        }

        /// <summary>
        /// Prints the supported security, lifetime and feature choices without starting the server.
        /// </summary>
        internal static void PrintHelp()
        {
            Console.WriteLine("""
                GmsServer [--port <0..65535>] [--seconds <1..120>] [--instances <1..2>]
                          [--pki <directory>] [--advanced] [--demo-trust] [--help]
                Defaults: 127.0.0.1:62558/GmsServer, 60 seconds, one mandatory-only GMS.
                SIMULATION ONLY: no equipment connection, measurement/job commands or physical effects.
                --advanced adds sensors, environmental warnings, loading, calibration, job/part observations,
                characteristics, corrections and result files. No complete GMS Production facet is claimed.
                --demo-trust accepts unknown certificates ONLY on the numeric loopback listener.
                SignAndEncrypt is always required. Otherwise administer the retained certificate stores.
                At most 256 results/8 MiB per instance; one metadata handle and one file transfer per instance.
                READY reports actual startup. Ctrl+C cancels and awaits instance/host cleanup.
                """);
        }
    }
}
