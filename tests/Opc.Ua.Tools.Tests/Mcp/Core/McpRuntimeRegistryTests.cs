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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpRuntime
{
    /// <summary>
    /// Pins admission, caller authority, opaque handle boundaries, and public runtime metadata.
    /// </summary>
    [TestFixture]
    [Category("Mcp")]
    public sealed class McpRuntimeRegistryTests
    {
        /// <summary>
        /// Inspection is local metadata work, not activation or an invocation on the owned resource.
        /// </summary>
        [Test]
        public async Task ConstructionAndListAreLazyAndMetadataDoesNotInvokeRuntimeAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();

            Assert.That(context.Registry.List().Count, Is.Zero);
            Assert.That(context.FactoryCalls, Is.Zero);
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            McpRuntimeInfo opened = await context.OpenAsync("metadata-only", runtime).ConfigureAwait(false);
            McpRuntimeInfo listed = context.Registry.List().ToArray()!.Single();

            Assert.That(listed, Is.EqualTo(opened));
            Assert.That(listed.Name, Is.EqualTo("metadata-only"));
            Assert.That(listed.Capability, Is.EqualTo(McpCapability.WotRuntime));
            Assert.That(listed.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(listed.CreatedAt, Is.EqualTo(McpRuntimeTestContext.Epoch));
            Assert.That(listed.IdleExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMinutes(5)));
            Assert.That(listed.ExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMinutes(30)));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(context.Clock.CreatedTimers, Is.EqualTo(2));
            Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
            Assert.That(runtime.BusinessCalls, Is.Zero);
            Assert.That(runtime.CommittedValue, Is.EqualTo(10));
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(
                typeof(McpRuntimeInfo).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Select(property => property.Name),
                Is.EquivalentTo(s_publicInfoPropertyNames));
            Assert.That(listed.ToString(), Does.Not.Contain(McpRuntimeTestContext.Owner.Subject));
            Assert.That(listed.ToString(), Does.Not.Contain(McpRuntimeTestContext.Owner.Issuer));
            Assert.That(listed.ToString(), Does.Not.Contain(nameof(McpTestRuntime)));
            Assert.That(context.Registry.List().ToArray()!.Single(), Is.EqualTo(listed));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(runtime.BusinessCalls, Is.Zero);
        }

        /// <summary>
        /// Constructor guards reject the exact missing collaborator without starting background work.
        /// </summary>
        [TestCase("policy")]
        [TestCase("options")]
        [TestCase("timeProvider")]
        [TestCase("telemetry")]
        public async Task ConstructorRejectsNullCollaboratorsAsync(string parameter)
        {
            await using var context = new McpRuntimeTestContext();
            Assert.That(
                () => new McpRuntimeRegistry(
                    parameter == "policy" ? null! : context.Policy,
                    parameter == "options" ? null! : context.Options,
                    parameter == "timeProvider" ? null! : context.Clock,
                    parameter == "telemetry" ? null! : McpRuntimeTestContext.CreateTelemetry()),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo(parameter));
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            Assert.That(context.FactoryCalls, Is.Zero);
        }

        /// <summary>
        /// Null, empty, and whitespace names never consume capacity or invoke the factory.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public async Task OpenRejectsInvalidNamesBeforeActivationAsync(string? name)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync(name!, runtime).AsTask()).ConfigureAwait(false);

            Assert.That(failure, Is.InstanceOf<ArgumentException>().With.Property("ParamName").EqualTo("name"));
            Assert.That(context.Registry.List().Count, Is.Zero);
            Assert.That(context.FactoryCalls, Is.Zero);
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.Zero);
        }

        /// <summary>
        /// The longest supported name works; the immediately longer name cannot activate anything.
        /// </summary>
        [Test]
        public async Task OpenNameHasAnExactLengthBoundaryAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var first = new McpTestRuntime();
            var refused = new McpTestRuntime();
            McpRuntimeInfo accepted = await context.OpenAsync(new string('n', 128), first).ConfigureAwait(false);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync(new string('n', 129), refused).AsTask()).ConfigureAwait(false);

            Assert.That(accepted.Name, Is.EqualTo(new string('n', 128)));
            Assert.That(failure, Is.TypeOf<ArgumentOutOfRangeException>()
                .With.Property("ParamName").EqualTo("name.Length"));
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(accepted.Handle));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(refused.DisposeCalls, Is.Zero);
        }

        /// <summary>
        /// Invalid factories and already cancelled calls are refused before quota or timer reservation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidFactoryOrPreCancellationDoesNotReserveQuotaAsync(bool preCancelled)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            var runtime = new McpTestRuntime();
            Task<McpRuntimeInfo> opening = preCancelled
                ? context.OpenAsync("cancelled", runtime, ct: cancellation.Token).AsTask()
                : context.Registry.OpenAsync<McpTestRuntime>("null", McpCapability.WotRuntime, null!).AsTask();
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);

            if (preCancelled)
            {
                Assert.That(failure, Is.InstanceOf<OperationCanceledException>()
                    .With.Property("CancellationToken").EqualTo(cancellation.Token));
            }
            else
            {
                Assert.That(failure, Is.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("factory"));
            }
            Assert.That(context.Registry.List().Count, Is.Zero);
            Assert.That(context.FactoryCalls, Is.Zero);
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.Zero);
        }

        /// <summary>
        /// The default caller can own exactly two resources and regain precisely a released slot.
        /// </summary>
        [Test]
        public async Task DefaultCallerQuotaRejectsThirdAndRecoversAfterCloseAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var first = new McpTestRuntime();
            var second = new McpTestRuntime();
            var third = new McpTestRuntime();
            McpRuntimeInfo firstInfo = await context.OpenAsync("first", first).ConfigureAwait(false);
            McpRuntimeInfo secondInfo = await context.OpenAsync("second", second).ConfigureAwait(false);
            Exception? refusal = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("third", third).AsTask()).ConfigureAwait(false);

            Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(context.Registry.List().ToArray()!.Select(info => info.Handle),
                Is.EquivalentTo([firstInfo.Handle, secondInfo.Handle]));
            Assert.That(context.FactoryCalls, Is.EqualTo(2));
            Assert.That(context.Clock.CreatedTimers, Is.EqualTo(3));
            Assert.That(context.Clock.ActiveTimers, Is.EqualTo(1));
            Assert.That(third.DisposeCalls, Is.Zero);

            await McpRuntimeTestContext.AwaitAsync(
                context.Registry.CloseAsync(firstInfo.Handle).AsTask()).ConfigureAwait(false);
            McpRuntimeInfo replacement = await context.OpenAsync("third", third).ConfigureAwait(false);
            Assert.That(context.Registry.List().ToArray()!.Select(info => info.Handle),
                Is.EquivalentTo([secondInfo.Handle, replacement.Handle]));
            Assert.That(context.FactoryCalls, Is.EqualTo(3));
            Assert.That(first.DisposeCalls, Is.EqualTo(1));
            Assert.That(second.DisposeCalls, Is.Zero);
            Assert.That(third.DisposeCalls, Is.Zero);
        }

        /// <summary>
        /// Different authorized owners cannot collectively activate a ninth default host resource.
        /// </summary>
        [Test]
        public async Task DefaultHostQuotaRejectsNinthAcrossOwnersAsync()
        {
            await using var context = new McpRuntimeTestContext();
            var runtimes = new List<McpTestRuntime>();
            var handles = new List<string>();
            for (int ownerIndex = 0; ownerIndex < 4; ownerIndex++)
            {
                using IDisposable caller = context.Callers.EnterTrustedCaller(McpRuntimeTestContext.Owners[ownerIndex]);
                for (int nameIndex = 0; nameIndex < 2; nameIndex++)
                {
                    var runtime = new McpTestRuntime();
                    runtimes.Add(runtime);
                    McpRuntimeInfo info = await context.OpenAsync($"runtime-{nameIndex}", runtime)
                        .ConfigureAwait(false);
                    handles.Add(info.Handle);
                }
                Assert.That(context.Registry.List().Count, Is.EqualTo(2));
            }
            var ninth = new McpTestRuntime();
            using (context.Callers.EnterTrustedCaller(McpRuntimeTestContext.Owners[4]))
            {
                Exception? refusal = await McpRuntimeTestContext.CaptureAsync(
                    context.OpenAsync("ninth", ninth).AsTask()).ConfigureAwait(false);
                Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(context.Registry.List().Count, Is.Zero);
            }
            Assert.That(handles.Distinct().Count(), Is.EqualTo(8));
            Assert.That(context.FactoryCalls, Is.EqualTo(8));
            Assert.That(ninth.DisposeCalls, Is.Zero);

            using (context.EnterOwner())
            {
                await McpRuntimeTestContext.AwaitAsync(
                    context.Registry.CloseAsync(handles[0]).AsTask()).ConfigureAwait(false);
            }
            using (context.Callers.EnterTrustedCaller(McpRuntimeTestContext.Owners[4]))
            {
                McpRuntimeInfo admitted = await context.OpenAsync("ninth", ninth).ConfigureAwait(false);
                Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(admitted.Handle));
            }
            Assert.That(context.FactoryCalls, Is.EqualTo(9));
            Assert.That(runtimes[0].DisposeCalls, Is.EqualTo(1));
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Assert.That(runtimes.Select(runtime => runtime.DisposeCalls), Is.All.EqualTo(1));
            Assert.That(ninth.DisposeCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Admission is reserved before awaiting activation, both for the caller and the entire host.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task PendingActivationConsumesCallerAndHostQuotaAsync(bool callerQuota)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = callerQuota ? 2 : 1;
                options.MaxRuntimesPerCaller = 1;
            });
            using IDisposable owner = context.EnterOwner();
            var runtime = new McpTestRuntime();
            var refusedRuntime = new McpTestRuntime();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "pending", _ => new ValueTask<McpTestRuntime>(activation.Task)).AsTask();
            try
            {
                Assert.That(opening.IsCompleted, Is.False);
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Opening));
                using IDisposable caller = context.Callers.EnterTrustedCaller(callerQuota
                    ? McpRuntimeTestContext.Owner
                    : McpRuntimeTestContext.Owners[1]);
                Exception? refusal = await McpRuntimeTestContext.CaptureAsync(
                    context.OpenAsync("adjacent", refusedRuntime).AsTask()).ConfigureAwait(false);
                Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
                Assert.That(context.FactoryCalls, Is.EqualTo(1));
                Assert.That(runtime.DisposeCalls, Is.Zero);
                Assert.That(refusedRuntime.DisposeCalls, Is.Zero);
            }
            finally
            {
                activation.TrySetResult(runtime);
                await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
            }
            Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Ready));
        }

        /// <summary>
        /// A public opening snapshot does not grant a borrow until the factory has returned a runtime.
        /// </summary>
        [Test]
        public async Task OpeningEntryCannotBeAcquiredBeforeActivationAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "opening", _ => new ValueTask<McpTestRuntime>(activation.Task)).AsTask();
            string handle = context.Registry.List().ToArray()!.Single().Handle;
            try
            {
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpTestRuntime>(handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadTypeMismatch));
                Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Opening));
                Assert.That(runtime.BusinessCalls, Is.Zero);
                Assert.That(runtime.DisposeCalls, Is.Zero);
            }
            finally
            {
                activation.TrySetResult(runtime);
                await McpRuntimeTestContext.CaptureAsync(opening).ConfigureAwait(false);
            }
            McpRuntimeInfo ready = await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
            await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                .AcquireAsync<McpTestRuntime>(ready.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            Assert.That(ready.Handle, Is.EqualTo(handle));
            Assert.That(ready.State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(use.Runtime, Is.SameAs(runtime));
        }

        /// <summary>
        /// Names are reserved throughout opening, readiness, and closing, even across runtime capabilities.
        /// </summary>
        [TestCase(McpRuntimeState.Opening)]
        [TestCase(McpRuntimeState.Ready)]
        [TestCase(McpRuntimeState.Closing)]
        public async Task SameNameCannotReplaceOpeningReadyOrClosingRuntimeAsync(McpRuntimeState state)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            TaskCompletionSource<bool> dispose = McpRuntimeTestContext.Completion<bool>();
            TaskCompletionSource<McpTestRuntime> activation = McpRuntimeTestContext.Completion<McpTestRuntime>();
            var original = new McpTestRuntime(dispose.Task);
            var replacement = new McpTestRuntime();
            if (state != McpRuntimeState.Opening)
            {
                activation.SetResult(original);
            }
            Task<McpRuntimeInfo> opening = context.OpenAsync(
                "reserved", _ => new ValueTask<McpTestRuntime>(activation.Task)).AsTask();
            Task? closing = null;
            try
            {
                if (state != McpRuntimeState.Opening)
                {
                    await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
                }
                string handle = context.Registry.List().ToArray()!.Single().Handle;
                if (state == McpRuntimeState.Closing)
                {
                    closing = context.Registry.CloseAsync(handle).AsTask();
                    await McpRuntimeTestContext.AwaitAsync(original.DisposalStarted.Task).ConfigureAwait(false);
                }
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(
                    context.OpenAsync("reserved", replacement, McpCapability.PubSubRuntime).AsTask())
                    .ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadEntryExists));
                McpRuntimeInfo remaining = context.Registry.List().ToArray()!.Single();
                Assert.That(remaining.Handle, Is.EqualTo(handle));
                Assert.That(remaining.State, Is.EqualTo(state));
                Assert.That(remaining.Capability, Is.EqualTo(McpCapability.WotRuntime));
                Assert.That(context.FactoryCalls, Is.EqualTo(1));
                Assert.That(replacement.DisposeCalls, Is.Zero);
                if (state == McpRuntimeState.Ready)
                {
                    await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                        .AcquireAsync<McpTestRuntime>(handle, McpCapability.WotRuntime).ConfigureAwait(false);
                    Assert.That(use.Runtime, Is.SameAs(original));
                }
            }
            finally
            {
                activation.TrySetResult(original);
                dispose.TrySetResult(true);
                await McpRuntimeTestContext.AwaitAsync(opening).ConfigureAwait(false);
                if (closing is not null)
                {
                    await McpRuntimeTestContext.AwaitAsync(closing).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Ordinal names may differ in case or owner, but none can be used as an access handle.
        /// </summary>
        [Test]
        public async Task NamesAreOrdinalCallerScopedAndNotHandlesAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            McpRuntimeInfo lower = await context.OpenAsync("named", new McpTestRuntime()).ConfigureAwait(false);
            McpRuntimeInfo upper = await context.OpenAsync("Named", new McpTestRuntime()).ConfigureAwait(false);
            Assert.That(context.Registry.List().ToArray()!.Select(info => info.Name), Is.EquivalentTo(s_ordinalNames));
            Exception? refusal = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>("named", McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
            using (context.Callers.EnterTrustedCaller(McpRuntimeTestContext.Owners[1]))
            {
                McpRuntimeInfo foreign = await context.OpenAsync("named", new McpTestRuntime()).ConfigureAwait(false);
                Assert.That(new[] { lower.Handle, upper.Handle, foreign.Handle }, Is.Unique);
                Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(foreign.Handle));
            }
            Assert.That(context.FactoryCalls, Is.EqualTo(3));
            Assert.That(context.Registry.List().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// Handles reveal neither names nor identities, and cannot resume across hosts or generations.
        /// </summary>
        [Test]
        public async Task HandlesAreOpaqueHostBoundAndNotReusedAfterReopenAsync()
        {
            await using var context = new McpRuntimeTestContext();
            await using var otherHost = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            using IDisposable otherCaller = otherHost.EnterOwner();
            var original = new McpTestRuntime();
            McpRuntimeInfo first = await context.OpenAsync("caller-visible-name", original).ConfigureAwait(false);
            Assert.That(first.Handle, Is.Not.Empty);
            Assert.That(first.Handle, Does.Not.Contain(first.Name));
            Assert.That(first.Handle, Does.Not.Contain(McpRuntimeTestContext.Owner.Subject));
            Assert.That(first.Handle, Does.Not.Contain(McpRuntimeTestContext.Owner.Issuer));
            Exception? foreign = await McpRuntimeTestContext.CaptureAsync(otherHost.Registry
                .AcquireAsync<McpTestRuntime>(first.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Exception? foreignClose = await McpRuntimeTestContext.CaptureAsync(
                otherHost.Registry.CloseAsync(first.Handle).AsTask()).ConfigureAwait(false);
            Assert.That(foreign, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(foreignClose, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(otherHost.Registry.List().Count, Is.Zero);
            Assert.That(otherHost.FactoryCalls, Is.Zero);
            Assert.That(original.DisposeCalls, Is.Zero);

            await McpRuntimeTestContext.AwaitAsync(
                context.Registry.CloseAsync(first.Handle).AsTask()).ConfigureAwait(false);
            McpRuntimeInfo second = await context.OpenAsync(first.Name, new McpTestRuntime()).ConfigureAwait(false);
            Exception? stale = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(first.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(second.Handle, Is.Not.EqualTo(first.Handle));
            Assert.That(stale, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(second.Handle));
            Assert.That(original.DisposeCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// A granted but different identity learns no more from a real foreign handle than a missing handle.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public async Task ForeignHandlesDoNotDiscloseRuntimeKindAcrossIdentityBoundariesAsync(int foreignOwner)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable owner = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("private-runtime", runtime).ConfigureAwait(false);
            using (context.Callers.EnterTrustedCaller(McpRuntimeTestContext.Owners[foreignOwner]))
            {
                Exception? knownKind = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask())
                    .ConfigureAwait(false);
                Exception? otherKind = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpOtherTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask())
                    .ConfigureAwait(false);
                Exception? missing = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpTestRuntime>("absent-handle", McpCapability.WotRuntime).AsTask())
                    .ConfigureAwait(false);
                Exception? close = await McpRuntimeTestContext.CaptureAsync(
                    context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);

                foreach (Exception? refusal in new[] { knownKind, otherKind, missing, close })
                {
                    Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                        .With.Property("StatusCode").EqualTo(StatusCodes.BadNodeIdUnknown));
                    Assert.That(refusal!.Message, Is.EqualTo(missing!.Message));
                    Assert.That(refusal.Message, Does.Not.Contain(info.Name));
                    Assert.That(refusal.Message, Does.Not.Contain(nameof(McpTestRuntime)));
                }
                Assert.That(context.Registry.List().Count, Is.Zero);
                Assert.That(runtime.DisposeCalls, Is.Zero);
                Assert.That(runtime.BusinessCalls, Is.Zero);
            }
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(info.Handle));
            await using McpRuntimeUse<McpTestRuntime> owned = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            Assert.That(owned.Runtime, Is.SameAs(runtime));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// An equivalent identity in a new request has the same ownership authority, without reference identity.
        /// </summary>
        [Test]
        public async Task EquivalentCallerRecordCanBorrowListAndCloseItsRuntimeAsync()
        {
            await using var context = new McpRuntimeTestContext();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info;
            using (context.EnterOwner())
            {
                info = await context.OpenAsync("new-request", runtime).ConfigureAwait(false);
            }
            var equivalent = new McpCallerIdentity("runtime-owner", "urn:test:authority");
            using (context.Callers.EnterTrustedCaller(equivalent))
            {
                Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(info.Handle));
                await using (McpRuntimeUse<McpTestRuntime> use = await context.Registry
                    .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false))
                {
                    Assert.That(use.Runtime, Is.SameAs(runtime));
                }
                await McpRuntimeTestContext.AwaitAsync(
                    context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);
                Assert.That(context.Registry.List().Count, Is.Zero);
            }
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Type rejection does not consume an operation slot; a compatible interface still borrows the same object.
        /// </summary>
        [Test]
        public async Task OwnedHandleRequiresTheRequestedRuntimeKindAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
                options.MaxConcurrentOperationsPerRuntime = 1);
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("typed", runtime).ConfigureAwait(false);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpOtherTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask())
                .ConfigureAwait(false);
            Assert.That(failure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadTypeMismatch));
            await using McpRuntimeUse<IAsyncDisposable> use = await McpRuntimeTestContext.AwaitAsync(context.Registry
                .AcquireAsync<IAsyncDisposable>(info.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(use.Runtime, Is.SameAs(runtime));
            Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(runtime.DisposeCalls, Is.Zero);

            var otherRuntime = new McpOtherTestRuntime();
            McpRuntimeInfo otherInfo = await context.Registry.OpenAsync(
                "other-kind", McpCapability.PubSubRuntime, _ => ValueTask.FromResult(otherRuntime))
                .ConfigureAwait(false);
            await using (McpRuntimeUse<McpOtherTestRuntime> otherUse =
                await McpRuntimeTestContext.AwaitAsync(context.Registry
                    .AcquireAsync<McpOtherTestRuntime>(otherInfo.Handle, McpCapability.PubSubRuntime).AsTask())
                    .ConfigureAwait(false))
            {
                Assert.That(otherUse.Runtime, Is.SameAs(otherRuntime));
            }
            Assert.That(otherRuntime.DisposeCalls, Is.Zero);
            await McpRuntimeTestContext.AwaitAsync(
                context.Registry.CloseAsync(otherInfo.Handle).AsTask()).ConfigureAwait(false);
            Assert.That(otherRuntime.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(info.Handle));
        }

        /// <summary>
        /// A pre-cancelled borrow neither renews idle expiry nor takes the only available operation permit.
        /// </summary>
        [Test]
        public async Task PreCancelledAcquireDoesNotRefreshIdleLeaseOrConsumePermitAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxConcurrentOperationsPerRuntime = 1;
                options.RuntimeIdleTimeout = TimeSpan.FromMilliseconds(200);
                options.RuntimeLeaseTimeout = TimeSpan.FromMilliseconds(800);
            });
            using IDisposable caller = context.EnterOwner();
            using var cancellation = new CancellationTokenSource();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo initial = await context.OpenAsync("cancelled-borrow", runtime).ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await cancellation.CancelAsync().ConfigureAwait(false);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(initial.Handle, McpCapability.WotRuntime, cancellation.Token).AsTask())
                .ConfigureAwait(false);
            Assert.That(failure, Is.InstanceOf<OperationCanceledException>()
                .With.Property("CancellationToken").EqualTo(cancellation.Token));
            Assert.That(context.Registry.List().ToArray()!.Single().IdleExpiresAt, Is.EqualTo(initial.IdleExpiresAt));
            Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Ready));
            Assert.That(context.Clock.CreatedTimers, Is.EqualTo(2));
            await using McpRuntimeUse<McpTestRuntime> use = await McpRuntimeTestContext.AwaitAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(initial.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(use.Runtime, Is.SameAs(runtime));
            Assert.That(runtime.DisposeCalls, Is.Zero);
        }

        /// <summary>
        /// Neither mutation permission nor continued runtime permission substitutes for the other.
        /// </summary>
        [TestCase(McpCapability.WotWrite, false, true)]
        [TestCase(McpCapability.WotWrite, true, false)]
        [TestCase(McpCapability.WotWrite, true, true)]
        [TestCase(McpCapability.WotInvoke, false, true)]
        [TestCase(McpCapability.WotInvoke, true, false)]
        [TestCase(McpCapability.WotInvoke, true, true)]
        public async Task AcquireRequiresRequestedAndOriginalCapabilitiesAsync(
            McpCapability required, bool operationGranted, bool runtimeGranted)
        {
            var grants = new HashSet<McpCapability> { McpCapability.WotRuntime };
            Mock<IMcpOperationPolicy> policy = McpRuntimeTestContext.MutablePolicy(grants);
            await using var context = new McpRuntimeTestContext(
                options => options.MaxConcurrentOperationsPerRuntime = 1, policy.Object);
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("dual-authority", runtime).ConfigureAwait(false);
            grants.Clear();
            if (operationGranted)
            {
                grants.Add(required);
            }
            if (runtimeGranted)
            {
                grants.Add(McpCapability.WotRuntime);
            }
            if (operationGranted && runtimeGranted)
            {
                await using McpRuntimeUse<McpTestRuntime> use = await context.Registry
                    .AcquireAsync<McpTestRuntime>(info.Handle, required).ConfigureAwait(false);
                Assert.That(use.Runtime, Is.SameAs(runtime));
                Assert.That(use.CancellationToken.IsCancellationRequested, Is.False);
            }
            else
            {
                Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                    .AcquireAsync<McpTestRuntime>(info.Handle, required).AsTask()).ConfigureAwait(false);
                Assert.That(failure, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadUserAccessDenied));
            }
            policy.Verify(value => value.Require(required), Times.Once);
            policy.Verify(value => value.Require(McpCapability.WotRuntime),
                Times.Exactly(operationGranted ? 2 : 1));
            Assert.That(runtime.BusinessCalls, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(context.Registry.List().ToArray()!.Single().State, Is.EqualTo(McpRuntimeState.Ready));
            grants.Add(McpCapability.WotRuntime);
            await using McpRuntimeUse<McpTestRuntime> probe = await McpRuntimeTestContext.AwaitAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(probe.Runtime, Is.SameAs(runtime));
        }

        /// <summary>
        /// A denied secondary authorization must not keep an otherwise idle runtime alive.
        /// </summary>
        [TestCase(McpCapability.WotWrite)]
        [TestCase(McpCapability.WotInvoke)]
        public async Task DeniedCapabilityDoesNotRefreshIdleLeaseAsync(McpCapability requested)
        {
            var grants = new HashSet<McpCapability> { McpCapability.WotRuntime };
            Mock<IMcpOperationPolicy> policy = McpRuntimeTestContext.MutablePolicy(grants);
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.RuntimeIdleTimeout = TimeSpan.FromMilliseconds(200);
                options.RuntimeLeaseTimeout = TimeSpan.FromMilliseconds(800);
            }, policy.Object);
            var runtime = new McpTestRuntime();
            McpRuntimeInfo initial = await context.OpenAsync("no-denial-renewal", runtime).ConfigureAwait(false);
            context.Clock.Advance(TimeSpan.FromMilliseconds(100));
            grants.Clear();
            grants.Add(requested);
            Exception? failure = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(initial.Handle, requested).AsTask()).ConfigureAwait(false);

            Assert.That(failure, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(context.Registry.List().ToArray()!.Single().IdleExpiresAt, Is.EqualTo(initial.IdleExpiresAt),
                "A rejected operation must not renew a lease before its entry capability is authorized.");
            Assert.That(context.Registry.List().ToArray()!.Single().ExpiresAt, Is.EqualTo(initial.ExpiresAt));
            Assert.That(runtime.BusinessCalls, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.Zero);
        }

        /// <summary>
        /// Missing identity, disabled host capability, and missing caller grant all refuse before activation.
        /// </summary>
        [TestCase("noCaller")]
        [TestCase("hostDisabled")]
        [TestCase("noGrant")]
        public async Task MissingAuthorityRefusesBeforeActivationAsync(string denial)
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                if (denial == "hostDisabled")
                {
                    options.EnabledCapabilities = [];
                }
                if (denial == "noGrant")
                {
                    options.CallerGrants = [];
                }
            });
            using IDisposable? caller = denial == "noCaller" ? null : context.EnterOwner();
            var runtime = new McpTestRuntime();
            Exception? opening = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("denied", runtime).AsTask()).ConfigureAwait(false);
            Exception? acquiring = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>("unknown", McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Assert.That(opening, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(acquiring, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadUserAccessDenied));
            if (denial == "noCaller")
            {
                Assert.That(context.Registry.List, Throws.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadUserAccessDenied));
                Exception? closing = await McpRuntimeTestContext.CaptureAsync(
                    context.Registry.CloseAsync("unknown").AsTask()).ConfigureAwait(false);
                Assert.That(closing, Is.TypeOf<ServiceResultException>()
                    .With.Property("StatusCode").EqualTo(StatusCodes.BadUserAccessDenied));
            }
            else
            {
                Assert.That(context.Registry.List().Count, Is.Zero);
            }
            Assert.That(context.FactoryCalls, Is.Zero);
            Assert.That(context.Clock.CreatedTimers, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(runtime.BusinessCalls, Is.Zero);
        }

        /// <summary>
        /// Removing permission to start or operate a runtime cannot prevent its owner from cleaning it up.
        /// </summary>
        [Test]
        public async Task RevokedStartPermissionDoesNotBlockListOrCloseAsync()
        {
            var grants = new HashSet<McpCapability> { McpCapability.WotRuntime };
            Mock<IMcpOperationPolicy> policy = McpRuntimeTestContext.MutablePolicy(grants);
            await using var context = new McpRuntimeTestContext(policy: policy.Object);
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("revoked", runtime).ConfigureAwait(false);
            grants.Clear();
            Assert.That(context.Registry.List().ToArray()!.Single().Handle, Is.EqualTo(info.Handle));
            await McpRuntimeTestContext.AwaitAsync(
                context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);
            Assert.That(context.Registry.List().Count, Is.Zero);
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(runtime.BusinessCalls, Is.Zero);
            policy.Verify(value => value.Require(McpCapability.WotRuntime), Times.Once);
            policy.Verify(value => value.RequireCaller(), Times.Exactly(3));
        }

        /// <summary>
        /// Invalid handles are rejected without disturbing an existing owned runtime.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" \t")]
        public async Task InvalidHandlesDoNotChangeExistingRuntimeAsync(string? handle)
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("untouched", runtime).ConfigureAwait(false);
            Exception? acquiring = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(handle!, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Exception? closing = await McpRuntimeTestContext.CaptureAsync(
                context.Registry.CloseAsync(handle!).AsTask()).ConfigureAwait(false);
            Assert.That(acquiring, Is.InstanceOf<ArgumentException>().With.Property("ParamName").EqualTo("handle"));
            Assert.That(closing, Is.InstanceOf<ArgumentException>().With.Property("ParamName").EqualTo("handle"));
            Assert.That(context.Registry.List().ToArray()!.Single(), Is.EqualTo(info));
            Assert.That(runtime.DisposeCalls, Is.Zero);
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Shutdown seals every entry point and cannot accidentally invoke a new factory.
        /// </summary>
        [Test]
        public async Task DisposedRegistryRefusesEveryEntryPointAsync()
        {
            await using var context = new McpRuntimeTestContext();
            using IDisposable caller = context.EnterOwner();
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("owned", runtime).ConfigureAwait(false);
            await McpRuntimeTestContext.AwaitAsync(context.Registry.DisposeAsync().AsTask()).ConfigureAwait(false);
            Exception? opening = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("after-shutdown", new McpTestRuntime()).AsTask()).ConfigureAwait(false);
            Exception? acquiring = await McpRuntimeTestContext.CaptureAsync(context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask()).ConfigureAwait(false);
            Exception? closing = await McpRuntimeTestContext.CaptureAsync(
                context.Registry.CloseAsync(info.Handle).AsTask()).ConfigureAwait(false);
            Assert.That(opening, Is.TypeOf<ObjectDisposedException>());
            Assert.That(acquiring, Is.TypeOf<ObjectDisposedException>());
            Assert.That(closing, Is.TypeOf<ObjectDisposedException>());
            Assert.That(context.Registry.List, Throws.TypeOf<ObjectDisposedException>());
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(runtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(context.Clock.ActiveTimers, Is.Zero);
        }

        /// <summary>
        /// Mutating host option objects later cannot relax captured quotas, concurrency, or deadlines.
        /// </summary>
        [Test]
        public async Task RegistrySnapshotsExecutionLimitsAsync()
        {
            await using var context = new McpRuntimeTestContext(options =>
            {
                options.MaxRuntimes = 1;
                options.MaxRuntimesPerCaller = 1;
                options.MaxConcurrentOperationsPerRuntime = 1;
                options.RuntimeIdleTimeout = TimeSpan.FromMilliseconds(500);
                options.RuntimeLeaseTimeout = TimeSpan.FromMilliseconds(800);
                options.OperationTimeout = TimeSpan.FromMilliseconds(100);
            });
            using IDisposable caller = context.EnterOwner();
            context.Options.MaxRuntimes = 8;
            context.Options.MaxRuntimesPerCaller = 8;
            context.Options.MaxConcurrentOperationsPerRuntime = 8;
            context.Options.RuntimeIdleTimeout = TimeSpan.FromMinutes(5);
            context.Options.RuntimeLeaseTimeout = TimeSpan.FromMinutes(30);
            context.Options.OperationTimeout = TimeSpan.FromMinutes(1);
            context.Options.EnabledCapabilities = [];
            var runtime = new McpTestRuntime();
            McpRuntimeInfo info = await context.OpenAsync("snapshot", runtime).ConfigureAwait(false);
            Exception? refusal = await McpRuntimeTestContext.CaptureAsync(
                context.OpenAsync("overflow", new McpTestRuntime()).AsTask()).ConfigureAwait(false);
            Assert.That(refusal, Is.TypeOf<ServiceResultException>()
                .With.Property("StatusCode").EqualTo(StatusCodes.BadTooManyOperations));
            Assert.That(info.IdleExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(500)));
            Assert.That(info.ExpiresAt, Is.EqualTo(McpRuntimeTestContext.Epoch.AddMilliseconds(800)));
            await using McpRuntimeUse<McpTestRuntime> first = await context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).ConfigureAwait(false);
            Task<McpRuntimeUse<McpTestRuntime>> queued = context.Registry
                .AcquireAsync<McpTestRuntime>(info.Handle, McpCapability.WotRuntime).AsTask();
            Assert.That(queued.IsCompleted, Is.False);
            context.Clock.Advance(TimeSpan.FromMilliseconds(100) - TimeSpan.FromTicks(1));
            Assert.That(first.CancellationToken.IsCancellationRequested, Is.False);
            context.Clock.Advance(TimeSpan.FromTicks(1));
            Exception? timeout = await McpRuntimeTestContext.CaptureAsync(queued).ConfigureAwait(false);
            Assert.That(timeout, Is.InstanceOf<OperationCanceledException>());
            Assert.That(first.CancellationToken.IsCancellationRequested, Is.True);
            Assert.That(context.FactoryCalls, Is.EqualTo(1));
            Assert.That(runtime.DisposeCalls, Is.Zero);
        }

        private static readonly string[] s_publicInfoPropertyNames =
        [
            "Handle", "Name", "Capability", "State", "CreatedAt", "ExpiresAt", "IdleExpiresAt"
        ];

        private static readonly string[] s_ordinalNames = ["named", "Named"];
    }
}
#endif
