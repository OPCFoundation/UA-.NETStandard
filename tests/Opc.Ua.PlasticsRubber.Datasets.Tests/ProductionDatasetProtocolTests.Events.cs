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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server;
using Opc.Ua.Server.FileSystem;
using Opc.Ua.Tests;
using ISubscription = Opc.Ua.Server.ISubscription;
using ISubscriptionManager = Opc.Ua.Client.Subscriptions.ISubscriptionManager;

namespace Opc.Ua.PlasticsRubber.Datasets.Tests
{
    public sealed partial class ProductionDatasetProtocolTests
    {
        /// <summary>
        /// Async dataset policies govern monitor admission for every selected status property and retain allowed reads.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF27StatusMonitoringAdmissionUsesSubscriberPolicy(
            [Values(StorageEnumeration.PRODUCTION, StorageEnumeration.PREPARATION)] StorageEnumeration storage,
            [Values("Information", "Modified", "Frozen")] string member)
        {
            var policy = new DatasetSecurityPolicy();
            (DatasetSecurityOwner owner, Mock<IServerInternal> server, ProductionDatasetBinding binding) =
                await CreateDatasetSecurityBindingAsync(policy, revision: () => 1).ConfigureAwait(false);
            using var queues = new MonitoredItemQueueFactory(server.Object.Telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            await using MasterNodeManager master = CreateDatasetSecurityMaster(server, owner);
            await using (binding.ConfigureAwait(false))
            {
                BaseVariableState variable = DatasetSecurityVariable(binding, storage, member);
                ServerSystemContext deniedContext = DatasetSecurityContext(
                    server, "denied", RequestType.CreateMonitoredItems);
                using OperationContext denied = deniedContext.OperationContext!;
                policy.DeniedSessions.TryAdd(denied.Session.Id, 0);
                (ServiceResult refused, DataValue empty) = await variable.ReadAttributeAsync(
                    deniedContext, Attributes.Value, NumericRange.Null, QualifiedName.Null,
                    default, CancellationToken.None).ConfigureAwait(false);
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(empty.WrappedValue.IsNull, Is.True);
                var errors = new ServiceResult[1];
                var filters = new MonitoringFilterResult[1];
                var items = new IMonitoredItem[1];
                await master.CreateMonitoredItemsAsync(denied, 1, 100, TimestampsToReturn.Both,
                    [DatasetSecurityMonitorRequest(variable)], errors, filters, items, false).ConfigureAwait(false);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(items[0], Is.Null);

                ServerSystemContext allowedContext = DatasetSecurityContext(
                    server, "allowed", RequestType.CreateMonitoredItems);
                using OperationContext allowed = allowedContext.OperationContext!;
                await master.CreateMonitoredItemsAsync(allowed, 2, 100, TimestampsToReturn.Both,
                    [DatasetSecurityMonitorRequest(variable)], errors, filters, items, false).ConfigureAwait(false);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                AssertDatasetSecurityInitialNotification(items[0], allowed, storage, member, revision: 1);
            }
        }

        /// <summary>
        /// Cancelling one asynchronous admission drains its policy call without poisoning a later authorized request.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF27CancelledMonitorAuthorizationPreservesLaterAllowedAdmission()
        {
            var policy = new DatasetSecurityPolicy();
            (DatasetSecurityOwner owner, Mock<IServerInternal> server, ProductionDatasetBinding binding) =
                await CreateDatasetSecurityBindingAsync(policy, revision: () => 1).ConfigureAwait(false);
            using var queues = new MonitoredItemQueueFactory(server.Object.Telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            await using MasterNodeManager master = CreateDatasetSecurityMaster(server, owner);
            await using (binding.ConfigureAwait(false))
            {
                BaseVariableState variable = binding.Node.ActiveProductionDatasetStatus!.Information!;
                ServerSystemContext context = DatasetSecurityContext(
                    server, "cancelled", RequestType.CreateMonitoredItems);
                using OperationContext operation = context.OperationContext!;
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                policy.BeforeCheck = async (_, _, ct) =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(ct).ConfigureAwait(false);
                };
                var errors = new ServiceResult[1];
                var filters = new MonitoringFilterResult[1];
                var items = new IMonitoredItem[1];
                using var cancellation = new CancellationTokenSource();
                Task admission = master.CreateMonitoredItemsAsync(
                    operation, 1, 100, TimestampsToReturn.Both, [DatasetSecurityMonitorRequest(variable)],
                    errors, filters, items, false, cancellation.Token).AsTask();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    await Assert.ThatAsync(async () => await admission.ConfigureAwait(false),
                        Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                    Assert.That(items[0], Is.Null);
                }
                finally
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    release.TrySetResult(true);
                }

                policy.BeforeCheck = null;
                await master.CreateMonitoredItemsAsync(operation, 2, 100, TimestampsToReturn.Both,
                    [DatasetSecurityMonitorRequest(variable)], errors, filters, items, false).ConfigureAwait(false);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                AssertDatasetSecurityInitialNotification(
                    items[0], operation, StorageEnumeration.PRODUCTION, "Information", revision: 1);
            }
        }

