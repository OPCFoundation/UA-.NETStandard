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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.MachineTool.Server;
using V2 = Opc.Ua.ISA95.JobControl.V2;
using static Opc.Ua.MachineTool.Tests.MachineToolJobSourceTests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Verifies bounded admission and that every refusal preserves both accepted representations.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class MachineToolJobSourceValidationTests
    {
        /// <summary>
        /// Resource bounds are checked before a live engine or observation can be created.
        /// </summary>
        [TestCase("Orders")]
        [TestCase("Responses")]
        [TestCase("Revisions")]
        [TestCase("Subscribers")]
        [TestCase("Changes")]
        [TestCase("Payload")]
        [TestCase("Text")]
        [TestCase("Array")]
        [TestCase("Descriptors")]
        public void InvalidOptionsAreRejected(string invalid)
        {
            MachineToolJobSourceOptions options = invalid switch
            {
                "Orders" => Options() with { MaximumJobOrders = 0 },
                "Responses" => Options() with { MaximumJobResponses = 0 },
                "Revisions" => Options() with { MaximumPendingRevisions = 1 },
                "Subscribers" => Options() with { MaximumSubscribers = 0 },
                "Changes" => Options() with { MaximumPendingChanges = 0 },
                "Payload" => Options() with { MaximumPayloadBytes = 127 },
                "Text" => Options() with { MaximumTextLength = 0 },
                "Array" => Options() with { MaximumArrayLength = 0 },
                _ => Options() with { MaximumArrayLength = 1, Jobs = [Descriptor(), Descriptor("Job-2")] }
            };
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MachineToolJobSource(options));
        }

        /// <summary>
        /// Required program identities and quantity mappings are supplied by applications, never synthesized.
        /// </summary>
        [TestCase("Job")]
        [TestCase("Master")]
        [TestCase("Program")]
        [TestCase("Runs")]
        [TestCase("PartsWithoutSet")]
        [TestCase("SetWithoutParts")]
        [TestCase("SetWithoutName")]
        [TestCase("Overflow")]
        [TestCase("Null")]
        [TestCase("Duplicate")]
        public void InvalidDescriptorsAreRejected(string invalid)
        {
            MachineToolJobSourceDescriptor descriptor = invalid switch
            {
                "Job" => Descriptor() with { JobOrderId = " " },
                "Master" => Descriptor() with { WorkMasterId = string.Empty },
                "Program" => Descriptor() with { ProgramName = string.Empty },
                "Runs" => Descriptor() with { RunsPlanned = 0 },
                "PartsWithoutSet" => Descriptor() with { PartsPlannedPerRun = 1 },
                "SetWithoutParts" => Descriptor() with { PartSetKey = "Set", PartSetName = "Parts" },
                "SetWithoutName" => Descriptor() with { PartSetKey = "Set", PartsPlannedPerRun = 1 },
                "Overflow" => Descriptor() with
                {
                    PartSetKey = "Set",
                    PartSetName = "Parts",
                    PartsPlannedPerRun = uint.MaxValue,
                    RunsPlanned = 2
                },
                "Null" => null!,
                _ => Descriptor()
            };
            MachineToolJobSourceOptions options = invalid == "Duplicate"
                ? Options() with { Jobs = [descriptor, descriptor] }
                : Options() with { Jobs = [descriptor] };
            Assert.Throws<ServiceResultException>(() => _ = new MachineToolJobSource(options));
        }

        /// <summary>
        /// Identity, count, nesting, Variant and total encoded-size bounds apply before invoking the engine.
        /// </summary>
        [TestCase("MissingMaster")]
        [TestCase("WrongMaster")]
        [TestCase("UnknownJob")]
        [TestCase("MissingId")]
        [TestCase("Mask")]
        [TestCase("String")]
        [TestCase("Array")]
        [TestCase("Bytes")]
        [TestCase("Total")]
        [TestCase("Nesting")]
        [TestCase("Comments")]
        public async Task InvalidOrOversizedOrderNeverCommitsAsync(string invalid)
        {
            await using var source = new MachineToolJobSource(Options() with
            {
                MaximumPayloadBytes = 256,
                MaximumArrayLength = 8,
                MaximumTextLength = 128
            });
            V2.ISA95JobOrderDataType order = Order();
            ArrayOf<LocalizedText> comments = default;
            switch (invalid)
            {
                case "MissingMaster":
                    order.WorkMasterID = [];
                    break;
                case "WrongMaster":
                    order.WorkMasterID[0].ID = "Not supplied";
                    break;
                case "UnknownJob":
                    order.JobOrderID = "Not supplied";
                    break;
                case "MissingId":
                    order.JobOrderID = null;
                    break;
                case "Mask":
                    order.EncodingMask = 0;
                    break;
                case "String":
                    order.EncodingMask |= (uint)V2.ISA95JobOrderDataTypeFields.Description;
                    order.Description = [new LocalizedText(new string('x', 129))];
                    break;
                case "Array":
                    order.EncodingMask |= (uint)V2.ISA95JobOrderDataTypeFields.Description;
                    order.Description = new LocalizedText[9].ToArrayOf();
                    break;
                case "Bytes":
                    order.EncodingMask |= (uint)V2.ISA95JobOrderDataTypeFields.JobOrderParameters;
                    order.JobOrderParameters =
                    [
                        new V2.ISA95ParameterDataType { Value = Variant.From(new ByteString(new byte[257])) }
                    ];
                    break;
                case "Total":
                    order.EncodingMask |= (uint)V2.ISA95JobOrderDataTypeFields.Description;
                    order.Description =
                    [
                        new LocalizedText(new string('x', 120)), new LocalizedText(new string('y', 120))
                    ];
                    break;
                case "Nesting":
                    order.EncodingMask |= (uint)V2.ISA95JobOrderDataTypeFields.JobOrderParameters;
                    var parameter = new V2.ISA95ParameterDataType
                    {
                        ID = "Recursive",
                        EncodingMask = (uint)V2.ISA95ParameterDataTypeFields.Subparameters
                    };
                    parameter.Subparameters = [parameter];
                    order.JobOrderParameters = [parameter];
                    break;
                case "Comments":
                    comments = [new LocalizedText(new string('x', 129))];
                    break;
            }
            Isa95JobOrderReceiptV2 receipt = await source.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store, order, comments).ConfigureAwait(false);
            Assert.That(receipt.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
            Assert.That(ServiceResult.IsGood(receipt.Result), Is.False);
            MachineToolJobSourceCapture capture = await source.CaptureAsync().ConfigureAwait(false);
            Assert.That(capture.Snapshot.Revision, Is.Zero);
            Assert.That(capture.Snapshot.Production.Jobs, Is.Empty);
            Assert.That(capture.JobOrders, Is.Empty);
        }

        /// <summary>
        /// The existing protocol engine remains authoritative about valid order operations.
        /// </summary>
        [TestCase(Isa95JobOrderOperationV2.Store, false)]
        [TestCase(Isa95JobOrderOperationV2.Pause, false)]
        [TestCase(Isa95JobOrderOperationV2.Resume, false)]
        [TestCase(Isa95JobOrderOperationV2.Stop, false)]
        [TestCase(Isa95JobOrderOperationV2.Clear, false)]
        [TestCase(Isa95JobOrderOperationV2.RevokeStart, false)]
        [TestCase(Isa95JobOrderOperationV2.Update, true)]
        [TestCase(Isa95JobOrderOperationV2.Cancel, true)]
        [TestCase(Isa95JobOrderOperationV2.Start, true)]
        [TestCase((Isa95JobOrderOperationV2)999, false)]
        public async Task EngineRefusalDoesNotPublishAStagedProjectionAsync(
            Isa95JobOrderOperationV2 operation, bool execute)
        {
            await using var source = new MachineToolJobSource(Options());
            if (execute)
            {
                await StartExecutingAsync(source).ConfigureAwait(false);
            }
            else
            {
                Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order())
                    .ConfigureAwait(false));
            }
            MachineToolJobSourceCapture before = await source.CaptureAsync().ConfigureAwait(false);
            Isa95JobOrderReceiptV2 receipt = await source.ReceiveJobOrderAsync(operation, Order())
                .ConfigureAwait(false);
            Assert.That(receipt.ReturnStatus, Is.Not.EqualTo(Isa95JobReturnStatus.Success));
            MachineToolJobSourceCapture after = await source.CaptureAsync().ConfigureAwait(false);
            Assert.That(after.Snapshot, Is.SameAs(before.Snapshot));
            Assert.That(after.JobOrders[0].IsEqual(before.JobOrders[0]), Is.True);
        }

        /// <summary>
        /// Each canonical nonterminal state can be aborted without falsely completing any planned run.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task AbortPreservesZeroCompletedRunsFromEveryNonterminalStateAsync(int stage)
        {
            await using var source = new MachineToolJobSource(Options());
            Success(await source.ReceiveJobOrderAsync(stage == 0
                ? Isa95JobOrderOperationV2.Store : Isa95JobOrderOperationV2.StoreAndStart, Order())
                .ConfigureAwait(false));
            if (stage >= 2)
            {
                Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.BeginExecution)
                    .ConfigureAwait(false));
            }
            if (stage == 3)
            {
                Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Pause, Order())
                    .ConfigureAwait(false));
            }
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Abort, Order()).ConfigureAwait(false));
            MachineToolJobSourceCapture captured = await source.CaptureAsync().ConfigureAwait(false);
            Assert.That(captured.JobOrders[0].State[0].StateNumber, Is.EqualTo(6));
            Assert.That(captured.Snapshot.Production.Jobs[0].Phase, Is.EqualTo(MachineToolProductionPhase.Aborted));
            Assert.That(captured.Snapshot.Production.Jobs[0].RunsCompleted, Is.Zero);
        }

        /// <summary>
        /// Intermediate run completions do not require fabricated part data when no part monitoring was requested.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitCompletionDistinguishesEarlyTerminationFromTheLastPlannedRunAsync(bool allRuns)
        {
            await using var source = new MachineToolJobSource(Options(Descriptor() with { RunsPlanned = 2 }));
            await StartExecutingAsync(source).ConfigureAwait(false);
            if (allRuns)
            {
                await source.CompleteRunAsync("Job-1").ConfigureAwait(false);
            }
            Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.Complete).ConfigureAwait(false));
            MachineToolProductionJob job = (await source.ReadAsync().ConfigureAwait(false)).Production.Jobs[0];
            Assert.That(job.Phase, Is.EqualTo(allRuns
                ? MachineToolProductionPhase.Ended : MachineToolProductionPhase.Aborted));
            Assert.That(job.RunsCompleted, Is.EqualTo(allRuns ? 2 : 0));
            Assert.That(job.PartSets, Is.Empty);
        }

        /// <summary>
        /// Capacity is the engine's real order/response capacity, and a second job cannot start on occupied equipment.
        /// </summary>
        [Test]
        public async Task EngineCapacityAndExplicitExecutionAdmissionPreserveStateAsync()
        {
            await using var source = new MachineToolJobSource(Options() with
            {
                Jobs = [Descriptor(), Descriptor("Job-2"), Descriptor("Job-3")],
                MaximumJobOrders = 2,
                MaximumJobResponses = 1
            });
            await StartExecutingAsync(source).ConfigureAwait(false);
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.StoreAndStart, Order("Job-2"))
                .ConfigureAwait(false));
            Assert.That((await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Store, Order("Job-3"))
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
            Assert.That((await source.TransitionAsync("Job-2", Isa95JobExecutionTransition.BeginExecution)
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
            Assert.That((await source.TransitionAsync("Unknown", Isa95JobExecutionTransition.BeginExecution)
                .ConfigureAwait(false)).ReturnStatus, Is.Not.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That((await source.TransitionAsync("Job-1", (Isa95JobExecutionTransition)999)
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
            MachineToolJobSourceCapture captured = await source.CaptureAsync().ConfigureAwait(false);
            var response = new V2.ISA95JobResponseDataType
            {
                JobResponseID = "R1",
                JobOrderID = "Job-1",
                JobState = captured.JobOrders[0].State
            };
            Assert.That((await source.ReceiveJobResponseAsync(response).ConfigureAwait(false)).ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.Success));
            ulong revision = (await source.ReadAsync().ConfigureAwait(false)).Revision;
            response.JobResponseID = "R2";
            Assert.That((await source.ReceiveJobResponseAsync(response).ConfigureAwait(false)).ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
            Assert.That((await source.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(revision));
        }

        /// <summary>
        /// Invalid current-run counts cannot revise cumulative history or finish an unobserved run.
        /// </summary>
        [Test]
        public async Task InvalidPartObservationsAndUnaccountedCompletionPreserveRevisionAsync()
        {
            await using var source = new MachineToolJobSource(Options(Descriptor() with
            {
                PartSetKey = "Set",
                PartSetName = "Parts",
                PartsPlannedPerRun = 3
            }));
            await StartExecutingAsync(source).ConfigureAwait(false);
            await source.RecordPartsAsync("Job-1", new MachineToolJobSourcePartCounts(2, 1, Acquired))
                .ConfigureAwait(false);
            MachineToolSnapshot before = await source.ReadAsync().ConfigureAwait(false);
            foreach (MachineToolJobSourcePartCounts invalid in new[]
            {
                new MachineToolJobSourcePartCounts(1, 1, Acquired),
                new MachineToolJobSourcePartCounts(4, 1, Acquired),
                new MachineToolJobSourcePartCounts(2, 3, Acquired),
                new MachineToolJobSourcePartCounts(2, 0, Acquired),
                new MachineToolJobSourcePartCounts(2, 1, new MachineToolSampleInfo(default, StatusCodes.Good))
            })
            {
                Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await source.RecordPartsAsync("Job-1", invalid).ConfigureAwait(false));
            }
            Assert.That((await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.Complete)
                .ConfigureAwait(false)).ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.UnableToAccept));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await source.CompleteRunAsync("Job-1").ConfigureAwait(false));
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await source.RecordPartsAsync("Unknown", new MachineToolJobSourcePartCounts()).ConfigureAwait(false));
            Assert.That(await source.ReadAsync().ConfigureAwait(false), Is.SameAs(before));
        }

        /// <summary>
        /// A monitoring acquisition retains supplied values and metadata instead of deriving measurements from jobs.
        /// </summary>
        [Test]
        public async Task RawMonitoringAndLegacyStatisticsArePreservedWithoutInventedDataAsync()
        {
            await using var source = new MachineToolJobSource(Options());
            var monitoring = new MachineToolSnapshot
            {
                Operation = new MachineToolOperation { Sample = Acquired, PartsProducedInLifetime = 42 },
                Channels = [new MachineToolChannel { Name = "NC", CombinedChannels = ["Raw"], Sample = Acquired }],
                Software = [new MachineToolSoftware("Controller", "2", new LocalizedText("Vendor"))],
                Spindles = [new MachineToolSpindle("S", false, Acquired)],
                Lasers = [new MachineToolLaser("L", false, default, Acquired)],
                EdmGenerators = [new MachineToolEdmGenerator("E", false, default, Acquired)],
                Stacklights =
                [
                    new MachineToolStacklight("Lamp", [new MachineToolLight(false, default, default, Acquired)])
                ],
                Production = new MachineToolProductionObservation
                {
                    Statistics = new MachineToolProductionStatistics(42, Acquired)
                }
            };
            await source.SetMonitoringAsync(monitoring).ConfigureAwait(false);
            MachineToolSnapshot current = await source.ReadAsync().ConfigureAwait(false);
            Assert.That(current.Operation, Is.SameAs(monitoring.Operation));
            Assert.That(current.Channels, Is.EqualTo(monitoring.Channels));
            Assert.That(current.Spindles, Is.EqualTo(monitoring.Spindles));
            Assert.That(current.Lasers, Is.EqualTo(monitoring.Lasers));
            Assert.That(current.EdmGenerators, Is.EqualTo(monitoring.EdmGenerators));
            Assert.That(current.Stacklights, Is.EqualTo(monitoring.Stacklights));
            Assert.That(current.Production.Statistics, Is.SameAs(monitoring.Production.Statistics));
            Assert.ThrowsAsync<ServiceResultException>(async () => await source.SetMonitoringAsync(monitoring with
            {
                Operation = monitoring.Operation with { PartsProducedInLifetime = 41 },
                Production = monitoring.Production with
                {
                    Statistics = new MachineToolProductionStatistics(41, Acquired)
                }
            }).ConfigureAwait(false));
            Assert.That(await source.ReadAsync().ConfigureAwait(false), Is.SameAs(current));
        }
    }
}
