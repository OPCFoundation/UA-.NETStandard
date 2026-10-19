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
using NUnit.Framework;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.PlasticsRubber.Client;
using Opc.Ua.PlasticsRubber.Server;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.PlasticsRubber.Datasets.Tests
{
    public sealed partial class ProductionDatasetProtocolTests
    {
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
                fixture.Provider.Modify(StorageEnumeration.PRODUCTION, ByteString.From(new byte[] { 9 }));
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
                await AssertStatus(StatusCodes.BadUserAccessDenied, async () =>
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
            var transfer = fixture.Binding.Node.ProductionDatasetTransfer!;
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
            await AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () => await fixture.Client.OpenWriteAsync(
                WriteOptions(StorageEnumeration.FILE_SYSTEM, new string('a', 65))).ConfigureAwait(false));
            var selected = new ProductionDatasetWriteOptionsType
            {
                Name = "too-many",
                Storage = StorageEnumeration.PRODUCTION,
                Components = new ushort[17].ToArrayOf()
            };
            await AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () =>
                await fixture.Client.OpenWriteAsync(selected).ConfigureAwait(false));
            ProductionDatasetListsTypeClient? lists = await fixture.Client.Proxy.GetProductionDatasetListsAsync(
                fixture.Session.MessageContext.Telemetry).ConfigureAwait(false);
            var oversized = new ProductionDatasetInformationType[9];
            for (int i = 0; i < oversized.Length; i++)
            {
                oversized[i] = new ProductionDatasetInformationType { Name = "dataset-" + i };
            }
            await AssertStatus(StatusCodes.BadEncodingLimitsExceeded, async () =>
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
    }
}
