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
using UaLens.Plugins.Companions;
using UaLens.Samples;

namespace UaLens.Tests.Companions;

[TestFixture]
public sealed class RoboticsAuthorizationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ExactSecureOwnedSampleGrantPinsRunAndExpiryAsync(bool draft)
    {
        var test = new AuthorizationContext(draft);
        CompanionDeploymentGrant grant = await test.Policy.AuthorizeRoboticsAsync(
            test.Services.Context, test.Draft, CancellationToken.None).ConfigureAwait(false);
        Assert.That(grant.RuleId, Is.EqualTo(
            "owned-" + test.Draft.Target.ProviderId + "-" + test.Snapshot.RunId.ToString("N")));
        Assert.That(grant.ExpiresAt, Is.EqualTo(test.Snapshot.CompanionOperationsExpireAt));
        Assert.That(grant.Revision, Is.EqualTo("owned-" + test.Draft.Target.ProviderId + "-v1"));
        Assert.That(test.Services.Services.Calls, Is.Empty);
    }

    [TestCase("wrong-sample")]
    [TestCase("no-opt-in")]
    [TestCase("stopped")]
    [TestCase("not-owned")]
    [TestCase("empty-run")]
    [TestCase("missing-evidence")]
    [TestCase("expired")]
    [TestCase("endpoint")]
    [TestCase("application")]
    [TestCase("peer")]
    [TestCase("none-security")]
    [TestCase("anonymous")]
    public async Task OwnedAuthorizationRejectsEveryMismatchedSamplePeerOrIdentityPartitionAsync(string invalid)
    {
        var test = new AuthorizationContext(true);
        switch (invalid)
        {
            case "wrong-sample":
                test.Snapshot = test.Snapshot with { Selection = RepositorySampleId.MinimalRobotServer };
                break;
            case "no-opt-in":
                test.Snapshot = test.Snapshot with { CompanionOperationsAuthorized = false };
                break;
            case "stopped":
                test.Snapshot = test.Snapshot with { Phase = RepositorySamplePhase.Configured };
                break;
            case "not-owned":
                test.Snapshot = test.Snapshot with { OwnsResources = false };
                break;
            case "empty-run":
                test.Snapshot = test.Snapshot with { RunId = Guid.Empty };
                break;
            case "missing-evidence":
                test.Snapshot = test.Snapshot with { Evidence = null };
                break;
            case "expired":
                test.Services.Clock.Now = test.Snapshot.CompanionOperationsExpireAt!.Value;
                break;
            case "endpoint":
                test.Services.Endpoint.EndpointUrl = "opc.tcp://localhost:4841/OtherRobot";
                break;
            case "application":
                test.Services.Endpoint.Server.ApplicationUri = "urn:other:robot";
                break;
            case "peer":
                test.Services.Endpoint.ServerCertificate = ByteString.From("different-public-peer"u8);
                break;
            case "none-security":
                test.Services.Endpoint.SecurityMode = MessageSecurityMode.None;
                break;
            case "anonymous":
                test.Services.Services.Session.SetupGet(session => session.Identity).Returns(new UserIdentity());
                break;
            default:
                throw new AssertionException("Missing authorization partition.");
        }
        await Assert.ThatAsync(() => test.Policy.AuthorizeRoboticsAsync(
            test.Services.Context, test.Draft, CancellationToken.None).AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(test.Services.Services.Calls, Is.Empty);
    }

    [TestCase(-1L)]
    [TestCase(0L)]
    [TestCase(1L)]
    public async Task GrantExpiryIsExclusiveAtTheExactBoundaryAsync(long offset)
    {
        var test = new AuthorizationContext(true);
        test.Services.Clock.Now = test.Snapshot.CompanionOperationsExpireAt!.Value.AddTicks(offset);
        if (offset < 0)
        {
            CompanionDeploymentGrant grant = await test.Policy.AuthorizeRoboticsAsync(
                test.Services.Context, test.Draft, CancellationToken.None).ConfigureAwait(false);
            Assert.That(grant.ExpiresAt, Is.GreaterThan(test.Services.Clock.Now));
            Assert.That(grant.ExpiresAt, Is.EqualTo(test.Snapshot.CompanionOperationsExpireAt));
        }
        else
        {
            await Assert.ThatAsync(() => test.Policy.AuthorizeRoboticsAsync(
                test.Services.Context, test.Draft, CancellationToken.None).AsTask(),
                Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        }
    }

    [Test]
    public async Task OwnedRunReplacementDuringAuthorizationCannotIssueAStaleGrantAsync()
    {
        var test = new AuthorizationContext(true);
        test.Samples.SetupSequence(samples => samples.Snapshot).Returns(test.Snapshot)
            .Returns(test.Snapshot with { RunId = Guid.NewGuid() });
        await Assert.ThatAsync(() => test.Policy.AuthorizeRoboticsAsync(
            test.Services.Context, test.Draft, CancellationToken.None).AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
        Assert.That(test.Services.Services.Calls, Is.Empty);
    }

    [TestCase("move")]
    [TestCase("retry")]
    [TestCase("cancel-all")]
    public async Task UnknownRobotOperationNeverFallsBackToConfiguredRulesAsync(string operation)
    {
        var test = new AuthorizationContext(true);
        var draft = new CompanionOperationDraft(test.Draft.Target,
            new CompanionOperation(operation, operation, CompanionOperationSafety.DeploymentMutation),
            null, test.Services.Context.Session, test.Services.Clock.Now.AddMinutes(5));
        await Assert.ThatAsync(() => test.Policy.AuthorizeRoboticsAsync(
            test.Services.Context, draft, CancellationToken.None).AsTask(),
            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
    }

    /// <summary>
    /// Supplies exact owned-host evidence without starting any process or trusting a real peer.
    /// </summary>
    private sealed class AuthorizationContext
    {
        public AuthorizationContext(bool draft)
        {
            string provider = draft ? "robot-intent" : "robotics";
            Snapshot = new RepositorySampleSnapshot(draft ? RepositorySampleId.IntentEnabledRobot :
                RepositorySampleId.MinimalRobotServer, RepositorySamplePhase.AdvertisedReady, "Owned simulation")
            {
                RunId = Guid.NewGuid(),
                OwnsResources = true,
                CompanionOperationsAuthorized = true,
                CompanionOperationsExpireAt = Services.Clock.Now.AddMinutes(1),
                Evidence = new RepositorySampleEvidence(new Uri(Services.Endpoint.EndpointUrl ??
                    throw new AssertionException("The test endpoint has no URL.")),
                    Services.Endpoint.Server.ApplicationUri ??
                    throw new AssertionException("The test endpoint has no application URI."),
                    "OwnedRobot", "urn:owned:robot-product",
                    ByteString.From(SHA256.HashData(Services.Endpoint.ServerCertificate.Span)))
            };
            Samples.SetupGet(samples => samples.Snapshot).Returns(() => Snapshot);
            Policy = new RepositorySampleCompanionPolicy(Samples.Object, timeProvider: Services.Clock);
            Draft = new CompanionOperationDraft(
                new CompanionTarget(provider, Services.Controller, "Owned simulation",
                    draft ? "DraftIntentController" : "SystemOperation"),
                new CompanionOperation(draft ? "request-control" : "get-ready", "Owned operation",
                    CompanionOperationSafety.DeploymentMutation),
                null, Services.Context.Session, Services.Clock.Now.AddMinutes(5));
        }

        public RoboticsTaskTestContext Services { get; } = new();
        public Mock<IRepositorySampleService> Samples { get; } = new(MockBehavior.Strict);
        public RepositorySampleCompanionPolicy Policy { get; }
        public RepositorySampleSnapshot Snapshot { get; set; }
        public CompanionOperationDraft Draft { get; }
    }
}
