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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using IndustrialAutomationSample;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.IA.Client;

namespace Opc.Ua.IA.Tests
{
    /// <summary>
    /// Uses the public sample as an encrypted, companion-owned server for generated client operations.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class IaClientServerTests
    {
        /// <summary>
        /// Exercises typed reads/subscriptions, authorized grouped reset, category interpretation and read-only writes.
        /// </summary>
        [Test]
        [Category("Integration")]
        public async Task PublicClientExercisesCompanionOwnedSample()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            CancellationToken ct = timeout.Token;
            var listener = new TcpListener(IPAddress.Loopback, 0);
            int port;
            try
            {
                listener.Start();
                port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
            string endpoint = $"opc.tcp://localhost:{port}/IA";
            string pki = Path.GetFullPath(Path.Combine("artifacts", "ia-tests", Guid.NewGuid().ToString("N")));
            IHost server = IaSampleHost.CreateServer(endpoint, pki, demoTrust: true);
            await using var serverLifetime = ((IAsyncDisposable)server).ConfigureAwait(false);
            IHost clientHost = IaSampleHost.CreateClient(endpoint, pki, demoTrust: true);
            await using var clientLifetime = ((IAsyncDisposable)clientHost).ConfigureAwait(false);
            try
            {
                await server.StartAsync(ct).ConfigureAwait(false);
                IaSampleModel model = server.Services.GetRequiredService<IaSampleModel>();
                await model.Ready.WaitAsync(ct).ConfigureAwait(false);
                await clientHost.StartAsync(ct).ConfigureAwait(false);
                IndustrialAutomationClient client = await clientHost.Services
                    .GetRequiredService<IndustrialAutomationClientFactory>().CreateAsync(ct).ConfigureAwait(false);
                Assert.That(client.Session.ConfiguredEndpoint.Description.SecurityMode,
                    Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                var entries = new List<IaEntry>();
                await foreach (IaEntry entry in client.DiscoverAsync(model.Device, cancellationToken: ct)
                    .ConfigureAwait(false))
                {
                    entries.Add(entry);
                }
                Assert.That(entries.Select(entry => entry.Concept), Is.EquivalentTo(new[]
                {
                    IaConcept.Stacklight, IaConcept.AggregateStatistics,
                    IaConcept.RollingStatistics, IaConcept.CalibrationTarget
                }));
                NodeId stackId = entries.Single(entry => entry.Concept == IaConcept.Stacklight).NodeId;
                NodeId aggregateId = entries.Single(entry => entry.Concept == IaConcept.AggregateStatistics).NodeId;
                NodeId rollingId = entries.Single(entry => entry.Concept == IaConcept.RollingStatistics).NodeId;
                NodeId targetId = entries.Single(entry => entry.Concept == IaConcept.CalibrationTarget).NodeId;
                BasicStacklightTypeClient stack = client.Stacklight(stackId);
                ClientRead<StacklightOperationMode> initial = await stack.StacklightModeVariable().ReadAsync(ct)
                    .ConfigureAwait(false);
                Assert.That(initial.HasValue, Is.True);
                Assert.That(initial.Value, Is.EqualTo(StacklightOperationMode.Segmented));
                Assert.That(initial.DataValue.SourceTimestamp.IsNull, Is.False);
                IAsyncEnumerator<ClientRead<StacklightOperationMode>> changes =
                    stack.StacklightModeVariable().ObserveAsync(cancellationToken: ct).GetAsyncEnumerator(ct);
                await using var changesLifetime = changes.ConfigureAwait(false);
                Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                await model.AdvanceAsync(StacklightOperationMode.Levelmeter, 4, ct).ConfigureAwait(false);
                while (changes.Current.Value != StacklightOperationMode.Levelmeter)
                {
                    Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                }
                Assert.That(changes.Current.DataValue.SourceTimestamp.IsNull, Is.False);
                Assert.That(changes.Current.DataValue.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                await model.AdvanceAsync(StacklightOperationMode.Running_Light, 5, ct).ConfigureAwait(false);
                await model.AdvanceAsync(StacklightOperationMode.Segmented, 6, ct).ConfigureAwait(false);
                await model.AdvanceAsync(StacklightOperationMode.Levelmeter, 7, ct).ConfigureAwait(false);
                AssertValue(await client.Session.ReadValueAsync(model.AggregateQuantity, ct).ConfigureAwait(false), 22);
                AssertValue(await client.Session.ReadValueAsync(model.RollingQuantity, ct).ConfigureAwait(false), 18);
                ClientRead<uint> window = await client.RollingStatistics(rollingId)
                    .WindowNumberOfValuesVariable().ReadAsync(ct).ConfigureAwait(false);
                Assert.That(window.Value, Is.EqualTo(3u));

                IAggregateStatisticsTypeClient aggregate = client.AggregateStatistics(aggregateId);
                ClientRead<DateTimeUtc> before = await aggregate.StartTimeVariable().ReadAsync(ct)
                    .ConfigureAwait(false);
                ServiceResultException? denied = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await aggregate.ResetStatisticsAsync(ct).ConfigureAwait(false));
                Assert.That(denied!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadUserAccessDenied));
                AssertValue(await client.Session.ReadValueAsync(model.AggregateQuantity, ct).ConfigureAwait(false), 22);
                model.AllowReset = true;
                await aggregate.ResetStatisticsAsync(ct).ConfigureAwait(false);
                AssertValue(await client.Session.ReadValueAsync(model.AggregateQuantity, ct).ConfigureAwait(false), 0);
                ClientRead<DateTimeUtc> after = await aggregate.StartTimeVariable().ReadAsync(ct).ConfigureAwait(false);
                Assert.That(after.Value, Is.GreaterThanOrEqualTo(before.Value));
                Assert.That(await client.ReadCalibrationCategoryAsync(targetId, ct).ConfigureAwait(false),
                    Is.EqualTo(IaTargetCategory.ReusableDevice));
                ArrayOf<IaCalibrationFeature> features = await client.ReadCalibrationFeaturesAsync(
                    targetId, cancellationToken: ct).ConfigureAwait(false);
                Assert.That(features.Count, Is.EqualTo(3));
                int numericFeatures = 0;
                int customFeatures = 0;
                for (int i = 0; i < features.Count; i++)
                {
                    if (features[i].Value.IsNull)
                    {
                        customFeatures++;
                        Assert.That(features[i].DisplayName,
                            Is.EqualTo(new LocalizedText("Target geometry definition")));
                        continue;
                    }
                    numericFeatures++;
                    Assert.That(features[i].Value.SourceTimestamp.IsNull, Is.False);
                    Assert.That(features[i].EngineeringUnits.WrappedValue.TryGetStructure<EUInformation>(
                        out EUInformation? units),
                        Is.True);
                    Assert.That(units!.DisplayName, Is.EqualTo(new LocalizedText("mm")));
                }
                Assert.That(numericFeatures, Is.EqualTo(2));
                Assert.That(customFeatures, Is.EqualTo(1));
                ServiceResultException? write = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await stack.StacklightModeVariable().WriteAsync(StacklightOperationMode.Segmented, ct)
                        .ConfigureAwait(false));
                Assert.That(write!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNotWritable));
                Assert.That(client.Session.Connected, Is.True);
                ServiceResultException? limit = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                {
                    await foreach (IaEntry _ in client.DiscoverAsync(model.Device, maximumNodes: 1,
                        cancellationToken: ct).ConfigureAwait(false))
                    {
                    }
                });
                Assert.That(limit!.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadEncodingLimitsExceeded));
            }
            finally
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await clientHost.StopAsync(shutdown.Token).ConfigureAwait(false);
                await server.StopAsync(shutdown.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks the native UInt32 rather than converting or boxing the sample quantity.
        /// </summary>
        private static void AssertValue(in DataValue value, uint expected)
        {
            Assert.That(value.WrappedValue.TryGetValue(out uint actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
