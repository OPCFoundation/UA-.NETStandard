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
            Assert.That(PubSubSecurityPolicyRegistry.All, Has.Count.EqualTo(3));
        }

        [Test]
        public void GetByUri_FindsNonePolicy()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.GetByUri(PubSubSecurityPolicyUri.None),
                Is.SameAs(PubSubNonePolicy.Instance));
        }

        [Test]
        public void GetByUri_FindsAes128Policy()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.GetByUri(PubSubSecurityPolicyUri.PubSubAes128Ctr),
                Is.SameAs(PubSubAes128CtrPolicy.Instance));
        }

        [Test]
        public void GetByUri_FindsAes256Policy()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.GetByUri(PubSubSecurityPolicyUri.PubSubAes256Ctr),
                Is.SameAs(PubSubAes256CtrPolicy.Instance));
        }

        [Test]
        public void GetByUri_ReturnsNullForUnknownUri()
        {
            Assert.That(
                PubSubSecurityPolicyRegistry.GetByUri("urn:does-not-exist"),
                Is.Null);
        }

        [Test]
        public void GetByUri_ReturnsNullForNullOrEmpty()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PubSubSecurityPolicyRegistry.GetByUri(null), Is.Null);
                Assert.That(PubSubSecurityPolicyRegistry.GetByUri(string.Empty), Is.Null);
            });
        }

        [Test]
        public void GetByUri_IsCaseSensitive()
        {
            string upper = PubSubSecurityPolicyUri.PubSubAes128Ctr.ToUpperInvariant();
            Assert.That(PubSubSecurityPolicyRegistry.GetByUri(upper), Is.Null);
        }
    }
}