        /// <summary>
        /// Disposal cancels and drains a subscriber policy before releasing resources, then closes new admission.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF27DisposalCancelsAndDrainsSubscriberAuthorization()
        {
            var policy = new DatasetSecurityPolicy();
            (DatasetSecurityOwner owner, Mock<IServerInternal> server, ProductionDatasetBinding binding) =
                await CreateDatasetSecurityBindingAsync(policy, revision: () => 1).ConfigureAwait(false);
            using var queues = new MonitoredItemQueueFactory(server.Object.Telemetry);
            server.SetupGet(value => value.MonitoredItemQueueFactory).Returns(queues);
            await using MasterNodeManager master = CreateDatasetSecurityMaster(server, owner);
            await using (binding.ConfigureAwait(false))
            {
                BaseVariableState variable = binding.Node.ActiveProductionDatasetStatus!.Information!;
                ServerSystemContext context = DatasetSecurityContext(
                    server, "stopping", RequestType.CreateMonitoredItems);
                using OperationContext operation = context.OperationContext!;
                ServerSystemContext reporter = DatasetSecurityContext(server, "reporter", RequestType.Call);
                using OperationContext reportingOperation = reporter.OperationContext!;
                var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var drained = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                policy.BeforeCheck = async (request, _, ct) =>
                {
                    if (request.SystemContext is not ISessionSystemContext caller ||
                        caller.SessionId.GetValueOrDefault() != operation.Session.Id)
                    {
                        return;
                    }
                    entered.TrySetResult(true);
                    try
                    {
                        await release.Task.WaitAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        canceled.TrySetResult(true);
                        await drained.Task.ConfigureAwait(false);
                        throw;
                    }
                };
                var delivered = new TaskCompletionSource<DataValue>(TaskCreationOptions.RunContinuationsAsynchronously);
                Mock<IDataChangeMonitoredItem2> item = DatasetSecurityItem(1, variable, operation, () => delivered);
                await using var monitored = new MonitoredNode2(owner, server.Object, variable);
                monitored.Add(item.Object);
                Task publication = binding.RefreshAsync(reporter).AsTask();
                try
                {
                    await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Task disposal = binding.DisposeAsync().AsTask();
                    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Assert.That(disposal.IsCompleted, Is.False,
                        "Disposal must wait until the cancelled authorization finishes its cleanup.");
                    drained.TrySetResult(true);
                    await Task.WhenAll(publication, disposal).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    DataValue rejected = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadShutdown));
                    Assert.That(rejected.WrappedValue.IsNull, Is.True);
                }
                finally
                {
                    drained.TrySetResult(true);
                    release.TrySetResult(true);
                }

