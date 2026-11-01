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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.LADS.Client;

namespace LadsSample.Client
{
    /// <summary>
    /// Discovers the incubator, runs an incubation program and downloads its result file.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Parses the options and runs the client until it completes, fails or is cancelled.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            using var cancelled = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancelled.Cancel();
            };
            try
            {
                await RunAsync(ClientOptions.Parse(args), cancelled.Token).ConfigureAwait(false);
                return 0;
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
                return 1;
            }
        }

        /// <summary>
        /// Connects through a managed session and the LADS client factory.
        /// </summary>
        private static async Task RunAsync(ClientOptions options, CancellationToken ct)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.EnableOpcUaRedaction();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddOpcUa()
                .AddClient(client =>
                {
                    client.ApplicationName = "LadsClient";
                    client.ApplicationUri = "urn:localhost:OPCFoundation:LadsClient";
                    client.ProductUri = "uri:opcfoundation.org:LadsClient";
                    client.PkiRoot = options.PkiRoot;
                    // Sample convenience only; never auto-accept untrusted certificates in production.
                    client.AutoAcceptUntrustedCertificates = options.AutoAccept;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = "LadsClient",
                        SessionTimeout = TimeSpan.FromSeconds(30)
                    };
                })
                .AddDiscoveryAndConnect(discovery =>
                {
                    discovery.DiscoveryUrl = options.Endpoint;
                    discovery.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    discovery.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                })
                .AddLadsClient();
            using IHost host = builder.Build();
            await host.StartAsync(ct).ConfigureAwait(false);
            try
            {
                LadsClient client = await host.Services.GetRequiredService<LadsClientFactory>()
                    .CreateAsync(ct).ConfigureAwait(false);
                try
                {
                    await RunIncubationAsync(client, ct).ConfigureAwait(false);
                }
                finally
                {
                    if (client.Session is ManagedSession session)
                    {
                        await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
                        await session.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                await host.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Shows the device, starts the incubation, watches the chamber and prints the result file.
        /// </summary>
        private static async Task RunIncubationAsync(LadsClient client, CancellationToken ct)
        {
            LadsDevice device = await client.GetFirstDeviceAsync(ct).ConfigureAwait(false);
            LadsDeviceSnapshot snapshot = await device.ReadAsync(ct).ConfigureAwait(false);
            Console.WriteLine(
                $"Device {snapshot.DisplayName}: {snapshot.Identification.Manufacturer} " +
                $"{snapshot.Identification.Model} ({snapshot.Identification.SerialNumber}), " +
                $"state {snapshot.DeviceState}");

            LadsFunctionalUnit chamber = await device
                .GetFunctionalUnitAsync(snapshot.FunctionalUnitNames[0], ct)
                .ConfigureAwait(false);
            LadsFunctionalUnitSnapshot unit = await chamber.ReadAsync(cancellationToken: ct).ConfigureAwait(false);
            Console.WriteLine($"Functional unit {unit.DisplayName}: {unit.State}");
            foreach (LadsFunctionSnapshot function in unit.Functions)
            {
                Console.WriteLine($"  {function.DisplayName,-12} {function.Kind,-28} {Describe(function)}");
            }
            foreach (LadsProgramTemplate template in await chamber.GetProgramTemplatesAsync(ct).ConfigureAwait(false))
            {
                Console.WriteLine($"  Program template {template.Name}: {template.Description}");
            }

            LadsFunction door = await chamber.GetFunctionAsync("Door", ct).ConfigureAwait(false);
            await door.LockAsync(ct).ConfigureAwait(false);
            Console.WriteLine($"Door: {(await door.ReadAsync(ct).ConfigureAwait(false)).State}");

            using var watching = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task watch = Task.WhenAll(
                WatchAsync(chamber, "Temperature", watching.Token),
                WatchAsync(chamber, "ShakerSpeed", watching.Token),
                WatchAsync(chamber, "CO2", watching.Token));

            string runId = await chamber.StartProgramAsync(LadsProgramRequest
                .For("Incubation")
                .WithProperty("Volume", "250")
                .WithSample("Flask-1", "Culture-42", "A1"), ct).ConfigureAwait(false);
            Console.WriteLine($"Started program run {runId}");
            await chamber.WaitForStateAsync("Running", TimeSpan.FromSeconds(10), cancellationToken: ct)
                .ConfigureAwait(false);
            await chamber.WaitForStateAsync("Stopped", TimeSpan.FromSeconds(60), cancellationToken: ct)
                .ConfigureAwait(false);
            watching.Cancel();
            await watch.ConfigureAwait(false);
            await door.UnlockAsync(ct).ConfigureAwait(false);

            LadsResult? result = null;
            for (int attempt = 0; result is null && attempt < 20; attempt++)
            {
                result = await chamber.FindResultAsync(runId, ct).ConfigureAwait(false);
                if (result is null)
                {
                    await Task.Delay(250, ct).ConfigureAwait(false);
                }
            }
            if (result is null)
            {
                throw new InvalidOperationException($"No result for program run {runId}.");
            }
            Console.WriteLine($"Program run {runId} completed with {result.Files.Count} result file(s).");
            for (int ii = 0; ii < result.Files.Count; ii++)
            {
                LadsResultFile file = result.Files[ii];
                ByteString content = await client.ReadResultFileAsync(file, ct).ConfigureAwait(false);
                Console.WriteLine($"--- {file.FileName} ({file.MimeType}) ---");
                Console.WriteLine(Encoding.UTF8.GetString(content.ToArray()).TrimEnd());
            }
        }

        /// <summary>
        /// Prints the measured value of a function whenever it changes.
        /// </summary>
        private static async Task WatchAsync(LadsFunctionalUnit chamber, string name, CancellationToken ct)
        {
            try
            {
                LadsFunction function = await chamber.GetFunctionAsync(name, ct).ConfigureAwait(false);
                await foreach (LadsValueChange change in function.ObserveAsync(cancellationToken: ct)
                    .ConfigureAwait(false))
                {
                    if (change.Name is LadsFunctionMembers.CurrentValue or LadsFunctionMembers.SensorValue &&
                        change.Value.WrappedValue.TryGetValue(out double value))
                    {
                        Console.WriteLine($"  {name,-12} {value.ToString("0.##", CultureInfo.InvariantCulture)}");
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Program finished.
            }
        }

        private static string Describe(LadsFunctionSnapshot function)
        {
            return function.Kind switch
            {
                LadsFunctionKind.Cover => function.State ?? string.Empty,
                _ when LadsFunctionKinds.IsSensor(function.Kind) =>
                    FormattableString.Invariant($"value {function.SensorValue}"),
                _ => FormattableString.Invariant($"current {function.CurrentValue}, target {function.TargetValue}")
            };
        }
    }
}
