/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Result;

namespace Opc.Ua.GMS.Tests
{
    /// <summary>
    /// Exercises the reference provider's atomic bounds and per-instance isolation.
    /// </summary>
    [TestFixture]
    [Category("GMS")]
    public sealed class GmsResultStoreTests
    {
        /// <summary>
        /// A full store refuses a new identifier without evicting a result another caller may have pinned.
        /// </summary>
        [Test]
        public async Task CapacityRefusesOverflowWithoutChangingLatestOrAnotherStore()
        {
            var limits = new GmsResultLimits { MaximumResults = 2 };
            var first = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext(), limits);
            var second = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext(), limits);
            Assert.That(await first.ReadLatestAsync(), Is.Null);
            await first.WriteAsync(Result("one"));
            Assert.That(await first.ListIdsAsync(2), Has.Count.EqualTo(1));
            await first.WriteAsync(Result("two"));

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await first.WriteAsync(Result("three"))))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That((await first.ReadLatestAsync())!.ResultId, Is.EqualTo("two"));
            Assert.That((await first.ListIdsAsync(2)).ToArray(), Is.EqualTo((string[])["two", "one"]));
            Assert.That(await second.ReadLatestAsync(), Is.Null);
            Assert.That(await first.ReadAsync("absent"), Is.Null);
            await first.WriteAsync(Result("one", 3.5));
            Assert.That((await first.ReadLatestAsync())!.ResultId, Is.EqualTo("one"));
            Assert.That(await first.RemoveAsync("two"), Is.True);
            Assert.That(await first.RemoveAsync("two"), Is.False);
            await first.WriteAsync(Result("three"));
            Assert.That((await first.ListIdsAsync(1)).ToArray(), Is.EqualTo((string[])["three"]));
        }

        /// <summary>
        /// Caller cancellation prevents state mutation; acknowledgments preserve individual domain failures.
        /// </summary>
        [Test]
        public async Task CancellationAndAcknowledgmentDoNotHideMissingResults()
        {
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext());
            await store.WriteAsync(Result("one"));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<System.OperationCanceledException>(
                async () => await store.WriteAsync(Result("two"), cancellation.Token));
            ArrayOf<int> errors = await store.AcknowledgeAsync(["one", "absent"]);
            Assert.That(errors[0], Is.Zero);
            Assert.That(errors[1], Is.EqualTo(unchecked((int)StatusCodes.BadNotFound.Code)));
            Assert.That(await store.ListIdsAsync(2), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// File bytes are bounded before mutation, including the exact maximum.
        /// </summary>
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public async Task FilePayloadHonorsTheConfiguredBoundary(int bytes, bool accepted)
        {
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext(),
                new GmsResultLimits { MaximumFileBytes = 3 });
            var result = new GmsResult(Result("one").Value, fileContent: new ByteString(new byte[bytes]));
            if (accepted)
            {
                await store.WriteAsync(result);
                Assert.That((await store.ReadAsync("one"))!.FileContent.Length, Is.EqualTo(bytes));
            }
            else
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await store.WriteAsync(result)))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadEncodingLimitsExceeded));
                Assert.That(await store.ReadLatestAsync(), Is.Null);
            }
        }

        /// <summary>
        /// The tool-count bound is independent of the number of retained results.
        /// </summary>
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, false)]
        public async Task UsedToolCountHasAnIndependentBound(int count, bool accepted)
        {
            var store = new InMemoryGmsResultStore(ModelTestContext.CreateMessageContext(),
                new GmsResultLimits { MaximumResults = 1, MaximumUsedTools = 3 });
            var tools = new string[count];
            for (int i = 0; i < tools.Length; i++)
            {
                tools[i] = "probe";
            }
            var result = new GmsResult(Result("tools").Value, tools);
            if (accepted)
            {
                await store.WriteAsync(result);
                Assert.That((await store.ReadLatestAsync())!.UsedTools, Has.Count.EqualTo(count));
            }
            else
            {
                await Assert.ThrowsAsync<ServiceResultException>(async () => await store.WriteAsync(result));
                Assert.That(await store.ReadLatestAsync(), Is.Null);
            }
        }

        /// <summary>
        /// Produces an independently known result envelope for provider tests.
        /// </summary>
        internal static GmsResult Result(string id, double value = 12.25)
        {
            return new GmsResult(new DataValue(Variant.FromStructure(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = id },
                ResultContent = [new Variant(value)]
            })));
        }
    }
}
