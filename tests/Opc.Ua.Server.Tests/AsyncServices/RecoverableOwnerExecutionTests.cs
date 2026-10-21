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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Bindings;
using Opc.Ua.Identity;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Server.TestFramework;
using Opc.Ua.TestModels.AsyncExecution;
using Opc.Ua.Tests;
using Model = Opc.Ua.TestModels.AsyncExecution;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests.AsyncServices
{
    /// <summary>
    /// Real RSE manager Calls and ordinary handlers. The injected owner resolver isolates lifetime behavior;
    /// it deliberately makes no authentication claim. No listener opens a socket.
    /// </summary>
    [TestFixture]
    [Category("AsyncServices")]
    [NonParallelizable]
    public sealed class RecoverableOwnerExecutionTests
    {
        [Test]
        public async Task RegistrationAloneLeavesSessionDefaultAndExperimentDisabled()
        {
            await using var defaultServer = new StandardServer(NUnitTelemetryContext.Create());
            Assert.That(defaultServer.AsyncServices.Enabled, Is.False);
            Assert.That(defaultServer.AsyncServices.RecoveryModes,
                Is.EqualTo([ServiceOperationRecoveryMode.Session]));
            var services = new ServiceCollection();
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            builder.WithRecoverableExecutionOwners(s_owner.AuthorityId,
                [new RecoverableExecutionApplicationMapping(s_owner.ApplicationId, new ByteString(new byte[32]))],
                [new RecoverableExecutionUserMapping(s_owner.UserId, "urn:synthetic:issuer", "operator")]);
            builder.WithRecoverableExecutionEligibility(new Qualification());
            await using ServiceProvider container = services.BuildServiceProvider();
            AsyncServiceExecutionOptions options = container.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AsyncServices;
            Assert.That(options.Enabled, Is.False);
            Assert.That(options.RecoveryModes,
                Is.EqualTo([ServiceOperationRecoveryMode.Session]));
            Assert.That(new AsyncServiceExecutionOptions().RecoveryModes,
                Is.EqualTo([ServiceOperationRecoveryMode.Session]));
            Assert.That(container.GetRequiredService<IRecoverableExecutionOwnerProvider>(),
                Is.InstanceOf<ConfiguredRecoverableExecutionOwnerProvider>());
            Assert.That(container.GetRequiredService<IRecoverableExecutionEligibilityProvider>(),
                Is.InstanceOf<Qualification>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DirectAndDiActivationPreserveExactModes(bool ownerFirst)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            ServiceOperationRecoveryMode[] modes = ownerFirst
                ? [ServiceOperationRecoveryMode.Owner, ServiceOperationRecoveryMode.Session]
                : [ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner];
            ServiceOperationRecoveryMode[] expected = [.. modes];
            var services = new ServiceCollection();
            services.AddSingleton<IRecoverableExecutionOwnerProvider>(test.Owners);
            IOpcUaServerBuilder builder = services.AddOpcUa().AddServer(_ => { });
            builder.WithRecoverableExecutionEligibility(test.Eligibility)
                .WithExperimentalAsyncServices(options => options.RecoveryModes = modes);
            await using ServiceProvider container = services.BuildServiceProvider();
            AsyncServiceExecutionOptions configured = container.GetRequiredService<IOptions<OpcUaServerOptions>>()
                .Value.AsyncServices;
            await using IAsyncServiceExecutionProvider viaDi = container
                .GetRequiredService<IAsyncServiceExecutionProviderFactory>()
                .Create(test.Server, test.Configuration, configured, test.Clock);
            var directOptions = new AsyncServiceExecutionOptions { Enabled = true, RecoveryModes = modes };
            await using var direct = new AsyncServiceExecutionProvider(
                test.Server, test.Configuration, directOptions, test.Clock,
                ownerProvider: test.Owners, eligibilityProvider: test.Eligibility);

            Assert.That(configured.Enabled, Is.True);
            Assert.That(viaDi.SupportedRecoveryModes, Is.EqualTo(expected));
            Assert.That(direct.SupportedRecoveryModes, Is.EqualTo(expected));
            directOptions.RecoveryModes = [ServiceOperationRecoveryMode.Session];
            configured.RecoveryModes = [ServiceOperationRecoveryMode.Session];
            modes[0] = ServiceOperationRecoveryMode.Durable;
            Assert.That(direct.SupportedRecoveryModes, Is.EqualTo(expected));
            Assert.That(viaDi.SupportedRecoveryModes, Is.EqualTo(expected));
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
        }

        [TestCase("mapping")]
        [TestCase("eligibility")]
        [TestCase("accounting")]
        public async Task OwnerActivationRequiresEveryQualification(string missing)
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            var isolation = new Mock<IServerResourceIsolationProvider>();
            if (missing != "accounting")
            {
                _ = isolation.As<IRecoverableExecutionResourceIsolationProvider>();
            }
            server.ResourceIsolationProvider = isolation.Object;
            var owner = new Mock<IRecoverableExecutionOwnerProvider>(MockBehavior.Strict);
            var eligibility = new Mock<IRecoverableExecutionEligibilityProvider>(MockBehavior.Strict);
            var options = new AsyncServiceExecutionOptions
            {
                Enabled = true,
                RecoveryModes = [ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner]
            };

            Assert.That(() => new AsyncServiceExecutionProvider(server,
                new ApplicationConfiguration { TransportQuotas = new TransportQuotas { MaxMessageSize = kLimit } },
                options, ownerProvider: missing == "mapping" ? null : owner.Object,
                eligibilityProvider: missing == "eligibility" ? null : eligibility.Object),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("options"));
            owner.VerifyNoOtherCalls();
            eligibility.VerifyNoOtherCalls();
            isolation.VerifyNoOtherCalls();
        }

        [Test]
        public async Task RegisteredProvidersDoNotEnableOwnerAdmission()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync(ownerMode: false).ConfigureAwait(false);
            await test.RejectAsync(test.First, RseBrowseNames.Submit, test.AddRequest(),
                StatusCodes.BadNotSupported).ConfigureAwait(false);
            Assert.That(test.Provider.SupportedRecoveryModes,
                Is.EqualTo([ServiceOperationRecoveryMode.Session]));
            Assert.That(test.Owners.Resolutions, Is.Zero);
            Assert.That(test.Eligibility.Attempts, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
        }

        [Test]
        public async Task DurableActivationRequiresExplicitQualification()
        {
            await using var server = new StandardServer(NUnitTelemetryContext.Create());
            ArgumentException exception = Assert.Throws<ArgumentException>(() =>
                new AsyncServiceExecutionProvider(server,
                    new ApplicationConfiguration { TransportQuotas = new TransportQuotas { MaxMessageSize = kLimit } },
                    new AsyncServiceExecutionOptions
                    {
                        Enabled = true,
                        RecoveryModes = [ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Durable]
                    }))!;
            Assert.That(exception.ParamName, Is.EqualTo("options"));
        }

        [Test]
        public async Task OwnerInvocationSurvivesSessionDisposalAndReplacementCollectsOriginalOutcome()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            SubmitRequestDataType request = test.AddRequest();
            using Reply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(test.First, request).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            Assert.That(admitted.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(admitted.Value.Operation.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Owner));
            Assert.That(token.Length, Is.EqualTo(32));
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            OperationContext owned = test.Server.OwnedContext!;
            NodeId issuingId = test.First.Session.Id;
            ArrayOf<NodeId> originalRoles = owned.UserIdentity.GrantedRoleIds;
            string channelId = test.First.Channel.SecureChannelId;
            test.First.Identity.Subject = "mutated-after-admission";
            test.First.Identity.ClaimGroups[0] = "mutated";
            test.First.Identity.RawClaims["source"] = "mutated";
            test.First.Channel.EndpointDescription!.EndpointUrl = "opc.tcp://mutated.invalid:1/";
            await test.CloseAsync(test.First).ConfigureAwait(false);
            test.First.Session.Dispose();

            Assert.That(owned.Session, Is.Null);
            Assert.That(owned.SessionId, Is.EqualTo(issuingId));
            Assert.That(owned.IsSessionIndependent, Is.True);
            Assert.That(owned.OperationDeadline, Is.EqualTo(DateTime.MaxValue));
            Assert.That(owned.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(owned.PreferredLocales, Is.EqualTo(s_locales));
            Assert.That(owned.ChannelContext!.SecureChannelId, Is.EqualTo(channelId));
            Assert.That(owned.ChannelContext.EndpointDescription!.EndpointUrl, Does.Not.Contain("mutated"));
            Assert.That(((IIdentityClaims)owned.UserIdentity).Subject, Is.EqualTo("operator"));
            Assert.That(((IIdentityClaims)owned.UserIdentity).Groups, Is.EqualTo(s_groups));
            Assert.That(((IIdentityClaims)owned.UserIdentity).Claims["source"], Is.EqualTo("original-claim"));
            Assert.That(owned.UserIdentity.GrantedRoleIds, Is.EqualTo(originalRoles));
            Assert.That(test.Eligibility.Active, Is.EqualTo(1));

            Peer replacement = await test.AddPeerAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> pending = await test.GetAsync(
                replacement, clientId: request.ClientOperationId).ConfigureAwait(false);
            Assert.That(pending.Value.Operation.ServerToken, Is.EqualTo(token));
            Assert.That(pending.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            pending.Dispose();
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> byId = await test.GetAsync(
                replacement, clientId: request.ClientOperationId).ConfigureAwait(false);
            AssertOriginalAdd(byId.Value.Result, test, 12);
            using Reply<GetResultResponseDataType> byToken =
                await test.GetAsync(replacement, token).ConfigureAwait(false);
            Assert.That(byToken.Value.Operation.ServerToken, Is.EqualTo(token));
            Assert.That(byToken.Value.Operation.ClientOperationId, Is.EqualTo(request.ClientOperationId));
            AssertOriginalAdd(byToken.Value.Result, test, 12);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(12));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));

            using Reply<ControlResponseDataType> ack = await test.ControlAsync(
                replacement, token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(ack.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            ack.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(200));
            using Reply<GetResultResponseDataType> gone = await test.GetAsync(replacement, token).ConfigureAwait(false);
            AssertUnknown(gone.Value.Operation, gone.Value.Result);
            gone.Dispose();
            AssertOriginalAdd(byId.Value.Result, test, 12);
            AssertOriginalAdd(byToken.Value.Result, test, 12);
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 0).ConfigureAwait(false);
            Assert.That(test.Provider.GetStatistics().RetainedOutcomeBytes, Is.EqualTo((ulong)kLimit),
                "Both selected responses share one original outcome after ACK/history.");
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 3L * kLimit).ConfigureAwait(false);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes), Is.EqualTo(3L * kLimit),
                "Two outer Call delivery leases remain in addition to the one shared outcome.");
            byId.Dispose();
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 2L * kLimit).ConfigureAwait(false);
            Assert.That(test.Provider.GetStatistics().RetainedOutcomeBytes, Is.EqualTo((ulong)kLimit));
            AssertOriginalAdd(byToken.Value.Result, test, 12);
            byToken.Dispose();
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
            Assert.That(test.Provider.GetStatistics().RetainedOutcomeBytes, Is.Zero);
        }

        [TestCase("application")]
        [TestCase("user")]
        [TestCase("authority")]
        public async Task ForeignOwnerAndModeMismatchAreCanonicalUnknownWithoutReplay(string changedPart)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            SubmitRequestDataType request = test.AddRequest();
            using Reply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(test.First, request).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            Peer foreign = await test.AddPeerAsync(OtherOwner(changedPart)).ConfigureAwait(false);
            byte[] changed = token.ToArray();
            changed[0] ^= 0xff;
            ByteString missing = new(changed);
            using Reply<GetResultResponseDataType> absent = await test.GetAsync(foreign, missing).ConfigureAwait(false);
            AssertUnknown(absent.Value.Operation, absent.Value.Result);
            byte[] canonical = BinaryEncoder.EncodeMessage(absent.Value.Operation, test.Server.MessageContext);
            absent.Dispose();
            using Reply<GetResultResponseDataType> foreignToken =
                await test.GetAsync(foreign, token).ConfigureAwait(false);
            AssertUnknown(foreignToken.Value.Operation, foreignToken.Value.Result);
            Assert.That(BinaryEncoder.EncodeMessage(foreignToken.Value.Operation, test.Server.MessageContext),
                Is.EqualTo(canonical));
            using Reply<GetResultResponseDataType> foreignId = await test.GetAsync(
                foreign, clientId: request.ClientOperationId).ConfigureAwait(false);
            AssertUnknown(foreignId.Value.Operation, foreignId.Value.Result, request.ClientOperationId);
            using Reply<GetResultResponseDataType> wrongMode = await test.GetAsync(
                test.First, token, mode: ServiceOperationRecoveryMode.Session).ConfigureAwait(false);
            AssertUnknown(wrongMode.Value.Operation, wrongMode.Value.Result);
            foreach (string method in new[]
            {
                RseBrowseNames.Acknowledge, RseBrowseNames.Cancel, RseBrowseNames.Abandon
            })
            {
                using Reply<ControlResponseDataType> unknown = await test.ControlAsync(foreign, missing, method)
                    .ConfigureAwait(false);
                using Reply<ControlResponseDataType> denied = await test.ControlAsync(foreign, token, method)
                    .ConfigureAwait(false);
                Assert.That(denied.Value.Result, Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
                Assert.That(BinaryEncoder.EncodeMessage(denied.Value, test.Server.MessageContext),
                    Is.EqualTo(BinaryEncoder.EncodeMessage(unknown.Value, test.Server.MessageContext)));
                AssertUnknown(denied.Value.Operation, default);
            }
            Assert.That(test.Server.OwnedContext!.CancellationToken.IsCancellationRequested, Is.False);
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> owner = await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertOriginalAdd(owner.Value.Result, test, 12);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1),
                "Unknown is lookup scope, not a claim that the original request did not execute.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DuplicateClientIdIsOwnerScopedAcrossSessionsRegardlessOfInput(bool differentInput)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            SubmitRequestDataType request = test.AddRequest();
            using Reply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(test.First, request).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            Peer replacement = await test.AddPeerAsync().ConfigureAwait(false);
            SubmitRequestDataType duplicate = test.AddRequest(amount: differentInput ? 500 : 5);
            duplicate.ClientOperationId = request.ClientOperationId;
            await test.RejectAsync(replacement, RseBrowseNames.Submit, duplicate, StatusCodes.BadInvalidState)
                .ConfigureAwait(false);
            Assert.That(test.Provider.GetStatistics().AdmittedCount, Is.EqualTo(1));
            Assert.That(test.Eligibility.Attempts, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.FinishAsync().ConfigureAwait(false);
            await test.RejectAsync(replacement, RseBrowseNames.Submit, duplicate, StatusCodes.BadInvalidState)
                .ConfigureAwait(false);
            using Reply<GetResultResponseDataType> result =
                await test.GetAsync(replacement, token).ConfigureAwait(false);
            AssertOriginalAdd(result.Value.Result, test, 12);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [Test]
        public async Task DifferentOwnersCanUseSameClientIdWithoutSharingOutcome()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            SubmitRequestDataType first = test.AddRequest();
            using Reply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(test.First, first).ConfigureAwait(false);
            ByteString firstToken = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            Peer other = await test.AddPeerAsync(OtherOwner("user")).ConfigureAwait(false);
            test.Server.Scenario.Blob = ByteString.From([42, 43]);
            SubmitRequestDataType second = OwnerScope.Wrap(new ReadRequest
            {
                RequestHeader = new RequestHeader { RequestHandle = 72 },
                TimestampsToReturn = TimestampsToReturn.Neither,
                NodesToRead =
                [
                    new ReadValueId { NodeId = test.Node(Model.VariableIds.Probe_Blob), AttributeId = Attributes.Value }
                ]
            }, wait: 5000);
            second.ClientOperationId = first.ClientOperationId;
            using Reply<SubmitResponseDataType> secondAdmission =
                await test.SubmitAsync(other, second).ConfigureAwait(false);
            ByteString secondToken = secondAdmission.Value.Operation.ServerToken;
            Assert.That(secondToken, Is.Not.EqualTo(firstToken));
            Assert.That(secondAdmission.Value.Result.TryGetValue(out ReadResponse? read), Is.True);
            Assert.That(read!.ResponseHeader.RequestHandle, Is.EqualTo(72u));
            Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(read.Results[0].WrappedValue.TryGetValue(out ByteString bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(ByteString.From([42, 43])));
            secondAdmission.Dispose();
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> original = await test.GetAsync(
                test.First, clientId: first.ClientOperationId).ConfigureAwait(false);
            Assert.That(original.Value.Operation.ServerToken, Is.EqualTo(firstToken));
            AssertOriginalAdd(original.Value.Result, test, 12);
            using Reply<GetResultResponseDataType> distinct = await test.GetAsync(
                other, clientId: first.ClientOperationId).ConfigureAwait(false);
            Assert.That(distinct.Value.Operation.ServerToken, Is.EqualTo(secondToken));
            Assert.That(distinct.Value.Result.TryGetValue(out ReadResponse? retained), Is.True);
            Assert.That(retained!.Results[0].WrappedValue, Is.EqualTo(new Variant(ByteString.From([42, 43]))));
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.BlobReads, Is.EqualTo(1));
            Assert.That(test.Provider.GetStatistics().AdmittedCount, Is.EqualTo(2));
        }

        [Test]
        public async Task SessionRecordRemainsCreatorScopedAndEndsWithIssuingSession()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            SubmitRequestDataType request = test.AddRequest(mode: ServiceOperationRecoveryMode.Session);
            using Reply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(test.First, request).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            Peer replacement = await test.AddPeerAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> wrongMode =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertUnknown(wrongMode.Value.Operation, wrongMode.Value.Result);
            using Reply<GetResultResponseDataType> foreign = await test.GetAsync(
                replacement, token, mode: ServiceOperationRecoveryMode.Session).ConfigureAwait(false);
            AssertUnknown(foreign.Value.Operation, foreign.Value.Result);
            await test.ReactivateAsync(test.First, "different-creator").ConfigureAwait(false);
            using Reply<GetResultResponseDataType> changed = await test.GetAsync(
                test.First, token, mode: ServiceOperationRecoveryMode.Session).ConfigureAwait(false);
            AssertUnknown(changed.Value.Operation, changed.Value.Result);
            using Reply<ControlResponseDataType> control = await test.ControlAsync(
                test.First, token, RseBrowseNames.Cancel).ConfigureAwait(false);
            Assert.That(control.Value.Result, Is.EqualTo(ServiceOperationControlResult.UnknownOperation));
            control.Dispose();
            await test.CloseAsync(test.First).ConfigureAwait(false);
            Assert.That(test.Server.OwnedContext!.IsSessionIndependent, Is.False);
            await WaitForCancellationAsync(test.Server.OwnedContext.CancellationToken, test.Timeout)
                .ConfigureAwait(false);
            Assert.That(test.Eligibility.Attempts, Is.Zero);
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> ended = await test.GetAsync(
                replacement, token, mode: ServiceOperationRecoveryMode.Session).ConfigureAwait(false);
            AssertUnknown(ended.Value.Operation, ended.Value.Result);
            Assert.That(test.Provider.GetStatistics().ActiveOperationCount, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UncertainResourcesAndPositiveMethodHintCannotBypassEligibility(bool positiveMethodHint)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            test.Eligibility.Rejection = StatusCodes.BadServiceUnsupported;
            SubmitRequestDataType request = positiveMethodHint
                ? test.AddRequest()
                : OwnerScope.Wrap(test.BrowseRequest(maximum: 1));
            Assert.That(test.Server.Scenario.RecoveryProperty!.Value, Is.True);

            await test.RejectAsync(test.First, RseBrowseNames.Submit, request, StatusCodes.BadServiceUnsupported)
                .ConfigureAwait(false);
            Assert.That(test.Eligibility.Attempts, Is.EqualTo(1));
            Assert.That(test.Eligibility.Acquisitions, Is.Zero);
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [TestCase(1, 1024)]
        [TestCase(704, 1024)]
        [TestCase(705, 1025)]
        [TestCase(65_216, 65_536)]
        [TestCase(65_217, 65_537)]
        public async Task OwnerHistoryEvidenceChargeHonorsFloorAndMessageBoundary(int evidenceBytes, int charge)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            // Exactly one numeric role: 256 metadata + evidence + 64 role footprint.
            test.First.Session.RefreshEffectiveIdentity(new SyntheticOwnerIdentity
            {
                TokenHandler = test.First.Identity.TokenHandler,
                EffectiveRoles = [ObjectIds.WellKnownRole_AuthenticatedUser]
            });
            test.Owners.Evidence = new ByteString(new byte[evidenceBytes]);
            if (charge > kLimit)
            {
                await test.RejectAsync(test.First, RseBrowseNames.Submit, test.AddRequest(),
                    StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
                Assert.That(test.Eligibility.Attempts, Is.Zero);
                Assert.That(test.Provider.GetStatistics().AdmittedCount, Is.Zero);
                Assert.That(test.Server.Scenario.Invocations, Is.Zero);
                Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
                await test.AssertNoReservationsAsync().ConfigureAwait(false);
                return;
            }
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes),
                Is.EqualTo((4L * kLimit) + charge));
            await test.FinishAsync().ConfigureAwait(false);
            Assert.That(test.Provider.GetStatistics().RetainedOutcomeBytes, Is.EqualTo((ulong)(kLimit + charge)));
            using Reply<GetResultResponseDataType> result =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertOriginalAdd(result.Value.Result, test, 12);
            result.Dispose();
            using Reply<ControlResponseDataType> ack = await test.ControlAsync(
                test.First, token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            ack.Dispose();
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, charge).ConfigureAwait(false);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            test.Clock.Advance(TimeSpan.FromMilliseconds(200));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [TestCase("numeric", 1184)]
        [TestCase("string", 1186)]
        [TestCase("opaque", 1187)]
        public async Task OwnerHistoryChargesRoleIdentifierFootprints(string roleKind, int charge)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            NodeId role = roleKind switch
            {
                "string" => new NodeId("é", 2),
                "opaque" => new NodeId(ByteString.From([1, 2, 3]), 2),
                _ => new NodeId(777, 2)
            };
            test.First.Session.RefreshEffectiveIdentity(new SyntheticOwnerIdentity
            {
                TokenHandler = test.First.Identity.TokenHandler,
                EffectiveRoles = [ObjectIds.WellKnownRole_AuthenticatedUser, role]
            });
            test.Owners.Evidence = new ByteString(new byte[800]);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes),
                Is.EqualTo((4L * kLimit) + charge));
            await test.FinishAsync().ConfigureAwait(false);
            Assert.That(test.Provider.GetStatistics().RetainedOutcomeBytes, Is.EqualTo((ulong)(kLimit + charge)));
            using Reply<GetResultResponseDataType> result =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertOriginalAdd(result.Value.Result, test, 12);
            result.Dispose();
            using Reply<ControlResponseDataType> ack = await test.ControlAsync(
                test.First, token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            ack.Dispose();
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, charge).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(200));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [Test]
        public async Task SessionSingleRoleRetainsExistingHistoryAndInputCharge()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            test.First.Session.RefreshEffectiveIdentity(new SyntheticOwnerIdentity
            {
                TokenHandler = test.First.Identity.TokenHandler,
                EffectiveRoles = [ObjectIds.WellKnownRole_AuthenticatedUser]
            });
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                test.First, test.AddRequest(mode: ServiceOperationRecoveryMode.Session)).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            Assert.That(test.Eligibility.Attempts, Is.Zero);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes),
                Is.EqualTo((3L * kLimit) + 1024));
            await test.FinishAsync().ConfigureAwait(false);
            Assert.That(test.Provider.GetStatistics().RetainedOutcomeBytes, Is.EqualTo((ulong)kLimit + 1024));
            using Reply<GetResultResponseDataType> result = await test.GetAsync(
                test.First, token, mode: ServiceOperationRecoveryMode.Session).ConfigureAwait(false);
            AssertOriginalAdd(result.Value.Result, test, 12);
            result.Dispose();
            using Reply<ControlResponseDataType> ack = await test.ControlAsync(
                test.First, token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            ack.Dispose();
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 1024).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(200));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OwnerCaptureBudgetFailureReturnsReservationsBeforeDispatch(bool upstream)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            if (upstream)
            {
                var source = new SyntheticOwnerIdentity();
                source.RawClaims["oversized"] = new string('x', kLimit);
                test.First.Channel.UpstreamIdentity = source;
            }
            else
            {
                test.First.Identity.RawClaims["oversized"] = new string('x', kLimit);
            }
            await test.RejectAsync(test.First, RseBrowseNames.Submit, test.AddRequest(),
                StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
            Assert.That(test.Eligibility.Acquisitions, Is.EqualTo(upstream ? 1 : 0));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(upstream ? 1 : 0));
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CancellationAfterQualificationReturnsTheHeldReservationBeforeDispatch()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            TaskCompletionSource<bool> acquired = Signal();
            TaskCompletionSource<bool> release = Signal();
            OperationContext? qualifying = null;
            test.Eligibility.AfterAcquire = async context =>
            {
                qualifying = context;
                acquired.TrySetResult(true);
                // Return the already-held reservation even after Cancel; production owns its rejection cleanup.
                await release.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            };
            RequestHeader header = test.Header(test.First);
            Task<IServiceResponse> submitting = test.SendAsync(
                test.First, RseBrowseNames.Submit, test.AddRequest(), header: header);
            try
            {
                await acquired.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                Assert.That(test.Eligibility.Active, Is.EqualTo(1));
                Assert.That(test.Server.OwnedDispatches, Is.Zero);
                uint cancelled = await test.CancelExchangeAsync(test.First, header.RequestHandle).ConfigureAwait(false);
                Assert.That(cancelled, Is.EqualTo(1u));
                Assert.That(qualifying!.CancellationToken.IsCancellationRequested, Is.True);
                Assert.That(qualifying.RequestLifetime.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            }
            finally
            {
                release.TrySetResult(true);
            }
            IServiceResponse response = await submitting.ConfigureAwait(false);
            try
            {
                AssertFailure(response, StatusCodes.BadRequestCancelledByClient);
            }
            finally
            {
                (response as IPooledEncodeable)?.Reuse();
            }
            Assert.That(test.Eligibility.Acquisitions, Is.EqualTo(1));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(test.Provider.GetStatistics().AdmittedCount, Is.Zero);
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task DeadlineAckAndHistoryKeepEligibilityUntilActualWorkEnds()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            SubmitRequestDataType request = test.AddRequest(execution: 100);
            using Reply<SubmitResponseDataType> admitted =
                await test.SubmitAsync(test.First, request).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(99));
            using Reply<GetResultResponseDataType> before =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(before.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(before.Value.Operation.ExecutionRemaining, Is.EqualTo(1));
            Assert.That(before.Value.Operation.CancellationRequested, Is.False);
            before.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            using Reply<GetResultResponseDataType> deadline =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(deadline.Value.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(deadline.Value.Operation.OutcomeKind,
                Is.EqualTo(ServiceOperationOutcome.ExecutionDeadlineExceeded));
            Assert.That(deadline.Value.Operation.OutcomeStatus, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(deadline.Value.Operation.ExecutionAllowance, Is.EqualTo(100));
            Assert.That(deadline.Value.Operation.ResultRetentionTime, Is.EqualTo(200));
            Assert.That(deadline.Value.Operation.ResultRemaining, Is.EqualTo(200));
            Assert.That(deadline.Value.Result.IsNull, Is.True);
            deadline.Dispose();
            await WaitForCancellationAsync(test.Server.OwnedContext!.CancellationToken, test.Timeout)
                .ConfigureAwait(false);
            await AssertRunningReservationsAsync(test).ConfigureAwait(false);
            using Reply<ControlResponseDataType> ack = await test.ControlAsync(
                test.First, token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
            Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(ack.Value.Operation.HistoryRemaining, Is.EqualTo(200));
            ack.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(199));
            using Reply<GetResultResponseDataType> history =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(history.Value.Operation.State, Is.EqualTo(ServiceOperationState.Acknowledged));
            Assert.That(history.Value.Operation.HistoryRemaining, Is.EqualTo(1));
            history.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            using Reply<GetResultResponseDataType> forgotten =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertUnknown(forgotten.Value.Operation, forgotten.Value.Result);
            forgotten.Dispose();
            await AssertRunningReservationsAsync(test).ConfigureAwait(false);
            await test.RejectAsync(test.First, RseBrowseNames.Submit, request, StatusCodes.BadInvalidState)
                .ConfigureAwait(false);
            await test.FinishAsync().ConfigureAwait(false);

            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(test.Eligibility.Active, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(12),
                "The non-cooperative original effect can finish after E, but must not rewrite its committed outcome.");
            using Reply<GetResultResponseDataType> after = await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertUnknown(after.Value.Operation, after.Value.Result);
            after.Dispose();
            Assert.That(test.Provider.GetStatistics().ExecutionDeadlineCount, Is.EqualTo(1));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task AbandonAndHistoryDoNotCancelOrReleaseRunningWork()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            using Reply<ControlResponseDataType> abandon = await test.ControlAsync(
                test.First, token, RseBrowseNames.Abandon).ConfigureAwait(false);
            Assert.That(abandon.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(abandon.Value.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(abandon.Value.Operation.CancellationRequested, Is.False);
            abandon.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(200));
            using Reply<GetResultResponseDataType> forgotten =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertUnknown(forgotten.Value.Operation, forgotten.Value.Result);
            forgotten.Dispose();
            await AssertRunningReservationsAsync(test).ConfigureAwait(false);
            Assert.That(test.Server.OwnedContext!.CancellationToken.IsCancellationRequested, Is.False);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.FinishAsync().ConfigureAwait(false);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(12));
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task OutcomeRetentionAndHistoryExpireAtExactOwnerBoundaries()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                test.First, test.AddRequest(execution: 1000)).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Clock.Advance(TimeSpan.FromMilliseconds(999));
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> ready = await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(ready.Value.Operation.ResultRemaining, Is.EqualTo(200));
            AssertOriginalAdd(ready.Value.Result, test, 12);
            ready.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(199));
            using Reply<GetResultResponseDataType> beforeExpiry =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(beforeExpiry.Value.Operation.ResultRemaining, Is.EqualTo(1));
            AssertOriginalAdd(beforeExpiry.Value.Result, test, 12);
            beforeExpiry.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            using Reply<GetResultResponseDataType> expired =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(expired.Value.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
            Assert.That(expired.Value.Operation.ResultRemaining, Is.Zero);
            Assert.That(expired.Value.Operation.HistoryRemaining, Is.EqualTo(200));
            Assert.That(expired.Value.Result.IsNull, Is.True);
            expired.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(199));
            using Reply<GetResultResponseDataType> history =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(history.Value.Operation.State, Is.EqualTo(ServiceOperationState.Expired));
            Assert.That(history.Value.Operation.HistoryRemaining, Is.EqualTo(1));
            history.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            using Reply<GetResultResponseDataType> gone = await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertUnknown(gone.Value.Operation, gone.Value.Result);
            gone.Dispose();
            test.Clock.Advance(TimeSpan.FromMilliseconds(1));
            using Reply<GetResultResponseDataType> after = await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertUnknown(after.Value.Operation, after.Value.Result);
            after.Dispose();
            Assert.That(test.Provider.GetStatistics().ExpiredCount, Is.EqualTo(1));
            Assert.That(test.Provider.GetStatistics().ExecutionDeadlineCount, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task CancellingCollectorWaitDoesNotCancelOwnedWork()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Policy.CollectionEntered = Signal();
            RequestHeader header = test.Header(test.First);
            // Deliberately collide with the original handle; ordinary Cancel must still exclude owned work.
            header.RequestHandle = 71;
            Task<IServiceResponse> waiting = test.SendAsync(test.First, RseBrowseNames.GetResult,
                new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Owner,
                    ServerToken = token,
                    WaitTime = 5000
                }, header: header);
            await test.Policy.CollectionEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            // The original is gated and holds one execution unit. The collector reaching one means it parked.
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
            uint cancelled = await test.CancelExchangeAsync(test.First, header.RequestHandle).ConfigureAwait(false);
            Assert.That(cancelled, Is.EqualTo(1u));
            IServiceResponse cancelledResponse = await waiting.ConfigureAwait(false);
            try
            {
                AssertFailure(cancelledResponse, StatusCodes.BadRequestCancelledByClient);
            }
            finally
            {
                (cancelledResponse as IPooledEncodeable)?.Reuse();
            }
            using Reply<GetResultResponseDataType> pending =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(pending.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            Assert.That(pending.Value.Operation.CancellationRequested, Is.False);
            pending.Dispose();
            Assert.That(test.Server.OwnedContext!.CancellationToken.IsCancellationRequested, Is.False);
            await AssertRunningReservationsAsync(test).ConfigureAwait(false);
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> result =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertOriginalAdd(result.Value.Result, test, 12);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [Test]
        public async Task CancelAfterValidationGatePreventsOriginalEffect()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            test.Server.ValidationRelease = Signal();
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.ValidationEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            using Reply<ControlResponseDataType> cancel = await test.ControlAsync(
                test.First, token, RseBrowseNames.Cancel).ConfigureAwait(false);
            Assert.That(cancel.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            cancel.Dispose();
            await WaitForCancellationAsync(test.Server.OwnedContext!.CancellationToken, test.Timeout)
                .ConfigureAwait(false);
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            await AssertRunningReservationsAsync(test).ConfigureAwait(false);
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> result =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(result.Value.Operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.ServiceResponse));
            Assert.That(result.Value.Result.TryGetValue(out ServiceFault? fault, test.Server.MessageContext), Is.True);
            Assert.That(fault!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(71u));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CancellationAndHandlerFailureReleaseEligibilityExactlyOnce(bool failValidation)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            test.Server.ValidationFailure = failValidation;
            test.Server.Scenario.IgnoreCancellation = false;
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            if (!failValidation)
            {
                await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
                using Reply<ControlResponseDataType> cancel = await test.ControlAsync(
                    test.First, token, RseBrowseNames.Cancel).ConfigureAwait(false);
                Assert.That(cancel.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            }
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            using Reply<GetResultResponseDataType> outcome =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            Assert.That(outcome.Value.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(outcome.Value.Result.TryGetValue(out ServiceFault? fault, test.Server.MessageContext), Is.True);
            Assert.That(fault!.ResponseHeader.ServiceResult, Is.EqualTo(
                failValidation ? StatusCodes.BadUnexpectedError : StatusCodes.BadRequestCancelledByClient));
            Assert.That(fault.ResponseHeader.RequestHandle, Is.EqualTo(71u));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(test.Eligibility.Active, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(failValidation ? 0 : 1));
        }

        [Test]
        public async Task ReplacementSessionCannotMultiplyOwnerExecutionCapacity()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync(ownerExecutionLimit: 1).ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            await test.CloseAsync(test.First).ConfigureAwait(false);
            Peer replacement = await test.AddPeerAsync().ConfigureAwait(false);
            await test.RejectAsync(replacement, RseBrowseNames.Submit, test.AddRequest(),
                StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
            Assert.That(test.Eligibility.Acquisitions, Is.EqualTo(1));
            using Reply<GetResultResponseDataType> recovered =
                await test.GetAsync(replacement, token).ConfigureAwait(false);
            Assert.That(recovered.Value.Operation.State, Is.EqualTo(ServiceOperationState.Pending));
            recovered.Dispose();
            // Response publication precedes the outer request queue's execution-lease cleanup.
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(test.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
            Assert.That(test.Eligibility.Active, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> original =
                await test.GetAsync(replacement, token).ConfigureAwait(false);
            AssertOriginalAdd(original.Value.Result, test, 12);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [TestCase("identity")]
        [TestCase("permission")]
        [TestCase("owner")]
        public async Task RevocationDuringWaitDeniesCollectionWithoutChangingOriginalOutcome(string revocation)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Policy.CollectionEntered = Signal();
            Task rejected = test.RejectAsync(test.First, RseBrowseNames.GetResult,
                new GetResultRequestDataType
                {
                    RecoveryMode = ServiceOperationRecoveryMode.Owner,
                    ServerToken = token,
                    WaitTime = 5000
                }, StatusCodes.BadUserAccessDenied);
            await test.Policy.CollectionEntered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            IUserIdentity previousIdentity = test.First.Session.EffectiveIdentity;
            ArrayOf<RolePermissionType> previousPermissions = test.Server.Scenario.DeclaredMethod!.RolePermissions;
            if (revocation == "owner")
            {
                test.Owners.Set(test.First.Session.Id, OtherOwner("user"));
            }
            else if (revocation == "identity")
            {
                test.First.Session.RefreshEffectiveIdentity(new SyntheticOwnerIdentity
                {
                    TokenHandler = test.First.Identity.TokenHandler,
                    EffectiveRoles = [ObjectIds.WellKnownRole_Anonymous]
                });
            }
            else
            {
                test.Server.Scenario.DeclaredMethod.RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_SecurityAdmin,
                        Permissions = (uint)(PermissionType.Call | PermissionType.Browse)
                    }
                ];
            }
            await test.FinishAsync().ConfigureAwait(false);
            await rejected.ConfigureAwait(false);
            Assert.That(test.Policy.CollectionChecks, Is.GreaterThanOrEqualTo(2));
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(12));
            test.Owners.Set(test.First.Session.Id, s_owner);
            test.First.Session.RefreshEffectiveIdentity(previousIdentity);
            test.Server.Scenario.DeclaredMethod.RolePermissions = previousPermissions;
            using Reply<GetResultResponseDataType> original =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertOriginalAdd(original.Value.Result, test, 12);
        }

        [TestCase(RseBrowseNames.Acknowledge)]
        [TestCase(RseBrowseNames.Cancel)]
        [TestCase(RseBrowseNames.Abandon)]
        public async Task RevocationDuringControlDeniesStateChange(string method)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            if (method == RseBrowseNames.Acknowledge)
            {
                await test.FinishAsync().ConfigureAwait(false);
            }
            TaskCompletionSource<bool> entered = Signal();
            TaskCompletionSource<bool> release = Signal();
            test.Policy.ControlGate = async () =>
            {
                entered.TrySetResult(true);
                await release.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            };
            Task denied = test.RejectAsync(test.First, method,
                new OperationControlRequestDataType { ServerToken = token }, StatusCodes.BadUserAccessDenied);
            await entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Owners.Set(test.First.Session.Id, OtherOwner("authority"));
            release.TrySetResult(true);
            await denied.ConfigureAwait(false);
            test.Policy.ControlGate = null;
            Peer replacement = await test.AddPeerAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> unchanged =
                await test.GetAsync(replacement, token).ConfigureAwait(false);
            Assert.That(unchanged.Value.Operation.State, Is.EqualTo(method == RseBrowseNames.Acknowledge
                ? ServiceOperationState.OutcomeAvailable : ServiceOperationState.Pending));
            Assert.That(unchanged.Value.Operation.CancellationRequested, Is.False);
            unchanged.Dispose();
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> original =
                await test.GetAsync(replacement, token).ConfigureAwait(false);
            AssertOriginalAdd(original.Value.Result, test, 12);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task OptionalOwnerDenialPreservesSessionControl(bool securityModeDenial)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                test.First, test.AddRequest(mode: ServiceOperationRecoveryMode.Session)).ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Owners.Failure = securityModeDenial
                ? StatusCodes.BadSecurityModeRejected : StatusCodes.BadUserAccessDenied;
            using Reply<ControlResponseDataType> abandon = await test.ControlAsync(
                test.First, token, RseBrowseNames.Abandon).ConfigureAwait(false);
            Assert.That(abandon.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
            Assert.That(abandon.Value.Operation.State, Is.EqualTo(ServiceOperationState.Abandoned));
            Assert.That(abandon.Value.Operation.RecoveryMode, Is.EqualTo(ServiceOperationRecoveryMode.Session));
            Assert.That(abandon.Value.Operation.CancellationRequested, Is.False);
            Assert.That(test.Eligibility.Attempts, Is.Zero);
            abandon.Dispose();
            await test.FinishAsync().ConfigureAwait(false);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(12));
        }

        [TestCase(RseBrowseNames.Acknowledge)]
        [TestCase(RseBrowseNames.Cancel)]
        [TestCase(RseBrowseNames.Abandon)]
        public async Task OwnerAvailabilityFailureDoesNotBecomeUnknown(string method)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(test.First, test.AddRequest())
                .ConfigureAwait(false);
            ByteString token = admitted.Value.Operation.ServerToken;
            admitted.Dispose();
            await test.Server.Scenario.Entered.WaitAsync(test.Timeout).ConfigureAwait(false);
            test.Owners.Failure = StatusCodes.BadResourceUnavailable;
            foreach (ByteString selector in new[] { token, ByteString.From([0x73]) })
            {
                int calls = test.Owners.Resolutions;
                await test.RejectAsync(
                    test.First, method, new OperationControlRequestDataType { ServerToken = selector },
                    StatusCodes.BadResourceUnavailable).ConfigureAwait(false);
                Assert.That(test.Owners.Resolutions, Is.EqualTo(calls + 1),
                    "Owner resolution must run before token lookup, including unknown tokens.");
            }
            Assert.That(test.Server.OwnedContext!.CancellationToken.IsCancellationRequested, Is.False);
            test.Owners.Failure = null;
            await test.FinishAsync().ConfigureAwait(false);
            using Reply<GetResultResponseDataType> original =
                await test.GetAsync(test.First, token).ConfigureAwait(false);
            AssertOriginalAdd(original.Value.Result, test, 12);
            Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
        }

        [Test]
        public async Task OwnerAuditDoesNotExposeSyntheticCredentialsOrServerToken()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync(delay: false).ConfigureAwait(false);
            var audits = new ConcurrentQueue<AuditServiceOperationEventState>();
            ServerObjectState server = test.Server.CurrentInstance.DiagnosticsNodeManager
                .FindPredefinedNode<ServerObjectState>(ObjectIds.Server);
            void Observe(ISystemContext context, NodeState sender, IFilterTarget value)
            {
                if (value is AuditServiceOperationEventState audit)
                {
                    audits.Enqueue(audit);
                }
            }
            server.EventReported += Observe;
            try
            {
                using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                    test.First, test.AddRequest(wait: 5000)).ConfigureAwait(false);
                ByteString token = admitted.Value.Operation.ServerToken;
                AssertOriginalAdd(admitted.Value.Result, test, 12);
                admitted.Dispose();
                await test.FinishAsync().ConfigureAwait(false);
                using Reply<ControlResponseDataType> ack = await test.ControlAsync(
                    test.First, token, RseBrowseNames.Acknowledge).ConfigureAwait(false);
                Assert.That(ack.Value.Result, Is.EqualTo(ServiceOperationControlResult.Applied));
                Assert.That(audits.Select(value => value.Action!.Value),
                    Does.Contain(ServiceOperationAuditAction.Admitted)
                        .And.Contain(ServiceOperationAuditAction.OutcomeCommitted)
                        .And.Contain(ServiceOperationAuditAction.Acknowledged));
                foreach (AuditServiceOperationEventState audit in audits)
                {
                    Assert.That(audit.SessionId!.Value, Is.EqualTo(test.First.Session.Id));
                    Assert.That(audit.RecoveryMode!.Value, Is.EqualTo(ServiceOperationRecoveryMode.Owner));
                    var children = new List<BaseInstanceState>();
                    audit.GetChildren(test.Server.CurrentInstance.DefaultSystemContext, children);
                    Assert.That(children.Select(child => child.BrowseName.Name),
                        Does.Not.Contain("ServerToken").And.Not.Contain("AuthenticationToken")
                            .And.Not.Contain("Identity").And.Not.Contain("ContinuityContext")
                            .And.Not.Contain("Request").And.Not.Contain("Result"));
                }
                Assert.That(test.Logs.Records, Is.Not.Empty, "Check an active logger, not a vacuous empty capture.");
                string base64Token = Convert.ToBase64String(token.ToArray());
