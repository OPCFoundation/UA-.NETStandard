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
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class CompanionDeploymentPolicyTests
    {
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(10)]
        public async Task ValidRuleReturnsPinnedEvidenceAndTheEarlierDeadlineAsync(int ruleLifetimeMinutes)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionDeploymentRule rule = CompanionDeploymentTestData.CreateRule(
                    context, expiresAt: context.UtcNow.AddMinutes(ruleLifetimeMinutes));
                var policy = new ConfiguredCompanionDeploymentPolicy([rule], context.Clock.Object);
                CompanionOperationDraft draft = CompanionDeploymentTestData.Capture(context);

                CompanionDeploymentGrant grant = await policy.AuthorizeAsync(
                    new CompanionContext(context.Session.Object, context
                        .Telemetry), draft, default).ConfigureAwait(false);

                Assert.That(grant.RuleId, Is.EqualTo("plant-rule"));
                Assert.That(grant.Revision, Is.EqualTo("revision-7"));
                Assert.That(grant.ExpiresAt,
                    Is.EqualTo(context.UtcNow.AddMinutes(Math.Min(ruleLifetimeMinutes, 5))));
                Assert.That(draft.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(5)));
                Assert.That(draft.DeploymentGrant, Is.Null);
                Assert.That(rule.SecurityPolicyUri, Does.EndWith("#Basic256Sha256"));
                context.VerifyInspectionCount(0);
                context.VerifyExecutionCount(0);
            }
        }

        [Test]
        public async Task EmptyPolicyDeniesAValidDeploymentDraftAsync()
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                var policy = new ConfiguredCompanionDeploymentPolicy(timeProvider: context.Clock.Object);

                await Assert.ThatAsync(
                    () => policy.AuthorizeAsync(
                        new CompanionContext(context.Session.Object, context.Telemetry),
                        CompanionDeploymentTestData.Capture(context), default).AsTask(),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("No configured"))
                    .ConfigureAwait(false);

                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(0);
            }
        }

        [TestCase("session")]
        [TestCase("namespace-order")]
        [TestCase("disconnected")]
        public async Task PolicyRejectsStaleDraftEvenWhenRuleDimensionsStillAuthorizeAsync(string change)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionDeploymentRule rule = CompanionDeploymentTestData.CreateRule(context);
                var policy = new ConfiguredCompanionDeploymentPolicy([rule], context.Clock.Object);
                CompanionOperationDraft draft = CompanionDeploymentTestData.Capture(context);
                if (change == "namespace-order")
                {
                    context.NamespaceUris.Update(
                        [Namespaces.OpcUa, "urn:another:server", "urn:ualens:test", "urn:ualens:server"]);
                }
                else
                {
                    CompanionDeploymentTestData.ChangeSession(context, change);
                }
                var borrowed = new CompanionContext(context.Session.Object, context.Telemetry);
                Assert.That(rule.Matches(borrowed, draft, context.UtcNow), Is.True);

                await Assert.ThatAsync(
                    () => policy.AuthorizeAsync(borrowed, draft, default).AsTask(),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("changed or expired"))
                    .ConfigureAwait(false);

                context.VerifyExecutionCount(0);
            }
        }

        [TestCase((ushort)1)]
        [TestCase((ushort)2)]
        [TestCase((ushort)3)]
        public async Task NamespaceUriRuleFollowsThePortableTargetAcrossNamespaceIndexesAsync(ushort namespaceIndex)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionDeploymentRule rule = CompanionDeploymentTestData.CreateRule(context);
                string[] namespaces = [Namespaces.OpcUa, "urn:first", "urn:second", "urn:third"];
                namespaces[namespaceIndex] = "urn:ualens:test";
                context.NamespaceUris.Update(namespaces);
                CompanionTarget target = context.Target with { NodeId = new NodeId(1234u, namespaceIndex) };
                var draft = new CompanionOperationDraft(
                    target, context.Operation, CompanionDeploymentTestData.Input,
                    context.Session.Object, context.UtcNow.AddMinutes(5));
                var policy = new ConfiguredCompanionDeploymentPolicy([rule], context.Clock.Object);

                CompanionDeploymentGrant grant = await policy.AuthorizeAsync(
                    new CompanionContext(context.Session.Object, context
                        .Telemetry), draft, default).ConfigureAwait(false);

                Assert.That(grant.RuleId, Is.EqualTo("plant-rule"));
                Assert.That(grant.Revision, Is.EqualTo("revision-7"));
                Assert.That(rule.TargetId.NamespaceUri, Is.EqualTo("urn:ualens:test"));
                Assert.That(draft.Target.NodeId, Is.EqualTo(new NodeId(1234u, namespaceIndex)));
                context.VerifyExecutionCount(0);
            }
        }

        [TestCase("endpoint")]
        [TestCase("endpoint-case")]
        [TestCase("endpoint-query")]
        [TestCase("application")]
        [TestCase("application-case")]
        [TestCase("missing-application")]
        [TestCase("policy")]
        [TestCase("policy-case")]
        [TestCase("policy-none")]
        [TestCase("mode-none")]
        [TestCase("mode-sign")]
        [TestCase("mode-invalid")]
        [TestCase("identity")]
        [TestCase("provider")]
        [TestCase("provider-case")]
        [TestCase("target")]
        [TestCase("target-namespace")]
        [TestCase("namespace-uri-missing")]
        [TestCase("operation")]
        [TestCase("operation-case")]
        [TestCase("input")]
        [TestCase("null-input")]
        [TestCase("expired-rule")]
        [TestCase("read-only")]
        [TestCase("local-file")]
        [TestCase("sample")]
        public async Task EveryIndependentRuleMismatchDeniesWithoutProviderDispatchAsync(string mismatch)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionDeploymentRule rule = CompanionDeploymentTestData.CreateRule(context);
                CompanionTarget target = context.Target;
                CompanionOperation operation = context.Operation;
                string? input = CompanionDeploymentTestData.Input;
                switch (mismatch)
                {
                    case "endpoint":
                        context.Endpoint.EndpointUrl = "opc.tcp://other.example:4840/Plant?profile=query-marker";
                        break;
                    case "endpoint-case":
                        context.Endpoint.EndpointUrl = CompanionDeploymentTestData.Endpoint.Replace(
                            "/Plant", "/plant", StringComparison.Ordinal);
                        break;
                    case "endpoint-query":
                        context.Endpoint.EndpointUrl = CompanionDeploymentTestData.Endpoint.Replace(
                            "query-marker", "another-profile", StringComparison.Ordinal);
                        break;
                    case "application":
                        context.Endpoint.Server.ApplicationUri = "urn:ualens:deployment:another-plant";
                        break;
                    case "application-case":
                        context.Endpoint.Server.ApplicationUri = "urn:ualens:deployment:Plant";
                        break;
                    case "missing-application":
                        context.Endpoint.Server.ApplicationUri = null!;
                        break;
                    case "policy":
                        context.Endpoint.SecurityPolicyUri =
                            "http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep";
                        break;
                    case "policy-case":
                        context.Endpoint.SecurityPolicyUri =
                            "http://opcfoundation.org/UA/SecurityPolicy#basic256sha256";
                        break;
                    case "policy-none":
                        context.Endpoint.SecurityPolicyUri = SecurityPolicies.None;
                        break;
                    case "mode-none":
                        context.Endpoint.SecurityMode = MessageSecurityMode.None;
                        break;
                    case "mode-sign":
                        context.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                        break;
                    case "mode-invalid":
                        context.Endpoint.SecurityMode = MessageSecurityMode.Invalid;
                        break;
                    case "identity":
                        context.Identity = new Mock<IUserIdentity>(MockBehavior.Strict).Object;
                        break;
                    case "provider":
                        target = target with { ProviderId = "another-provider" };
                        break;
                    case "provider-case":
                        target = target with { ProviderId = "Sample" };
                        break;
                    case "target":
                        target = target with { NodeId = new NodeId(5678u, 2) };
                        break;
                    case "target-namespace":
                        target = target with { NodeId = new NodeId(1234u, 1) };
                        break;
                    case "namespace-uri-missing":
                        context.NamespaceUris.Update([Namespaces.OpcUa, "urn:ualens:server", "urn:another:model"]);
                        break;
                    case "operation":
                        operation = operation with { Id = "another-operation" };
                        break;
                    case "operation-case":
                        operation = operation with { Id = "Apply" };
                        break;
                    case "input":
                        input = "recipe=9";
                        break;
                    case "null-input":
                        input = null;
                        break;
                    case "expired-rule":
                        context.UtcNow = rule.ExpiresAt;
                        break;
                    case "read-only":
                        operation = operation with { Safety = CompanionOperationSafety.ReadOnly };
                        break;
                    case "local-file":
                        operation = operation with { Safety = CompanionOperationSafety.LocalFile };
                        break;
                    case "sample":
                        operation = operation with { Safety = CompanionOperationSafety.SampleMutation };
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(mismatch));
                }
                var draft = new CompanionOperationDraft(
                    target, operation, input, context.Session.Object, context.UtcNow.AddMinutes(5));
                var policy = new ConfiguredCompanionDeploymentPolicy([rule], context.Clock.Object);
                Assert.That(draft.Matches(context.Session.Object, context.UtcNow), Is.True);

                await Assert.ThatAsync(
                    () => policy.AuthorizeAsync(
                        new CompanionContext(context.Session.Object, context.Telemetry), draft, default).AsTask(),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("No configured"))
                    .ConfigureAwait(false);

                context.VerifyExecutionCount(0);
                context.VerifyInspectionCount(0);
            }
        }

        [TestCase(true, -1)]
        [TestCase(true, 0)]
        [TestCase(true, 1)]
        [TestCase(false, -1)]
        [TestCase(false, 0)]
        [TestCase(false, 1)]
        public async Task RuleAndDraftDeadlinesAreExclusiveAsync(bool ruleExpiresFirst, int ticksFromDeadline)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                DateTimeOffset deadline = context.UtcNow.AddMinutes(1);
                CompanionDeploymentRule rule = CompanionDeploymentTestData.CreateRule(
                    context, expiresAt: ruleExpiresFirst ? deadline : deadline.AddMinutes(5));
                var draft = new CompanionOperationDraft(
                    context.Target, context.Operation, CompanionDeploymentTestData.Input,
                    context.Session.Object, ruleExpiresFirst ? deadline.AddMinutes(5) : deadline);
                var policy = new ConfiguredCompanionDeploymentPolicy([rule], context.Clock.Object);
                context.UtcNow = deadline.AddTicks(ticksFromDeadline);

                if (ticksFromDeadline < 0)
                {
                    CompanionDeploymentGrant grant = await policy.AuthorizeAsync(
                        new CompanionContext(context.Session.Object, context.Telemetry), draft, default)
                        .ConfigureAwait(false);
                    Assert.That(grant.ExpiresAt, Is.EqualTo(deadline));
                    Assert.That(grant.RuleId, Is.EqualTo(rule.Id));
                }
                else
                {
                    await Assert.ThatAsync(
                        () => policy.AuthorizeAsync(
                            new CompanionContext(context.Session.Object, context.Telemetry), draft, default).AsTask(),
                        Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                }
                context.VerifyExecutionCount(0);
            }
        }

        [TestCase("id")]
        [TestCase("revision")]
        [TestCase("provider")]
        [TestCase("operation")]
        public void RuleConstructorRejectsInvalidReferences(string field)
        {
            string?[] invalid =
            [
                null, string.Empty, " ", "contains space", "../path", "name?query", "name#fragment",
                "nonascii-\u00e4", new string('a', 129)
            ];
            foreach (string? value in invalid)
            {
                Assert.That(() => ConstructRule(
                    new ExpandedNodeId(1234u, "urn:ualens:test"),
                    id: field == "id" ? value! : "plant-rule",
                    revision: field == "revision" ? value! : "revision-7",
                    providerId: field == "provider" ? value! : "sample",
                    operationId: field == "operation" ? value! : "apply"),
                    Throws.ArgumentException, $"Invalid {field} must be rejected.");
            }
        }

        [TestCase("endpoint", null)]
        [TestCase("endpoint", "")]
        [TestCase("endpoint", "relative")]
        [TestCase("endpoint", "opc.tcp://userinfo@deployment.example:4840/Plant")]
        [TestCase("endpoint", "opc.tcp://deployment.example:4840/Plant#fragment")]
        [TestCase("application", null)]
        [TestCase("application", "")]
        [TestCase("application", "relative")]
        [TestCase("application", "https://userinfo@deployment.example/application")]
        [TestCase("application", "urn:deployment:plant#fragment")]
        [TestCase("application", "urn:deployment:plant?query")]
        [TestCase("application", "oversized")]
        [TestCase("policy", null)]
        [TestCase("policy", "")]
        [TestCase("policy", " ")]
        [TestCase("policy", "relative")]
        [TestCase("policy", "https://userinfo@deployment.example/policy#secure")]
        [TestCase("policy", "https://deployment.example/policy?query#secure")]
        [TestCase("policy", "http://opcfoundation.org/UA/SecurityPolicy#None")]
        [TestCase("policy", "oversized")]
        public void RuleConstructorRejectsInvalidUris(string field, string? value)
        {
            string? invalid = value == "oversized" ? "urn:" + new string('a', 2045) : value;

            Assert.That(() => ConstructRule(
                new ExpandedNodeId(1234u, "urn:ualens:test"),
                endpoint: field == "endpoint" ? invalid! : CompanionDeploymentTestData.Endpoint,
                application: field == "application" ? invalid! : CompanionDeploymentTestData.ApplicationUri,
                policy: field == "policy" ? invalid! : SecurityPolicies.Basic256Sha256),
                Throws.ArgumentException);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RuleConstructorRequiresBothPredicates(bool missingIdentity)
        {
            Assert.That(() => new CompanionDeploymentRule(
                "plant-rule", "revision-7", CompanionDeploymentTestData.Endpoint,
                CompanionDeploymentTestData.ApplicationUri, SecurityPolicies.Basic256Sha256,
                "sample", new ExpandedNodeId(1234u, "urn:ualens:test"), "apply", DateTimeOffset.MaxValue,
                missingIdentity ? null! : static _ => true,
                missingIdentity ? static _ => true : null!),
                Throws.ArgumentNullException.With.Property("ParamName")
                    .EqualTo(missingIdentity ? "acceptsIdentity" : "acceptsInput"));
        }

        [TestCase("null", false)]
        [TestCase("indexed", false)]
        [TestCase("remote", false)]
        [TestCase("remote-standard", false)]
        [TestCase("portable", true)]
        [TestCase("standard", true)]
        public void RuleConstructorValidatesPortablePrimaryServerTargets(string kind, bool accepted)
        {
            ExpandedNodeId target = kind switch
            {
                "null" => ExpandedNodeId.Null,
                "indexed" => new ExpandedNodeId(new NodeId(1234u, 2)),
                "remote" => ExpandedNodeId.Parse("svr=1;nsu=urn:ualens:test;i=1234"),
                "remote-standard" => ExpandedNodeId.Parse("svr=1;i=1234"),
                "portable" => new ExpandedNodeId(1234u, "urn:ualens:test"),
                "standard" => new ExpandedNodeId(1234u),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };

            if (accepted)
            {
                CompanionDeploymentRule rule = ConstructRule(target);
                Assert.That(rule.TargetId, Is.EqualTo(target));
                Assert.That(rule.ProviderId, Is.EqualTo("sample"));
            }
            else
            {
                Assert.That(() => ConstructRule(target),
                    Throws.ArgumentException.With.Property("ParamName").EqualTo("targetId"));
            }
        }

        [Test]
        public void RuleConstructorAcceptsMaximumReferenceAndUriLengths()
        {
            string reference = new('a', 128);
            string application = "urn:" + new string('a', 2044);
            string policy = "urn:" + new string('p', 2037) + "#secure";
            CompanionDeploymentRule rule = ConstructRule(
                new ExpandedNodeId(1234u, "urn:ualens:test"),
                id: reference, revision: reference, application: application, policy: policy,
providerId: reference, operationId: reference);

            Assert.That(rule.Id, Is.EqualTo(reference));
            Assert.That(rule.Revision, Is.EqualTo(reference));
            Assert.That(rule.ProviderId, Is.EqualTo(reference));
            Assert.That(rule.OperationId, Is.EqualTo(reference));
            Assert.That(rule.ServerApplicationUri, Is.EqualTo(application).And.Length.EqualTo(2048));
            Assert.That(rule.SecurityPolicyUri, Is.EqualTo(policy).And.Length.EqualTo(2048));
            Assert.That(rule.EndpointUrl, Is.EqualTo(CompanionDeploymentTestData.Endpoint));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(256)]
        [TestCase(257)]
        public async Task RuleCountPartitionsAreBoundedAndSelectTheOnlyMatchAsync(int count)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionDeploymentRule[] rules = [.. Enumerable.Range(0, count).Select(index =>
                    CompanionDeploymentTestData.CreateRule(context,
                        id: "rule-" + index.ToString(CultureInfo.InvariantCulture),
                        acceptsInput: _ => index == count - 1))];
                if (count > 256)
                {
                    Assert.That(() => new ConfiguredCompanionDeploymentPolicy(
                        ArrayOf.Wrapped(rules), context.Clock.Object),
                        Throws.ArgumentException.With.Property("ParamName").EqualTo("rules"));
                }
                else
                {
                    var policy = new ConfiguredCompanionDeploymentPolicy(ArrayOf.Wrapped(rules), context.Clock.Object);
                    CompanionOperationDraft draft = CompanionDeploymentTestData.Capture(context);
                    var borrowed = new CompanionContext(context.Session.Object, context.Telemetry);
                    if (count == 0)
                    {
                        await Assert.ThatAsync(
                            () => policy.AuthorizeAsync(borrowed, draft, default).AsTask(),
                            Throws.TypeOf<UnauthorizedAccessException>()).ConfigureAwait(false);
                    }
                    else
                    {
                        CompanionDeploymentGrant grant = await policy.AuthorizeAsync(borrowed, draft, default)
                            .ConfigureAwait(false);
                        Assert.That(grant.RuleId,
                            Is.EqualTo("rule-" + (count - 1).ToString(CultureInfo.InvariantCulture)));
                        Assert.That(grant.Revision, Is.EqualTo("revision-7"));
                        Assert.That(grant.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(2)));
                    }
                }
                context.VerifyExecutionCount(0);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DuplicateRuleIdsRejectAndOverlappingRulesDenyAsync(bool reverse)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionDeploymentRule first = CompanionDeploymentTestData.CreateRule(context);
                CompanionDeploymentRule duplicate = CompanionDeploymentTestData.CreateRule(
                    context, revision: "different-revision");
                Assert.That(() => new ConfiguredCompanionDeploymentPolicy([first, duplicate], context.Clock.Object),
                    Throws.ArgumentException.With.Message.Contains("unique"));
                CompanionDeploymentRule second = CompanionDeploymentTestData.CreateRule(context, id: "other-rule");
                var policy = new ConfiguredCompanionDeploymentPolicy(
                    reverse ? [second, first] : [first, second], context.Clock.Object);

                await Assert.ThatAsync(
                    () => policy.AuthorizeAsync(
                        new CompanionContext(context.Session.Object, context.Telemetry),
                        CompanionDeploymentTestData.Capture(context), default).AsTask(),
                    Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("More than one"))
                    .ConfigureAwait(false);

                context.VerifyExecutionCount(0);
            }
        }

        [Test]
        public async Task PolicySnapshotsTheCallerOwnedRuleArrayAsync()
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionDeploymentRule[] rules = [CompanionDeploymentTestData.CreateRule(context)];
                var policy = new ConfiguredCompanionDeploymentPolicy(ArrayOf.Wrapped(rules), context.Clock.Object);
                rules[0] = CompanionDeploymentTestData.CreateRule(
                    context, id: "replacement", acceptsInput: static _ => false);

                CompanionDeploymentGrant grant = await policy.AuthorizeAsync(
                    new CompanionContext(context.Session.Object, context.Telemetry),
                    CompanionDeploymentTestData.Capture(context), default).ConfigureAwait(false);

                Assert.That(grant.RuleId, Is.EqualTo("plant-rule"));
                Assert.That(grant.Revision, Is.EqualTo("revision-7"));
                Assert.That(rules[0].Id, Is.EqualTo("replacement"));
                context.VerifyExecutionCount(0);
            }
        }

        [Test]
        public async Task PolicyRejectsNullRulesAndArgumentsAsync()
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                Assert.That(() => new ConfiguredCompanionDeploymentPolicy([null!], context.Clock.Object),
                    Throws.ArgumentNullException);
                var policy = new ConfiguredCompanionDeploymentPolicy(timeProvider: context.Clock.Object);
                CompanionOperationDraft draft = CompanionDeploymentTestData.Capture(context);

                await Assert.ThatAsync(
                    () => policy.AuthorizeAsync(null!, draft, default).AsTask(),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("context")).ConfigureAwait(false);
                await Assert.ThatAsync(
                    () => policy.AuthorizeAsync(
                        new CompanionContext(context.Session.Object, context.Telemetry), null!, default).AsTask(),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("draft")).ConfigureAwait(false);
                Assert.That(() => draft.WithGrant(null!),
                    Throws.ArgumentNullException.With.Property("ParamName").EqualTo("grant"));
                context.VerifyExecutionCount(0);
            }
        }

        [Test]
        public async Task CanceledAuthorizationDoesNotEvaluatePredicatesAsync()
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                int inputChecks = 0;
                CompanionDeploymentRule rule = CompanionDeploymentTestData.CreateRule(context,
                    acceptsInput: _ =>
                    {
                        inputChecks++;
                        return true;
                    });
                var policy = new ConfiguredCompanionDeploymentPolicy([rule], context.Clock.Object);
                using var cancellation = new CancellationTokenSource();
                await cancellation.CancelAsync().ConfigureAwait(false);

                await Assert.ThatAsync(
                    () => policy.AuthorizeAsync(
                        new CompanionContext(context.Session.Object, context.Telemetry),
                        CompanionDeploymentTestData.Capture(context), cancellation.Token).AsTask(),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

                Assert.That(inputChecks, Is.Zero);
                context.VerifyExecutionCount(0);
            }
        }

        [TestCase("identity")]
        [TestCase("application")]
        [TestCase("session")]
        [TestCase("namespace")]
        [TestCase("namespace-order")]
        [TestCase("endpoint")]
        [TestCase("mode")]
        [TestCase("policy")]
        [TestCase("disconnected")]
        public async Task GrantClonePreservesTheOriginalSessionBindingAsync(string change)
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionOperationDraft original = CompanionDeploymentTestData.Capture(context);
                var grant = new CompanionDeploymentGrant("plant-rule", "revision-7", context.UtcNow.AddMinutes(1));
                CompanionDeploymentTestData.ChangeSession(context, change);

                CompanionOperationDraft authorized = original.WithGrant(grant);

                Assert.That(authorized.Matches(context.Session.Object, context.UtcNow), Is.False);
                Assert.That(original.Matches(context.Session.Object, context.UtcNow), Is.False);
                Assert.That(authorized.DeploymentGrant, Is.SameAs(grant));
                Assert.That(authorized.Target, Is.SameAs(original.Target));
                Assert.That(authorized.Operation, Is.SameAs(original.Operation));
                Assert.That(authorized.Input, Is.EqualTo(CompanionDeploymentTestData.Input));
                Assert.That(authorized.EndpointUrl, Is.EqualTo("opc.tcp://deployment.example:4840/Plant"));
                Assert.That(authorized.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(1)));
                Assert.That(original.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(5)));
                context.VerifyExecutionCount(0);
            }
        }

        [Test]
        public async Task GrantCloneShortensValidityWithoutChangingTheOriginalDeadlineAsync()
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                CompanionOperationDraft original = CompanionDeploymentTestData.Capture(context);
                DateTimeOffset deadline = context.UtcNow.AddMinutes(1);
                CompanionOperationDraft authorized = original.WithGrant(
                    new CompanionDeploymentGrant("plant-rule", "revision-7", deadline));

                Assert.That(authorized.Matches(context.Session.Object, deadline.AddTicks(-1)), Is.True);
                Assert.That(authorized.Matches(context.Session.Object, deadline), Is.False);
                Assert.That(authorized.Matches(context.Session.Object, deadline.AddTicks(1)), Is.False);
                Assert.That(original.Matches(context.Session.Object, deadline), Is.True);
                Assert.That(original.ExpiresAt, Is.EqualTo(context.UtcNow.AddMinutes(5)));
                Assert.That(original.DeploymentGrant, Is.Null);
            }
        }

        [Test]
        public async Task DraftInputsAreDeeplyIsolatedAndGrantSummaryContainsOnlyReviewEvidenceAsync()
        {
            CompanionPreparedOperationTestContext context = CompanionDeploymentTestData.CreateContext();
            await using (context.ConfigureAwait(false))
            {
                uint[] sourceNumbers = [7u, 11u];
                CompanionValue[] sourceFields =
                [
                    new("parameters", Variant.From(ArrayOf.Wrapped(sourceNumbers))),
                    new("label", Variant.From("raw-typed-input-marker"))
                ];
                var domainInput = new CompanionTestTaskInput("Reviewed two parameters.");
                var original = new CompanionOperationDraft(
                    context.Target, context.Operation, "raw-legacy-input-marker", context.Session.Object,
                    context.UtcNow.AddMinutes(5), domainInput, ArrayOf.Wrapped(sourceFields));
                CompanionOperationDraft authorized = original.WithGrant(
                    new CompanionDeploymentGrant("plant-rule", "revision-7", context.UtcNow.AddMinutes(1)));
                sourceNumbers[0] = 99u;
                sourceFields[1] = new CompanionValue("replacement", Variant.From("changed"));
                ArrayOf<CompanionValue> returned = authorized.Inputs;
                Assert.That(returned[0].Value.TryGetValue(out ArrayOf<uint> returnedNumbers), Is.True);
                // Mutate the public memory, not ToArray(), which would hide aliasing bugs.
                Assert.That(MemoryMarshal.TryGetArray(returnedNumbers.Memory, out ArraySegment<uint> numbers), Is.True);
                uint[] numberStorage = numbers.Array ??
                    throw new AssertionException("Expected array-backed test input.");
                numberStorage[numbers.Offset] = 55u;
                Assert.That(
                    MemoryMarshal.TryGetArray(returned.Memory, out ArraySegment<CompanionValue> fields),
                    Is.True);
                CompanionValue[] fieldStorage = fields.Array ??
                    throw new AssertionException("Expected array-backed returned fields.");
                fieldStorage[fields.Offset + 1] = new CompanionValue("tampered", Variant.From("changed-again"));

                foreach (CompanionOperationDraft draft in new[] { original, authorized })
                {
                    ArrayOf<CompanionValue> actual = draft.Inputs;
                    Assert.That(actual[0].Name, Is.EqualTo("parameters"));
                    Assert.That(actual[0].Value.TryGetValue(out ArrayOf<uint> actualNumbers), Is.True);
                    Assert.That(actualNumbers.ToArray(), Is.EqualTo(s_expectedNumbers));
                    Assert.That(actual[1].Name, Is.EqualTo("label"));
                    Assert.That(actual[1].Value.TryGetValue(out string label), Is.True);
                    Assert.That(label, Is.EqualTo("raw-typed-input-marker"));
                    Assert.That(draft.TaskInput, Is.SameAs(domainInput));
                }
                Assert.That(authorized.Summary, Does.Contain("Deployment rule: plant-rule (revision revision-7)"));
                Assert.That(
                    authorized.Summary,
                    Does.Contain("Reviewed two parameters.").And.Contain("DeploymentMutation"));
                Assert.That(authorized.Summary, Does.Not.Contain("raw-legacy-input-marker"));
                Assert.That(authorized.Summary, Does.Not.Contain("raw-typed-input-marker"));
                Assert.That(authorized.Summary, Does.Not.Contain("query-marker").And.Not.Contain("?"));
                Assert.That(original.Summary, Does.Not.Contain("Deployment rule:"));
                context.VerifyExecutionCount(0);
            }
        }

        private static CompanionDeploymentRule ConstructRule(
            ExpandedNodeId target,
            string id = "plant-rule",
            string revision = "revision-7",
            string endpoint = CompanionDeploymentTestData.Endpoint,
            string application = CompanionDeploymentTestData.ApplicationUri,
            string policy = SecurityPolicies.Basic256Sha256,
            string providerId = "sample",
            string operationId = "apply")
        {
            return new CompanionDeploymentRule(
                id, revision, endpoint, application, policy, providerId, target, operationId,
                DateTimeOffset.MaxValue, static _ => true, static _ => true);
        }

        private static readonly uint[] s_expectedNumbers = [7u, 11u];
    }

    internal static class CompanionDeploymentTestData
    {
        public static CompanionPreparedOperationTestContext CreateContext(
            ICompanionDeploymentPolicy? policy = null,
            ITelemetryContext? telemetry = null,
            CompanionOperationSafety safety = CompanionOperationSafety.DeploymentMutation,
            string endpoint = Endpoint)
        {
            var context = new CompanionPreparedOperationTestContext(
                safety, endpoint, telemetry, deploymentPolicy: policy);
            context.Endpoint.Server = new ApplicationDescription { ApplicationUri = ApplicationUri };
            return context;
        }

        public static CompanionDeploymentRule CreateRule(
            CompanionPreparedOperationTestContext context,
            string id = "plant-rule",
            string revision = "revision-7",
            DateTimeOffset? expiresAt = null,
            Func<CompanionOperationDraft, bool>? acceptsInput = null)
        {
            IUserIdentity originalIdentity = context.Identity;
            string endpointUrl = context.Endpoint.EndpointUrl ??
                throw new AssertionException("The deployment test endpoint URL is missing.");
            string applicationUri = context.Endpoint.Server.ApplicationUri ??
                throw new AssertionException("The deployment test server application URI is missing.");
            return new CompanionDeploymentRule(
                id, revision, endpointUrl, applicationUri,
                SecurityPolicies.Basic256Sha256, context.Target.ProviderId,
                NodeId.ToExpandedNodeId(context.Target.NodeId, context.NamespaceUris), context.Operation.Id,
                expiresAt ?? context.UtcNow.AddMinutes(2),
                identity => ReferenceEquals(identity, originalIdentity),
                acceptsInput ?? (static draft => draft.Input == Input));
        }

        public static CompanionOperationDraft Capture(CompanionPreparedOperationTestContext context)
        {
            return new CompanionOperationDraft(
                context.Target, context.Operation, Input, context.Session.Object, context.UtcNow.AddMinutes(5));
        }

        public static void ChangeSession(CompanionPreparedOperationTestContext context, string change)
        {
            switch (change)
            {
                case "identity":
                    context.Identity = new Mock<IUserIdentity>(MockBehavior.Strict).Object;
                    break;
                case "application":
                    context.Endpoint.Server.ApplicationUri = "urn:ualens:deployment:replacement";
                    break;
                case "session":
                    context.SessionId = new NodeId(202u);
                    break;
                case "namespace":
                    context.NamespaceUris.Update([Namespaces.OpcUa, "urn:ualens:server", "urn:replacement"]);
                    break;
                case "namespace-order":
                    context.NamespaceUris.Update([Namespaces.OpcUa, "urn:ualens:test", "urn:ualens:server"]);
                    break;
                case "endpoint":
                    context.Endpoint.EndpointUrl = "opc.tcp://deployment.example:4840/Other";
                    break;
                case "mode":
                    context.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "policy":
                    context.Endpoint.SecurityPolicyUri =
                        "http://opcfoundation.org/UA/SecurityPolicy#Aes128_Sha256_RsaOaep";
                    break;
                case "disconnected":
                    context.Connected = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }
        }

        public const string Endpoint = "opc.tcp://deployment.example:4840/Plant?profile=query-marker";
        public const string ApplicationUri = "urn:ualens:deployment:plant";
        public const string Input = "recipe=7";
    }
}
