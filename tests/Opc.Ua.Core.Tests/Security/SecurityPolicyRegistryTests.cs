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
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Security
{
    /// <summary>
    /// Covers the registry that owns an application's security policy set.
    /// </summary>
    /// <remarks>
    /// The property that matters is isolation. Before the policy set became an
    /// object, registering a policy mutated process-wide state, so two
    /// applications in one process shared a list and a test had to undo itself.
    /// </remarks>
    [TestFixture]
    [Category("Security")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable(ParallelScope.All)]
    public class SecurityPolicyRegistryTests
    {
        [Test]
        public void ARegistryStartsWithTheBuiltInPolicies()
        {
            using var registry = new SecurityPolicies();

            Assert.Multiple(() =>
            {
                Assert.That(registry.Policies, Is.Not.Empty);
                Assert.That(
                    registry.GetInfo(SecurityPolicies.Basic256Sha256),
                    Is.Not.Null);
                Assert.That(
                    registry.GetUri(nameof(SecurityPolicies.Basic256Sha256)),
                    Is.EqualTo(SecurityPolicies.Basic256Sha256));
            });
        }

        /// <summary>
        /// The defect the object model removes: a policy registered by one
        /// application used to be visible to every other one in the process.
        /// </summary>
        [Test]
        public void RegisteringInOneRegistryDoesNotReachAnother()
        {
            using var first = new SecurityPolicies();
            using var second = new SecurityPolicies();

            string uri = SecurityPolicies.BaseUri + "IsolatedRegistryPolicy";
            using IDisposable registration = first.Register(
                new SecurityPolicyInfo(uri, "IsolatedRegistryPolicy"));

            Assert.Multiple(() =>
            {
                Assert.That(first.Find(uri), Is.Not.Null);
                Assert.That(second.Find(uri), Is.Null);
                Assert.That(SecurityPolicies.Default.Find(uri), Is.Null);
            });
        }

        [Test]
        public void DisposingARegistrationRestoresTheSet()
        {
            using var registry = new SecurityPolicies();

            string uri = SecurityPolicies.BaseUri + "TransientRegistryPolicy";
            int before = registry.Policies.Count;

            IDisposable registration = registry.Register(
                new SecurityPolicyInfo(uri, "TransientRegistryPolicy"));

            Assert.That(registry.Find(uri), Is.Not.Null);

            registration.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(registry.Find(uri), Is.Null);
                Assert.That(registry.Policies.Count, Is.EqualTo(before));
            });
        }

        /// <summary>
        /// Disposing the registry undoes what was registered through it, so a
        /// container that owns one does not leave the set changed.
        /// </summary>
        [Test]
        public void DisposingTheRegistryUndoesItsRegistrations()
        {
            var registry = new SecurityPolicies();

            string uri = SecurityPolicies.BaseUri + "OwnedRegistryPolicy";
            registry.Register(new SecurityPolicyInfo(uri, "OwnedRegistryPolicy"));
            Assert.That(registry.Find(uri), Is.Not.Null);

            registry.Dispose();

            Assert.That(registry.Find(uri), Is.Null);
        }

        [Test]
        public void FindIgnoresPlatformSupportAndGetInfoDoesNot()
        {
            using var registry = new SecurityPolicies();

            string uri = SecurityPolicies.BaseUri + "UnsupportedRegistryPolicy";
            using IDisposable registration = registry.Register(
                new SecurityPolicyInfo(uri, "UnsupportedRegistryPolicy")
                {
                    PlatformSupport = () => false
                });

            Assert.Multiple(() =>
            {
                Assert.That(registry.Find(uri), Is.Not.Null);
                Assert.That(registry.GetInfoIgnoringPlatformSupport(uri), Is.Not.Null);
                Assert.That(registry.GetInfo(uri), Is.Null);
            });
        }

        /// <summary>
        /// Nothing is injected on the paths that run before a container exists,
        /// so the fallback has to carry the same set the built-ins describe.
        /// </summary>
        [Test]
        public void DefaultCarriesTheSameBuiltInSetAsAFreshRegistry()
        {
            using var fresh = new SecurityPolicies();

            Assert.Multiple(() =>
            {
                Assert.That(
                    SecurityPolicies.Default.GetDefaultUris(),
                    Is.EqualTo(fresh.GetDefaultUris()));
                Assert.That(
                    SecurityPolicies.Default.GetDefaultEccUris(),
                    Is.EqualTo(fresh.GetDefaultEccUris()));
                Assert.That(
                    SecurityPolicies.Default.GetDefaultDeprecatedUris(),
                    Is.EqualTo(fresh.GetDefaultDeprecatedUris()));
                Assert.That(
                    SecurityPolicies.Default.GetDisplayNames(),
                    Is.EqualTo(fresh.GetDisplayNames()));
            });
        }

        [Test]
        public void DefaultIsTheSameInstanceEveryTime()
        {
            Assert.That(
                SecurityPolicies.Default,
                Is.SameAs(SecurityPolicies.Default));
        }

        [Test]
        public void ARegistryReportsThroughTheTelemetryItWasGiven()
        {
            // The registry creates its logger from the telemetry it is handed,
            // which is what removed the ILogger argument from the crypto members.
            using var registry = new SecurityPolicies(null);

            Assert.That(registry.GetInfo(SecurityPolicies.Basic256Sha256), Is.Not.Null);
        }

        [Test]
        public void ModernBuiltInPoliciesMeetMinimumSecurityRequirements()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    SecurityPolicyInfo.Basic256Sha256.MeetsMinimumSecurityRequirements,
                    Is.True);
                Assert.That(
                    SecurityPolicyInfo.ECC_nistP256.MeetsMinimumSecurityRequirements,
                    Is.True);
                Assert.That(
                    SecurityPolicyInfo.RSA_DH_AesGcm.MeetsMinimumSecurityRequirements,
                    Is.True);
            });
        }

        [Test]
        public void DeprecatedAndUnencryptedPoliciesDoNotMeetMinimumSecurityRequirements()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    SecurityPolicyInfo.Basic128Rsa15.MeetsMinimumSecurityRequirements,
                    Is.False);
                Assert.That(SecurityPolicyInfo.None.MeetsMinimumSecurityRequirements, Is.False);
            });
        }

        [Test]
        public void MismatchedEphemeralKeyDoesNotMeetMinimumSecurityRequirements()
        {
            var policy = new SecurityPolicyInfo(SecurityPolicyInfo.Basic256Sha256)
            {
                EphemeralKeyAlgorithm = CertificateKeyAlgorithm.NistP256
            };

            Assert.That(policy.MeetsMinimumSecurityRequirements, Is.False);
        }
    }
}
