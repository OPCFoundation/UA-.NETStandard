/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;
using ValidationOptions = Opc.Ua.Security.Certificates.CertificateValidationOptions;

namespace Opc.Ua.Core.Tests.Security.Certificates
{
    [TestFixture]
    [Category("CertificateManager")]
    [Category("SecurityRegression")]
    [NonParallelizable]
    public sealed class CertificateScopedValidationTests
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            (m_telemetry as IDisposable)?.Dispose();
        }

        [Test]
        public async Task ScopedValidationKeepsConcurrentTrustRootsDisjointAsync()
        {
            using Certificate rootA = CreateAuthority("CN=Scoped root A");
            using Certificate rootB = CreateAuthority("CN=Scoped root B");
            using Certificate leafA = CreateIssued("CN=Scoped leaf A", rootA);
            using Certificate leafB = CreateIssued("CN=Scoped leaf B", rootB);
            await using var manager = new CertificateManager(m_telemetry);
            var scopeA = Scope([rootA], []);
            var scopeB = Scope([rootB], []);

            CertificateValidationResult[] results = await ValidateConcurrentlyAsync(
                manager, leafA, leafB, scopeA, scopeB).ConfigureAwait(false);

            Assert.That(results[0].IsValid, Is.True);
            Assert.That(results[1].IsValid, Is.True);
            Assert.That(results[2].IsValid, Is.False,
                "A concurrently validated root must not leak into another call's isolated scope.");
        }

        [Test]
        public async Task ScopedValidationDoesNotInheritGlobalRootsAutoAcceptOrAcceptErrorAsync()
        {
            using Certificate globalRoot = CreateAuthority("CN=Global trusted root");
            using Certificate scopedRoot = CreateAuthority("CN=Only scoped root");
            using Certificate globalLeaf = CreateIssued("CN=Leaf under global root", globalRoot);
            var configuration = ConfigurationWithTrustedRoot(globalRoot, autoAccept: true);
            await using var manager = new CertificateManager(m_telemetry)
            {
                AcceptError = static (_, _) => true
            };
            manager.MapFromSecurityConfiguration(configuration);
            var scope = Scope([scopedRoot], [globalRoot]);

            using var chain = new CertificateCollection { globalLeaf };
            CertificateValidationResult result = await manager.ValidateAsync(
                chain,
                TrustListIdentifier.Peers,
                new ValidationOptions
                {
                    TrustScope = scope,
                    RejectUnknownRevocationStatus = false,
                    RecordRejectedCertificates = false
                }).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(manager.AutoAcceptUntrustedCertificates, Is.True,
                "The per-call scope must not mutate the manager's permissive default.");
            Assert.That(manager.AcceptError, Is.Not.Null);
            Assert.That(manager.AcceptError!(globalLeaf, new ServiceResult(StatusCodes.BadCertificateUntrusted)),
                Is.True, "The manager retains its permissive callback, but scoped validation must not inherit it.");
        }

        [Test]
        public async Task ScopedValidationWithEmptyAnchorsDoesNotFallBackToGlobalTrustAsync()
        {
            using Certificate globalRoot = CreateAuthority("CN=Global root with empty scope");
            using Certificate globalLeaf = CreateIssued("CN=Global-root leaf", globalRoot);
            await using var manager = new CertificateManager(m_telemetry);
            manager.MapFromSecurityConfiguration(ConfigurationWithTrustedRoot(globalRoot, autoAccept: true));

            CertificateValidationResult result = await ValidateScopedAsync(
                manager,
                globalLeaf,
                Scope([], [globalRoot])).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.Not.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ScopedIssuerMaterialCompletesChainsButDoesNotEstablishTrustAsync()
        {
            using Certificate root = CreateAuthority("CN=Scoped chain root");
            using Certificate intermediate = CreateIntermediate("CN=Scoped intermediate", root);
            using Certificate leaf = CreateIssued("CN=Scoped leaf", intermediate);
            using Certificate unrelatedRoot = CreateAuthority("CN=Issuer-only untrusted root");
            using Certificate unrelatedLeaf = CreateIssued("CN=Issuer-only leaf", unrelatedRoot);
            await using var manager = new CertificateManager(m_telemetry);

            CertificateValidationResult completed = await ValidateScopedAsync(
                manager, leaf, Scope([root], [intermediate])).ConfigureAwait(false);
            CertificateValidationResult issuerOnly = await ValidateScopedAsync(
                manager, unrelatedLeaf, Scope([], [unrelatedRoot])).ConfigureAwait(false);

            Assert.That(completed.IsValid, Is.True,
                "A root anchor plus an intermediate supplied only as issuer material completes the chain.");
            Assert.That(issuerOnly.IsValid, Is.False,
                "Issuer certificates, including self-signed roots, must never establish trust.");
        }

        [Test]
        public async Task ScopedValidationRejectsCertificateRevokedByScopedCrlAsync()
        {
            using Certificate root = CreateAuthority("CN=CRL scoped root");
            using Certificate leaf = CreateIssued("CN=Revoked scoped leaf", root);
            IX509CRL crl = CrlBuilder.Create(root.SubjectName)
                .SetThisUpdate(DateTime.UtcNow.AddMinutes(-1))
                .SetNextUpdate(DateTime.UtcNow.AddDays(1))
                .AddRevokedCertificate(leaf)
                .CreateForRSA(root);
            await using var manager = new CertificateManager(m_telemetry);
            var scope = Scope([root], [], [crl.RawData]);

            CertificateValidationResult result = await ValidateScopedAsync(manager, leaf, scope)
                .ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadCertificateRevoked));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ScopedValidationRequiresCurrentCrlWhenUnknownRevocationIsRejected(
            bool includeCrl,
            bool currentCrl)
        {
            using Certificate root = CreateAuthority("CN=Scoped revocation policy root");
            using Certificate leaf = CreateIssued("CN=Scoped revocation policy leaf", root);
            DateTime now = DateTime.UtcNow;
            IX509CRL crl = CrlBuilder.Create(root.SubjectName)
                .SetThisUpdate(now.AddDays(-2))
                .SetNextUpdate(currentCrl ? now.AddDays(1) : now.AddDays(-1))
                .CreateForRSA(root);
            var scope = Scope([root], [], includeCrl ? [crl.RawData] : []);
            await using var manager = new CertificateManager(m_telemetry);
            using var chain = new CertificateCollection { leaf };

            CertificateValidationResult result = await manager.ValidateAsync(
                chain,
                TrustListIdentifier.Peers,
                new ValidationOptions
                {
                    TrustScope = scope,
                    RejectUnknownRevocationStatus = true,
                    RecordRejectedCertificates = false
                }).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.EqualTo(includeCrl && currentCrl));
            Assert.That(result.StatusCode, Is.EqualTo(includeCrl && currentCrl
                ? StatusCodes.Good
                : StatusCodes.BadCertificateRevocationUnknown));
        }

        [Test]
        public async Task ScopedValidationRejectsExpiredAuthorityWithoutTimestampOverrideAsync()
        {
            using Certificate expiredAuthority = CreateAuthority(
                "CN=Expired scoped authority",
                new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            using Certificate leaf = CreateIssued("CN=Leaf signed by expired authority", expiredAuthority);
            await using var manager = new CertificateManager(m_telemetry);

            CertificateValidationResult result = await ValidateScopedAsync(
                manager, leaf, Scope([expiredAuthority], [])).ConfigureAwait(false);

            Assert.That(result.IsValid, Is.False);
            Assert.That(
                result.Errors.Any(error =>
                    error.StatusCode == StatusCodes.BadCertificateIssuerTimeInvalid ||
                    error.StatusCode == StatusCodes.BadCertificateTimeInvalid) ||
                    result.StatusCode == StatusCodes.BadCertificateIssuerTimeInvalid ||
                    result.StatusCode == StatusCodes.BadCertificateTimeInvalid,
                Is.True,
                "An expired authority must not be accepted without valid signing-timestamp evidence.");
        }

        [Test]
        public async Task ScopedValidationCannotPoisonOrConsumeDefaultCertificateValidationCacheAsync()
        {
            using Certificate root = CreateAuthority("CN=Cached default root");
            using Certificate unrelatedRoot = CreateAuthority("CN=Unrelated per-call root");
            using Certificate leaf = CreateIssued("CN=Cached default leaf", root);
            await using var manager = new CertificateManager(m_telemetry);
            SecurityConfiguration configuration = ConfigurationWithTrustedRoot(root, autoAccept: false);
            configuration.UseValidatedCertificates = true;
            manager.MapFromSecurityConfiguration(configuration);

            CertificateValidationResult initialDefault = await manager.ValidateAsync(
                leaf, TrustListIdentifier.Peers).ConfigureAwait(false);
            CertificateValidationResult scoped = await ValidateScopedAsync(
                manager, leaf, Scope([unrelatedRoot], [root])).ConfigureAwait(false);
            CertificateValidationResult laterDefault = await manager.ValidateAsync(
                leaf, TrustListIdentifier.Peers).ConfigureAwait(false);

            Assert.That(initialDefault.IsValid, Is.True);
            Assert.That(scoped.IsValid, Is.False,
                "A previously cached default path must not satisfy a different per-call trust scope.");
            Assert.That(laterDefault.IsValid, Is.True,
                "A scoped failure must not invalidate or replace the ordinary application validation cache.");
        }

        [Test]
        public void CertificateValidationScopeCopiesInputAndGetterSnapshots()
        {
            byte[] mutableDer = [0x01, 0x02, 0x03];
            var input = new[]
            {
                new ByteString((ReadOnlyMemory<byte>)mutableDer)
            };
            var scope = new CertificateValidationScope(input.ToArrayOf());

            mutableDer[0] = 0xff;
            input[0] = ByteString.From(new byte[] { 0x09 });
            ArrayOf<ByteString> firstRead = scope.TrustedCertificates;
            Assert.That(firstRead[0].ToArray(), Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }));

            Assert.That(MemoryMarshal.TryGetArray(firstRead.Memory, out ArraySegment<ByteString> returned), Is.True);
            returned.Array![returned.Offset] = ByteString.From(new byte[] { 0x08 });
            Assert.That(
                scope.TrustedCertificates[0].ToArray(),
                Is.EqualTo(new byte[] { 0x01, 0x02, 0x03 }),
                "Mutating one returned array must not mutate the scope or later snapshots.");
            Assert.That(scope.IssuerCertificates.IsEmpty, Is.True);
            Assert.That(scope.CertificateRevocationLists.IsEmpty, Is.True);
        }

        /// <summary>
        /// Joins every validation before returning control to the caller that owns the borrowed handles.
        /// </summary>
        private static Task<CertificateValidationResult[]> ValidateConcurrentlyAsync(
            CertificateManager manager,
            Certificate leafA,
            Certificate leafB,
            CertificateValidationScope scopeA,
            CertificateValidationScope scopeB)
        {
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<CertificateValidationResult> aWithA = ValidateAfterStartAsync(start.Task, manager, leafA, scopeA);
            Task<CertificateValidationResult> bWithB = ValidateAfterStartAsync(start.Task, manager, leafB, scopeB);
            Task<CertificateValidationResult> bWithA = ValidateAfterStartAsync(start.Task, manager, leafB, scopeA);
            start.SetResult(true);
            return Task.WhenAll(aWithA, bWithB, bWithA);
        }

        private static async Task<CertificateValidationResult> ValidateAfterStartAsync(
            Task start,
            CertificateManager manager,
            Certificate certificate,
            CertificateValidationScope scope)
        {
            await start.ConfigureAwait(false);
            return await ValidateScopedAsync(manager, certificate, scope).ConfigureAwait(false);
        }

        private static async Task<CertificateValidationResult> ValidateScopedAsync(
            CertificateManager manager,
            Certificate certificate,
            CertificateValidationScope scope)
        {
            using var chain = new CertificateCollection { certificate };
            return await manager.ValidateAsync(
                chain,
                TrustListIdentifier.Peers,
                new ValidationOptions
                {
                    TrustScope = scope,
                    AutoAcceptUntrustedCertificates = false,
                    RejectSHA1SignedCertificates = true,
                    RejectUnknownRevocationStatus = false,
                    MinimumCertificateKeySize = 2048,
                    AcceptError = static (_, _) => false,
                    RecordRejectedCertificates = false
                }).ConfigureAwait(false);
        }

        private static CertificateValidationScope Scope(
            Certificate[] trusted,
            Certificate[] issuers,
            byte[][]? crls = null)
        {
            return new CertificateValidationScope(
                trusted.Select(ToByteString).ToArray().ToArrayOf(),
                issuers.Select(ToByteString).ToArray().ToArrayOf(),
                (crls ?? []).Select(raw => new ByteString((ReadOnlyMemory<byte>)raw))
                    .ToArray()
                    .ToArrayOf());
        }

        private static ByteString ToByteString(Certificate certificate)
        {
            return new ByteString((ReadOnlyMemory<byte>)certificate.RawData);
        }

        private static SecurityConfiguration ConfigurationWithTrustedRoot(
            Certificate root,
            bool autoAccept)
        {
            return new SecurityConfiguration
            {
                AutoAcceptUntrustedCertificates = autoAccept,
                TrustedPeerCertificates =
                {
                    TrustedCertificates = [new CertificateIdentifier { RawData = root.RawData }]
                }
            };
        }

        private static Certificate CreateAuthority(
            string subject,
            DateTime? notBefore = null,
            DateTime? notAfter = null)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(notBefore ?? s_validFrom)
                .SetNotAfter(notAfter ?? s_validTo)
                .SetCAConstraint(-1)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static Certificate CreateIntermediate(string subject, Certificate issuer)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(s_validFrom)
                .SetNotAfter(s_validTo)
                .SetCAConstraint(0)
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static Certificate CreateIssued(string subject, Certificate issuer)
        {
            return CertificateBuilder.Create(subject)
                .SetNotBefore(s_validFrom)
                .SetNotAfter(s_validTo)
                .SetIssuer(issuer)
                .SetRSAKeySize(2048)
                .CreateForRSA();
        }

        private static readonly DateTime s_validFrom =
            new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime s_validTo =
            new(2090, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private ITelemetryContext m_telemetry = null!;
    }
}