                (ServiceResult refused, DataValue empty) = await variable.ReadAttributeAsync(
                    context, Attributes.Value, NumericRange.Null, QualifiedName.Null,
                    default, CancellationToken.None).ConfigureAwait(false);
                Assert.That(refused.StatusCode, Is.EqualTo(StatusCodes.BadShutdown));
                Assert.That(empty.WrappedValue.IsNull, Is.True);
                var errors = new ServiceResult[1];
                var filters = new MonitoringFilterResult[1];
                var items = new IMonitoredItem[1];
                await master.CreateMonitoredItemsAsync(operation, 2, 100, TimestampsToReturn.Both,
                    [DatasetSecurityMonitorRequest(variable)], errors, filters, items, false).ConfigureAwait(false);
                Assert.That(errors[0].StatusCode, Is.EqualTo(StatusCodes.BadShutdown));
                Assert.That(items[0], Is.Null, "A retired binding must not leave a permissive subscription path.");
            }
        }

        /// <summary>
        /// An authorized reporter never lends read permission to other subscribers, including after revocation.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF27StatusDeliveryRechecksSubscribersAndRevocation(
            [Values(StorageEnumeration.PRODUCTION, StorageEnumeration.PREPARATION)] StorageEnumeration storage,
            [Values("Information", "Modified", "Frozen")] string member,
            [Values] bool reporterCanRead)
        {
            var policy = new DatasetSecurityPolicy();
            int revision = 1;
            (DatasetSecurityOwner owner, Mock<IServerInternal> server, ProductionDatasetBinding binding) =
                await CreateDatasetSecurityBindingAsync(policy, () => revision).ConfigureAwait(false);
            await using MasterNodeManager master = CreateDatasetSecurityMaster(server, owner);
            await using (binding.ConfigureAwait(false))
            {
                ServerSystemContext reporter = DatasetSecurityContext(server, "reporter", RequestType.Call);
                using OperationContext reportingOperation = reporter.OperationContext!;
                ServerSystemContext denied = DatasetSecurityContext(server, "denied", RequestType.CreateMonitoredItems);
                using OperationContext deniedOperation = denied.OperationContext!;
                ServerSystemContext allowed = DatasetSecurityContext(
                    server, "allowed", RequestType.CreateMonitoredItems);
                using OperationContext allowedOperation = allowed.OperationContext!;
                ServerSystemContext late = DatasetSecurityContext(server, "late", RequestType.CreateMonitoredItems);
                using OperationContext lateOperation = late.OperationContext!;
                policy.DeniedSessions.TryAdd(deniedOperation.Session.Id, 0);
                BaseVariableState variable = DatasetSecurityVariable(binding, storage, member);
                if (!reporterCanRead)
                {
                    policy.BeforeCheck = (request, memberId, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        if (memberId == variable.NodeId &&
                            request.SystemContext is ISessionSystemContext caller &&
                            caller.SessionId.GetValueOrDefault() == reportingOperation.Session.Id)
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        return default;
                    };
                }
                var deniedDelivery = new TaskCompletionSource<DataValue>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var allowedDelivery = new TaskCompletionSource<DataValue>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                var lateDelivery = new TaskCompletionSource<DataValue>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Mock<IDataChangeMonitoredItem2> deniedItem = DatasetSecurityItem(
                    1, variable, deniedOperation, () => deniedDelivery);
                Mock<IDataChangeMonitoredItem2> allowedItem = DatasetSecurityItem(
                    2, variable, allowedOperation, () => allowedDelivery);
                Mock<IDataChangeMonitoredItem2> lateItem = DatasetSecurityItem(
                    3, variable, lateOperation, () => lateDelivery);
                await using var monitored = new MonitoredNode2(owner, server.Object, variable);
                monitored.Add(deniedItem.Object);
                monitored.Add(allowedItem.Object);
                await binding.RefreshAsync(reporter).ConfigureAwait(false);
                DataValue[] initial = await Task.WhenAll(deniedDelivery.Task, allowedDelivery.Task)
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(initial[0].StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(initial[0].WrappedValue.IsNull, Is.True);
                AssertDatasetSecurityValue(initial[1], storage, member, revision);

                policy.DeniedSessions.TryAdd(allowedOperation.Session.Id, 0);
                revision = 2;
                deniedDelivery = new TaskCompletionSource<DataValue>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                allowedDelivery = new TaskCompletionSource<DataValue>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                monitored.Add(lateItem.Object);
                await binding.RefreshAsync(reporter).ConfigureAwait(false);
                DataValue[] updated = await Task.WhenAll(deniedDelivery.Task, allowedDelivery.Task, lateDelivery.Task)
                    .WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                foreach (DataValue rejected in updated.Take(2))
                {
                    Assert.That(rejected.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                    Assert.That(rejected.WrappedValue.IsNull, Is.True);
                }
                AssertDatasetSecurityValue(updated[2], storage, member, revision);
                Assert.That(deniedItem.Invocations.Count(value =>
                    value.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue)), Is.EqualTo(2));
                Assert.That(allowedItem.Invocations.Count(value =>
                    value.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue)), Is.EqualTo(2));
                Assert.That(lateItem.Invocations.Count(value =>
                    value.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue)), Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Authoritative refresh can notify a subscribed status without re-entering a held protocol gate.
        /// </summary>
        [Test]
        public async Task StatusMonitoringAndFileMetadataMonitoringDoNotDeadlockCommitAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await UploadAsync(fixture, "monitored", StorageEnumeration.PRODUCTION, [1], [0]).ConfigureAwait(false);
            ISystemContext caller = fixture.Provider.LastCommitRequest.SystemContext;
            Assert.That(fixture.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            await using var streaming = new StreamingSubscription(manager!, new SubscriptionOptions
            {
                PublishingInterval = TimeSpan.FromMilliseconds(50)
            });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            IAsyncEnumerator<DataValueChange> changes = streaming.SubscribeDataChangesAsync(
                fixture.Binding.Node.ActiveProductionDatasetStatus!.Modified!.NodeId, ct: deadline.Token)
                .GetAsyncEnumerator(deadline.Token);
            try
            {
                Assert.That(await changes.MoveNextAsync().ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Value.WrappedValue.TryGetValue(out bool initial), Is.True);
                Assert.That(initial, Is.False);
                fixture.Provider.Modify(StorageEnumeration.PRODUCTION, ByteString.From("\t"u8.ToArray()));
                Task<bool> pending = changes.MoveNextAsync().AsTask();
                await fixture.Binding.RefreshAsync(caller, deadline.Token).ConfigureAwait(false);
                Assert.That(await pending.WaitAsync(deadline.Token).ConfigureAwait(false), Is.True);
                Assert.That(changes.Current.Value.WrappedValue.TryGetValue(out bool modified), Is.True);
                Assert.That(modified, Is.True);

                UaTemporaryWriteFile file = await fixture.Client.OpenWriteAsync(
                    WriteOptions(StorageEnumeration.FILE_SYSTEM, "metadata-monitor"),
                        deadline.Token).ConfigureAwait(false);
                await using (file.ConfigureAwait(false))
                {
                    FileState state = fixture.Manager.FindPredefinedNode<FileState>(file.FileNodeId)!;
                    IAsyncEnumerator<DataValueChange> sizes = streaming.SubscribeDataChangesAsync(
                        state.Size!.NodeId, ct: deadline.Token).GetAsyncEnumerator(deadline.Token);
                    try
                    {
                        Assert.That(await sizes.MoveNextAsync().ConfigureAwait(false), Is.True);
                        using var content = new MemoryStream([1, 2, 3]);
                        await content.CopyToAsync(file.Stream, 64, deadline.Token).ConfigureAwait(false);
                        Assert.That((await file.CommitAsync(deadline.Token).ConfigureAwait(false)).IsNull, Is.True);
                    }
                    finally
                    {
                        await sizes.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                deadline.Cancel();
                await changes.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// All three standard request events reach real clients through generated subtype-aware decoding.
        /// </summary>
        [Test]
        public async Task RequestEventsRetainTypedFieldsAndUseConcreteSubtypesOverUaAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await fixture.Client.ReadFrozenAsync(StorageEnumeration.PRODUCTION).ConfigureAwait(false);
            ISystemContext caller = fixture.Provider.LastRequest.SystemContext;
            Assert.That(fixture.Session.TryGetSubscriptionManager(out ISubscriptionManager? manager), Is.True);
            await using var streaming = new StreamingSubscription(manager!, new SubscriptionOptions
            {
                PublishingInterval = TimeSpan.FromMilliseconds(50)
            });
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            IAsyncEnumerator<RequestProductionDatasetListEventTypeRecord> lists =
                fixture.Client.ObserveListRequestsAsync(streaming, cancellationToken: deadline.Token)
                    .GetAsyncEnumerator(deadline.Token);
            IAsyncEnumerator<RequestProductionDatasetReadEventTypeRecord> reads =
                fixture.Client.ObserveReadRequestsAsync(streaming, cancellationToken: deadline.Token)
                    .GetAsyncEnumerator(deadline.Token);
            IAsyncEnumerator<RequestProductionDatasetWriteEventTypeRecord> writes =
                fixture.Client.ObserveWriteRequestsAsync(streaming, cancellationToken: deadline.Token)
                    .GetAsyncEnumerator(deadline.Token);
            try
            {
                Task<bool> list = lists.MoveNextAsync().AsTask();
                Task<bool> read = reads.MoveNextAsync().AsTask();
                Task<bool> write = writes.MoveNextAsync().AsTask();
                while (manager!.Items.Sum(subscription => subscription.MonitoredItems.Items.Count(
                    item => item.Created && ServiceResult.IsGood(item.Error))) < 3)
                {
                    await Task.Delay(10, deadline.Token).ConfigureAwait(false);
                }
                await fixture.Binding.RequestListAsync(caller, "3*", "mould", deadline.Token).ConfigureAwait(false);
                await fixture.Binding.RequestReadAsync(
                    caller, ReadOptions(StorageEnumeration.PRODUCTION), deadline.Token).ConfigureAwait(false);
                await fixture.Binding.RequestWriteAsync(caller, new ProductionDatasetWriteOptionsType
                {
                    Name = "request",
                    Storage = StorageEnumeration.PREPARATION | StorageEnumeration.FILE_SYSTEM,
                    Components = [1, 1]
                }, deadline.Token).ConfigureAwait(false);
                await Task.WhenAll(list, read, write).WaitAsync(deadline.Token).ConfigureAwait(false);
                Assert.Multiple(() =>
                {
                    Assert.That(lists.Current.NameFilter, Is.EqualTo("3*"));
                    Assert.That(lists.Current.MouldId, Is.EqualTo("mould"));
                    Assert.That(lists.Current.SourceNode,
                        Is.EqualTo(fixture.Binding.Node.ProductionDatasetLists!.NodeId));
                    Assert.That(reads.Current.Storage, Is.EqualTo(StorageEnumeration.PRODUCTION));
                    Assert.That(reads.Current.Name, Is.Empty);
                    Assert.That(writes.Current.Name, Is.EqualTo("request"));
                    Assert.That(writes.Current.Storage,
                        Is.EqualTo(StorageEnumeration.PREPARATION | StorageEnumeration.FILE_SYSTEM));
                    Assert.That(writes.Current.Components, Is.EqualTo(new ushort[] { 1, 1 }));
                    Assert.That(writes.Current.SourceNode,
                        Is.EqualTo(fixture.Binding.Node.ProductionDatasetTransfer!.NodeId));
                    Assert.That(
                        fixture.Manager.FindPredefinedNode<BaseObjectTypeState>(writes.Current.EventType)!.IsAbstract,
                        Is.False);
                });
                fixture.Policy.DenyAll = true;
                AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
                    await fixture.Binding.RequestListAsync(caller,
                        cancellationToken: deadline.Token).ConfigureAwait(false));
            }
            finally
            {
                deadline.Cancel();
                await lists.DisposeAsync().ConfigureAwait(false);
                await reads.DisposeAsync().ConfigureAwait(false);
                await writes.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Malformed service argument lists and oversized arrays fail before provider mutation.
        /// </summary>
        [Test]
        public async Task MalformedArgumentsAndMetadataBoundsAreEnforcedBeforeProviderAccessAsync()
        {
            await using var fixture = new ProductionDatasetServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            TemporaryFileTransferState transfer = fixture.Binding.Node.ProductionDatasetTransfer!;
            ArrayOf<CallMethodRequest> requests =
            [
                new CallMethodRequest
                {
                    ObjectId = transfer.NodeId,
                    MethodId = transfer.GenerateFileForRead!.NodeId,
                    InputArguments = []
                },
                new CallMethodRequest
                {
                    ObjectId = transfer.NodeId,
                    MethodId = transfer.GenerateFileForRead.NodeId,
                    InputArguments = [Variant.From(1), Variant.From(2)]
                }
            ];
            CallResponse calls = await fixture.Session.CallAsync(null, requests, CancellationToken.None)
                .ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(calls.Results[0].StatusCode, Is.EqualTo(StatusCodes.BadArgumentsMissing));
                Assert.That(calls.Results[1].StatusCode, Is.EqualTo(StatusCodes.BadTooManyArguments));
            });
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () => await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, new string('a', 65))).ConfigureAwait(false));
            var selected = new ProductionDatasetWriteOptionsType
            {
                Name = "too-many",
                Storage = StorageEnumeration.PRODUCTION,
                Components = new ushort[17].ToArrayOf()
            };
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () =>
                await fixture.Client.OpenWriteAsync(selected).ConfigureAwait(false));
            ProductionDatasetListsTypeClient? lists = await fixture.Client.Proxy.GetProductionDatasetListsAsync(
                fixture.Session.MessageContext.Telemetry).ConfigureAwait(false);
            var oversized = new ProductionDatasetInformationType[9];
            for (int i = 0; i < oversized.Length; i++)
            {
                oversized[i] = new ProductionDatasetInformationType { Name = "dataset-" + i };
            }
            AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () =>
                await lists!.SendProductionDatasetListAsync(oversized.ToArrayOf()).ConfigureAwait(false));
            Assert.That(fixture.Provider.CommitCount, Is.Zero);
        }

        /// <summary>
        /// Factories compose with existing registrations and require no implicit identity or machine provider.
        /// </summary>
        [Test]
        public void FactoriesResolveFromDependencyInjectionAndRetainInjectedFileInfrastructure()
        {
            var services = new ServiceCollection();
            var binder = new FileTypeBinder();
            services.AddSingleton<IFileTypeBinder>(binder);
            services.AddProductionDatasets();
            services.AddProductionDatasets();
            services.AddProductionDatasetClient();
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Multiple(() =>
            {
                Assert.That(provider.GetRequiredService<IFileTypeBinder>(), Is.SameAs(binder));
                Assert.That(provider.GetRequiredService<ProductionDatasetBindingFactory>(), Is.Not.Null);
                Assert.That(provider.GetRequiredService<ProductionDatasetClientFactory>(), Is.Not.Null);
                Assert.That(provider.GetService<IProductionDatasetProvider>(), Is.Null);
                Assert.That(provider.GetService<IPlasticsRubberAccessPolicy>(), Is.Null);
            });
        }

        /// <summary>
        /// Binds generated dataset instances to explicit asynchronous policy and provider dependencies without sockets.
        /// </summary>
        private static async Task<(
            DatasetSecurityOwner Owner,
            Mock<IServerInternal> Server,
            ProductionDatasetBinding Binding)> CreateDatasetSecurityBindingAsync(
            DatasetSecurityPolicy policy,
            Func<int> revision)
        {
            var namespaces = new NamespaceTable();
            namespaces.GetIndexOrAppend("urn:dataset-security:instances");
            namespaces.GetIndexOrAppend(Di.Namespaces.OpcUaDi);
            ushort modelNamespace = namespaces.GetIndexOrAppend(Namespaces.PlasticsRubber);
            var types = new TypeTable(namespaces);
            types.AddSubtype(Ua.ObjectTypeIds.BaseObjectType, NodeId.Null);
            types.AddSubtype(Ua.ObjectTypeIds.BaseEventType, Ua.ObjectTypeIds.BaseObjectType);
            types.AddSubtype(new NodeId(ObjectTypes.RequestProductionDatasetReadEventType, modelNamespace),
                Ua.ObjectTypeIds.BaseEventType);
            types.AddSubtype(new NodeId(ObjectTypes.RequestProductionDatasetWriteEventType, modelNamespace),
                Ua.ObjectTypeIds.BaseEventType);
            var server = new Mock<IServerInternal>();
            server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
            server.SetupGet(value => value.ServerUris).Returns(new StringTable());
            server.SetupGet(value => value.TypeTree).Returns(types);
            server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
            server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
            server.SetupGet(value => value.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            var owner = new DatasetSecurityOwner(server.Object);
            try
            {
                var provider = new Mock<IProductionDatasetProvider>();
                provider.Setup(value => value.ReadStatusAsync(
                        It.IsAny<PlasticsRubberRequestContext>(), It.IsAny<StorageEnumeration>(),
                        It.IsAny<CancellationToken>()))
                    .Returns((PlasticsRubberRequestContext _, StorageEnumeration storage, CancellationToken _) =>
                        new ValueTask<ProductionDatasetStatus>(new ProductionDatasetStatus
                        {
                            Information = new ProductionDatasetInformationType
                            {
                                Name = storage + "-secret-" + revision(),
                                Description = "Confidential dataset",
                                MESId = "confidential-mes",
                                ProductName = ["confidential-product"],
                                MouldId = "confidential-mould",
                                Components = [0]
                            },
                            Modified = revision() == 1,
                            Frozen = revision() == 1
                        }));
                var parent = new FolderState(null)
                {
                    NodeId = new NodeId("DatasetSecurityRoot", 1),
                    BrowseName = new QualifiedName("DatasetSecurityRoot", 1)
                };
                ProductionDatasetManagementState node = owner.SystemContext
                    .CreateInstanceOfProductionDatasetManagementType(parent, new QualifiedName("Datasets", 1));
                ProductionDatasetBinding binding = await ProductionDatasetBinding.CreateAsync(
                    owner, node, provider.Object, policy, new ProductionDatasetOptions
                    {
                        Features = ProductionDatasetFeatures.Preparation |
                            ProductionDatasetFeatures.Modified |
                            ProductionDatasetFeatures.Frozen
                    }, timeProvider: new ProductionDatasetTestClock()).ConfigureAwait(false);
                return (owner, server, binding);
            }
            catch
            {
                await owner.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Uses real service routing and permission checks with stubbed core managers, not a listening server.
        /// </summary>
        private static MasterNodeManager CreateDatasetSecurityMaster(
            Mock<IServerInternal> server,
            DatasetSecurityOwner owner)
        {
            var configuration = new Mock<IConfigurationNodeManager>();
            var core = new Mock<ICoreNodeManager>();
            configuration.SetupGet(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            core.SetupGet(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager()).Returns(configuration.Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>())).Returns(core.Object);
            server.SetupGet(value => value.MainNodeManagerFactory).Returns(factory.Object);
            server.SetupGet(value => value.ConfigurationNodeManager).Returns(configuration.Object);
            server.SetupGet(value => value.CoreNodeManager).Returns(core.Object);
            var master = new MasterNodeManager(server.Object,
                new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() }, null, [owner]);
            server.SetupGet(value => value.NodeManager).Returns(master);
            return master;
        }

        /// <summary>
        /// Gives every actor a distinct session and an encrypted endpoint so only dataset authorization is in dispute.
        /// </summary>
        private static ServerSystemContext DatasetSecurityContext(
            Mock<IServerInternal> server,
            string actor,
            RequestType requestType)
        {
            var endpoint = new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt };
            var channel = new SecureChannelContext(actor, endpoint, RequestEncoding.Binary, null, null, null);
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId(actor, 1));
            session.SetupGet(value => value.EffectiveIdentity).Returns(new UserIdentity());
            session.SetupGet(value => value.EndpointDescription).Returns(endpoint);
            session.SetupGet(value => value.PreferredLocales).Returns([]);
            return new ServerSystemContext(server.Object, new OperationContext(
                new RequestHeader(), channel, requestType, RequestLifetime.None, session.Object));
        }

        /// <summary>
        /// Selects the actual generated property guarded by the dataset binding.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="member"/> is not a supported dataset status property.
        /// </exception>
        private static BaseVariableState DatasetSecurityVariable(
            ProductionDatasetBinding binding,
            StorageEnumeration storage,
            string member)
        {
            ProductionDatasetStatusState status = storage == StorageEnumeration.PRODUCTION
                ? binding.Node.ActiveProductionDatasetStatus! : binding.Node.ProductionDatasetInPreparationStatus!;
            return member switch
            {
                "Information" => status.Information!,
                "Modified" => status.Modified!,
                "Frozen" => status.Frozen!,
                _ => throw new ArgumentOutOfRangeException(nameof(member))
            };
        }

        /// <summary>
        /// Requests ordinary report-by-exception status monitoring through the service dispatcher.
        /// </summary>
        private static MonitoredItemCreateRequest DatasetSecurityMonitorRequest(BaseVariableState variable)
        {
            return new MonitoredItemCreateRequest
            {
                ItemToMonitor = new ReadValueId { NodeId = variable.NodeId, AttributeId = Attributes.Value },
                MonitoringMode = MonitoringMode.Reporting,
                RequestedParameters = new MonitoringParameters
                {
                    ClientHandle = 1,
                    SamplingInterval = 0,
                    QueueSize = 1,
                    DiscardOldest = true
                }
            };
        }

        /// <summary>
        /// Publishes the actual initial monitored value after associating its real owning session.
        /// </summary>
        private static void AssertDatasetSecurityInitialNotification(
            IMonitoredItem? created,
            OperationContext operation,
            StorageEnumeration storage,
            string member,
            int revision)
        {
            Assert.That(created, Is.InstanceOf<MonitoredItem>());
            var item = (MonitoredItem)created!;
            var subscription = new Mock<ISubscription>();
            subscription.SetupGet(value => value.Session).Returns(operation.Session);
            subscription.SetupGet(value => value.EffectiveIdentity).Returns(operation.UserIdentity);
            item.SubscriptionCallback = subscription.Object;
            var notifications = new Queue<MonitoredItemNotification>();
            var diagnostics = new Queue<DiagnosticInfo>();
            item.Publish(operation, notifications, diagnostics, 1, NullLogger.Instance);
            Assert.That(notifications, Has.Count.EqualTo(1));
            AssertDatasetSecurityValue(notifications.Dequeue().Value, storage, member, revision);
        }

        /// <summary>
        /// Records the actual subscriber-specific deliveries produced by MonitoredNode2.
        /// </summary>
        private static Mock<IDataChangeMonitoredItem2> DatasetSecurityItem(
            uint id,
            BaseVariableState variable,
            OperationContext operation,
            Func<TaskCompletionSource<DataValue>> delivery)
        {
            var item = new Mock<IDataChangeMonitoredItem2>();
            item.SetupGet(value => value.Id).Returns(id);
            item.SetupGet(value => value.NodeId).Returns(variable.NodeId);
            item.SetupGet(value => value.AttributeId).Returns(Attributes.Value);
            item.SetupGet(value => value.IndexRange).Returns(NumericRange.Null);
            item.SetupGet(value => value.DataEncoding).Returns(QualifiedName.Null);
            item.SetupGet(value => value.Session).Returns(operation.Session);
            item.SetupGet(value => value.EffectiveIdentity).Returns(operation.UserIdentity);
            item.Setup(value => value.QueueValue(It.Ref<DataValue>.IsAny, It.IsAny<ServiceResult>()))
                .Callback(new DatasetQueueValueCallback((in value, _) =>
                    delivery().TrySetResult(value)));
            return item;
        }

        /// <summary>
        /// Verifies both exact confidential metadata and the positive Boolean and source-quality notifications.
        /// </summary>
        private static void AssertDatasetSecurityValue(
            in DataValue value,
            StorageEnumeration storage,
            string member,
            int revision)
        {
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.SourceTimestamp, Is.EqualTo(new DateTimeUtc(2026, 1, 1, 0, 0, 0)));
            if (member == "Information")
            {
                Assert.That(value.WrappedValue.TryGetValue(out ExtensionObject extension), Is.True);
                Assert.That(extension.TryGetValue(out ProductionDatasetInformationType? information), Is.True);
                Assert.That(information!.Name, Is.EqualTo(storage + "-secret-" + revision));
                Assert.That(information.Description, Is.EqualTo("Confidential dataset"));
                Assert.That(information.MESId, Is.EqualTo("confidential-mes"));
                Assert.That(information.ProductName.ToArray(), Is.EqualTo(s_securityProducts));
                Assert.That(information.MouldId, Is.EqualTo("confidential-mould"));
            }
            else
            {
                Assert.That(value.WrappedValue.TryGetValue(out bool actual), Is.True);
                Assert.That(actual, Is.EqualTo(revision == 1));
            }
        }

        /// <summary>The exact product metadata expected in authorized status notifications.</summary>
        private static readonly string[] s_securityProducts = ["confidential-product"];

        /// <summary>
        /// An ordinary report-by-exception owner with no test-only permission overrides.
        /// </summary>
        private sealed class DatasetSecurityOwner : AsyncCustomNodeManager
        {
            /// <summary>
            /// Registers only the namespaces used by the generated test instance.
            /// </summary>
            internal DatasetSecurityOwner(IServerInternal server)
                : base(server, "urn:dataset-security:instances", Di.Namespaces.OpcUaDi, Namespaces.PlasticsRubber)
            {
            }
        }

        /// <summary>
        /// Models asynchronous per-session decisions that can change while a subscription remains alive.
        /// </summary>
        private sealed class DatasetSecurityPolicy : IPlasticsRubberAccessPolicy
        {
            /// <summary>
            /// Gets or sets a cancellable authorization gate for lifetime and request-cancellation tests.
            /// </summary>
            internal Func<PlasticsRubberRequestContext, NodeId, CancellationToken, ValueTask>? BeforeCheck { get; set; }

            /// <summary>
            /// Gets the independently revocable sessions.
            /// </summary>
            internal ConcurrentDictionary<NodeId, byte> DeniedSessions { get; } = new();

            /// <inheritdoc/>
            public async ValueTask AuthorizeAsync(
                PlasticsRubberRequestContext context,
                NodeId memberId,
                CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                if (BeforeCheck is { } beforeCheck)
                {
                    await beforeCheck(context, memberId, cancellationToken).ConfigureAwait(false);
                }
                if (context.SystemContext is ISessionSystemContext caller &&
                    DeniedSessions.ContainsKey(caller.SessionId.GetValueOrDefault()))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
            }
        }

        /// <summary>
        /// Receives a value from the real asynchronous monitored-node consumer.
        /// </summary>
        private delegate void DatasetQueueValueCallback(in DataValue value, ServiceResult? error);
    }
}
