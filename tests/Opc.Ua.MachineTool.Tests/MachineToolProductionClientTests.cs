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
    /// Reads retained OPC 40501-1 section 8.4 state and inventory through the typed client and real services.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    [Category("Integration")]
    public sealed class MachineToolProductionClientTests
    {
        /// <summary>
        /// Invalid aggregate bounds are rejected by the public options before any server operation.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void ProductionReadBoundsAreFiniteAndPositive(int scenario)
        {
            MachineToolProductionReadOptions options = scenario switch
            {
                0 => new() { MaximumElements = 0 },
                1 => new() { MaximumElements = 65537 },
                2 => new() { MaximumReferences = 0 },
                3 => new() { MaximumReferences = 100001 },
                4 => new() { MaximumPayloadBytes = 0 },
                _ => new() { MaximumPayloadBytes = (1024 * 1024) + 1 }
            };

            Assert.That(options.Validate, Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        /// <summary>
        /// Optional absence is preserved, but access denial or a payload limit is never an empty Good result.
        /// </summary>
        [Test]
        public async Task MandatoryProductionPreservesOptionalAbsenceAndReadFailuresAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 8, 9, 10, 11, 12, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("NoPlan")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot
                {
                    ActiveProgram = new MachineToolProgram { Name = "Selected", Sample = sample }
                })).WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Production client");
                    identity.SerialNumber = "NoPlan";
                    identity.ProductInstanceUri = "urn:production-client:no-plan";
                }).BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "ProductionAbsence").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);
            MachineToolProductionReadSnapshot read = await client.ReadProductionAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(read.ProductionPlanId.IsNull, Is.True);
            Assert.That(read.NodeVersion.IsNull, Is.True);
            Assert.That(read.Jobs.Count, Is.Zero);
            Assert.That(read.PartsProducedInLifetime.IsNull, Is.True);
            Assert.That(read.ActiveProgram.Name.StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(read.ActiveProgram.Name.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
            ServiceResultException oversized = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadProductionAsync(runtime.NodeId,
                    new MachineToolProductionReadOptions { MaximumPayloadBytes = 1 }).ConfigureAwait(false))!;
            Assert.That(oversized.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            runtime.State.Production!.ActiveProgram!.Name!.UserAccessLevel = AccessLevels.None;
            ServiceResultException denied = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadProductionAsync(runtime.NodeId).ConfigureAwait(false))!;
            Assert.That(denied.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// The workflow exposes actual jobs, programs, part sets, parts and LastTransition without executing them.
        /// </summary>
        [Test]
        public async Task ProductionReadPreservesRetainedInventoryHistoryAndSampleMetadataAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var sample = new MachineToolSampleInfo(
                new DateTime(2026, 8, 9, 10, 11, 12, DateTimeKind.Utc), StatusCodes.UncertainLastUsableValue);
            var program = new MachineToolProgram
            {
                Name = "MAIN.NC",
                JobIdentifier = "Order1",
                Phase = MachineToolProductionPhase.Running,
                Sample = sample
            };
            var part = new MachineToolProductionPart
            {
                Key = "Part1",
                Name = "Housing",
                Identifier = "H1",
                Phase = MachineToolProductionPhase.Running,
                Sample = sample
            };
            var set = new MachineToolProductionPartSet
            {
                Key = "Lot",
                Name = "Housings",
                PartsPlannedPerRun = 1,
                Parts = [part],
                Sample = sample
            };
            var job = new MachineToolProductionJob
            {
                Identifier = "Order1",
                Programs = [program],
                PartSets = [set],
                Phase = MachineToolProductionPhase.Running,
                RunsPlanned = 1,
                RunsPlannedIsValid = true,
                Sample = sample
            };
            var initial = new MachineToolSnapshot
            {
                ActiveProgram = program,
                Production = new MachineToolProductionObservation { Jobs = [job] }
            };
            var provider = new InMemoryMachineToolProvider(initial);
            await using MachineToolRuntime<MachineToolState> runtime = await fixture.Context.AddMachineTool("ReadPlan")
                .WithProvider(provider).WithProduction(new MachineToolProductionOptions
                {
                    Features = MachineToolFeatures.Production | MachineToolFeatures.ProductionParts
                })
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Client tests");
                    identity.SerialNumber = "ReadPlan";
                    identity.ProductInstanceUri = "urn:production-client:plan";
                }).BuildAsync().ConfigureAwait(false);
            program = program with { Phase = MachineToolProductionPhase.Ended, TransitionSequence = 1 };
            part = part with
            {
                Phase = MachineToolProductionPhase.Ended,
                TransitionSequence = 1,
                Quality = PartQuality.Good
            };
            set = set with { PartsCompletedPerRun = 1, Parts = [part] };
            job = job with
            {
                Phase = MachineToolProductionPhase.Ended,
                TransitionSequence = 1,
                RunsCompleted = 1,
                PartsCompleted = 1,
                PartsGood = 1,
                Programs = [program],
                PartSets = [set]
            };
            await provider.SetSnapshotAsync(initial with
            {
                Revision = 1,
                ActiveProgram = program,
                Production = new MachineToolProductionObservation { Jobs = [job] }
            }).ConfigureAwait(false);
            await runtime.RefreshAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "ProductionRead").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            await using MachineToolClient client = session.CreateMachineToolClient(telemetry);

            MachineToolProductionReadSnapshot read = await client.ReadProductionAsync(runtime.NodeId)
                .ConfigureAwait(false);
            Assert.That(runtime.State.Production!.ProductionPlan!.ReferenceTypeId,
                Is.EqualTo(ReferenceTypeIds.HasComponent),
                "The actual retained plan must use the released aggregate reference, not just HasNotifier.");
            Assert.That(read.ProductionPlanId.IsNull, Is.False, "The generated optional plan must resolve.");

            Assert.That(read.Jobs.Count, Is.EqualTo(1));
            Assert.That(read.Jobs[0].Programs.Count, Is.EqualTo(1));
            Assert.That(read.Jobs[0].PartSets.Count, Is.EqualTo(1));
            Assert.That(read.Jobs[0].PartSets[0].Parts.Count, Is.EqualTo(1));
            Assert.That(read.ActiveProgram.Name.WrappedValue.TryGetValue(out string name), Is.True);
            Assert.That(name, Is.EqualTo("MAIN.NC"));
            Assert.That(read.Jobs[0].PartsGood.WrappedValue.TryGetValue(out uint good), Is.True);
            Assert.That(good, Is.EqualTo(1));
            Assert.That(read.Jobs[0].Identifier.SourceTimestamp, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(read.Jobs[0].Identifier.StatusCode, Is.EqualTo(sample.StatusCode));
            Assert.That(read.Jobs[0].State.LastTransitionTime.WrappedValue.TryGetValue(out DateTimeUtc transition),
                Is.True);
            Assert.That(transition, Is.EqualTo(sample.SourceTimestamp));
            Assert.That(read.PartsProducedInLifetime.IsNull, Is.True);
            ServiceResultException limit = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.ReadProductionAsync(runtime.NodeId,
                    new MachineToolProductionReadOptions { MaximumElements = 1 }).ConfigureAwait(false))!;
            Assert.That(limit.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }
}
