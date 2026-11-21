// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Linq;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Recycling;

if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
{
    Console.WriteLine(
        "Generic recycling sample (.NET 10 and 11).\n" +
        "From source: dotnet run -f <net10.0|net11.0> -- <options>.\n" +
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
