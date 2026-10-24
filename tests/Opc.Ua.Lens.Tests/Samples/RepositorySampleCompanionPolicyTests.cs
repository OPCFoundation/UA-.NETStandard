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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using UaLens.Samples;

namespace UaLens.Tests.Samples
{
    [TestFixture]
    public sealed class RepositorySampleCompanionPolicyTests
    {
        [Test]
        public async Task ExactOptedInLivePeerGetsAFixedGrantWithoutStartingAnythingAsync()
        {
            var context = new PolicyContext();
            WeighingSimulationGrant first = await context.Policy.AuthorizeAsync(
                context.Context, context.Target, "set-zero", CancellationToken.None).ConfigureAwait(false);
            context.Clock.Now += TimeSpan.FromSeconds(10);
            WeighingSimulationGrant second = await context.Policy.AuthorizeAsync(
                context.Context, context.Target, "set-zero", CancellationToken.None).ConfigureAwait(false);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.RunId, Is.EqualTo(context.Snapshot.RunId));
            Assert.That(first.ExpiresAt, Is.EqualTo(context.Snapshot.CompanionOperationsExpireAt));
            context.Samples.VerifyGet(value => value.Snapshot, Times.Exactly(4));
            context.Samples.VerifyNoOtherCalls();
        }

        [TestCase("prepare-sample")]
        [TestCase("upload-package-sample")]
        [TestCase("install-package-sample")]
        [TestCase("abort-prepare-sample")]
        [TestCase("resume-prepare-sample")]
        [TestCase("resume-installation-sample")]
        [TestCase("confirm-sample")]
        public async Task DiOperationsRequireTheOptedInOwnedUpdateSimulatorAsync(string operation)
        {
            var context = new PolicyContext();
            context.Snapshot = context.Snapshot with { Selection = RepositorySampleId.PumpSoftwareUpdateSimulator };
            var target = context.Target with { ProviderId = "di", TypeName = "Software update" };
            var draft = new CompanionOperationDraft(target,
                new CompanionOperation(operation, operation, CompanionOperationSafety.DeploymentMutation),
                null, context.Session.Object, context.Clock.Now.AddSeconds(20));

            CompanionDeploymentGrant grant = await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false);

