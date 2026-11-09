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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.MachineTool.Server;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises the public shared reference source without bypassing its existing ISA-95 provider facets.
    /// </summary>
    [TestFixture]
    [Category("MachineTool")]
    public sealed class MachineToolJobSourceTests
    {
        /// <summary>
        /// Permission to start is not execution, and engine audit comments and work-master identities survive updates.
        /// </summary>
        [Test]
        public async Task StartAuthorizationDoesNotExecuteOrReplaceAcquisitionMetadataAsync()
        {
            var monitoring = new MachineToolSnapshot
            {
                Operation = new MachineToolOperation { Sample = Acquired, Mode = MachineOperationMode.Automatic },
                Channels = [new MachineToolChannel { Name = "NC", FeedOverride = 73, Sample = Acquired }]
            };
            await using var source = new MachineToolJobSource(Options(), monitoring);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using IAsyncEnumerator<Isa95JobStatusNotificationV2> events =
                source.SubscribeAsync(timeout.Token).GetAsyncEnumerator();
            Task<bool> first = events.MoveNextAsync().AsTask();
            V2.ISA95JobOrderDataType order = Order();
            order.Priority = 3;
            Success(await source.ReceiveJobOrderAsync(
                Isa95JobOrderOperationV2.Store, order, [new LocalizedText("en", "approved plan")])
                .ConfigureAwait(false));
            Assert.That(await first.ConfigureAwait(false), Is.True);
            Assert.That(events.Current.Comment[0].Text, Is.EqualTo("approved plan"));
            order.Priority = 99;
            order.WorkMasterID[0].ID = "tampered";
            MachineToolJobSourceCapture stored = await source.CaptureAsync().ConfigureAwait(false);
            Assert.That(stored.JobOrders[0].JobOrder.Priority, Is.EqualTo(3));
            Assert.That(stored.JobOrders[0].JobOrder.WorkMasterID[0].ID, Is.EqualTo("WM-1"));
            Assert.That(stored.Snapshot.ActiveProgram.Name, Is.Empty);
            Assert.That(stored.Snapshot.Production.Jobs[0].Phase, Is.EqualTo(MachineToolProductionPhase.Initializing));
            Assert.That(StatusCode.IsGood(stored.Snapshot.Production.Jobs[0].Sample.StatusCode), Is.False);
            Assert.That(stored.Snapshot.Operation, Is.SameAs(monitoring.Operation));
            Assert.That(stored.Snapshot.Channels, Is.EqualTo(monitoring.Channels));

            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Start, Order(),
                [new LocalizedText("de", "freigegeben")]).ConfigureAwait(false));
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(events.Current.StateNumber, Is.EqualTo(2));
            Assert.That(events.Current.Comment[0].Text, Is.EqualTo("freigegeben"));
            V2.ISA95JobOrderDataType updated = Order();
            updated.Priority = 7;
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Update, updated).ConfigureAwait(false));
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(events.Current.Comment[0].Text, Is.EqualTo("freigegeben"));
            Assert.That(events.Current.JobOrder.Priority, Is.EqualTo(7));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.RevokeStart, Order())
                .ConfigureAwait(false));
            Assert.That(await events.MoveNextAsync().ConfigureAwait(false), Is.True);
            Assert.That(events.Current.StateNumber, Is.EqualTo(1));
            MachineToolSnapshot snapshot = await source.ReadAsync().ConfigureAwait(false);
            Assert.That(snapshot.Production.Jobs[0].TransitionSequence, Is.Zero);
            Assert.That(snapshot.ActiveProgram.Name, Is.Empty);
            Assert.That(snapshot.Operation.Sample, Is.EqualTo(Acquired));
            Assert.That((await source.GetJobOrdersV1Async().ConfigureAwait(false))[0].ID, Is.EqualTo("Job-1"));
        }

        /// <summary>
        /// Intermediate and final runs preserve contiguous state history and explicit current/cumulative part totals.
        /// </summary>
        [Test]
        public async Task CompleteRunsPreserveExactHistoryAndCurrentVersusCumulativeCountersAsync()
        {
            await using var source = new MachineToolJobSource(Options(Descriptor() with
            {
                RunsPlanned = 2,
                PartSetKey = "Parts",
                PartSetName = "Explicit batch",
                PartsPlannedPerRun = 2
            }));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.StoreAndStart, Order())
                .ConfigureAwait(false));
            Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.BeginExecution)
                .ConfigureAwait(false));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Pause, Order()).ConfigureAwait(false));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Resume, Order()).ConfigureAwait(false));
            await source.RecordPartsAsync("Job-1", new MachineToolJobSourcePartCounts(2, 1, Acquired))
                .ConfigureAwait(false);
            await source.CompleteRunAsync("Job-1").ConfigureAwait(false);
            MachineToolProductionJob repeated = (await source.ReadAsync().ConfigureAwait(false)).Production.Jobs[0];
            Assert.Multiple(() =>
            {
                Assert.That(repeated.Phase, Is.EqualTo(MachineToolProductionPhase.Running));
                Assert.That(repeated.TransitionSequence, Is.EqualTo(4));
                Assert.That(repeated.RunsCompleted, Is.EqualTo(1));
                Assert.That(repeated.PartsCompleted, Is.EqualTo(2));
                Assert.That(repeated.PartsGood, Is.EqualTo(1));
                Assert.That(repeated.PartsCompletedBeforeCurrentRun, Is.EqualTo(2));
                Assert.That(repeated.PartsGoodBeforeCurrentRun, Is.EqualTo(1));
                Assert.That(repeated.PartSets[0].PartsCompletedPerRun, Is.Zero);
                Assert.That(repeated.PartSets[0].Sample, Is.EqualTo(MachineToolSampleInfo.Unavailable));
            });
            await source.RecordPartsAsync("Job-1", new MachineToolJobSourcePartCounts(2, 2, Acquired))
                .ConfigureAwait(false);
            await source.CompleteRunAsync("Job-1").ConfigureAwait(false);
            MachineToolJobSourceCapture complete = await source.CaptureAsync().ConfigureAwait(false);
            MachineToolProductionJob final = complete.Snapshot.Production.Jobs[0];
            Assert.Multiple(() =>
            {
                Assert.That(final.Phase, Is.EqualTo(MachineToolProductionPhase.Ended));
                Assert.That(final.TransitionSequence, Is.EqualTo(5));
                Assert.That(final.RunsCompleted, Is.EqualTo(2));
                Assert.That(final.PartsCompleted, Is.EqualTo(4));
                Assert.That(final.PartsGood, Is.EqualTo(3));
                Assert.That(final.PartSets[0].PartsCompletedPerRun, Is.EqualTo(2));
                Assert.That(final.PartSets[0].Sample, Is.EqualTo(Acquired));
                Assert.That(complete.JobOrders[0].State[0].StateNumber, Is.EqualTo(5));
                Assert.That(complete.JobOrders[0].State[1].StateNumber, Is.EqualTo(1));
                Assert.That(complete.Snapshot.ActiveProgram, Is.EqualTo(final.Programs[0]));
                Assert.That(complete.Snapshot.Operation.Sample, Is.EqualTo(MachineToolSampleInfo.Unavailable));
                Assert.That(complete.Snapshot.Operation.PartsProducedInLifetime, Is.Null);
            });
            Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.Close).ConfigureAwait(false));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Clear, Order()).ConfigureAwait(false));
            var history = new List<MachineToolSnapshot>();
            await source.DrainAsync(async ct =>
            {
                MachineToolSnapshot snapshot = await source.ReadAsync(ct).ConfigureAwait(false);
                MachineToolJobSourceCapture capture = await source.CaptureAsync(ct).ConfigureAwait(false);
                Assert.That(capture.Snapshot, Is.SameAs(snapshot));
                history.Add(snapshot);
            }).ConfigureAwait(false);
            Assert.That(history, Has.Count.EqualTo(11));
            for (int i = 0; i < history.Count; i++)
            {
                Assert.That(history[i].Revision, Is.EqualTo((ulong)i));
            }
            Assert.That(history[5].Production.Jobs[0].PartSets[0].Sample, Is.EqualTo(Acquired));
            Assert.That(history[8].Production.Jobs[0].TransitionSequence, Is.EqualTo(5));
            Assert.That(history[9].Production.Jobs[0], Is.EqualTo(history[8].Production.Jobs[0]));
            Assert.That(history[10].Production.Jobs, Is.Empty);
        }

        /// <summary>
        /// Stop and interrupted completion cannot fabricate a normal completed run or an unobserved resume.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task EarlyStopOrInterruptedCompletionIsLegacyAbortedAsync(bool interrupted, bool complete)
        {
            await using var source = new MachineToolJobSource(Options());
            await StartExecutingAsync(source).ConfigureAwait(false);
            if (interrupted)
            {
                Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.Hold).ConfigureAwait(false));
            }
            if (complete)
            {
                Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.Complete)
                    .ConfigureAwait(false));
            }
            else
            {
                Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Stop, Order())
                    .ConfigureAwait(false));
            }
            MachineToolJobSourceCapture capture = await source.CaptureAsync().ConfigureAwait(false);
            Assert.That(capture.JobOrders[0].State[0].StateNumber, Is.EqualTo(5));
            Assert.That(capture.Snapshot.Production.Jobs[0].Phase, Is.EqualTo(MachineToolProductionPhase.Aborted));
            Assert.That(capture.Snapshot.Production.Jobs[0].RunsCompleted, Is.Zero);
            Assert.That(capture.Snapshot.Production.Jobs[0].TransitionSequence, Is.EqualTo(interrupted ? 3 : 2));
        }

        /// <summary>
        /// Cancellation retains the final canonical legacy state before removing a not-started order.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CancelRetainsCanonicalFinalObservationBeforeRemovingTheJobAsync(bool allowed)
        {
            await using var source = new MachineToolJobSource(Options());
            Success(await source.ReceiveJobOrderAsync(allowed
                ? Isa95JobOrderOperationV2.StoreAndStart : Isa95JobOrderOperationV2.Store, Order())
                .ConfigureAwait(false));
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.Cancel, Order()).ConfigureAwait(false));
            Assert.That(await source.GetJobOrdersV2Async().ConfigureAwait(false), Is.Empty);
            var captures = new List<MachineToolJobSourceCapture>();
            await source.DrainAsync(async ct => captures.Add(await source.CaptureAsync(ct).ConfigureAwait(false)))
                .ConfigureAwait(false);
            Assert.That(captures, Has.Count.EqualTo(4));
            Assert.That(captures[2].JobOrders, Is.Empty);
            Assert.That(captures[2].Snapshot.Production.Jobs[0].Phase, Is.EqualTo(MachineToolProductionPhase.Aborted));
            Assert.That(captures[2].Snapshot.Production.Jobs[0].TransitionSequence, Is.EqualTo(1));
            Assert.That(captures[3].Snapshot.Production.Jobs, Is.Empty);
        }

        /// <summary>
        /// Responses are explicit historical results, not synthetic acknowledgments or execution commands.
        /// </summary>
        [Test]
        public async Task ExplicitResponsesUseEngineQueriesAndCannotRewriteAcceptedStateAsync()
        {
            await using var source = new MachineToolJobSource(Options());
            await StartExecutingAsync(source).ConfigureAwait(false);
            MachineToolJobSourceCapture running = await source.CaptureAsync().ConfigureAwait(false);
            Assert.That(running.JobResponses, Is.Empty);
            var response = new V2.ISA95JobResponseDataType
            {
                EncodingMask = (uint)(V2.ISA95JobResponseDataTypeFields.Description |
                    V2.ISA95JobResponseDataTypeFields.JobResponseData),
                JobResponseID = "Response-1",
                JobOrderID = "Job-1",
                JobState = running.JobOrders[0].State,
                Description = new LocalizedText("Explicit external result"),
                JobResponseData = [new V2.ISA95ParameterDataType { ID = "observed", Value = Variant.From(17) }]
            };
            Isa95JobResponseReceiptV2 received = await source.ReceiveJobResponseAsync(response).ConfigureAwait(false);
            Assert.That(received.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            response.JobResponseData[0].Value = Variant.From(999);
            Isa95JobResponseByIdResultV2 query = await source.RequestJobResponseByJobOrderIdAsync("Job-1")
                .ConfigureAwait(false);
            Assert.That(query.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(query.Response!.JobResponseData[0].Value.TryGetValue(out int observed), Is.True);
            Assert.That(observed, Is.EqualTo(17));
            query.Response.JobOrderID = "tampered";
            MachineToolJobSourceCapture captured = await source.CaptureAsync().ConfigureAwait(false);
            captured.JobOrders[0].JobOrder.JobOrderID = "tampered";
            captured.JobResponses[0].JobResponseID = "tampered";
            Assert.That((await source.GetJobOrdersV2Async().ConfigureAwait(false))[0].JobOrder.JobOrderID,
                Is.EqualTo("Job-1"));
            Assert.That((await source.GetJobResponsesV2Async().ConfigureAwait(false))[0].JobResponseID,
                Is.EqualTo("Response-1"));
            Isa95JobResponsesByStateResultV2 byState = await source.RequestJobResponsesByStateAsync(response.JobState)
                .ConfigureAwait(false);
            Assert.That(byState.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success));
            Assert.That(byState.Responses, Has.Count.EqualTo(1));
            ulong revision = (await source.ReadAsync().ConfigureAwait(false)).Revision;
            Assert.That((await source.ReceiveJobResponseAsync(response).ConfigureAwait(false)).ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidRequest));
            response.JobResponseID = "Response-2";
            response.JobState = [new V2.ISA95StateDataType { StateNumber = 5 }];
            Assert.That((await source.ReceiveJobResponseAsync(response).ConfigureAwait(false)).ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidStatus));
            response.JobOrderID = "Unknown";
            Assert.That((await source.ReceiveJobResponseAsync(response).ConfigureAwait(false)).ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));
            Assert.That((await source.ReadAsync().ConfigureAwait(false)).Revision, Is.EqualTo(revision));
            Assert.That((await source.RequestJobResponsesByStateAsync([]).ConfigureAwait(false)).ReturnStatus,
                Is.EqualTo(Isa95JobReturnStatus.InvalidStatus));
            Assert.That((await source.RequestJobResponseByJobOrderIdAsync("Unknown").ConfigureAwait(false))
                .ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.UnknownJobOrderId));
        }

        /// <summary>
        /// DI facets share one engine per scope, never execution, counters, or source lifetime across machines.
        /// </summary>
        [Test]
        public async Task ScopedMachinesAreIndependentAndAllProviderFacetsResolveToOneSourceAsync()
        {
            var services = new ServiceCollection();
            services.AddMachineToolJobSource(Options());
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using AsyncServiceScope first = provider.CreateAsyncScope();
            await using AsyncServiceScope second = provider.CreateAsyncScope();
            MachineToolJobSource left = first.ServiceProvider.GetRequiredService<MachineToolJobSource>();
            MachineToolJobSource right = second.ServiceProvider.GetRequiredService<MachineToolJobSource>();
            Assert.That(first.ServiceProvider.GetRequiredService<IMachineToolProvider>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobOrderReceiverV2>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobResponseProviderV2>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobResponseReceiverV2>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobExecutionController>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobOrderCatalog>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobResponseCatalog>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobStatusSourceV2>(), Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobOrderCatalogChangeSource>(),
                Is.SameAs(left));
            Assert.That(first.ServiceProvider.GetRequiredService<IIsa95JobResponseChangeSource>(), Is.SameAs(left));
            await StartExecutingAsync(left).ConfigureAwait(false);
            Assert.That(await right.GetJobOrdersV2Async().ConfigureAwait(false), Is.Empty);
            Assert.That((await right.ReadAsync().ConfigureAwait(false)).Revision, Is.Zero);
            await left.DisposeAsync().ConfigureAwait(false);
            await StartExecutingAsync(right).ConfigureAwait(false);
            Assert.That((await right.ReadAsync().ConfigureAwait(false)).Production.Jobs[0].TransitionSequence,
                Is.EqualTo(1));
        }

        /// <summary>
        /// Creates explicit, deterministic identities for this test family.
        /// </summary>
        internal static MachineToolJobSourceDescriptor Descriptor(string id = "Job-1") => new()
        {
            JobOrderId = id,
            WorkMasterId = "WM-1",
            ProgramName = "SUPPLIED.NC",
            CustomerOrderIdentifier = "Customer-1",
            OrderIdentifier = "Order-1"
        };

        /// <summary>
        /// Creates a source configuration with one declared job.
        /// </summary>
        internal static MachineToolJobSourceOptions Options(MachineToolJobSourceDescriptor? descriptor = null) =>
            new() { Jobs = [descriptor ?? Descriptor()] };

        /// <summary>
        /// Creates an actual generated order with an explicit work-master reference.
        /// </summary>
        internal static V2.ISA95JobOrderDataType Order(string id = "Job-1") => new()
        {
            EncodingMask = (uint)(V2.ISA95JobOrderDataTypeFields.WorkMasterID |
                V2.ISA95JobOrderDataTypeFields.Priority),
            JobOrderID = id,
            WorkMasterID = [new V2.ISA95WorkMasterDataType { ID = "WM-1" }]
        };

        /// <summary>
        /// Requires the actual standard success bit, not an enum ordinal or an empty bitmap.
        /// </summary>
        internal static void Success(Isa95JobOrderReceiptV2 receipt)
        {
            Assert.That(receipt.ReturnStatus, Is.EqualTo(Isa95JobReturnStatus.Success), receipt.Result.ToString());
            Assert.That(ServiceResult.IsGood(receipt.Result), Is.True);
        }

        /// <summary>
        /// Explicitly authorizes then begins simulated execution through the existing controller facet.
        /// </summary>
        internal static async ValueTask StartExecutingAsync(MachineToolJobSource source)
        {
            Success(await source.ReceiveJobOrderAsync(Isa95JobOrderOperationV2.StoreAndStart, Order())
                .ConfigureAwait(false));
            Success(await source.TransitionAsync("Job-1", Isa95JobExecutionTransition.BeginExecution)
                .ConfigureAwait(false));
        }

        /// <summary>Raw observation metadata that must survive modern job operations unchanged.</summary>
        internal static readonly MachineToolSampleInfo Acquired =
            new(new DateTimeUtc(2026, 10, 1, 2, 3, 4), StatusCodes.UncertainLastUsableValue);
    }
}
