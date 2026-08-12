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
using Opc.Ua.AI.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;

try
{
    string endpoint = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
        ? args[0]
        : "opc.tcp://localhost:62640/ModelManagementServer";

    bool insecure = Array.IndexOf(args, "--insecure") >= 0;

    Console.WriteLine("OPC UA AI Model Management client");
    Console.WriteLine("Endpoint: {0}", endpoint);

    if (insecure)
    {
        Console.Error.WriteLine(
            "WARNING: --insecure selects an endpoint without message security.");
    }

    Console.WriteLine();

    HostApplicationBuilder builder = Host.CreateApplicationBuilder();
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.SetMinimumLevel(LogLevel.Warning);

    builder.Services
        .AddOpcUa()
        .AddClient(options =>
        {
            const string applicationName = "ModelManagementClient";
            options.ApplicationName = applicationName;
            options.ApplicationUri = "urn:localhost:OPCFoundation:ModelManagementClient";
            options.ProductUri = "uri:opcfoundation.org:ModelManagementClient";
            options.PkiRoot = Path.Combine(
                Path.GetTempPath(), "OPC Foundation", applicationName, "pki");
            // Sample convenience only; never auto-accept in production.
            options.AutoAcceptUntrustedCertificates = true;
            options.RejectSHA1SignedCertificates = true;
            options.MinimumCertificateKeySize = 2048;
            options.Session = new ManagedSessionOptions
            {
                SessionName = applicationName,
                SessionTimeout = TimeSpan.FromSeconds(60)
            };
        })
        .AddDiscoveryAndConnect(options =>
        {
            options.DiscoveryUrl = endpoint;
            options.SecurityMode = insecure
                ? MessageSecurityMode.None
                : MessageSecurityMode.SignAndEncrypt;
            options.SecurityPolicyUri = insecure
                ? SecurityPolicies.None
                : SecurityPolicies.Basic256Sha256;
        })
        .AddAiClient();

    using IHost host = builder.Build();
    await host.StartAsync(CancellationToken.None).ConfigureAwait(false);

    try
    {
        Func<CancellationToken, Task<ManagedSession>> connect = host.Services
            .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();

        ManagedSession session = await connect(CancellationToken.None).ConfigureAwait(false);

        await using (session)
        {
            Console.WriteLine("Connected.");
            Console.WriteLine();

            ITelemetryContext telemetry = host.Services.GetRequiredService<ITelemetryContext>();
            AiScenarioRunner? runner = AiScenarioRunner.TryCreate(session, telemetry);

            if (runner is null)
            {
                Console.Error.WriteLine(
                    "This Server does not implement OPC UA - AI Model Management.");
                return 2;
            }

            await runner.RunAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
    finally
    {
        await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}
