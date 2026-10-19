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

#if NET10_0
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpPubSub
{
    /// <summary>
    /// Named ownership, quota, stopped-state and explicit publication behavior over real applications.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class PubSubExpansionRuntimeTests
    {
        [Test]
        public async Task CreateStatusAndValidateDoNotActivateAndDuplicateNameDoesNotReplaceAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            PubSubApplicationInput input = PubSubExpansionTestContext.Input();

            JsonObject created = await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false);
            string handle = created["handle"]!.GetValue<string>();
            JsonObject state = await fixture.Runtimes.StatusAsync(handle).ConfigureAwait(false);
            ServiceResultException? duplicate = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.CreateAsync(input).ConfigureAwait(false));

            Assert.That(state["isRunning"]!.GetValue<bool>(), Is.False);
            Assert.That(state["state"]!.GetValue<string>(), Is.EqualTo("Stopped"));
            Assert.That(fixture.Transports.Factories, Is.Zero);
            Assert.That(duplicate!.StatusCode, Is.EqualTo(StatusCodes.BadEntryExists));
            Assert.That(fixture.Registry.List().ToList().Single().Handle, Is.EqualTo(handle));
            Assert.That(created["legacyDefaultReplaced"]!.GetValue<bool>(), Is.False);
        }

        [Test]
        public async Task QuotaAdmissionPrecedesFactoriesAndRemovalReleasesCapacityAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            JsonObject first = await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input("one"))
                .ConfigureAwait(false);
            _ = await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input("two")).ConfigureAwait(false);

            ServiceResultException? refused = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input("three")).ConfigureAwait(false));
            Assert.That(refused!.StatusCode, Is.EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(2));
            Assert.That(fixture.Transports.Factories, Is.Zero);

            _ = await fixture.Runtimes.RemoveAsync(first["handle"]!.GetValue<string>()).ConfigureAwait(false);
            JsonObject third = await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input("three"))
                .ConfigureAwait(false);

            Assert.That(fixture.Registry.List().ToList().Select(info => info.Name),
                Is.EquivalentTo(s_remainingRuntimeNames));
            Assert.That(third["handle"]!.GetValue<string>(), Is.Not.EqualTo(first["handle"]!.GetValue<string>()));
        }

        [Test]
        public async Task SameNameAcrossCallersIsIsolatedAndHandlesCannotBeBorrowedAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            string firstHandle;
            using (fixture.Enter())
            {
                firstHandle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                    .ConfigureAwait(false))["handle"]!.GetValue<string>();
            }
            using (fixture.Enter(PubSubExpansionTestContext.Other))
            {
                string secondHandle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                    .ConfigureAwait(false))["handle"]!.GetValue<string>();
                ServiceResultException? refused = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await fixture.Runtimes.StatusAsync(firstHandle).ConfigureAwait(false));

                Assert.That(refused!.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(secondHandle, Is.Not.EqualTo(firstHandle));
                Assert.That(fixture.Registry.List().ToList().Single().Handle, Is.EqualTo(secondHandle));
            }
        }

        [Test]
        public async Task StartPublishStopAndRemoveHaveDistinctStateEffectsAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string handle = (await fixture.Runtimes.CreateAsync(PubSubExpansionTestContext.Input())
                .ConfigureAwait(false))
                ["handle"]!.GetValue<string>();

            JsonObject started = await fixture.Runtimes.StartAsync(handle).ConfigureAwait(false);
            JsonObject update = await fixture.Runtimes.PublishAsync(handle, new PubSubPublishInput
            {
                Fields = [new PubSubFieldValueInput
                {
                    Name = "Count", Value = PubSubExpansionTestContext.Element("23")
                }]
            }).ConfigureAwait(false);
            JsonObject stopped = await fixture.Runtimes.StopAsync(handle).ConfigureAwait(false);

            Assert.That(started["isRunning"]!.GetValue<bool>(), Is.True);
            Assert.That(update["updatedFields"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(update["state"]!.GetValue<string>(), Is.EqualTo("sample-updated"));
            Assert.That(stopped["isRunning"]!.GetValue<bool>(), Is.False);
            Assert.That(fixture.Registry.List(), Has.Count.EqualTo(1));
            Assert.That(fixture.Transports.Created.Single().DisposeCalls, Is.EqualTo(1));
            ServiceResultException? stoppedWrite = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.PublishAsync(handle, new PubSubPublishInput { Fields = [] })
                    .ConfigureAwait(false));
            Assert.That(stoppedWrite!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));

            _ = await fixture.Runtimes.RemoveAsync(handle).ConfigureAwait(false);
            Assert.That(fixture.Registry.List(), Is.Empty);
        }

        [Test]
        public async Task StoppedSubscriberReadIsExplicitAndPublisherReadIsRefusedAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            string subscriber = (await fixture.Runtimes.CreateAsync(
                PubSubExpansionTestContext.Input("subscriber", "Subscriber")).ConfigureAwait(false))
                ["handle"]!.GetValue<string>();
            string publisher = (await fixture.Runtimes.CreateAsync(
                PubSubExpansionTestContext.Input("publisher")).ConfigureAwait(false))["handle"]!.GetValue<string>();

            JsonObject read = await fixture.Runtimes.ReadAsync(subscriber).ConfigureAwait(false);
            ServiceResultException? invalid = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await fixture.Runtimes.ReadAsync(publisher).ConfigureAwait(false));

            Assert.That(read["running"]!.GetValue<bool>(), Is.False);
            Assert.That(read["dataSets"]!.AsArray(), Is.Empty);
            Assert.That(read["bufferedBytes"]!.GetValue<int>(), Is.Zero);
            Assert.That(invalid!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(fixture.Transports.Factories, Is.Zero);
        }

        [Test]
        public async Task LegacyOwnerAndUntilStopLifetimeRemainSeparateFromNamedLeasesAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            PubSubRuntimeManager legacy = fixture.Services.GetRequiredService<PubSubRuntimeManager>();
            using (fixture.Enter())
            {
                _ = await legacy.StartPublisherAsync("opc.udp://127.0.0.1:4849", 42, 1, "Count:Int32")
                    .ConfigureAwait(false);
                fixture.Clock.Advance(TimeSpan.FromHours(1));
                Assert.That((await legacy.StatusAsync().ConfigureAwait(false)).IsRunning, Is.True);
                Assert.That(fixture.Registry.List(), Is.Empty);
            }

            using (fixture.Enter(PubSubExpansionTestContext.Other))
            {
                ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                    await legacy.StartPublisherAsync("opc.udp://127.0.0.1:4849", 7, 1, "Other:Int32")
                        .ConfigureAwait(false));
                Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            }
            using (fixture.Enter())
            {
                PubSubRuntimeStatus status = await legacy.StatusAsync().ConfigureAwait(false);
                Assert.That(status.IsRunning, Is.True);
                Assert.That(status.PublisherId, Is.EqualTo(42));
                Assert.That((await legacy.StopAsync().ConfigureAwait(false)).IsRunning, Is.False);
            }
        }

        [Test]
        public async Task LegacyExecutionRemainsDefaultDeniedEvenForAKnownCallerAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync(
                enabled: false).ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            PubSubRuntimeManager legacy = fixture.Services.GetRequiredService<PubSubRuntimeManager>();

            ServiceResultException? rejected = await Assert.ThrowsAsync<ServiceResultException>(async () =>
                await legacy.StartPublisherAsync("opc.udp://127.0.0.1:4849", 42, 1, "Count:Int32")
                    .ConfigureAwait(false));

            Assert.That(rejected!.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Transports.Factories, Is.Zero);
            Assert.That((await legacy.StatusAsync().ConfigureAwait(false)).IsRunning, Is.False);
        }

        [Test]
        public async Task LegacyPublishPreservesSubsequentUpdatesStatusAndExplicitStopAsync()
        {
            await using PubSubExpansionTestContext fixture = await PubSubExpansionTestContext.CreateAsync()
                .ConfigureAwait(false);
            using IDisposable caller = fixture.Enter();
            PubSubRuntimeManager legacy = fixture.Services.GetRequiredService<PubSubRuntimeManager>();
            _ = await legacy.StartPublisherAsync("opc.udp://127.0.0.1:4849", 42, 1, "Count:Int32")
                .ConfigureAwait(false);

            PubSubRuntimePublishResult first = await legacy.PublishAsync("Count=5").ConfigureAwait(false);
            PubSubRuntimePublishResult second = await legacy.PublishAsync("Count=9").ConfigureAwait(false);
            PubSubRuntimeStatus active = await legacy.StatusAsync().ConfigureAwait(false);
            PubSubRuntimeStatus stopped = await legacy.StopAsync().ConfigureAwait(false);

            Assert.That(first.Fields[0].Value, Is.EqualTo("5"));
            Assert.That(second.Fields[0].Value, Is.EqualTo("9"));
            Assert.That(active.IsRunning, Is.True);
            Assert.That(stopped.IsRunning, Is.False);
            Assert.That(fixture.Transports.Factories, Is.EqualTo(1));
            Assert.That(fixture.Transports.Created.Single().DisposeCalls, Is.EqualTo(1));
        }

        private static readonly string[] s_remainingRuntimeNames = ["two", "three"];
    }
}
#endif
