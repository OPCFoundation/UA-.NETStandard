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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.LADS.Server;
using Opc.Ua.LADS.Tests.Integration;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Exercises storage/address-space compensation, identity ownership and complete shutdown cleanup.
    /// </summary>
    [TestFixture]
    [Category("LADS")]
    [NonParallelizable]
    public sealed class LadsTransactionTests
    {
        /// <summary>
        /// Restores template storage and native registration for failures before and after each node change.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task TemplatePublicationFailureRestoresStorageAndBrowsingAndAllowsRetryAsync(
            bool remove, bool failAfterChange)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = failAfterChange };
            var store = new FaultingStore();
            await using LadsHostedServer server = await StartAsync(
                nameof(TemplatePublicationFailureRestoresStorageAndBrowsingAndAllowsRetryAsync), store, nodes)
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            var programs = (LadsProgramManager)device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            string id = remove ? LadsIncubator.QuickTemplate : "Added";
            nodes.FailNextAdd = !remove;
            nodes.FailNextRemove = remove;
            await Assert.ThrowsAsync<IOException>(async () =>
            {
                if (remove)
                {
                    await programs.RemoveTemplateAsync(id).ConfigureAwait(false);
                }
                else
                {
                    await programs.AddTemplateAsync(Template(id)).ConfigureAwait(false);
                }
            });
            Assert.That(programs.Templates.ToList().Any(template => template.Id == id), Is.EqualTo(remove));
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.FunctionalUnits[0].Templates.ToList().Any(template => template.Id == id),
                Is.EqualTo(remove));
            Assert.That(saved.FunctionalUnits[0].RemovedTemplateIds.Count, Is.Zero);
            AssertTemplateNode(device, programs, id, remove);
            if (remove)
            {
                Assert.That(await programs.RemoveTemplateAsync(id).ConfigureAwait(false), Is.True);
            }
            else
            {
                await programs.AddTemplateAsync(Template(id)).ConfigureAwait(false);
            }
            AssertTemplateNode(device, programs, id, !remove);
        }

        /// <summary>
        /// Preserves the previous result and its client-readable files when publication or removal fails.
        /// </summary>
        [TestCase("add")]
        [TestCase("remove")]
        [TestCase("prune")]
        public async Task ResultPublicationFailureKeepsRetainedFilesBrowsableAndAllowsRetryAsync(string operation)
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            var store = new FaultingStore();
            await using LadsClientConnection connection = await LadsClientConnection.StartAsync(
                nameof(ResultPublicationFailureKeepsRetainedFilesBrowsableAndAllowsRetryAsync) + operation,
                builder =>
                {
                    builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                    builder.AddLads(options =>
                    {
                        options.Store = store;
                        options.MaxRetainedResults = operation == "prune" ? 1 : 10;
                    }).ConfigureLads(LadsIncubator.Configure);
                }).ConfigureAwait(false);
            var device = (LadsDevice)await connection.Server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            ILadsProgramManager programs = unit.Programs!;
            string retainedId = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            ILadsResult retained = await programs.WaitForResultAsync(retainedId).ConfigureAwait(false);
            nodes.FailNextAdd = operation == "add";
            nodes.FailNextRemove = operation != "add";
            await Assert.ThrowsAsync<IOException>(async () =>
            {
                if (operation == "remove")
                {
                    await programs.RemoveResultAsync(retainedId).ConfigureAwait(false);
                }
                else
                {
                    string failedId = await programs.StartProgramAsync(LadsIncubator.QuickTemplate)
                        .ConfigureAwait(false);
                    await programs.WaitForResultAsync(failedId).ConfigureAwait(false);
                }
            });
            Assert.That(programs.Results.Count, Is.EqualTo(1));
            Assert.That(programs.Results[0], Is.SameAs(retained));
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.OperationCycles, Is.EqualTo(1));
            Assert.That(saved.FunctionalUnits[0].Results.Count, Is.EqualTo(1));
            Assert.That(saved.FunctionalUnits[0].Results[0].RunId, Is.EqualTo(retainedId));
            Assert.That(saved.FunctionalUnits[0].Results[0].Files[0].Content.ToArray(),
                Is.EqualTo(retained.Files[0].Content.ToArray()));
            Opc.Ua.LADS.Client.LadsFunctionalUnit remoteUnit = await connection.ChamberAsync().ConfigureAwait(false);
            Opc.Ua.LADS.Client.LadsResult? remote = await remoteUnit.FindResultAsync(retainedId).ConfigureAwait(false);
            Assert.That(remote, Is.Not.Null);
            ByteString downloaded = await connection.Client.ReadResultFileAsync(remote!.Files[0])
                .ConfigureAwait(false);
            Assert.That(downloaded.ToArray(), Is.EqualTo(retained.Files[0].Content.ToArray()));
            if (operation != "remove")
            {
                await unit.ClearAsync().ConfigureAwait(false);
                string retry = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
                await programs.WaitForResultAsync(retry).ConfigureAwait(false);
                Assert.That(device.OperationCycleCounter, Is.EqualTo(2));
            }
            Assert.That(await programs.RemoveResultAsync(retainedId).ConfigureAwait(false),
                Is.EqualTo(operation != "prune"));
        }

        /// <summary>
        /// Rejects mutations after incomplete compensation and rebuilds the retained snapshot on restart.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedCompensationQuarantinesDeviceAndPreservesBothErrorsAsync(bool storageRollback)
        {
            var nodes = new FaultingNodeOperations
            {
                FailAfterChange = true,
                FailRemovalBeforeChange = true
            };
            var store = new FaultingStore();
            await using (LadsHostedServer server = await StartAsync(
                nameof(FailedCompensationQuarantinesDeviceAndPreservesBothErrorsAsync), store, nodes)
                .ConfigureAwait(false))
            {
                var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
                ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
                ILadsProgramManager programs = unit.Programs!;
                nodes.FailNextAdd = true;
                nodes.FailNextRemove = !storageRollback;
                store.FailSaveNumber = storageRollback ? store.Saves + 2 : 0;
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                    await programs.AddTemplateAsync(Template("Uncertain")).ConfigureAwait(false));
                string[] messages = failure!.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
                Assert.That(messages, Does.Contain("Node add failed."));
                Assert.That(messages, Does.Contain(storageRollback ? "Store save failed." : "Node remove failed."));
                Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Shutdown));
                Assert.That(programs.Templates.ToList().Any(template => template.Id == "Uncertain"), Is.False);
                AssertTemplateNode(device, (LadsProgramManager)programs, "Uncertain", !storageRollback);
                int saves = store.Saves;
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.AddTemplateAsync(Template("Rejected")).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.RemoveTemplateAsync("Missing").ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.RemoveResultAsync("Missing").ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.GetMaintenanceTask(LadsIncubator.MaintenanceName).StartAsync().ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.GotoOperateAsync().ConfigureAwait(false));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.SetOperationModeAsync(MachineryOperationModeValue.Processing).ConfigureAwait(false));
                Assert.Throws<InvalidOperationException>(() => device.IncrementOperationCycles());
                Assert.Throws<InvalidOperationException>(() => device.AddOperationDuration(TimeSpan.FromSeconds(1)));
                IAnalogControlFunction temperature = unit.GetFunction<IAnalogControlFunction>("Temperature");
                double target = temperature.TargetValue;
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await temperature.SetTargetValueAsync(target + 1).ConfigureAwait(false));
                Assert.Throws<InvalidOperationException>(() => temperature.UpdateCurrentValue(30));
                Assert.Throws<InvalidOperationException>(() => temperature.IsEnabled = false);
                IAnalogScalarSensorFunction sensor = unit.GetFunction<IAnalogScalarSensorFunction>("CO2");
                Assert.Throws<InvalidOperationException>(() => sensor.UpdateValue(10));
                Assert.That(temperature.TargetValue, Is.EqualTo(target));
                Assert.That(temperature.CurrentValue, Is.EqualTo(22));
                Assert.That(temperature.IsEnabled, Is.True);
                Assert.That(sensor.SensorValue, Is.EqualTo(5));
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await device.SaveStoredDataAsync().ConfigureAwait(false));
                Assert.That(store.Saves, Is.EqualTo(saves));
            }
            await using LadsHostedServer restarted = await StartAsync(
                nameof(FailedCompensationQuarantinesDeviceAndPreservesBothErrorsAsync) + "Restart", store, nodes)
                .ConfigureAwait(false);
            var recovered = (LadsDevice)await restarted.DeviceAsync().ConfigureAwait(false);
            var recoveredPrograms = (LadsProgramManager)recovered.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            Assert.That(recovered.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            Assert.That(recoveredPrograms.Templates.ToList().Any(template => template.Id == "Uncertain"),
                Is.EqualTo(storageRollback));
            AssertTemplateNode(recovered, recoveredPrograms, "Uncertain", storageRollback);
            await recoveredPrograms.AddTemplateAsync(Template("Recovered")).ConfigureAwait(false);
            AssertTemplateNode(recovered, recoveredPrograms, "Recovered", true);
        }

        /// <summary>
        /// Prevents another unit's checkpoint from being overwritten by a delayed rollback.
        /// </summary>
        [Test]
        public async Task CompensationKeepsDeviceStoreExclusiveAcrossOtherUnitsAsync()
        {
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            var store = new FaultingStore();
            await using LadsHostedServer server = await StartAsync(
                nameof(CompensationKeepsDeviceStoreExclusiveAcrossOtherUnitsAsync), store, nodes,
                extra: device => device.FunctionalUnit("Second", unit => unit.Programs(_ => { })))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager first = device.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            ILadsProgramManager second = device.GetFunctionalUnit("Second").Programs!;
            nodes.FailNextAdd = true;
            nodes.PauseNextRemove = true;
            Task rejected = first.AddTemplateAsync(Template("RolledBack")).AsTask();
            Task? accepted = null;
            try
            {
                await nodes.RemoveEntered.Task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                int saves = store.Saves;
                accepted = second.AddTemplateAsync(Template("Retained")).AsTask();
                Assert.That(accepted.IsCompleted, Is.False);
                Assert.That(store.Saves, Is.EqualTo(saves));
                Assert.That(second.Templates.Count, Is.Zero);
            }
            finally
            {
                nodes.ReleaseRemove.TrySetResult(true);
                await Assert.ThrowsAsync<IOException>(async () =>
                    await rejected.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false));
                if (accepted != null)
                {
                    await accepted.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
            }
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.FunctionalUnits[0].Templates.ToList().Any(template => template.Id == "RolledBack"),
                Is.False);
            Assert.That(saved.FunctionalUnits[1].Templates[0].Id, Is.EqualTo("Retained"));
        }

        /// <summary>
        /// Rejects colliding explicit and fallback keys without accessing the provider.
        /// </summary>
        [TestCase("explicit")]
        [TestCase("fallback")]
        [TestCase("reverse-fallback")]
        public async Task CollidingIdentitiesAreRejectedBeforeAnyStoreAccessAsync(string collision)
        {
            var store = new FaultingStore();
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(CollidingIdentitiesAreRejectedBeforeAnyStoreAccessAsync),
                builder => builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                {
                    setup = context;
                    LadsIncubator.Configure(context, LadsIncubator.DeviceName, device =>
                    {
                        if (collision != "fallback")
                        {
                            device.Identification(id => id.ProductInstanceUri = collision == "reverse-fallback"
                                ? "urn:opcua-netstandard:lads:instances#Second" : "urn:tests:lads:shared");
                        }
                    });
                })).ConfigureAwait(false);
            var first = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsProgramManager programs = first.GetFunctionalUnit(LadsIncubator.UnitName).Programs!;
            await programs.AddTemplateAsync(Template("OwnedByFirst")).ConfigureAwait(false);
            int loads = store.Loads;
            int saves = store.Saves;
            ServiceResultException? failure = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await setup!.AddDeviceAsync("Second", device =>
                {
                    if (collision != "reverse-fallback")
                    {
                        device.Identification(id => id.ProductInstanceUri = first.StoreIdentity);
                    }
                }).ConfigureAwait(false));
            Assert.That(failure!.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(store.Loads, Is.EqualTo(loads));
            Assert.That(store.Saves, Is.EqualTo(saves));
            LadsStoredDeviceData saved = (await store.LoadAsync(first.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.FunctionalUnits[0].Templates.ToList().Any(template => template.Id == "OwnedByFirst"),
                Is.True);
            ILadsDevice replacement = await setup!.AddDeviceAsync("Second", device => device.Identification(id =>
                id.ProductInstanceUri = "urn:tests:lads:second")).ConfigureAwait(false);
            Assert.That(replacement.Name, Is.EqualTo("Second"));
        }

        /// <summary>
        /// Makes failed registration reservations available to a corrected retry.
        /// </summary>
        [TestCase("load")]
        [TestCase("save")]
        [TestCase("add")]
        [TestCase("add-cleanup")]
        public async Task FailedRegistrationReleasesNameAndStorageIdentityAsync(string operation)
        {
            var store = new FaultingStore();
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            ILadsSetupContext? setup = null;
            await using LadsHostedServer server = await LadsHostedServer.StartAsync(
                nameof(FailedRegistrationReleasesNameAndStorageIdentityAsync),
                builder =>
                {
                    builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                    builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                    {
                        setup = context;
                        LadsIncubator.Configure(context);
                    });
                }).ConfigureAwait(false);
            store.FailNextLoad = operation == "load";
            store.FailSaveNumber = operation == "save" ? store.Saves + 1 : 0;
            nodes.FailNextAdd = operation is "add" or "add-cleanup";
            nodes.FailNextRemove = operation == "add-cleanup";
            Exception? failure = await Assert.CatchAsync<Exception>(async () =>
                await setup!.AddDeviceAsync("Retry", device => device.Identification(id =>
                    id.ProductInstanceUri = "urn:tests:lads:retry")).ConfigureAwait(false));
            string[] messages = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions.Select(error => error.Message).ToArray()
                : [failure!.Message];
            Assert.That(messages, Does.Contain(operation switch
            {
                "load" => "Store load failed.",
                "save" => "Store save failed.",
                _ => "Node add failed."
            }));
            if (operation == "add-cleanup")
            {
                Assert.That(messages, Does.Contain("Node remove failed."));
            }
            ILadsDevice retry = await setup!.AddDeviceAsync("Retry", device => device.Identification(id =>
                id.ProductInstanceUri = "urn:tests:lads:retry")).ConfigureAwait(false);
            Assert.That(retry.Name, Is.EqualTo("Retry"));
            Assert.That(retry.State, Is.EqualTo(LadsDeviceStateValue.Operate));
        }

        /// <summary>
        /// Reports checkpoint and disposal failures while attempting every remaining owner exactly once.
        /// </summary>
        [Test]
        public async Task ShutdownPreservesCheckpointAndBindingFailuresAndReleasesEveryOwnerAsync()
        {
            var store = new FaultingStore();
            var binder = new FailingFileBinder(1, 3, 5, 8);
            await using LadsHostedServer server = await StartAsync(
                nameof(ShutdownPreservesCheckpointAndBindingFailuresAndReleasesEveryOwnerAsync), store,
                new FaultingNodeOperations(),
                builder =>
                {
                    builder.Services.AddSingleton<IFileTypeBinder>(binder);
                    builder.Services.AddSingleton<ILadsProgramExecutor>(new ResultFileExecutor());
                },
                device => device.FunctionalUnit("Second", unit => unit.Programs(programs =>
                    programs.Template("Run", template => template.Step("Step", TimeSpan.Zero)))))
                .ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            foreach (ILadsFunctionalUnit unit in device.FunctionalUnits.ToArray())
            {
                ILadsProgramManager programs = unit.Programs!;
                for (int index = 0; index < (unit.Name == "Second" ? 1 : 2); index++)
                {
                    string id = await programs.StartProgramAsync(
                        unit.Name == "Second" ? "Run" : LadsIncubator.QuickTemplate).ConfigureAwait(false);
                    await programs.WaitForResultAsync(id).ConfigureAwait(false);
                }
            }
            Assert.That(binder.Bindings, Is.EqualTo(9));
            store.FailAllSaves = true;
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await device.DisposeAsync().ConfigureAwait(false));
            string[] messages = failure!.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
            Assert.That(messages, Does.Contain("Store save failed."));
            Assert.That(messages, Does.Contain("Binding 1 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 3 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 5 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 8 cleanup failed."));
            Assert.That(binder.Releases, Is.EqualTo(binder.Bindings));
            foreach (ILadsFunctionalUnit unit in device.FunctionalUnits.ToArray())
            {
                await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                    await ((LadsFunctionalUnit)unit).Gate.WaitAsync().ConfigureAwait(false));
            }
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await device.GetMaintenanceTask(LadsIncubator.MaintenanceName).StartAsync().ConfigureAwait(false));
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await device.SaveStoredDataAsync().ConfigureAwait(false));
            int saves = store.Saves;
            AggregateException? repeated = await Assert.ThrowsAsync<AggregateException>(async () =>
                await device.DisposeAsync().ConfigureAwait(false));
            Assert.That(repeated, Is.SameAs(failure));
            Assert.That(binder.Releases, Is.EqualTo(binder.Bindings));
            Assert.That(store.Saves, Is.EqualTo(saves));
        }

        /// <summary>
        /// Reports publication and candidate cleanup errors without losing either cause or preventing retry.
        /// </summary>
        [Test]
        public async Task ResultPublicationAndFileCleanupFailuresPreserveBothErrorsAndAllowRetryAsync()
        {
            var store = new FaultingStore();
            var nodes = new FaultingNodeOperations { FailAfterChange = true };
            var binder = new FailingFileBinder(1, 3);
            await using LadsHostedServer server = await StartAsync(
                nameof(ResultPublicationAndFileCleanupFailuresPreserveBothErrorsAndAllowRetryAsync),
                store, nodes, builder =>
                {
                    builder.Services.AddSingleton<IFileTypeBinder>(binder);
                    builder.Services.AddSingleton<ILadsProgramExecutor>(new ResultFileExecutor());
                }).ConfigureAwait(false);
            var device = (LadsDevice)await server.DeviceAsync().ConfigureAwait(false);
            ILadsFunctionalUnit unit = device.GetFunctionalUnit(LadsIncubator.UnitName);
            var programs = (LadsProgramManager)unit.Programs!;
            nodes.FailNextAdd = true;
            string id = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(async () =>
                await programs.WaitForResultAsync(id).ConfigureAwait(false));
            string[] messages = failure!.Flatten().InnerExceptions.Select(error => error.Message).ToArray();
            Assert.That(messages, Does.Contain("Node add failed."));
            Assert.That(messages, Does.Contain("Binding 1 cleanup failed."));
            Assert.That(messages, Does.Contain("Binding 3 cleanup failed."));
            Assert.That(binder.Bindings, Is.EqualTo(3));
            Assert.That(binder.Releases, Is.EqualTo(3));
            Assert.That(device.OperationCycleCounter, Is.Zero);
            Assert.That(device.State, Is.EqualTo(LadsDeviceStateValue.Operate));
            Assert.That(programs.Results.Count, Is.Zero);
            var children = new List<BaseInstanceState>();
            programs.Node.ResultSet!.GetChildren(device.Runtime.Context, children);
            Assert.That(children, Is.Empty);
            LadsStoredDeviceData saved = (await store.LoadAsync(device.StoreIdentity).ConfigureAwait(false))!;
            Assert.That(saved.OperationCycles, Is.Zero);
            Assert.That(saved.FunctionalUnits[0].Results.Count, Is.Zero);
            await unit.ClearAsync().ConfigureAwait(false);
            string retry = await programs.StartProgramAsync(LadsIncubator.QuickTemplate).ConfigureAwait(false);
            await programs.WaitForResultAsync(retry).ConfigureAwait(false);
            Assert.That(device.OperationCycleCounter, Is.EqualTo(1));
            Assert.That(programs.Results.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// Creates a zero-duration template without physical side effects.
        /// </summary>
        private static LadsProgramTemplate Template(string id)
        {
            return new LadsProgramTemplate(id).Step("Step", TimeSpan.Zero);
        }

        /// <summary>
        /// Checks both browse containment and node-manager registration.
        /// </summary>
        private static void AssertTemplateNode(
            LadsDevice device, LadsProgramManager programs, string id, bool present)
        {
            var children = new List<BaseInstanceState>();
            programs.Node.ProgramTemplateSet!.GetChildren(device.Runtime.Context, children);
            BaseInstanceState[] matches = children.Where(child => child.BrowseName.Name == id).ToArray();
            Assert.That(matches.Length, Is.EqualTo(present ? 1 : 0));
            if (present)
            {
                Assert.That(device.Runtime.Manager.FindPredefinedNode(matches[0].NodeId), Is.SameAs(matches[0]));
            }
        }

        /// <summary>
        /// Starts a real server with deterministic storage and node-operation failure seams.
        /// </summary>
        private static Task<LadsHostedServer> StartAsync(
            string name,
            FaultingStore store,
            FaultingNodeOperations nodes,
            Action<IOpcUaServerBuilder>? compose = null,
            Action<ILadsDeviceBuilder>? extra = null)
        {
            return LadsHostedServer.StartAsync(name, builder =>
            {
                builder.Services.AddSingleton<ILadsNodeOperations>(nodes);
                compose?.Invoke(builder);
                builder.AddLads(options => options.Store = store).ConfigureLads(context =>
                    LadsIncubator.Configure(context, LadsIncubator.DeviceName, extra));
            });
        }

        /// <summary>
        /// Produces multiple owned file bindings without performing physical work.
        /// </summary>
        private sealed class ResultFileExecutor : ILadsProgramExecutor
        {
            /// <inheritdoc/>
            public ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                run.AddResultFile("First.bin", "application/octet-stream", ByteString.From(1));
                run.AddResultFile("Second.bin", "application/octet-stream", ByteString.From(2));
                run.AddResultFile("Third.bin", "application/octet-stream", ByteString.From(3));
                return default;
            }
        }

        /// <summary>
        /// Reports selected disposal failures after releasing the real bindings.
        /// </summary>
        private sealed class FailingFileBinder : IFileTypeBinder
        {
            /// <summary>
            /// Creates a binder that rejects the listed one-based disposal calls.
            /// </summary>
            public FailingFileBinder(params int[] failures)
            {
                m_failures = failures;
            }

            /// <summary>
            /// Gets the number of bindings created.
            /// </summary>
            public int Bindings { get; private set; }

            /// <summary>
            /// Gets the number of bindings released.
            /// </summary>
            public int Releases => Volatile.Read(ref m_releases);

            /// <inheritdoc/>
            public IFileTypeBinding Bind(
                FileState file,
                ISystemContext context,
                ByteString content,
                Func<ByteString, CancellationToken, ValueTask>? commit = null,
                bool commitOnClose = false,
                NodeId ownerSession = default,
                int maximumBytes = 16 * 1024 * 1024)
            {
                IFileTypeBinding inner = m_inner.Bind(
                    file, context, content, commit, commitOnClose, ownerSession, maximumBytes);
                int number = ++Bindings;
                var binding = new Mock<IFileTypeBinding>();
                binding.SetupGet(value => value.File).Returns(inner.File);
                binding.Setup(value => value.DisposeAsync()).Returns(async () =>
                {
                    await inner.DisposeAsync().ConfigureAwait(false);
                    Interlocked.Increment(ref m_releases);
                    if (m_failures.Contains(number))
                    {
                        throw new IOException("Binding " + number + " cleanup failed.");
                    }
                });
                return binding.Object;
            }

            /// <summary>The real file binder used before injecting disposal errors.</summary>
            private readonly FileTypeBinder m_inner = new();

            /// <summary>The selected disposal calls that report errors.</summary>
            private readonly int[] m_failures;

            /// <summary>The number of bindings whose real disposal completed.</summary>
            private int m_releases;
        }

        /// <summary>
        /// Injects failures immediately before or after actual node-manager operations.
        /// </summary>
        private sealed class FaultingNodeOperations : ILadsNodeOperations
        {
            /// <summary>
            /// Gets or sets whether to fail the next add.
            /// </summary>
            public bool FailNextAdd { get; set; }

            /// <summary>
            /// Gets or sets whether to fail the next removal.
            /// </summary>
            public bool FailNextRemove { get; set; }

            /// <summary>
            /// Gets or sets whether the real operation completes before the injected error.
            /// </summary>
            public bool FailAfterChange { get; set; }

            /// <summary>
            /// Gets or sets whether a failing removal leaves the real subtree registered.
            /// </summary>
            public bool FailRemovalBeforeChange { get; set; }

            /// <summary>
            /// Gets or sets whether the next removal waits for explicit release.
            /// </summary>
            public bool PauseNextRemove { get; set; }

            /// <summary>
            /// Gets the notification that compensation has entered.
            /// </summary>
            public TaskCompletionSource<bool> RemoveEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>
            /// Gets the release signal for a paused compensation.
            /// </summary>
            public TaskCompletionSource<bool> ReleaseRemove { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public async ValueTask AddAsync(
                DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken)
            {
                bool fail = FailNextAdd;
                FailNextAdd = false;
                if (!fail || FailAfterChange)
                {
                    await m_inner.AddAsync(manager, node, cancellationToken).ConfigureAwait(false);
                }
                if (fail)
                {
                    throw new IOException("Node add failed.");
                }
            }

            /// <inheritdoc/>
            public async ValueTask RemoveAsync(
                DiNodeManager manager, BaseInstanceState node, CancellationToken cancellationToken)
            {
                if (PauseNextRemove)
                {
                    PauseNextRemove = false;
                    RemoveEntered.TrySetResult(true);
                    await ReleaseRemove.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                bool fail = FailNextRemove;
                FailNextRemove = false;
                if (!fail || (FailAfterChange && !FailRemovalBeforeChange))
                {
                    await m_inner.RemoveAsync(manager, node, cancellationToken).ConfigureAwait(false);
                }
                if (fail)
                {
                    throw new IOException("Node remove failed.");
                }
            }

            /// <summary>The actual node-manager operation dispatcher.</summary>
            private readonly LadsNodeOperations m_inner = new();
        }

        /// <summary>
        /// Counts actual store calls and rejects selected loads or saves.
        /// </summary>
        private sealed class FaultingStore : ILadsStore
        {
            /// <summary>
            /// Gets the number of load calls.
            /// </summary>
            public int Loads { get; private set; }

            /// <summary>
            /// Gets the number of save calls.
            /// </summary>
            public int Saves { get; private set; }

            /// <summary>
            /// Gets or sets whether the next load is rejected.
            /// </summary>
            public bool FailNextLoad { get; set; }

            /// <summary>
            /// Gets or sets the one-based save call to reject.
            /// </summary>
            public int FailSaveNumber { get; set; }

            /// <summary>
            /// Gets or sets whether every checkpoint is rejected.
            /// </summary>
            public bool FailAllSaves { get; set; }

            /// <inheritdoc/>
            public ValueTask<LadsStoredDeviceData?> LoadAsync(
                string deviceIdentity, CancellationToken cancellationToken = default)
            {
                Loads++;
                if (FailNextLoad)
                {
                    FailNextLoad = false;
                    throw new IOException("Store load failed.");
                }
                return m_inner.LoadAsync(deviceIdentity, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask SaveAsync(
                string deviceIdentity, LadsStoredDeviceData data, CancellationToken cancellationToken = default)
            {
                Saves++;
                if (FailAllSaves || Saves == FailSaveNumber)
                {
                    throw new IOException("Store save failed.");
                }
                return m_inner.SaveAsync(deviceIdentity, data, cancellationToken);
            }

            /// <inheritdoc/>
            public ValueTask DeleteAsync(string deviceIdentity, CancellationToken cancellationToken = default)
            {
                return m_inner.DeleteAsync(deviceIdentity, cancellationToken);
            }

            /// <summary>The retained snapshots underlying injected provider failures.</summary>
            private readonly InMemoryLadsStore m_inner = new();
        }
    }
}
