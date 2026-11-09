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
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Mcp;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    [TestFixture]
    public sealed class McpFoundationCallerContextTests
    {
        [TestCase(nameof(McpCallerIdentity.Subject), 1)]
        [TestCase(nameof(McpCallerIdentity.Subject), 19)]
        [TestCase(nameof(McpCallerIdentity.Subject), 4095)]
        [TestCase(nameof(McpCallerIdentity.Subject), 4096)]
        [TestCase(nameof(McpCallerIdentity.Issuer), 1)]
        [TestCase(nameof(McpCallerIdentity.Issuer), 19)]
        [TestCase(nameof(McpCallerIdentity.Issuer), 4095)]
        [TestCase(nameof(McpCallerIdentity.Issuer), 4096)]
        public void IdentityValidationAcceptsExactLengthBoundaries(string field, int length)
        {
            string text = length == 1 ? "X" : " " + new string('A', length - 2) + " ";
            string subject = field == nameof(McpCallerIdentity.Subject) ? text : "Operator-A";
            string issuer = field == nameof(McpCallerIdentity.Issuer) ? text : "urn:Issuer-A";
            var identity = new McpCallerIdentity(subject, issuer);
            var callers = new McpCallerContext();

            Assert.That(identity.Validate, Throws.Nothing);
            using (callers.EnterTrustedCaller(identity))
            {
                Assert.That(callers.Current, Is.SameAs(identity));
                Assert.That(callers.Current!.Subject, Is.EqualTo(subject));
                Assert.That(callers.Current!.Issuer, Is.EqualTo(issuer));
                Assert.That(callers.Current!.IsLocal, Is.False);
                Assert.That(callers.HasRequestScope, Is.True);
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [TestCaseSource(nameof(InvalidIdentityFields))]
        public void IdentityValidationRejectsMissingOrOversizedFields(
            string field,
            string? value,
            Type exceptionType)
        {
            McpCallerIdentity identity = field == nameof(McpCallerIdentity.Subject)
                ? new McpCallerIdentity(value!, "urn:issuer-a")
                : new McpCallerIdentity("operator", value!);

            Assert.That(identity.Validate, Throws.TypeOf(exceptionType));

            McpCallerIdentity repaired = field == nameof(McpCallerIdentity.Subject)
                ? identity with { Subject = "operator" }
                : identity with { Issuer = "urn:issuer-a" };
            Assert.That(repaired.Validate, Throws.Nothing);
            Assert.That(repaired, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
        }

        [TestCase("operator", "urn:issuer-a", false, true)]
        [TestCase("other", "urn:issuer-a", false, false)]
        [TestCase("operator", "urn:issuer-b", false, false)]
        [TestCase("operator", "urn:issuer-a", true, false)]
        [TestCase("Operator", "urn:issuer-a", false, false)]
        [TestCase("operator", "urn:Issuer-a", false, false)]
        [TestCase(" operator ", "urn:issuer-a", false, false)]
        [TestCase("operator", " urn:issuer-a ", false, false)]
        public void IdentityEqualityIncludesSubjectIssuerAndLocalFlag(
            string subject,
            string issuer,
            bool isLocal,
            bool equal)
        {
            var original = new McpCallerIdentity("operator", "urn:issuer-a");
            var candidate = new McpCallerIdentity(subject, issuer, isLocal);

            Assert.That(original.Equals(candidate), Is.EqualTo(equal));
            Assert.That(original == candidate, Is.EqualTo(equal));
            Assert.That(original != candidate, Is.EqualTo(!equal));
            if (equal)
            {
                Assert.That(original.GetHashCode(), Is.EqualTo(candidate.GetHashCode()));
                Assert.That(original, Is.Not.SameAs(candidate));
            }
        }

        [TestCaseSource(nameof(InvalidTrustedCallers))]
        public void TrustedCallerEntryRejectsNullOrInvalidIdentity(McpCallerIdentity? identity, Type exceptionType)
        {
            var callers = new McpCallerContext();

            Assert.That(() => callers.EnterTrustedCaller(identity!), Throws.TypeOf(exceptionType));
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);

            var intended = new McpCallerIdentity("operator", "urn:issuer-a");
            using (callers.EnterTrustedCaller(intended))
            {
                Assert.That(() => callers.EnterTrustedCaller(identity!), Throws.TypeOf(exceptionType));
                Assert.That(callers.Current, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
                Assert.That(callers.HasRequestScope, Is.True);
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [TestCase(ClaimTypes.NameIdentifier, "urn:issuer-a")]
        [TestCase("sub", "urn:issuer-a")]
        [TestCase(ClaimTypes.NameIdentifier, "urn:opcfoundation:mcp:local-host")]
        [TestCase("sub", "urn:opcfoundation:mcp:local-host")]
        public void FromPrincipalExtractsExactAuthenticatedIdentity(string claimType, string issuer)
        {
            var authenticated = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "Display Name"),
                    new Claim(ClaimTypes.Role, "Administrator"),
                    new Claim(claimType, " Operator-A ", ClaimValueTypes.String, issuer)
                ],
                "TestAuthentication");
            var principal = new ClaimsPrincipal(authenticated);

            McpCallerIdentity? caller = McpCallerContext.FromPrincipal(principal);

            Assert.That(caller, Is.EqualTo(new McpCallerIdentity(" Operator-A ", issuer, false)));
            Assert.That(caller!.IsLocal, Is.False);
            var callers = new McpCallerContext();
            using (callers.EnterRequest(caller))
            {
                Assert.That(callers.Current, Is.EqualTo(new McpCallerIdentity(" Operator-A ", issuer, false)));
                Assert.That(callers.HasRequestScope, Is.True);
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [TestCaseSource(nameof(PrincipalsWithoutTrustedSubjects))]
        public void FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims(ClaimsPrincipal? principal)
        {
            McpCallerIdentity? caller = McpCallerContext.FromPrincipal(principal);

            Assert.That(caller, Is.Null);
            var callers = new McpCallerContext();
            using (callers.EnterRequest(caller))
            {
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.True);
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [TestCase(ClaimTypes.NameIdentifier, true)]
        [TestCase(ClaimTypes.NameIdentifier, false)]
        [TestCase("sub", true)]
        [TestCase("sub", false)]
        public void FromPrincipalIgnoresUnauthenticatedIdentityBesideTrustedIdentity(
            string claimType,
            bool attackerFirst)
        {
            var attacker = new ClaimsIdentity(
                [new Claim(claimType, "attacker", ClaimValueTypes.String, "urn:untrusted")]);
            var trusted = new ClaimsIdentity(
                [new Claim(claimType, "operator", ClaimValueTypes.String, "urn:issuer-a")],
                "TestAuthentication");
            ClaimsIdentity[] identities = attackerFirst ? [attacker, trusted] : [trusted, attacker];
            var principal = new ClaimsPrincipal(identities);

            McpCallerIdentity? caller = McpCallerContext.FromPrincipal(principal);

            Assert.That(caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a", false)));
            Assert.That(caller!.IsLocal, Is.False);
            Assert.That(principal.Identities.Count(), Is.EqualTo(2));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FromPrincipalIgnoresSubjectlessAuthenticatedIdentityBesideTrustedIdentity(bool subjectlessFirst)
        {
            var subjectless = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "Other Display Name"), new Claim(ClaimTypes.Role, "Administrator")],
                "OtherAuthentication");
            var trusted = new ClaimsIdentity(
                [new Claim("sub", "operator", ClaimValueTypes.String, "urn:issuer-a")],
                "TestAuthentication");
            ClaimsIdentity[] identities = subjectlessFirst ? [subjectless, trusted] : [trusted, subjectless];

            McpCallerIdentity? caller = McpCallerContext.FromPrincipal(new ClaimsPrincipal(identities));

            Assert.That(caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a", false)));
            Assert.That(caller!.IsLocal, Is.False);
            Assert.That(subjectless.IsAuthenticated, Is.True);
        }

        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, false, false)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, false, true)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, true, false)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, true, true)]
        [TestCase("sub", "sub", false, false)]
        [TestCase("sub", "sub", false, true)]
        [TestCase("sub", "sub", true, false)]
        [TestCase("sub", "sub", true, true)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", false, false)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", false, true)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", true, false)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", true, true)]
        public void FromPrincipalRejectsDistinctAuthenticatedIdentities(
            string firstType,
            string secondType,
            bool differentIssuer,
            bool reverseOrder)
        {
            var first = new ClaimsIdentity(
                [new Claim(firstType, "operator", ClaimValueTypes.String, "urn:issuer-a")],
                "FirstAuthentication");
            var second = new ClaimsIdentity(
                [
                    new Claim(
                        secondType,
                        differentIssuer ? "operator" : "other",
                        ClaimValueTypes.String,
                        differentIssuer ? "urn:issuer-b" : "urn:issuer-a")
                ],
                "SecondAuthentication");
            ClaimsIdentity[] identities = reverseOrder ? [second, first] : [first, second];
            var principal = new ClaimsPrincipal(identities);

            Assert.That(principal.Identities.Count(identity => identity.IsAuthenticated), Is.EqualTo(2));
            Assert.That(
                () => McpCallerContext.FromPrincipal(principal),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
        }

        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, false)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, true)]
        [TestCase("sub", "sub", false)]
        [TestCase("sub", "sub", true)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", false)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", true)]
        public void FromPrincipalAcceptsRepeatedIdenticalAuthenticatedIdentities(
            string firstType,
            string secondType,
            bool reverseOrder)
        {
            var first = new ClaimsIdentity(
                [
                    new Claim(firstType, "operator", ClaimValueTypes.String, "urn:issuer-a"),
                    new Claim(ClaimTypes.Name, "First Display Name")
                ],
                "FirstAuthentication");
            var second = new ClaimsIdentity(
                [
                    new Claim(secondType, "operator", ClaimValueTypes.String, "urn:issuer-a"),
                    new Claim(ClaimTypes.Name, "Second Display Name"),
                    new Claim(ClaimTypes.Role, "Administrator")
                ],
                "SecondAuthentication");
            ClaimsIdentity[] identities = reverseOrder ? [second, first] : [first, second];

            McpCallerIdentity? caller = McpCallerContext.FromPrincipal(new ClaimsPrincipal(identities));

            Assert.That(caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a", false)));
            Assert.That(caller!.IsLocal, Is.False);
            Assert.That(caller!.Validate, Throws.Nothing);
        }

        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, false, false)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, false, true)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, true, false)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, true, true)]
        [TestCase("sub", "sub", false, false)]
        [TestCase("sub", "sub", false, true)]
        [TestCase("sub", "sub", true, false)]
        [TestCase("sub", "sub", true, true)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", false, false)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", false, true)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", true, false)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", true, true)]
        public void FromPrincipalRejectsConflictingClaimsWithinOneIdentity(
            string firstType,
            string secondType,
            bool differentIssuer,
            bool reverseOrder)
        {
            var first = new Claim(firstType, "operator", ClaimValueTypes.String, "urn:issuer-a");
            var second = new Claim(
                secondType,
                differentIssuer ? "operator" : "other",
                ClaimValueTypes.String,
                differentIssuer ? "urn:issuer-b" : "urn:issuer-a");
            Claim[] claims = reverseOrder ? [second, first] : [first, second];
            var authenticated = new ClaimsIdentity(claims, "TestAuthentication");
            var principal = new ClaimsPrincipal(authenticated);

            Assert.That(principal.Identities.Count(), Is.EqualTo(1));
            Assert.That(authenticated.IsAuthenticated, Is.True);
            Assert.That(authenticated.Claims.Count(), Is.EqualTo(2));
            Assert.That(
                () => McpCallerContext.FromPrincipal(principal),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
        }

        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, false)]
        [TestCase(ClaimTypes.NameIdentifier, ClaimTypes.NameIdentifier, true)]
        [TestCase("sub", "sub", false)]
        [TestCase("sub", "sub", true)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", false)]
        [TestCase(ClaimTypes.NameIdentifier, "sub", true)]
        public void FromPrincipalAcceptsConsistentClaimsWithinOneIdentity(
            string firstType,
            string secondType,
            bool reverseOrder)
        {
            var first = new Claim(firstType, "operator", ClaimValueTypes.String, "urn:issuer-a");
            var second = new Claim(secondType, "operator", ClaimValueTypes.String, "urn:issuer-a");
            Claim[] claims = reverseOrder ? [second, first] : [first, second];
            var authenticated = new ClaimsIdentity(claims, "TestAuthentication");
            var principal = new ClaimsPrincipal(authenticated);

            McpCallerIdentity? caller = McpCallerContext.FromPrincipal(principal);

            Assert.That(caller, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a", false)));
            Assert.That(caller!.IsLocal, Is.False);
            Assert.That(principal.Identities.Count(), Is.EqualTo(1));
            Assert.That(authenticated.Claims.Count(), Is.EqualTo(2));
        }

        [TestCase(ClaimTypes.NameIdentifier, nameof(McpCallerIdentity.Subject), 4097)]
        [TestCase(ClaimTypes.NameIdentifier, nameof(McpCallerIdentity.Subject), 4098)]
        [TestCase(ClaimTypes.NameIdentifier, nameof(McpCallerIdentity.Issuer), 4097)]
        [TestCase(ClaimTypes.NameIdentifier, nameof(McpCallerIdentity.Issuer), 4098)]
        [TestCase("sub", nameof(McpCallerIdentity.Subject), 4097)]
        [TestCase("sub", nameof(McpCallerIdentity.Subject), 4098)]
        [TestCase("sub", nameof(McpCallerIdentity.Issuer), 4097)]
        [TestCase("sub", nameof(McpCallerIdentity.Issuer), 4098)]
        public void FromPrincipalValidatesSelectedIdentityLengths(string claimType, string field, int length)
        {
            string subject = field == nameof(McpCallerIdentity.Subject) ? new string('s', length) : "operator";
            string issuer = field == nameof(McpCallerIdentity.Issuer) ? new string('i', length) : "urn:issuer-a";
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "Valid Display Name"),
                    new Claim(ClaimTypes.Role, "Administrator"),
                    new Claim(claimType, subject, ClaimValueTypes.String, issuer)
                ],
                "TestAuthentication"));

            Assert.That(
                () => McpCallerContext.FromPrincipal(principal),
                Throws.TypeOf<ArgumentOutOfRangeException>());

            string validSubject = field == nameof(McpCallerIdentity.Subject) ? new string('s', 4096) : "operator";
            string validIssuer = field == nameof(McpCallerIdentity.Issuer) ? new string('i', 4096) : "urn:issuer-a";
            var boundaryPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(claimType, validSubject, ClaimValueTypes.String, validIssuer)],
                "TestAuthentication"));
            Assert.That(
                McpCallerContext.FromPrincipal(boundaryPrincipal),
                Is.EqualTo(new McpCallerIdentity(validSubject, validIssuer, false)));
        }

        [Test]
        public void LocalFallbackCannotCrossARequestScope()
        {
            var local = new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true);
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                LocalCallerId = "host-A",
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = local,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());

            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(policy.RequireCaller(), Is.EqualTo(local));
            Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(local));
            using (callers.EnterRequest(null))
            {
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.True);
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.True);
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(
                policy.RequireCaller(),
                Is.EqualTo(new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true)));
            Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(local));
        }

        [TestCase("urn:issuer-a")]
        [TestCase("urn:opcfoundation:mcp:local-host")]
        public void LocalFallbackCannotReplaceAnAuthenticatedRemoteCaller(string issuer)
        {
            var local = new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true);
            var remote = new McpCallerIdentity("host-A", issuer, false);
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                LocalCallerId = "host-A",
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = local,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());

            using (callers.EnterRequest(remote))
            {
                Assert.That(callers.Current, Is.EqualTo(new McpCallerIdentity("host-A", issuer, false)));
                Assert.That(callers.HasRequestScope, Is.True);
                Assert.That(policy.RequireCaller(), Is.EqualTo(remote));
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(local));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedScopesRestoreExactPreviousAuthority(bool anonymousInner)
        {
            var first = new McpCallerIdentity("operator-a", "urn:issuer-a");
            var second = new McpCallerIdentity("operator-b", "urn:issuer-b");
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = first,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    },
                    new McpCallerGrant
                    {
                        Caller = second,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());

            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            using IDisposable outer = callers.EnterTrustedCaller(first);
            Assert.That(callers.Current, Is.EqualTo(first));
            Assert.That(callers.HasRequestScope, Is.True);
            Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(first));
            using IDisposable inner = anonymousInner
                ? callers.EnterRequest(null) : callers.EnterTrustedCaller(second);
            Assert.That(callers.Current, Is.EqualTo(anonymousInner ? null : second));
            Assert.That(callers.HasRequestScope, Is.True);
            if (anonymousInner)
            {
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
            }
            else
            {
                Assert.That(policy.RequireCaller(), Is.EqualTo(second));
                Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(second));
            }

            inner.Dispose();
            Assert.That(callers.Current, Is.EqualTo(first));
            Assert.That(callers.HasRequestScope, Is.True);
            Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(first));
            inner.Dispose();
            Assert.That(callers.Current, Is.EqualTo(first));
            Assert.That(callers.HasRequestScope, Is.True);
            Assert.That(policy.RequireCaller(), Is.EqualTo(first));

            outer.Dispose();
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(
                () => policy.RequireCaller(),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            outer.Dispose();
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
        }

        [Test]
        public void IndependentCallerContextsNeverBorrowAuthority()
        {
            var first = new McpCallerIdentity("operator", "urn:issuer-a");
            var second = new McpCallerIdentity("operator", "urn:issuer-b");
            var firstContext = new McpCallerContext();
            var secondContext = new McpCallerContext();
            var firstOptions = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = first,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var secondOptions = new McpExecutionOptions
            {
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = second,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var firstPolicy = new McpExecutionPolicy(firstOptions, firstContext, CreateTelemetry());
            var secondPolicy = new McpExecutionPolicy(secondOptions, secondContext, CreateTelemetry());

            using (firstContext.EnterTrustedCaller(first))
            {
                Assert.That(firstContext.Current, Is.EqualTo(first));
                Assert.That(firstContext.HasRequestScope, Is.True);
                Assert.That(firstPolicy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(first));
                Assert.That(secondContext.Current, Is.Null);
                Assert.That(secondContext.HasRequestScope, Is.False);
                Assert.That(
                    () => secondPolicy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => secondPolicy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                using (secondContext.EnterTrustedCaller(second))
                {
                    Assert.That(secondContext.Current, Is.EqualTo(second));
                    Assert.That(secondContext.HasRequestScope, Is.True);
                    Assert.That(secondPolicy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(second));
                    Assert.That(firstContext.Current, Is.EqualTo(first));
                    Assert.That(firstContext.HasRequestScope, Is.True);
                    Assert.That(firstPolicy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(first));
                    using (firstContext.EnterTrustedCaller(second))
                    {
                        Assert.That(firstContext.Current, Is.EqualTo(second));
                        Assert.That(firstContext.HasRequestScope, Is.True);
                        Assert.That(
                            () => firstPolicy.Require(McpCapability.DirectoryAdministration),
                            Throws.TypeOf<ServiceResultException>()
                                .With.Property(nameof(ServiceResultException.Code))
                                .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                        Assert.That(secondContext.Current, Is.EqualTo(second));
                        Assert.That(secondContext.HasRequestScope, Is.True);
                        Assert.That(secondPolicy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(second));
                    }
                    Assert.That(firstContext.Current, Is.EqualTo(first));
                    Assert.That(firstContext.HasRequestScope, Is.True);
                    Assert.That(firstPolicy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(first));
                }
                Assert.That(secondContext.Current, Is.Null);
                Assert.That(secondContext.HasRequestScope, Is.False);
                Assert.That(firstPolicy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(first));
            }
            Assert.That(firstContext.Current, Is.Null);
            Assert.That(firstContext.HasRequestScope, Is.False);
            Assert.That(secondContext.Current, Is.Null);
            Assert.That(secondContext.HasRequestScope, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task CapturedChildLosesDisposedAuthorityAsync(bool requestScope)
        {
            var remote = new McpCallerIdentity("operator", "urn:issuer-a");
            var local = new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true);
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                LocalCallerId = "host-A",
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = remote,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    },
                    new McpCallerGrant
                    {
                        Caller = local,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable scope = requestScope
                ? callers.EnterRequest(remote) : callers.EnterTrustedCaller(remote);
            var child = Task.Run(async () =>
            {
                try
                {
                    Assert.That(callers.Current, Is.EqualTo(new McpCallerIdentity("operator", "urn:issuer-a")));
                    Assert.That(callers.HasRequestScope, Is.True);
                    Assert.That(policy.RequireCaller(), Is.EqualTo(remote));
                    Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(remote));
                }
                finally
                {
                    captured.TrySetResult();
                }

                await release.Task.ConfigureAwait(false);

                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.True);
                Assert.That(
                    () => policy.RequireCaller(),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(
                    () => policy.Require(McpCapability.DirectoryAdministration),
                    Throws.TypeOf<ServiceResultException>()
                        .With.Property(nameof(ServiceResultException.Code))
                        .EqualTo(StatusCodes.BadUserAccessDenied.Code));
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.True);
            });

            try
            {
                await captured.Task.ConfigureAwait(false);
                scope.Dispose();
                Assert.That(callers.Current, Is.Null);
                Assert.That(callers.HasRequestScope, Is.False);
                Assert.That(policy.RequireCaller(), Is.EqualTo(local));
                Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(local));
            }
            finally
            {
                scope.Dispose();
                release.TrySetResult();
                await child.ConfigureAwait(false);
            }

            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.False);
            Assert.That(
                policy.RequireCaller(),
                Is.EqualTo(new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true)));
        }

        [Test]
        public void DisposingOuterScopeCannotReviveRevokedAuthority()
        {
            var first = new McpCallerIdentity("operator-a", "urn:issuer-a");
            var second = new McpCallerIdentity("operator-b", "urn:issuer-b");
            var local = new McpCallerIdentity("host-A", "urn:opcfoundation:mcp:local-host", true);
            var callers = new McpCallerContext();
            var options = new McpExecutionOptions
            {
                LocalCallerId = "host-A",
                EnabledCapabilities = [McpCapability.DirectoryAdministration],
                CallerGrants =
                [
                    new McpCallerGrant
                    {
                        Caller = first,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    },
                    new McpCallerGrant
                    {
                        Caller = second,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    },
                    new McpCallerGrant
                    {
                        Caller = local,
                        Capabilities = [McpCapability.DirectoryAdministration]
                    }
                ]
            };
            var policy = new McpExecutionPolicy(options, callers, CreateTelemetry());
            using IDisposable outer = callers.EnterRequest(first);
            using IDisposable inner = callers.EnterRequest(second);

            outer.Dispose();
            Assert.That(callers.Current, Is.EqualTo(second));
            Assert.That(callers.HasRequestScope, Is.True);
            Assert.That(policy.Require(McpCapability.DirectoryAdministration), Is.EqualTo(second));

            inner.Dispose();
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.True);
            Assert.That(
                () => policy.RequireCaller(),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
            Assert.That(
                () => policy.Require(McpCapability.DirectoryAdministration),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));

            outer.Dispose();
            inner.Dispose();
            Assert.That(callers.Current, Is.Null);
            Assert.That(callers.HasRequestScope, Is.True);
            Assert.That(
                () => policy.RequireCaller(),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.Code)).EqualTo(StatusCodes.BadUserAccessDenied.Code));
        }

        private static IEnumerable<TestCaseData> InvalidIdentityFields()
        {
            (string Label, string? Value, Type ExceptionType)[] invalidValues =
            [
                ("Null", null, typeof(ArgumentNullException)),
                ("Empty", string.Empty, typeof(ArgumentException)),
                ("Whitespace", " \t ", typeof(ArgumentException)),
                ("4097", new string('x', 4097), typeof(ArgumentOutOfRangeException)),
                ("4098", new string('x', 4098), typeof(ArgumentOutOfRangeException))
            ];
            foreach ((string label, string? value, Type exceptionType) in invalidValues)
            {
                yield return new TestCaseData(nameof(McpCallerIdentity.Subject), value, exceptionType)
                    .SetName($"{nameof(IdentityValidationRejectsMissingOrOversizedFields)}(Subject{label})");
                yield return new TestCaseData(nameof(McpCallerIdentity.Issuer), value, exceptionType)
                    .SetName($"{nameof(IdentityValidationRejectsMissingOrOversizedFields)}(Issuer{label})");
            }
        }

        private static IEnumerable<TestCaseData> InvalidTrustedCallers()
        {
            yield return new TestCaseData(null, typeof(ArgumentNullException))
                .SetName($"{nameof(TrustedCallerEntryRejectsNullOrInvalidIdentity)}(NullCaller)");
            (string Label, string? Value, Type ExceptionType)[] invalidValues =
            [
                ("Null", null, typeof(ArgumentNullException)),
                ("Empty", string.Empty, typeof(ArgumentException)),
                ("Whitespace", " \t ", typeof(ArgumentException)),
                ("4097", new string('x', 4097), typeof(ArgumentOutOfRangeException)),
                ("4098", new string('x', 4098), typeof(ArgumentOutOfRangeException))
            ];
            foreach ((string label, string? value, Type exceptionType) in invalidValues)
            {
                yield return new TestCaseData(new McpCallerIdentity(value!, "urn:issuer-a"), exceptionType)
                    .SetName($"{nameof(TrustedCallerEntryRejectsNullOrInvalidIdentity)}(Subject{label})");
                yield return new TestCaseData(new McpCallerIdentity("operator", value!), exceptionType)
                    .SetName($"{nameof(TrustedCallerEntryRejectsNullOrInvalidIdentity)}(Issuer{label})");
            }
        }

        private static IEnumerable<TestCaseData> PrincipalsWithoutTrustedSubjects()
        {
            yield return new TestCaseData((ClaimsPrincipal?)null)
                .SetName($"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}(NullPrincipal)");
            yield return new TestCaseData(new ClaimsPrincipal())
                .SetName($"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}(EmptyPrincipal)");
            yield return new TestCaseData(new ClaimsPrincipal(new ClaimsIdentity([], "TestAuthentication")))
                .SetName(
                    $"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}(AuthenticatedWithoutClaims)");

            (string Label, string Type, string Value)[] untrustedClaims =
            [
                ("NameIdentifier", ClaimTypes.NameIdentifier, "attacker"),
                ("Sub", "sub", "attacker"),
                ("Role", ClaimTypes.Role, "Administrator"),
                ("Name", ClaimTypes.Name, "Display Name")
            ];
            foreach ((string label, string type, string value) in untrustedClaims)
            {
                var unauthenticated = new ClaimsIdentity(
                    [new Claim(type, value, ClaimValueTypes.String, "urn:untrusted")]);
                yield return new TestCaseData(new ClaimsPrincipal(unauthenticated))
                    .SetName(
                        $"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}(Unauthenticated{label})");
            }

            (string Label, string Type)[] displayClaims =
                [("RoleOnly", ClaimTypes.Role), ("NameOnly", ClaimTypes.Name)];
            foreach ((string label, string type) in displayClaims)
            {
                var authenticated = new ClaimsIdentity([new Claim(type, "Administrator")], "TestAuthentication");
                yield return new TestCaseData(new ClaimsPrincipal(authenticated))
                    .SetName($"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}({label})");
            }

            (string Label, string Type)[] subjectTypes =
                [("NameIdentifier", ClaimTypes.NameIdentifier), ("Sub", "sub")];
            foreach ((string label, string type) in subjectTypes)
            {
                yield return new TestCaseData(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(type, string.Empty, ClaimValueTypes.String, "urn:issuer-a")],
                    "TestAuthentication")))
                    .SetName($"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}({label}EmptySubject)");
                yield return new TestCaseData(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(type, " \t ", ClaimValueTypes.String, "urn:issuer-a")],
                    "TestAuthentication")))
                    .SetName($"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}({label}BlankSubject)");
                yield return new TestCaseData(new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(type, "operator", ClaimValueTypes.String, " \t ")],
                    "TestAuthentication")))
                    .SetName($"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}({label}BlankIssuer)");
            }
            yield return new TestCaseData(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", new string('x', 4097), ClaimValueTypes.String, "urn:untrusted")])))
                .SetName(
                    $"{nameof(FromPrincipalIgnoresUnauthenticatedOrSubjectlessClaims)}(UnauthenticatedLongSubject)");
        }

        private static ITelemetryContext CreateTelemetry()
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(context => context.LoggerFactory).Returns(NullLoggerFactory.Instance);
            return telemetry.Object;
        }
    }
}
#endif
