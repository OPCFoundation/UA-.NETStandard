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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises native job and part observations on the accepted retained-production state and lifetime machinery.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [Category("Integration")]
    [NonParallelizable]
    public sealed class GmsSystemProductionTests
    {
        /// <summary>
        /// Replaying an accepted acquisition after a later rejection cannot project the rejected native metadata.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task RejectedAcquisitionPreservesJobAndPartMetadataOnReplayAsync(bool rejectWarning)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot accepted = Example() with { Sensors = [GmsEquipmentTests.Sensor("Probe")] };
            var provider = new ReplayProvider(accepted);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "AtomicAcquisition", provider)
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Production | GmsFeatures.Sensors })
                .WithMachineToolFeatures(MachineToolFeatures.ProductionStatistics)
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            var job = (GMSJobState)handle.State.Production!.ProductionPlan!.FindChild(context.Context,
                new QualifiedName("JobSlot", context.InstanceNamespaceIndex))!;
            var set = (ProductionPartSetState)job.PartSets!.FindChild(context.Context,
                new QualifiedName("Set", context.InstanceNamespaceIndex))!;
            var part = (GMSPartState)set.PartsPerRun!.FindChild(context.Context,
                new QualifiedName("Part", context.InstanceNamespaceIndex))!;
            NodeId jobId = job.NodeId;
            NodeId partId = part.NodeId;
            GmsSystemSnapshot rejected = accepted with
            {
                Jobs =
                [
                    accepted.Jobs[0] with
                    {
                        RemainingTime = GmsEquipmentTests.Sample(Variant.From(100.0)),
                        BatchIdentifier = GmsEquipmentTests.Sample(Variant.From("Rejected batch"))
                    }
                ],
                Parts =
                [
                    accepted.Parts[0] with
                    {
                        PartDescription = GmsEquipmentTests.Sample(Variant.From("Rejected part"))
                    }
                ]
            };
            provider.Snapshot = rejectWarning
                ? rejected with { SensorWarnings = [] }
                : rejected with { Sensors = [accepted.Sensors[0] with { Qualified = null }] };
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await handle.RefreshAsync().ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(error.Message, Does.Contain(rejectWarning ? "SensorWarnings" : "qualification countdown"));
            Assert.Multiple(() =>
            {
                Assert.That(handle.Observation, Is.SameAs(accepted));
                Assert.That(job.RemainingTime!.Value, Is.EqualTo(6000.0));
                Assert.That(job.BatchIdentifier!.Value, Is.EqualTo("B-2026"));
                Assert.That(part.PartDescription!.Value, Is.EqualTo("Inspection workpiece"));
            });
            provider.Snapshot = accepted;
            await handle.RefreshAsync().ConfigureAwait(false);
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            ReadResponse read = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
            [
                new ReadValueId { NodeId = job.RemainingTime!.NodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = job.BatchIdentifier!.NodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = part.PartDescription!.NodeId, AttributeId = Attributes.Value }
            ], default).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(handle.Observation, Is.SameAs(accepted));
                Assert.That(job.NodeId, Is.EqualTo(jobId));
                Assert.That(part.NodeId, Is.EqualTo(partId));
                Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[1].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[2].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(read.Results[0].WrappedValue, Is.EqualTo(Variant.From(6000.0)));
                Assert.That(read.Results[1].WrappedValue, Is.EqualTo(Variant.From("B-2026")));
                Assert.That(read.Results[2].WrappedValue, Is.EqualTo(Variant.From("Inspection workpiece")));
            });
        }

        /// <summary>
        /// GMS metadata and sparse numeric catalogs survive shared production updates without introducing commands.
        /// </summary>
        [Test]
        public async Task RetainedProductionUsesNativeJobsPartsAndCatalogMetadataAsync()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext context = GmsSystemTests.Context(fixture);
            GmsSystemSnapshot initial = Example();
            var provider = new InMemoryGmsProvider(initial);
            await using GmsHandle handle = await GmsSystemTests.Builder(context, "ProductionGms", provider)
                .WithOptions(new GmsSystemOptions { Features = GmsFeatures.Production })
                .WithMachineToolFeatures(MachineToolFeatures.ProductionStatistics)
                .WithResults(new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()))
                .BuildAsync().ConfigureAwait(false);
            var job = handle.State.Production!.ProductionPlan!.FindChild(context.Context,
                new QualifiedName("JobSlot", context.InstanceNamespaceIndex)) as GMSJobState;
            Assert.That(job, Is.Not.Null);
            Assert.That(job!.MeasurementReason!.Value, Is.EqualTo(MeasurementReasonEnum.AuditMeasurement));
            Assert.That(job.BatchIdentifier!.Value, Is.EqualTo("B-2026"));
            var set = (ProductionPartSetState)job.PartSets!.FindChild(context.Context,
                new QualifiedName("Set", context.InstanceNamespaceIndex))!;
            var part = set.PartsPerRun!.FindChild(context.Context,
                new QualifiedName("Part", context.InstanceNamespaceIndex)) as GMSPartState;
            Assert.That(part, Is.Not.Null);
            Assert.That(part!.PartDescription!.Value, Is.EqualTo("Inspection workpiece"));
            Assert.That(part.NestIdentifier!.CatalogName!.Value, Is.EqualTo("Fixture nests"));
            Assert.That(part.NestIdentifier.CatalogEntry!.Value, Is.EqualTo("Nest-42"));
            ISession session = await fixture.ConnectAsync().ConfigureAwait(false);
            DataValue nest = await session.ReadValueAsync(part.NestIdentifier.NodeId).ConfigureAwait(false);
            Assert.That(nest.WrappedValue.TryGetValue(out ushort number), Is.True);
            Assert.That(number, Is.EqualTo((ushort)42));
            NodeId jobId = job.NodeId;
            NodeId partId = part.NodeId;
            await provider.SetSnapshotAsync(initial with
            {
                Jobs = [initial.Jobs[0] with { RemainingTime = GmsEquipmentTests.Sample(Variant.From(5000.0)) }],
                Parts =
                [
                    initial.Parts[0] with { PartDescription = GmsEquipmentTests.Sample(Variant.From("Inspected")) }
                ]
            }).ConfigureAwait(false);
            await handle.RefreshAsync().ConfigureAwait(false);
            Assert.That(job.NodeId, Is.EqualTo(jobId));
            Assert.That(part.NodeId, Is.EqualTo(partId));
            Assert.That(job.RemainingTime!.Value, Is.EqualTo(5000));
            Assert.That(part.PartDescription.Value, Is.EqualTo("Inspected"));
            Assert.That(handle.State.Production.Statistics!.PartsProducedInLifetime!.Value, Is.EqualTo(10u));
            Assert.That(job.ProductionPrograms, Is.Not.Null);
        }

        /// <summary>
        /// Supplies a native monitoring-only job and part graph with an explicit inherited statistics observation.
        /// </summary>
        internal static GmsSystemSnapshot Example()
        {
            var metadata = new MachineToolSampleInfo(GmsEquipmentTests.AcquisitionTime, StatusCodes.Good);
            var program = new MachineToolProgram
            {
                Key = "Program",
                Name = "Inspect",
                JobIdentifier = "J-1",
                Sample = metadata
            };
            return new GmsSystemSnapshot
            {
                MachineTool = new MachineToolSnapshot
                {
                    ActiveProgram = program,
                    Production = new MachineToolProductionObservation
                    {
                        Jobs =
                        [
                            new MachineToolProductionJob
                            {
                                Key = "JobSlot", Identifier = "J-1", Sample = metadata, Programs = [program],
                                RunsPlanned = 1, RunsPlannedIsValid = true,
                                PartSets =
                                [
                                    new MachineToolProductionPartSet
                                    {
                                        Key = "Set", Name = "Workpieces", PartsPlannedPerRun = 1, Sample = metadata,
                                        Parts =
                                        [
                                            new MachineToolProductionPart
                                            {
                                                Key = "Part", Name = "Workpiece", Identifier = "P-1", Sample = metadata
                                            }
                                        ]
                                    }
                                ]
                            }
                        ],
                        Statistics = new MachineToolProductionStatistics(10, metadata)
                    }
                },
                Jobs =
                [
                    new GmsJobObservation
                    {
                        Key = "JobSlot",
                        BatchIdentifier = GmsEquipmentTests.Sample(Variant.From("B-2026")),
                        Duration = GmsEquipmentTests.Sample(Variant.From(1000.0)),
                        RemainingTime = GmsEquipmentTests.Sample(Variant.From(6000.0)),
                        MeasurementReason =
                            GmsEquipmentTests.Sample(Variant.From(MeasurementReasonEnum.AuditMeasurement))
                    }
                ],
                Parts =
                [
                    new GmsPartObservation
                    {
                        JobKey = "JobSlot", PartSetKey = "Set", PartKey = "Part",
                        PartDescription = GmsEquipmentTests.Sample(Variant.From("Inspection workpiece")),
                        NestIdentifier = new GmsCatalogObservation
                        {
                            Value = GmsEquipmentTests.Sample(Variant.From((ushort)42)),
                            CatalogName = "Fixture nests", CatalogEntry = "Nest-42",
                            EnumValues = [new EnumValueType { Value = 42, DisplayName = new LocalizedText("Nest 42") }]
                        }
                    }
                ]
            };
        }

        /// <summary>
        /// Replays immutable acquisitions without replacing their identities or revisions.
        /// </summary>
        private sealed class ReplayProvider(GmsSystemSnapshot snapshot) : IGmsProvider
        {
            /// <summary>
            /// Gets or sets the exact acquisition returned through the public provider seam.
            /// </summary>
            internal GmsSystemSnapshot Snapshot { get; set; } = snapshot;

            /// <inheritdoc/>
            public ValueTask<GmsSystemSnapshot> ReadAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new ValueTask<GmsSystemSnapshot>(Snapshot);
            }
        }
    }
}