#if NET8_0_OR_GREATER
                string hexToken = Convert.ToHexString(token.Span);
#else
                string hexToken = BitConverter.ToString(token.ToArray()).Replace("-", string.Empty);
#endif
                byte[] passwordBytes = Encoding.UTF8.GetBytes(kSyntheticPassword);
                foreach (RecordedLogRecord record in test.Logs.Records)
                {
                    string rendered = record.Message +
                        record.Exception +
                        string.Join(";", record.Properties.Select(pair => pair.Key + "=" + pair.Value));
                    Assert.That(rendered, Does.Not.Contain(kSyntheticPassword)
                        .And.Not.Contain(base64Token).And.Not.Contain(hexToken));
                    Assert.That(record.Properties.Keys,
                        Does.Not.Contain("ServerToken").And.Not.Contain("Password").And.Not.Contain("TokenData"));
                    foreach (object? value in record.Properties.Values)
                    {
                        Assert.That(value, Is.Not.InstanceOf<IUserIdentity>());
                        if (value is ByteString byteString)
                        {
                            Assert.That(byteString.Span.IndexOf(token.Span), Is.LessThan(0));
                            Assert.That(byteString.Span.IndexOf(passwordBytes.AsSpan()), Is.LessThan(0));
                        }
                        else if (value is byte[] bytes)
                        {
                            Assert.That(bytes.AsSpan().IndexOf(token.Span), Is.LessThan(0));
                            Assert.That(bytes.AsSpan().IndexOf(passwordBytes.AsSpan()), Is.LessThan(0));
                        }
                    }
                }
                Assert.That(test.Server.Scenario.Invocations, Is.EqualTo(1));
            }
            finally
            {
                server.EventReported -= Observe;
            }
        }

        [Test]
        public async Task QualifiedSelfContainedBrowseReturnsNoContinuation()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync(delay: false).ConfigureAwait(false);
            test.Eligibility.BeforeReturn = request =>
            {
                Assert.That(request, Is.InstanceOf<BrowseRequest>());
                var browse = (BrowseRequest)request;
                Assert.That(browse.RequestedMaxReferencesPerNode, Is.EqualTo(100u));
                Assert.That(browse.NodesToBrowse[0].NodeId, Is.EqualTo(test.Node(Model.ObjectIds.Probe)));
                return Task.CompletedTask;
            };
            using Reply<SubmitResponseDataType> admitted = await test.SubmitAsync(
                test.First, OwnerScope.Wrap(test.BrowseRequest(), wait: 5000)).ConfigureAwait(false);
            Assert.That(admitted.Value.Operation.State, Is.EqualTo(ServiceOperationState.OutcomeAvailable));
            Assert.That(admitted.Value.Result.TryGetValue(
                out BrowseResponse? response, test.Server.MessageContext), Is.True);
            Assert.That(response!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results, Has.Count.EqualTo(1));
            Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(response.Results[0].ContinuationPoint.Length, Is.Zero);
            Assert.That(response.Results[0].References.Span.ToArray().Select(item => ExpandedNodeId.ToNodeId(
                item.NodeId, test.Server.MessageContext.NamespaceUris)),
                Is.EquivalentTo((NodeId[])
                [
                    test.Node(Model.MethodIds.Probe_DeclaredAdd),
                    test.Node(Model.MethodIds.Probe_DelayedAdd)
                ]));
            admitted.Dispose();
            await test.FinishAsync().ConfigureAwait(false);
            Assert.That(test.Eligibility.Acquisitions, Is.EqualTo(1));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(test.Server.OwnedDispatches, Is.EqualTo(1));
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
        }

        [TestCase("browse-next-empty")]
        [TestCase("browse-next-token")]
        [TestCase("history-token")]
        public async Task KnownContinuationInputsRejectBeforeQualification(string kind)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            IServiceRequest original = kind == "history-token"
                ? new HistoryReadRequest
                {
                    RequestHeader = new RequestHeader(),
                    HistoryReadDetails = new ExtensionObject(new ReadRawModifiedDetails()),
                    NodesToRead =
                    [
                        new HistoryReadValueId
                        {
                            NodeId = test.Node(Model.VariableIds.Probe_Counter),
                            ContinuationPoint = ByteString.From([1])
                        }
                    ]
                }
                : new BrowseNextRequest
                {
                    RequestHeader = new RequestHeader(),
                    ContinuationPoints = kind == "browse-next-token" ? [ByteString.From([1])] : []
                };

            await test.RejectAsync(test.First, RseBrowseNames.Submit, OwnerScope.Wrap(original),
                StatusCodes.BadServiceUnsupported).ConfigureAwait(false);
            Assert.That(test.Eligibility.Attempts, Is.Zero);
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyHistoryContinuationStillRequiresApplicationQualification(bool emptyRatherThanNull)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            test.Eligibility.Rejection = StatusCodes.BadServiceUnsupported;
            var original = new HistoryReadRequest
            {
                RequestHeader = new RequestHeader(),
                HistoryReadDetails = new ExtensionObject(new ReadRawModifiedDetails()),
                NodesToRead =
                [
                    new HistoryReadValueId
                    {
                        NodeId = test.Node(Model.VariableIds.Probe_Counter),
                        ContinuationPoint = emptyRatherThanNull ? ByteString.Empty : default
                    }
                ]
            };
            await test.RejectAsync(test.First, RseBrowseNames.Submit, OwnerScope.Wrap(original),
                StatusCodes.BadServiceUnsupported).ConfigureAwait(false);
            Assert.That(test.Eligibility.Attempts, Is.EqualTo(1),
                "Only nonempty continuation input is a known resource; an empty input still needs qualification.");
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task QueryFamiliesRemainUnsupportedInThisPrototype(bool next)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            IServiceRequest original = next
                ? new QueryNextRequest { RequestHeader = new RequestHeader() }
                : new QueryFirstRequest { RequestHeader = new RequestHeader() };
            await test.RejectAsync(test.First, RseBrowseNames.Submit, OwnerScope.Wrap(original),
                StatusCodes.BadServiceUnsupported).ConfigureAwait(false);
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Eligibility.Attempts, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task QualificationMutationOrOwnerRemapRejectsBeforeDispatch(bool remapOwner)
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            TaskCompletionSource<bool> release = Signal();
            test.Eligibility.BeforeReturn = async original =>
            {
                await release.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                if (remapOwner)
                {
                    test.Owners.Set(test.First.Session.Id, OtherOwner("application"));
                }
                else
                {
                    ((CallRequest)original).MethodsToCall[0].InputArguments = [new Variant(500)];
                }
            };
            SubmitRequestDataType submitted = test.AddRequest();
            Task rejected = test.RejectAsync(test.First, RseBrowseNames.Submit, submitted,
                StatusCodes.BadServiceUnsupported);
            await test.Eligibility.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            release.TrySetResult(true);
            await rejected.ConfigureAwait(false);

            Assert.That(test.Eligibility.Acquisitions, Is.EqualTo(1));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(test.Eligibility.Active, Is.Zero);
            Assert.That(test.Provider.GetStatistics().AdmittedCount, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            Assert.That(submitted.Request.TryGetValue(out CallRequest? unchanged), Is.True);
            Assert.That(unchanged!.MethodsToCall[0].InputArguments[0], Is.EqualTo(new Variant(5)),
                "Eligibility receives a frozen copy, never the caller's original object.");
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task IdentityRevocationDuringQualificationReleasesReservationBeforeDispatch()
        {
            await using OwnerScope test = await OwnerScope.CreateAsync().ConfigureAwait(false);
            TaskCompletionSource<bool> release = Signal();
            test.Eligibility.BeforeReturn = _ => release.Task.WaitAsync(test.Timeout);
            Task rejected = test.RejectAsync(test.First, RseBrowseNames.Submit, test.AddRequest(),
                StatusCodes.BadUserAccessDenied);
            try
            {
                await test.Eligibility.Entered.Task.WaitAsync(test.Timeout).ConfigureAwait(false);
                test.First.Session.RefreshEffectiveIdentity(new SyntheticOwnerIdentity
                {
                    TokenHandler = test.First.Identity.TokenHandler,
                    EffectiveRoles = [ObjectIds.WellKnownRole_Anonymous]
                });
            }
            finally
            {
                release.TrySetResult(true);
            }
            await rejected.ConfigureAwait(false);
            Assert.That(test.Eligibility.Acquisitions, Is.EqualTo(1));
            Assert.That(test.Eligibility.Releases, Is.EqualTo(1));
            Assert.That(test.Provider.GetStatistics().AdmittedCount, Is.Zero);
            Assert.That(test.Server.OwnedDispatches, Is.Zero);
            Assert.That(test.Server.Scenario.Invocations, Is.Zero);
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(7));
            await test.AssertNoReservationsAsync().ConfigureAwait(false);
        }

        private static void AssertOriginalAdd(ExtensionObject result, OwnerScope test, int expected)
        {
            Assert.That(result.TryGetValue(out CallResponse? original, test.Server.MessageContext), Is.True);
            Assert.That(original!.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(original.ResponseHeader.RequestHandle, Is.EqualTo(71u));
            Assert.That(original.Results, Has.Count.EqualTo(1));
            Assert.That(original.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(original.Results[0].OutputArguments, Has.Count.EqualTo(1));
            Assert.That(original.Results[0].OutputArguments[0].TryGetValue(out int value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
            Assert.That(test.Server.Scenario.Counter, Is.EqualTo(expected));
        }

        private static void AssertUnknown(
            ServiceOperationInfoDataType operation, ExtensionObject result, Uuid clientId = default)
        {
            Assert.That(operation.State, Is.EqualTo(ServiceOperationState.Unknown));
            Assert.That(operation.ClientOperationId, Is.EqualTo(clientId));
            Assert.That(operation.ServerToken.IsNull, Is.True);
            Assert.That(operation.ServiceId.IsNull, Is.True);
            Assert.That(operation.OutcomeKind, Is.EqualTo(ServiceOperationOutcome.None));
            Assert.That(operation.OutcomeStatus, Is.EqualTo(StatusCodes.Good));
            Assert.That(operation.ExecutionAllowance, Is.Zero);
            Assert.That(operation.ResultRetentionTime, Is.Zero);
            Assert.That(operation.ExecutionRemaining, Is.Zero);
            Assert.That(operation.ResultRemaining, Is.Zero);
            Assert.That(operation.HistoryRemaining, Is.Zero);
            Assert.That(operation.CancellationRequested, Is.False);
            Assert.That(result.IsNull, Is.True);
        }

        private static async Task AssertRunningReservationsAsync(OwnerScope test)
        {
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 1).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 1).ConfigureAwait(false);
            await test.WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, (4L * kLimit) + 1024)
                .ConfigureAwait(false);
            Assert.That(test.Eligibility.Active, Is.EqualTo(1));
            Assert.That(test.Eligibility.Releases, Is.Zero);
            Assert.That(test.Provider.GetStatistics().ActiveOperationCount, Is.EqualTo(1));
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestExecution), Is.EqualTo(1));
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.ParkedRequest), Is.EqualTo(1));
            Assert.That(test.Isolation.GetUsage(ResourceIsolationStage.RequestQueueBytes),
                Is.EqualTo((4L * kLimit) + 1024));
        }

        private static void AssertFailure(IServiceResponse response, StatusCode status)
        {
            if (response is ServiceFault fault)
            {
                Assert.That(fault.ResponseHeader.ServiceResult, Is.EqualTo(status));
                return;
            }
            Assert.That(response, Is.InstanceOf<CallResponse>());
            var call = (CallResponse)response;
            Assert.That(call.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
            Assert.That(call.Results, Has.Count.EqualTo(1));
            Assert.That(call.Results[0].StatusCode, Is.EqualTo(status));
            Assert.That(call.Results[0].OutputArguments, Is.Empty);
        }

        private static async Task WaitForCancellationAsync(CancellationToken work, CancellationToken watchdog)
        {
            TaskCompletionSource<bool> cancelled = Signal();
            using CancellationTokenRegistration registration = work.Register(() => cancelled.TrySetResult(true));
            await cancelled.Task.WaitAsync(watchdog).ConfigureAwait(false);
        }

        private static RecoverableExecutionOwnerId OtherOwner(string part)
        {
            return new RecoverableExecutionOwnerId(
                part == "authority" ? s_other : s_owner.AuthorityId,
                part == "application" ? s_other : s_owner.ApplicationId,
                part == "user" ? s_other : s_owner.UserId);
        }

        private static TaskCompletionSource<bool> Signal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed record Peer(
            RequestHeader Header, SecureChannelContext Channel, ServerSession Session, SyntheticOwnerIdentity Identity);

        /// <summary>
        /// Only transport admission is mocked. The SessionEndpoint, manager Methods, generated model
        /// and handlers are real.
        /// </summary>
        private sealed class OwnerScope : IAsyncDisposable
        {
            private OwnerScope(
                ServerFixture<OwnerServer> fixture, OwnerServer server, InjectedOwners owners,
                Qualification eligibility, CurrentPolicy policy, FakeTimeProvider clock, RecordingLoggerProvider logs)
            {
                m_fixture = fixture;
                Server = server;
                Owners = owners;
                Eligibility = eligibility;
                Policy = policy;
                Clock = clock;
                Logs = logs;
                m_endpoint = new SessionEndpoint(server);
            }

            public OwnerServer Server { get; }
            public InjectedOwners Owners { get; }
            public Qualification Eligibility { get; }
            public CurrentPolicy Policy { get; }
            public FakeTimeProvider Clock { get; }
            public RecordingLoggerProvider Logs { get; }
            public Peer First { get; private set; } = null!;
            public CancellationToken Timeout => m_watchdog.Token;
            public ApplicationConfiguration Configuration => m_fixture.Config;
            public AsyncServiceExecutionProvider Provider => Server.Factory.Provider!;

            public DefaultServerResourceIsolationProvider Isolation =>
                (DefaultServerResourceIsolationProvider)Server.ResourceIsolationProvider!;

            public static async Task<OwnerScope> CreateAsync(
                bool delay = true, bool ownerMode = true, long? ownerExecutionLimit = null)
            {
                var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
                var owners = new InjectedOwners();
                var qualification = new Qualification();
                var policy = new CurrentPolicy();
                var logs = new RecordingLoggerProvider();
                ITelemetryContext telemetry = DefaultTelemetry.Create(
                    builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
                var registry = new DefaultTransportBindingRegistry();
                registry.RegisterListenerFactory(new NoSocketListenerFactory());
                var fixture = new ServerFixture<OwnerServer>(_ =>
                {
                    var server = new OwnerServer(telemetry, clock, owners, qualification, policy, ownerMode);
                    server.Scenario.Delay = delay;
                    if (ownerExecutionLimit.HasValue)
                    {
                        server.ResourceIsolationOptions.Stages =
                        [
                            new ResourceIsolationStageOptions
                            {
                                Stage = ResourceIsolationStage.RequestExecution,
                                OwnerHardLimit = ownerExecutionLimit.Value
                            }
                        ];
                    }
                    return server;
                })
                {
                    SecurityNone = true,
                    AutoAccept = true,
                    AllNodeManagers = false,
                    TransportBindingRegistry = registry
                };
                try
                {
                    await fixture.LoadConfigurationAsync().ConfigureAwait(false);
                    fixture.Config.TransportQuotas!.MaxMessageSize = kLimit;
                    fixture.Config.ServerConfiguration!.AuditingEnabled = true;
                    fixture.Config.ServerConfiguration.UserTokenPolicies =
                    [
                        new UserTokenPolicy
                        {
                            PolicyId = "synthetic-policy", TokenType = UserTokenType.UserName,
                            SecurityPolicyUri = SecurityPolicies.None
                        }
                    ];
                    // Positive metadata-only port avoids ServerFixture's free-port socket probe.
                    OwnerServer server = await fixture.StartAsync(port: 1).ConfigureAwait(false);
                    server.CurrentInstance.IdentityRegistry.Register(new UserNamePasswordAuthenticator((handler, ct) =>
                    {
                        // The real configurable authenticator accepts only these synthetic test credentials.
                        // The injected Owner resolver below still isolates lifetime behavior, not authentication.
                        ct.ThrowIfCancellationRequested();
                        if (handler.UserName is not ("operator" or "different-creator") ||
                            handler.Token is not UserNameIdentityToken token ||
                            token.Password != new ByteString(Encoding.UTF8.GetBytes(kSyntheticPassword)))
                        {
                            throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                        }
                        var identity = new SyntheticOwnerIdentity
                        {
                            DisplayName = "synthetic-operator",
                            PolicyId = handler.Token.PolicyId!,
                            TokenHandler = handler.Copy(),
                            Issuer = "urn:synthetic:issuer",
                            Subject = handler.UserName,
                            EffectiveRoles =
                            [
                                ObjectIds.WellKnownRole_AuthenticatedUser,
                                ObjectIds.WellKnownRole_Operator
                            ],
                            ClaimGroups = ["original-group"]
                        };
                        identity.RawClaims["source"] = "original-claim";
                        return new ValueTask<IUserIdentity>(identity);
                    }));
                    var scope = new OwnerScope(fixture, server, owners, qualification, policy, clock, logs);
                    scope.First = await scope.AddPeerAsync().ConfigureAwait(false);
                    return scope;
                }
                catch
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                    logs.Dispose();
                    throw;
                }
            }

            public async Task<Peer> AddPeerAsync(RecoverableExecutionOwnerId? owner = null)
            {
                string channelId = "owner-runtime-" + Interlocked.Increment(ref m_nextPeer);
                (RequestHeader header, SecureChannelContext channel) = await Server.CreateAndActivateSessionAsync(
                    channelId, identityToken: IdentityToken("operator"), localeIds: ["de-DE", "en-US"])
                    .ConfigureAwait(false);
                var session = (ServerSession)Server.CurrentInstance.SessionManager
                    .GetSession(header.AuthenticationToken)!;
                Owners.Set(session.Id, owner ?? s_owner);
                return new Peer(header, channel, session, (SyntheticOwnerIdentity)session.Identity);
            }

            public async Task ReactivateAsync(Peer peer, string user)
            {
                IServiceResponse response = await m_endpoint.ProcessRequestAsync(peer.Channel,
                    new ActivateSessionRequest
                    {
                        RequestHeader = Header(peer),
                        LocaleIds = ["de-DE", "en-US"],
                        UserIdentityToken = new ExtensionObject(IdentityToken(user))
                    }, Timeout).ConfigureAwait(false);
                try
                {
                    Assert.That(response, Is.InstanceOf<ActivateSessionResponse>());
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                }
                finally
                {
                    (response as IPooledEncodeable)?.Reuse();
                }
            }

            public Task CloseAsync(Peer peer)
            {
                return Server.CloseSessionAsync(peer.Channel, Header(peer), Timeout);
            }

            public NodeId Node(ExpandedNodeId id)
            {
                return ExpandedNodeId.ToNodeId(id, Server.MessageContext.NamespaceUris);
            }

            public SubmitRequestDataType AddRequest(
                int amount = 5, double execution = 10_000, double wait = 0,
                ServiceOperationRecoveryMode mode = ServiceOperationRecoveryMode.Owner)
            {
                return Wrap(new CallRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 71, TimeoutHint = 1 },
                    MethodsToCall =
                    [
                        new CallMethodRequest
                        {
                            ObjectId = Node(Model.ObjectIds.Probe),
                            MethodId = Node(Model.MethodIds.Probe_DeclaredAdd),
                            InputArguments = [new Variant(amount)]
                        }
                    ]
                }, execution, wait, mode);
            }

            public BrowseRequest BrowseRequest(uint maximum = 100)
            {
                return new BrowseRequest
                {
                    RequestHeader = new RequestHeader { RequestHandle = 71, TimeoutHint = 1 },
                    RequestedMaxReferencesPerNode = maximum,
                    NodesToBrowse =
                    [
                        new BrowseDescription
                        {
                            NodeId = Node(Model.ObjectIds.Probe),
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.HasComponent,
                            IncludeSubtypes = true,
                            NodeClassMask = (uint)NodeClass.Method,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ]
                };
            }

            public static SubmitRequestDataType Wrap(
                IServiceRequest original, double execution = 10_000, double wait = 0,
                ServiceOperationRecoveryMode mode = ServiceOperationRecoveryMode.Owner)
            {
                return new SubmitRequestDataType
                {
                    ClientOperationId = Uuid.NewUuid(),
                    RecoveryMode = mode,
                    Request = new ExtensionObject(original),
                    ExecutionAllowance = execution,
                    ResultRetentionTime = 200,
                    WaitTime = wait
                };
            }

            public Task<Reply<SubmitResponseDataType>> SubmitAsync(Peer peer, SubmitRequestDataType request)
            {
                return InvokeAsync<SubmitResponseDataType>(peer, RseBrowseNames.Submit, request);
            }

            public Task<Reply<GetResultResponseDataType>> GetAsync(
                Peer peer, ByteString token = default, Uuid clientId = default,
                ServiceOperationRecoveryMode mode = ServiceOperationRecoveryMode.Owner)
            {
                return InvokeAsync<GetResultResponseDataType>(peer, RseBrowseNames.GetResult,
                    new GetResultRequestDataType
                    {
                        RecoveryMode = mode,
                        ServerToken = token,
                        ClientOperationId = clientId
                    });
            }

            public Task<Reply<ControlResponseDataType>> ControlAsync(Peer peer, ByteString token, string method)
            {
                return InvokeAsync<ControlResponseDataType>(peer, method,
                    new OperationControlRequestDataType { ServerToken = token });
            }

            public async Task<IServiceResponse> SendAsync(
                Peer peer, string method, IEncodeable argument, RequestHeader? header = null,
                CancellationToken cancellationToken = default)
            {
                RecoverableExecutionManagerState manager = Server.CurrentInstance.DiagnosticsNodeManager
                    .FindPredefinedNode<RecoverableExecutionManagerState>(
                        Node(RseObjectIds.RecoverableExecutionManager));
                MethodState target = method switch
                {
                    RseBrowseNames.Submit => manager.Submit!,
                    RseBrowseNames.GetResult => manager.GetResult!,
                    RseBrowseNames.Acknowledge => manager.Acknowledge!,
                    RseBrowseNames.Cancel => manager.Cancel!,
                    RseBrowseNames.Abandon => manager.Abandon!,
                    _ => throw new ArgumentException("Not an RSE manager Method.", nameof(method))
                };
                return await m_endpoint.ProcessRequestAsync(peer.Channel, new CallRequest
                {
                    RequestHeader = header ?? Header(peer),
                    MethodsToCall =
                    [
                        new CallMethodRequest
                        {
                            ObjectId = manager.NodeId, MethodId = target.NodeId,
                            InputArguments = [Variant.FromStructure(argument)]
                        }
                    ]
                }, cancellationToken.CanBeCanceled ? cancellationToken : Timeout).ConfigureAwait(false);
            }

            public async Task<uint> CancelExchangeAsync(Peer peer, uint requestHandle)
            {
                IServiceResponse response = await m_endpoint.ProcessRequestAsync(peer.Channel, new CancelRequest
                {
                    RequestHeader = Header(peer),
                    RequestHandle = requestHandle
                }, Timeout).ConfigureAwait(false);
                try
                {
                    Assert.That(response, Is.InstanceOf<CancelResponse>());
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    return ((CancelResponse)response).CancelCount;
                }
                finally
                {
                    (response as IPooledEncodeable)?.Reuse();
                }
            }

            public async Task RejectAsync(Peer peer, string method, IEncodeable argument, StatusCode expected)
            {
                IServiceResponse response = await SendAsync(peer, method, argument).ConfigureAwait(false);
                try
                {
                    Assert.That(response, Is.InstanceOf<CallResponse>(),
                        "An admission/control denial must remain a Method result in the ordinary Call.");
                    AssertFailure(response, expected);
                }
                finally
                {
                    (response as IPooledEncodeable)?.Reuse();
                }
            }

            private async Task<Reply<T>> InvokeAsync<T>(Peer peer, string method, IEncodeable argument)
                where T : class, IEncodeable
            {
                IServiceResponse response = await SendAsync(peer, method, argument).ConfigureAwait(false);
                Assert.That(response, Is.InstanceOf<CallResponse>(), response.ResponseHeader.ServiceResult.ToString());
                return new Reply<T>((CallResponse)response, Server.MessageContext);
            }

            public async Task FinishAsync()
            {
                Server.ValidationRelease?.TrySetResult(true);
                Server.Scenario.Release();
                await WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
            }

            public async Task AssertNoReservationsAsync()
            {
                await WaitForUsageAsync(ResourceIsolationStage.RequestExecution, 0).ConfigureAwait(false);
                await WaitForUsageAsync(ResourceIsolationStage.ParkedRequest, 0).ConfigureAwait(false);
                await WaitForUsageAsync(ResourceIsolationStage.RequestQueueBytes, 0).ConfigureAwait(false);
                Assert.That(Eligibility.Active, Is.Zero);
                Assert.That(Provider.GetStatistics().ActiveOperationCount, Is.Zero);
            }

            public async Task WaitForUsageAsync(ResourceIsolationStage stage, long expected)
            {
                TaskCompletionSource<bool> reached = Signal();
                void Changed(ResourceIsolationStage released)
                {
                    if (released == stage && Isolation.GetUsage(stage) == expected)
                    {
                        reached.TrySetResult(true);
                    }
                }
                Isolation.CapacityAvailable += Changed;
                try
                {
                    Changed(stage);
                    await reached.Task.WaitAsync(Timeout).ConfigureAwait(false);
                    Assert.That(Isolation.GetUsage(stage), Is.EqualTo(expected));
                }
                finally
                {
                    Isolation.CapacityAvailable -= Changed;
                }
            }

            public async ValueTask DisposeAsync()
            {
                Server.ValidationRelease?.TrySetResult(true);
                Server.Scenario.Release();
                try
                {
                    await m_fixture.StopAsync().ConfigureAwait(false);
                }
                finally
                {
                    m_watchdog.Dispose();
                    Logs.Dispose();
                }
            }

            public RequestHeader Header(Peer peer)
            {
                return new RequestHeader
                {
                    AuthenticationToken = peer.Header.AuthenticationToken,
                    RequestHandle = (uint)Interlocked.Increment(ref m_nextHandle),
                    Timestamp = Clock.GetUtcNow().UtcDateTime,
                    TimeoutHint = 15_000
                };
            }

            private UserNameIdentityToken IdentityToken(string user)
            {
                UserTokenPolicy policy = Server.GetEndpoints().Span.ToArray()
                    .SelectMany(endpoint => endpoint.UserIdentityTokens.Span.ToArray())
                    .First(candidate => candidate.TokenType == UserTokenType.UserName &&
                        candidate.SecurityPolicyUri == SecurityPolicies.None);
                return new UserNameIdentityToken
                {
                    PolicyId = policy.PolicyId,
                    UserName = user,
                    Password = new ByteString(Encoding.UTF8.GetBytes(kSyntheticPassword))
                };
            }

            private readonly ServerFixture<OwnerServer> m_fixture;
            private readonly SessionEndpoint m_endpoint;
            private readonly CancellationTokenSource m_watchdog = new(TimeSpan.FromSeconds(20));
            private int m_nextPeer;
            private int m_nextHandle;
        }

        private sealed class Reply<T> : IDisposable where T : class, IEncodeable
        {
            public Reply(CallResponse response, IServiceMessageContext context)
            {
                m_response = response;
                try
                {
                    Assert.That(response.ResponseHeader.ServiceResult, Is.EqualTo(StatusCodes.Good));
                    Assert.That(response.Results, Has.Count.EqualTo(1));
                    Assert.That(response.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(response.Results[0].OutputArguments, Has.Count.EqualTo(1));
                    Assert.That(response.Results[0].OutputArguments[0].TryGetValue(out ExtensionObject body), Is.True);
                    Assert.That(body.TryGetValue(out T? value, context), Is.True);
                    Value = value!;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public T Value { get; }

            public void Dispose()
            {
                Interlocked.Exchange(ref m_response, null)?.Reuse();
            }

            private CallResponse? m_response;
        }

        private sealed class OwnerServer : StandardServer
        {
            public OwnerServer(
                ITelemetryContext telemetry, TimeProvider clock, InjectedOwners owners, Qualification eligibility,
                CurrentPolicy policy, bool ownerMode)
                : base(telemetry, clock)
            {
                AsyncServices.Enabled = true;
                AsyncServices.RecoveryModes = ownerMode
                    ? [ServiceOperationRecoveryMode.Session, ServiceOperationRecoveryMode.Owner]
                    : [ServiceOperationRecoveryMode.Session];
                Factory = new ExecutionFactory(owners, eligibility, policy);
                AsyncServiceExecutionProviderFactory = Factory;
                AddNodeManager(new AsyncExecutionScenarioFactory(Scenario));
            }

            public ExecutionFactory Factory { get; }

            public AsyncExecutionScenario Scenario { get; } = new()
            {
                Delay = true,
                IgnoreCancellation = true,
                RecoverableHint = true
            };

            public TaskCompletionSource<bool> ValidationEntered { get; } = Signal();
            public TaskCompletionSource<bool>? ValidationRelease { get; set; }
            public bool ValidationFailure { get; set; }
            public OperationContext? OwnedContext { get; private set; }
            public int OwnedDispatches => Volatile.Read(ref m_dispatches);

            protected override async ValueTask OnRequestValidatedAsync(OperationContext context)
            {
                if (context.IsOwnedInvocation)
                {
                    OwnedContext = context;
                    Interlocked.Increment(ref m_dispatches);
                    ValidationEntered.TrySetResult(true);
                    if (ValidationRelease != null)
                    {
                        // Intentionally non-cooperative to check cancellation again after the validation hook.
                        await ValidationRelease.Task.ConfigureAwait(false);
                    }
                    if (ValidationFailure)
                    {
                        throw new ServiceResultException(StatusCodes.BadUnexpectedError);
                    }
                }
                await base.OnRequestValidatedAsync(context).ConfigureAwait(false);
            }

            private int m_dispatches;
        }

        private sealed class ExecutionFactory(
            InjectedOwners owners, Qualification eligibility, CurrentPolicy policy)
            : IAsyncServiceExecutionProviderFactory
        {
            public AsyncServiceExecutionProvider? Provider { get; private set; }

            public IAsyncServiceExecutionProvider Create(
                StandardServer server, ApplicationConfiguration configuration,
                AsyncServiceExecutionOptions options, TimeProvider timeProvider)
            {
                Provider = new AsyncServiceExecutionProvider(
                    server, configuration, options, timeProvider, policy, owners, eligibility);
                return Provider;
            }
        }

        /// <summary>
        /// An explicit test seam for identity changes, not a replacement for configured authentication/mapping.
        /// </summary>
        private sealed class InjectedOwners : IRecoverableExecutionOwnerProvider
        {
            public StatusCode? Failure { get; set; }
            public ByteString Evidence { get; set; } = ByteString.From([1]);
            public int Resolutions => Volatile.Read(ref m_resolutions);

            public void Set(NodeId sessionId, RecoverableExecutionOwnerId owner)
            {
                m_owners[sessionId] = owner;
            }

            public ValueTask<RecoverableExecutionOwner> ResolveAsync(
                OperationContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_resolutions);
                if (Failure is { } failure)
                {
                    throw new ServiceResultException(failure);
                }
                if (!m_owners.TryGetValue(context.SessionId, out RecoverableExecutionOwnerId owner))
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }
                return new ValueTask<RecoverableExecutionOwner>(new RecoverableExecutionOwner(owner, Evidence));
            }

            public ValueTask ValidatePersistedOwnerAsync(
                RecoverableExecutionOwner owner, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Failure is { } failure)
                {
                    throw new ServiceResultException(failure);
                }
                return default;
            }

            private readonly ConcurrentDictionary<NodeId, RecoverableExecutionOwnerId> m_owners = new();
            private int m_resolutions;
        }

        /// <summary>
        /// Qualifies known synthetic self-contained routes before dispatch and exposes
        /// the reservation's actual lifetime.
        /// </summary>
        private sealed class Qualification : IRecoverableExecutionEligibilityProvider
        {
            public TaskCompletionSource<bool> Entered { get; } = Signal();
            public Func<IServiceRequest, Task>? BeforeReturn { get; set; }
            public Func<OperationContext, Task>? AfterAcquire { get; set; }
            public StatusCode? Rejection { get; set; }
            public int Attempts => Volatile.Read(ref m_attempts);
            public int Acquisitions => Volatile.Read(ref m_acquisitions);
            public int Releases => Volatile.Read(ref m_releases);
            public int Active => Acquisitions - Releases;

            public async ValueTask<IAsyncDisposable> AcquireAsync(
                OperationContext context, IServiceRequest request, ServiceOperationRecoveryMode mode,
                RecoverableExecutionOwner owner, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_attempts);
                Entered.TrySetResult(true);
                Assert.That(mode, Is.EqualTo(ServiceOperationRecoveryMode.Owner));
                Assert.That(context.Session, Is.Not.Null);
                if (Rejection is { } rejection)
                {
                    throw new ServiceResultException(rejection);
                }
                if (BeforeReturn != null)
                {
                    await BeforeReturn(request).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref m_acquisitions);
                var reservation = new Reservation(this);
                try
                {
                    if (AfterAcquire != null)
                    {
                        await AfterAcquire(context).ConfigureAwait(false);
                    }
                    return reservation;
                }
                catch
                {
                    await reservation.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            private sealed class Reservation(Qualification owner) : IAsyncDisposable
            {
                public ValueTask DisposeAsync()
                {
                    // Count every call: an idempotent fake or an assertion swallowed by production logging
                    // would hide double-disposal. The calling test asserts the exact release count.
                    Interlocked.Increment(ref owner.m_releases);
                    return default;
                }
            }

            private int m_attempts;
            private int m_acquisitions;
            private int m_releases;
        }

        private sealed class CurrentPolicy : IAsyncServiceAuthorizationPolicy
        {
            public TaskCompletionSource<bool>? CollectionEntered { get; set; }
            public Func<Task>? ControlGate { get; set; }
            public int CollectionChecks => Volatile.Read(ref m_collectionChecks);

            public async ValueTask AuthorizeAsync(
                OperationContext context, Uuid clientOperationId, NodeId serviceId,
                AsyncServiceAccess access, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (access == AsyncServiceAccess.Collect)
                {
                    Interlocked.Increment(ref m_collectionChecks);
                    CollectionEntered?.TrySetResult(true);
                }
                if (access is AsyncServiceAccess.Acknowledge or AsyncServiceAccess.Cancel or
                    AsyncServiceAccess.Abandon &&
                    ControlGate != null)
                {
                    await ControlGate().ConfigureAwait(false);
                }
            }

            private int m_collectionChecks;
        }

        /// <summary>
        /// Uses the real endpoint-description builder but no socket, port binding or fabricated service response.
        /// </summary>
        private sealed class NoSocketListenerFactory : TcpServiceHost
        {
            public override string UriScheme => Utils.UriSchemeOpcTcp;

            public override ITransportListener Create(ITelemetryContext telemetry)
            {
                var listener = new Mock<ITransportListener>();
                listener.SetupGet(value => value.ListenerId).Returns("owner-runtime-no-socket");
                listener.SetupGet(value => value.UriScheme).Returns(Utils.UriSchemeOpcTcp);
                return listener.Object;
            }
        }

        private const int kLimit = 65_536;
        private const string kSyntheticPassword = "RSE-SYNTHETIC-NONPRODUCTION-PASSWORD";
        private static readonly string[] s_locales = ["de-DE", "en-US"];
        private static readonly string[] s_groups = ["original-group"];

        private static readonly RecoverableExecutionOwnerId s_owner = new(
            new Uuid("51000000-0000-0000-0000-000000000001"),
            new Uuid("52000000-0000-0000-0000-000000000001"),
            new Uuid("53000000-0000-0000-0000-000000000001"));

        private static readonly Uuid s_other = new("54000000-0000-0000-0000-000000000001");
    }
}
