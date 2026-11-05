/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
 * of the Software, and to permit persons to whom the Software is furnished to
 * do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.IA;
using Opc.Ua.IA.Client;

namespace IndustrialAutomationSample
{
    /// <summary>
    /// Runs a bounded encrypted IA information simulation through its public SDK APIs.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Starts server, client, or a combined public-API check and reports failures through the exit code.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            try
            {
                IConfiguration options = new ConfigurationBuilder().AddCommandLine(args).Build();
                string mode = options["mode"] ?? "selfcheck";
                string endpoint = options["endpoint"] ?? "opc.tcp://localhost:62560/IA";
                string pkiRoot = Path.GetFullPath(options["pki-root"] ?? Path.Combine("artifacts", "ia-sample", "pki"));
                bool demoTrust = BooleanOption(options, "demo-trust");
                bool permitReset = BooleanOption(options, "permit-reset");
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                Console.WriteLine("SIMULATION ONLY: no physical light outputs or calibration procedure.");
                if (mode == "client")
                {
                    await ReadClientAsync(endpoint, pkiRoot, demoTrust, permitReset, deadline.Token)
                        .ConfigureAwait(false);
                    return 0;
                }
                if (mode is not "server" and not "selfcheck")
                {
                    throw new ArgumentException("--mode must be server, client or selfcheck.");
                }
                IHost server = IaSampleHost.CreateServer(endpoint, pkiRoot, demoTrust, permitReset);
                await using var serverLifetime = ((IAsyncDisposable)server).ConfigureAwait(false);
                try
                {
                    await server.StartAsync(deadline.Token).ConfigureAwait(false);
                    IaSampleModel model = server.Services.GetRequiredService<IaSampleModel>();
                    await model.Ready.WaitAsync(deadline.Token).ConfigureAwait(false);
                    ArrayOf<StacklightOperationMode> modes =
                        [StacklightOperationMode.Segmented, StacklightOperationMode.Levelmeter,
                            StacklightOperationMode.Running_Light];
                    for (int i = 0; i < modes.Count; i++)
                    {
                        await model.AdvanceAsync(modes[i], (uint)(i + 1), deadline.Token).ConfigureAwait(false);
                        Console.WriteLine($"MODE {modes[i]}");
                        if (mode == "selfcheck")
                        {
                            await ReadClientAsync(endpoint, pkiRoot, demoTrust, permitReset, deadline.Token)
                                .ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), deadline.Token).ConfigureAwait(false);
                        }
                    }
                    Console.WriteLine("IA sample completed.");
                }
                finally
                {
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await server.StopAsync(shutdown.Token).ConfigureAwait(false);
                }
                return 0;
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
                return 1;
            }
        }

        /// <summary>
        /// Reads all three concepts over one container-owned managed session; reset is explicitly opt-in.
        /// </summary>
        private static async Task ReadClientAsync(
            string endpoint, string pkiRoot, bool demoTrust, bool reset, CancellationToken ct)
        {
            IHost host = IaSampleHost.CreateClient(endpoint, pkiRoot, demoTrust);
            await using var hostLifetime = ((IAsyncDisposable)host).ConfigureAwait(false);
            try
            {
                await host.StartAsync(ct).ConfigureAwait(false);
                IndustrialAutomationClient client = await host.Services
                    .GetRequiredService<IndustrialAutomationClientFactory>().CreateAsync(ct).ConfigureAwait(false);
                var concepts = new HashSet<IaConcept>();
                await foreach (IaEntry entry in client.DiscoverAsync(Opc.Ua.ObjectIds.ObjectsFolder,
                    cancellationToken: ct).ConfigureAwait(false))
                {
                    concepts.Add(entry.Concept);
                    Console.WriteLine($"{entry.Concept}: {entry.BrowseName}");
                    switch (entry.Concept)
                    {
                        case IaConcept.Stacklight:
                        {
                            BasicStacklightTypeClient stack = client.Stacklight(entry.NodeId);
                            ClientRead<StacklightOperationMode> light = await stack.StacklightModeVariable()
                                .ReadAsync(ct).ConfigureAwait(false);
                            if (!light.HasValue)
                            {
                                throw new ServiceResultException(StatusCodes.BadNoData, "The stacklight has no mode.");
                            }
                            IAsyncEnumerator<ClientRead<StacklightOperationMode>> changes =
                                stack.StacklightModeVariable().ObserveAsync(cancellationToken: ct)
                                    .GetAsyncEnumerator(ct);
                            await using var changesLifetime = changes.ConfigureAwait(false);
                            if (!await changes.MoveNextAsync().ConfigureAwait(false) || !changes.Current.HasValue ||
                                changes.Current.Value != light.Value)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadUnexpectedError,
                                    "The initial mode subscription disagrees with Read.");
                            }
                            Console.WriteLine($"  mode={light.Value}, status={light.DataValue.StatusCode}");
                            break;
                        }
                        case IaConcept.AggregateStatistics:
                            IAggregateStatisticsTypeClient statistics = client.AggregateStatistics(entry.NodeId);
                            ClientRead<DateTimeUtc> start = await statistics.StartTimeVariable().ReadAsync(ct)
                                .ConfigureAwait(false);
                            if (!start.HasValue || start.Value.IsNull)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadNoData, "The aggregate collection has no StartTime.");
                            }
                            Console.WriteLine($"  collection started={start.Value}");
                            if (reset)
                            {
                                await statistics.ResetStatisticsAsync(ct).ConfigureAwait(false);
                            }
                            break;
                        case IaConcept.RollingStatistics:
                            ClientRead<uint> window = await client.RollingStatistics(entry.NodeId)
                                .WindowNumberOfValuesVariable().ReadAsync(ct).ConfigureAwait(false);
                            if (!window.HasValue || window.Value == 0)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadNoData, "The sample rolling collection has no count window.");
                            }
                            Console.WriteLine($"  rolling window={window.Value} acquisitions");
                            break;
                        case IaConcept.CalibrationTarget:
                            IaTargetCategory category = await client.ReadCalibrationCategoryAsync(entry.NodeId, ct)
                                .ConfigureAwait(false);
                            ArrayOf<Opc.Ua.IA.Client.IaCalibrationFeature> features =
                                await client.ReadCalibrationFeaturesAsync(entry.NodeId, cancellationToken: ct)
                                    .ConfigureAwait(false);
                            Console.WriteLine($"  category={category}, features={features.Count}");
                            if (category != IaTargetCategory.ReusableDevice || features.Count != 3)
                            {
                                throw new ServiceResultException(
                                    StatusCodes.BadUnexpectedError, "The sample target information is incomplete.");
                            }
                            break;
                    }
                }
                if (concepts.Count != 4)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNotFound, "The sample requires all four IA building-block instances.");
                }
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await host.StopAsync(shutdown.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Requires an explicit Boolean value instead of silently enabling demo policy.
        /// </summary>
        private static bool BooleanOption(IConfiguration configuration, string name)
        {
            string? value = configuration[name];
            return value == null ? false : bool.TryParse(value, out bool result)
                ? result : throw new ArgumentException($"--{name} must be true or false.");
        }
    }
}