            Assert.That(grant.RuleId, Is.EqualTo("owned-di-" + context.Snapshot.RunId.ToString("N")));
            Assert.That(grant.ExpiresAt, Is.EqualTo(draft.ExpiresAt));
            context.Snapshot = context.Snapshot with { CompanionOperationsAuthorized = false };
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            context.Snapshot = context.Snapshot with
            {
                CompanionOperationsAuthorized = true,
                Selection = RepositorySampleId.PumpsServer
            };
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [TestCase("not-opted")]
        [TestCase("wrong-sample")]
        [TestCase("starting")]
        [TestCase("stopped")]
        [TestCase("not-owned")]
        [TestCase("empty-run")]
        [TestCase("expired")]
        [TestCase("no-deadline")]
        [TestCase("no-evidence")]
        [TestCase("wrong-peer")]
        [TestCase("wrong-url")]
        [TestCase("wrong-application")]
        [TestCase("none")]
        [TestCase("sign-only")]
        [TestCase("disconnected")]
        public async Task AuthorizationRequiresAllOwnedRunAndAuthenticatedPeerEvidenceAsync(string missing)
        {
            var context = new PolicyContext();
            switch (missing)
            {
                case "not-opted":
                    context.Snapshot = context.Snapshot with { CompanionOperationsAuthorized = false };
                    break;
                case "wrong-sample":
                    context.Snapshot = context.Snapshot with { Selection = RepositorySampleId.PumpsServer };
                    break;
                case "starting":
                    context.Snapshot = context.Snapshot with { Phase = RepositorySamplePhase.Starting };
                    break;
                case "stopped":
                    context.Snapshot = context.Snapshot with { Phase = RepositorySamplePhase.Stopped };
                    break;
                case "not-owned":
                    context.Snapshot = context.Snapshot with { OwnsResources = false };
                    break;
                case "empty-run":
                    context.Snapshot = context.Snapshot with { RunId = Guid.Empty };
                    break;
                case "expired":
                    context.Clock.Now = context.Snapshot.CompanionOperationsExpireAt!.Value;
                    break;
                case "no-deadline":
                    context.Snapshot = context.Snapshot with { CompanionOperationsExpireAt = null };
                    break;
                case "no-evidence":
                    context.Snapshot = context.Snapshot with { Evidence = null };
                    break;
                case "wrong-peer":
                    context.Endpoint.ServerCertificate = ByteString.From("different-peer"u8);
                    break;
                case "wrong-url":
                    context.Endpoint.EndpointUrl = "opc.tcp://localhost:58124/ScalesServer";
                    break;
                case "wrong-application":
                    context.Endpoint.Server!.ApplicationUri = "urn:not-the-owned-sample";
                    break;
                case "none":
                    context.Endpoint.SecurityPolicyUri = SecurityPolicies.None;
                    break;
                case "sign-only":
                    context.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "disconnected":
                    context.Session.SetupGet(value => value.Connected).Returns(false);
                    break;
                default:
                    throw new AssertionException("Unknown evidence partition.");
            }
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, context.Target, "set-zero", CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [TestCase("calibrate")]
        [TestCase("call")]
        [TestCase("start-machine")]
        [TestCase("SET-ZERO")]
        public async Task UnknownOrPhysicalOperationNamesCannotUseTheGrantAsync(string operation)
        {
            var context = new PolicyContext();
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, context.Target, operation, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task AStoppedRunCannotReturnAGrantFromEarlierEvidenceAsync()
        {
            var context = new PolicyContext();
            RepositorySampleSnapshot ready = context.Snapshot;
            context.Samples.SetupSequence(value => value.Snapshot)
                .Returns(ready)
                .Returns(ready with { CompanionOperationsAuthorized = false });
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, context.Target, "set-zero", CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task DeploymentGrantCannotOutlivePreparationAndOtherProvidersRemainDefaultDenyAsync()
        {
            var context = new PolicyContext();
            DateTimeOffset preparationExpiry = context.Clock.Now.AddSeconds(5);
            var draft = new CompanionOperationDraft(context.Target,
                new CompanionOperation("set-zero", "Set zero", CompanionOperationSafety.DeploymentMutation),
                null, context.Session.Object, preparationExpiry);
            CompanionDeploymentGrant grant = await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false);
            Assert.That(grant.ExpiresAt, Is.EqualTo(preparationExpiry));
            Assert.That(grant.RuleId, Does.Contain(context.Snapshot.RunId.ToString("N")));
            var other = new CompanionOperationDraft(context.Target with { ProviderId = "another-provider" },
                draft.Operation, null, context.Session.Object, preparationExpiry);
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, other, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task PackMlCommandsUseOnlyTheMatchingOwnedScaleRunAndStandardCommandListAsync()
        {
            var context = new PolicyContext();
            CompanionTarget target = context.Target with { ProviderId = "packml", TypeName = "PackML base" };
            var draft = new CompanionOperationDraft(
                target, new CompanionOperation("stop", "Stop", CompanionOperationSafety.DeploymentMutation),
                null, context.Session.Object, context.Clock.Now.AddSeconds(30));
            CompanionDeploymentGrant allowed = await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false);
            Assert.That(allowed.RuleId, Does.StartWith("owned-packml-"));
            var invalid = new CompanionOperationDraft(
                target, new CompanionOperation("complete-acting-state", "Pretend completion",
                    CompanionOperationSafety.DeploymentMutation),
                null, context.Session.Object, context.Clock.Now.AddSeconds(30));
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, invalid, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            context.Snapshot = context.Snapshot with { Selection = RepositorySampleId.AmbServer };
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task AssetMetadataGrantCannotAuthorizeAWeighingOrArbitraryOperationAsync()
        {
            var context = new PolicyContext();
            context.Endpoint.EndpointUrl = "opc.tcp://127.0.0.1:58124/AmbServer";
            context.Endpoint.Server!.ApplicationUri = "urn:owned:AmbServer";
            context.Snapshot = context.Snapshot with
            {
                Selection = RepositorySampleId.AmbServer,
                Evidence = new RepositorySampleEvidence(
                    new Uri(context.Endpoint.EndpointUrl), "urn:owned:AmbServer", "AmbServer",
                    "uri:opcfoundation.org:AmbServer", context.Snapshot.Evidence!.CertificateSha256)
            };
            CompanionTarget target = context.Target with { ProviderId = "amb", TypeName = "Managed asset" };
            var draft = new CompanionOperationDraft(
                target, new CompanionOperation("set-asset-id", "Set AssetId", CompanionOperationSafety.DeploymentMutation),
                "ASSET-2", context.Session.Object, context.Clock.Now.AddSeconds(30));
            CompanionDeploymentGrant grant = await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false);
            Assert.That(grant.RuleId, Does.StartWith("owned-amb-"));
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, target, "set-zero", CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
            var wrongOperation = new CompanionOperationDraft(
                target, new CompanionOperation("call", "Raw method", CompanionOperationSafety.DeploymentMutation),
                null, context.Session.Object, context.Clock.Now.AddSeconds(30));
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, wrongOperation, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task MachineryJobGrantRequiresVerifiedTwoFacetPreparationAsync(bool machineryBinding)
        {
            var context = new PolicyContext();
            context.Endpoint.EndpointUrl = "opc.tcp://127.0.0.1:58124/MachineryServer";
            context.Endpoint.Server!.ApplicationUri = "urn:owned:MachineryServer";
            context.Snapshot = context.Snapshot with
            {
                Selection = RepositorySampleId.MachineryServer,
                Evidence = context.Snapshot.Evidence! with
                {
                    Endpoint = new Uri(context.Endpoint.EndpointUrl),
                    ApplicationUri = context.Endpoint.Server.ApplicationUri
                }
            };
            var target = new CompanionTarget("isa95", new NodeId(1u), "JobOrderControl", "V2 order receiver");
            var task = Isa95JobTask.Find(true, "start-job");
            var binding = new Isa95TaskBinding(target.NodeId, new NodeId(2u), NodeId.Null)
            {
                MachineryOwner = machineryBinding ? new NodeId(3u) : NodeId.Null
            };
            var prepared = new Isa95PreparedTask(target, true, task, binding, new NodeId(4u), "job-one",
                Variant.From("job-one"), Variant.From(ArrayOf<LocalizedText>.Empty), null,
                ServiceMessageContext.Create(context.Context.Telemetry));
            var draft = new CompanionOperationDraft(target, task.Operation, null, context.Session.Object,
                context.Clock.Now.AddSeconds(30), prepared);
            if (machineryBinding)
            {
                CompanionDeploymentGrant grant = await context.Policy.AuthorizeAsync(
                    context.Context, draft, CancellationToken.None).ConfigureAwait(false);
                Assert.That(grant.RuleId, Does.StartWith("owned-isa95-"));
                Assert.That(grant.ExpiresAt, Is.EqualTo(draft.ExpiresAt));
                context.Snapshot = context.Snapshot with { CompanionOperationsAuthorized = false };
            }
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, draft, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        [Test]
        public async Task CancellationAndMissingSampleServiceCannotAuthorizeOrStartAProcessAsync()
        {
            var context = new PolicyContext();
            await Assert.ThatAsync(async () => await context.Policy.AuthorizeAsync(
                context.Context, context.Target, "set-zero", new CancellationToken(true)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            context.Samples.VerifyNoOtherCalls();
            var unavailable = new RepositorySampleCompanionPolicy(null);
            await Assert.ThatAsync(async () => await unavailable.AuthorizeAsync(
                context.Context, context.Target, "set-zero", CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }

        private sealed class PolicyContext
        {
            public PolicyContext()
            {
                ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
                var namespaces = new NamespaceTable();
                Endpoint = new EndpointDescription("opc.tcp://127.0.0.1:58124/ScalesServer")
                {
                    Server = new ApplicationDescription { ApplicationUri = "urn:owned:ScalesServer" },
                    ServerCertificate = ByteString.From("owned-public-certificate"u8),
                    SecurityMode = MessageSecurityMode.SignAndEncrypt,
                    SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                };
                Snapshot = new RepositorySampleSnapshot(
                    RepositorySampleId.ScalesServer, RepositorySamplePhase.AdvertisedReady, "Owned")
                {
                    RunId = Guid.NewGuid(),
                    OwnsResources = true,
                    CompanionOperationsAuthorized = true,
                    CompanionOperationsExpireAt = Clock.Now.AddMinutes(1),
                    Evidence = new RepositorySampleEvidence(
                        new Uri(Endpoint.EndpointUrl ?? throw new AssertionException("The endpoint URL is absent.")),
                        Endpoint.Server.ApplicationUri, "ScalesServer",
                        "uri:opcfoundation.org:ScalesServer",
                        ByteString.From(SHA256.HashData(Endpoint.ServerCertificate.Span)))
                };
                Samples.SetupGet(value => value.Snapshot).Returns(() => Snapshot);
                Session.SetupGet(value => value.Connected).Returns(true);
                Session.SetupGet(value => value.Endpoint).Returns(Endpoint);
                Session.SetupGet(value => value.SessionId).Returns(new NodeId(1234u));
                Session.SetupGet(value => value.Identity).Returns(new UserIdentity());
                Session.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                Context = new CompanionContext(Session.Object, telemetry);
                Policy = new RepositorySampleCompanionPolicy(Samples.Object, timeProvider: Clock);
            }

            public PolicyClock Clock { get; } = new();
            public Mock<IRepositorySampleService> Samples { get; } = new(MockBehavior.Strict);
            public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
            public EndpointDescription Endpoint { get; }
            public RepositorySampleSnapshot Snapshot { get; set; }
            public CompanionContext Context { get; }
            public CompanionTarget Target { get; } = new("weighing", new NodeId(1u), "Scale", "Scale");
            public RepositorySampleCompanionPolicy Policy { get; }
        }

        private sealed class PolicyClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

            public override DateTimeOffset GetUtcNow()
            {
                return Now;
            }
        }
    }
}
