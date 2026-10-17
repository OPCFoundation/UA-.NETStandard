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
using System.Linq;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Recycling;

if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine(
        "Generic recycling sample (.NET 8, 9 and 10).\n" +
        "From source: dotnet run -f <net8.0|net9.0|net10.0> -- <options>.\n" +
        "Select --role coordinator or --process <collection|sorting|shredding|washing|density-separation|" +
        "drying|extrusion|melt-filtration|pelletizing|bagging>.\n" +
        "Required: --identities <provisioned-role-mapping.json>.\n" +
        "Views also require --coordinator <opc.tcp-url> --identity-store <store> --identity-subject <subject>.\n" +
        "Optional: --line-id <id> --endpoint <opc.tcp-url> --pki-root <path> --state-root <path> " +
        "--scenario <healthy|sorter-drift|utility-disturbance|combined|recovery|equipment-faults|file.json> " +
        "--pace-ms <100..60000> --view-poll-ms <100..less-than-half-lease-ms>.\n" +
        "Trust is explicit; unknown peer/user certificates are never automatically accepted.\n" +
        "Native companion data bindings are read-only. No controls connect to physical equipment. " +
        "See the sample README.");
    return 0;
}

RecyclingHostOptions options = await RecyclingCommandLine.LoadAsync(args.ToArrayOf()).ConfigureAwait(false);
IHost host = RecyclingHost.Build(options);
await host.RunAsync().ConfigureAwait(false);
return 0;
