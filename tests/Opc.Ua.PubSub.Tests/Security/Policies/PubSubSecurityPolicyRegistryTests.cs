/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua.PubSub.Security;
using Opc.Ua.PubSub.Security.Policies;

namespace Opc.Ua.PubSub.Tests.Security.Policies
{
    /// <summary>
    /// Tests for <see cref="PubSubSecurityPolicyRegistry"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("7.2.4.4.3.1", Summary = "PubSub security policy URI registry")]
    public class PubSubSecurityPolicyRegistryTests
    {
        [Test]
        public void All_ContainsThreeBuiltInPolicies()
        {
            Assert.That(PubSubSecurityPolicyRegistry.Default.Policies.Count, Is.EqualTo(3));
        }

        [Test]
        public void GetByUri_FindsNonePolicy()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.None),
                Is.SameAs(PubSubNonePolicy.Instance));
        }

        [Test]
        public void GetByUri_FindsAes128Policy()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr),
                Is.SameAs(PubSubAes128CtrPolicy.Instance));
        }

        [Test]
        public void GetByUri_FindsAes256Policy()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.Default.GetByUri(PubSubSecurityPolicyUri.PubSubAes256Ctr),
                Is.SameAs(PubSubAes256CtrPolicy.Instance));
        }

        [Test]
        public void GetByUri_ReturnsNullForUnknownUri()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.Default.GetByUri("urn:does-not-exist"),
                Is.Null);
        }

        [Test]
        public void GetByUri_ReturnsNullForNullOrEmpty()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PubSubSecurityPolicyRegistry.Default.GetByUri(null), Is.Null);
                Assert.That(PubSubSecurityPolicyRegistry.Default.GetByUri(string.Empty), Is.Null);
            });
        }

        [Test]
        public void GetByUri_IsCaseSensitive()
        {
            string upper = PubSubSecurityPolicyUri.PubSubAes128Ctr.ToUpperInvariant();
            Assert.That(PubSubSecurityPolicyRegistry.Default.GetByUri(upper), Is.Null);
        }
    }
}
