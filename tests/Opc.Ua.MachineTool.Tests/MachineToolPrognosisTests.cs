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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.MachineTool.Client;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises all released prognosis variants as observations, never as machine-control commands.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class MachineToolPrognosisTests
    {
        /// <summary>
        /// Every concrete standard variant keeps its actual type, provider-predicted time and optional presence.
        /// </summary>
        [Test]
        public async Task AllStandardPrognosesKeepProviderTimesAndTypedWireFieldsAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 7, 8, 9, 10, 11, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            DateTimeUtc prediction = new DateTime(2026, 7, 9, 12, 13, 14, DateTimeKind.Utc);
            var entries = new MachineToolPrognosis[10];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = Example((MachineToolPrognosisKind)i, sample, prediction);
            }
            var provider = new InMemoryMachineToolProvider(new MachineToolSnapshot
            {
                Prognoses = new() { Entries = entries.ToArrayOf() }
            });
            await using MachineToolRuntime<MachineToolState> runtime =
                await MachineToolEquipmentLifetimeTests.Builder(fixture, provider, "Prognoses")
                    .WithPrognoses(new MachineToolPrognosisOptions
                    {
                        Features = MachineToolFeatures.Prognoses,
                        SupportedKinds = StandardKinds
                    }).BuildAsync().ConfigureAwait(false);
            Assert.That(runtime.Prognoses.State!.EventNotifier & EventNotifiers.SubscribeToEvents, Is.Not.Zero);
            await using var client = new ClientFixture(NUnitTelemetryContext.Create());
            await client.LoadClientConfigurationAsync(fixture.Root, "PrognosisClient").ConfigureAwait(false);
            using ISession session = await client.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using var facade = new MachineToolClient(session, NUnitTelemetryContext.Create());
            MachineToolPrognosisReadSnapshot received = await facade.ReadPrognosesAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(received.Entries.Count, Is.EqualTo(10));
            uint[] types = [9, 10, 1, 5, 8, 37, 45, 4, 18, 6];
            for (int i = 0; i < types.Length; i++)
            {
                MachineToolPrognosisReadEntry entry = received.Entries[i];
                Assert.That(entry.TypeDefinitionId,
                    Is.EqualTo(NodeId.Create(types[i], Namespaces.MachineTool, session.NamespaceUris)));
                Assert.That(entry.Kind, Is.EqualTo((MachineToolPrognosisKind)i));
                Assert.That(entry.PredictedTime.WrappedValue.TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.EqualTo(prediction));
                Assert.That(entry.PredictedTime.StatusCode, Is.EqualTo(sample.StatusCode));
                Assert.That(entry.PredictedTime.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
                Assert.That(entry.ToolNodeId.IsNull, Is.True);
                Assert.That(entry.PartNodeId.IsNull, Is.True);
                Assert.That(entry.JobNodeId.IsNull, Is.True);
            }
            Assert.That(received.Entries[0].Activity.WrappedValue.TryGetValue(out LocalizedText activity), Is.True);
            Assert.That(activity.Text, Is.EqualTo("Replace filter"));
            Assert.That(received.Entries[2].PartName.WrappedValue.TryGetValue(out string? partName), Is.True);
            Assert.That(partName, Is.EqualTo("Part"));
            Assert.That(received.Entries[5].SourceIdentifier.WrappedValue.TryGetValue(out string? job), Is.True);
            Assert.That(job, Is.EqualTo("ExternalJob"));
            Assert.That(received.Entries[7].ToolIdentifier.IsNull, Is.True);
            Assert.That(received.Entries[9].UtilityName.WrappedValue.TryGetValue(out string? utility), Is.True);
            Assert.That(utility, Is.EqualTo("Coolant"));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates independent standard examples, including entities not currently modeled in the address space.
        /// </summary>
        internal static MachineToolPrognosis Example(
            MachineToolPrognosisKind kind, MachineToolSampleInfo sample, DateTimeUtc predicted)
        {
            var value = new MachineToolPrognosis
            {
                Key = kind.ToString(),
                Kind = kind,
                Sample = sample,
                PredictedTime = MachineToolEquipmentTests.Sample(Variant.From(predicted), sample)
            };
            return kind switch
            {
                MachineToolPrognosisKind.Maintenance => value with { Activity = new LocalizedText("Replace filter") },
                MachineToolPrognosisKind.ManualActivity => value with
                {
                    Activity = new LocalizedText("Inspect surface")
                },
                MachineToolPrognosisKind.ProcessChangeover => value with
                {
                    Activity = new LocalizedText("Replace fixture"),
                    Location = new LocalizedText("Work area")
                },
                MachineToolPrognosisKind.PartLoad or MachineToolPrognosisKind.PartUnload => value with
                {
                    Location = new LocalizedText("Work area"),
                    PartName = "Part"
                },
                MachineToolPrognosisKind.ProductionJobEnd => value with { SourceIdentifier = "ExternalJob" },
                MachineToolPrognosisKind.UtilityChange => value with { UtilityName = "Coolant" },
                _ => value with { Location = new LocalizedText("Magazine"), ToolName = "Replacement drill" }
            };
        }

        /// <summary>
        /// Gets every concrete subtype declared by the released MachineTool specification.
        /// </summary>
        internal static ArrayOf<MachineToolPrognosisKind> StandardKinds =>
        [
            MachineToolPrognosisKind.Maintenance, MachineToolPrognosisKind.ManualActivity,
            MachineToolPrognosisKind.PartLoad, MachineToolPrognosisKind.PartUnload,
            MachineToolPrognosisKind.ProcessChangeover, MachineToolPrognosisKind.ProductionJobEnd,
            MachineToolPrognosisKind.ToolChange, MachineToolPrognosisKind.ToolLoad,
            MachineToolPrognosisKind.ToolUnload, MachineToolPrognosisKind.UtilityChange
        ];
    }
}
