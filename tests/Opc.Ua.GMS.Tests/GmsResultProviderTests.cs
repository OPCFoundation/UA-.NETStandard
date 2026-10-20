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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.GMS.Client;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Tests;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Verifies provider isolation, negative response handling and asynchronous binding lifetime.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    [NonParallelizable]
    public sealed class GmsResultProviderTests
    {
        /// <summary>
        /// Missing providers fail even with every optional result capability disabled.
        /// </summary>
        [Test]
        public async Task MissingProviderCannotBorrowFromAnotherMachine()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            IMachineBuilder<GMSState> builder = fixture.Stage("MissingProvider");
            ServiceResultException error = Assert.Throws<ServiceResultException>(() => builder.WithGmsResults(null!, out _,
                new GmsResultBindingOptions { EnableFileTransfer = false, PublishedResults = 0 }))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Manager.FindPredefinedNode(builder.State.NodeId), Is.Null);
            await builder.AbortAsync();
        }

        /// <summary>
        /// A provider cannot back two active instances and a publisher cannot be attached to another owner.
        /// </summary>
        [Test]
        public async Task SharedProviderIsRejectedWithoutAffectingTheFirstMachine()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext());
            IMachineHandle<GMSState> first = await fixture.Stage("First").WithGmsResults(
                store, out GmsResultBinding binding).BuildAsync();
            IMachineBuilder<GMSState> second = fixture.Stage("Second").WithGmsResults(store, out _);
            ServiceResultException shared = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await second.BuildAsync()))!;
            Assert.That(shared.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.Manager.FindPredefinedNode(second.State.NodeId), Is.Null);
            IMachineHandle<GMSState> independent = await fixture.Stage("Independent").WithGmsResults(
                new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext()), out _).BuildAsync();
            ServiceResultException owner = Assert.Throws<ServiceResultException>(() =>
                binding.CreatePublisher(independent))!;
            Assert.That(owner.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            await binding.CreatePublisher(first).PublishAsync(GmsResultStoreTests.Result("first-only"));
            ISession session = await fixture.ConnectAsync();
            var firstClient = new GmsResultClient(session, first.NodeId, NUnitTelemetryContext.Create());
            var otherClient = new GmsResultClient(session, independent.NodeId, NUnitTelemetryContext.Create());
            Assert.That((await firstClient.GetLatestAsync()).ResultMetaData!.ResultId, Is.EqualTo("first-only"));
            GmsResultDomainException absent = (await Assert.ThrowsAsync<GmsResultDomainException>(
                async () => await otherClient.GetLatestAsync()))!;
            Assert.That(absent.Error, Is.EqualTo(unchecked((int)StatusCodes.BadNoDataAvailable.Code)));
        }

        /// <summary>
        /// Invalid provider responses fail explicitly and never become a successful empty or cross-instance result.
        /// </summary>
        [TestCase("WrongIdentity", 0x80010000u)]
        [TestCase("NullIds", 0x80010000u)]
        [TestCase("DuplicateIds", 0x80010000u)]
        [TestCase("ExcessIds", 0x80080000u)]
        [TestCase("MissingAcknowledgments", 0x80010000u)]
        [TestCase("ServiceFailure", 0x808B0000u)]
        public async Task ProviderFailuresRemainVisibleOverTcp(string mode, uint expectedCode)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            var provider = new Mock<IGmsResultStore>(MockBehavior.Strict);
            provider.Setup(value => value.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<GmsResult?>(GmsResultStoreTests.Result("wrong")));
            provider.Setup(value => value.ListIdsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<string>>(mode switch
                {
                    "DuplicateIds" => ["one", "one"],
                    "ExcessIds" => ["one", "two", "three"],
                    _ => default
                }));
            provider.Setup(value => value.AcknowledgeAsync(
                It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<ArrayOf<int>>(ArrayOf<int>.Empty));
            provider.Setup(value => value.ReadLatestAsync(It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadDeviceFailure));
            IMachineHandle<GMSState> machine = await fixture.Stage("BrokenProvider").WithGmsResults(
                provider.Object, out _, new GmsResultBindingOptions
                {
                    Limits = new GmsResultLimits { MaximumResults = 2 },
                    PublishedResults = 1
                }).BuildAsync();
            ISession session = await fixture.ConnectAsync();
            var client = new GmsResultClient(session, machine.NodeId, NUnitTelemetryContext.Create());
            ServiceResultException failure = (await Assert.ThrowsAsync<ServiceResultException>(async () =>
            {
                switch (mode)
                {
                    case "WrongIdentity":
                        await client.GetByIdAsync("requested");
                        break;
                    case "MissingAcknowledgments":
                        await client.AcknowledgeAsync(["one"]);
                        break;
                    case "ServiceFailure":
                        await client.GetLatestAsync();
                        break;
                    default:
                        await client.QueryAsync(2);
                        break;
                }
            }))!;
            Assert.That(failure.StatusCode.Code, Is.EqualTo(expectedCode));
        }

        /// <summary>
        /// A callback that ignores cancellation cannot publish a late result after its binding is disposed.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task DisposalAndCallerCancellationPreventLatePublication(bool callerCancellation)
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var provider = new Mock<IGmsResultStore>(MockBehavior.Strict);
            provider.Setup(value => value.WriteAsync(It.IsAny<GmsResult>(), It.IsAny<CancellationToken>()))
                .Returns(async (GmsResult _, CancellationToken _) =>
                {
                    started.TrySetResult(true);
                    await resume.Task.ConfigureAwait(false);
                });
            IMachineHandle<GMSState> machine = await fixture.Stage("Pending").WithGmsResults(
                provider.Object, out GmsResultBinding binding).BuildAsync();
            int events = 0;
            machine.State.ResultManagement!.OnReportEventAsync = (_, _, _, _) =>
            {
                Interlocked.Increment(ref events);
                return default;
            };
            using var cancellation = new CancellationTokenSource();
            Task publication = binding.CreatePublisher(machine).PublishAsync(
                GmsResultStoreTests.Result("late"), cancellation.Token).AsTask();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await started.Task.WaitAsync(timeout.Token);
            if (callerCancellation)
            {
                cancellation.Cancel();
            }
            else
            {
                await binding.DisposeAsync();
                binding.Dispose();
            }
            resume.TrySetResult(true);
            Assert.That(async () => await publication.WaitAsync(timeout.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(events, Is.Zero);
            var slots = new List<BaseInstanceState>();
            machine.State.ResultManagement.Results!.GetChildren(fixture.Manager.SystemContext, slots);
            Assert.That(((GMSResultState)slots[0]).StatusCode, Is.EqualTo(StatusCodes.BadWaitingForInitialData));
            if (!callerCancellation)
            {
                Assert.Throws<ServiceResultException>(() => binding.CreatePublisher(machine));
            }
        }

        /// <summary>
        /// A provider callback may end its owner without a publication gate deadlocking the callback.
        /// </summary>
        [Test]
        public async Task ProviderCallbackCanDisposeItsBindingWithoutDeadlock()
        {
            await using var fixture = new GmsResultServerFixture();
            await fixture.StartAsync();
            var provider = new Mock<IGmsResultStore>(MockBehavior.Strict);
            GmsResultBinding? owner = null;
            provider.Setup(value => value.WriteAsync(It.IsAny<GmsResult>(), It.IsAny<CancellationToken>()))
                .Returns((GmsResult _, CancellationToken _) =>
                {
                    owner!.Dispose();
                    return default;
                });
            IMachineHandle<GMSState> machine = await fixture.Stage("ReentrantProvider")
                .WithGmsResults(provider.Object, out GmsResultBinding binding).BuildAsync();
            owner = binding;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Assert.That(async () => await binding.CreatePublisher(machine).PublishAsync(
                GmsResultStoreTests.Result("never-visible")).AsTask().WaitAsync(timeout.Token),
                Throws.InstanceOf<OperationCanceledException>());
        }
    }
}
