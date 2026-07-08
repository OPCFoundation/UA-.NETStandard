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
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Tests.Application
{
    /// <summary>
    /// Coverage for <see cref="PubSubResponseAddressPolicy"/> (SA-ACT-03).
    /// </summary>
    [TestFixture]
    [TestSpec("SA-ACT-03", Summary = "PubSub Action response-address policy")]
    public class PubSubResponseAddressPolicyTests
    {
        private static PubSubResponseAddressContext Context(string? address, bool usesTopics)
        {
            return new PubSubResponseAddressContext
            {
                ConnectionName = "conn",
                DataSetWriterId = 1,
                ActionTargetId = 2,
                ResponseAddress = address,
                TransportUsesTopics = usesTopics
            };
        }

        [Test]
        public void Default_RejectsRequestorTopicOnTopicTransport()
        {
            PubSubResponseAddressPolicy policy = PubSubResponseAddressPolicy.Default;
            Assert.Multiple(() =>
            {
                Assert.That(policy.IsAllowed(Context("attacker/topic", usesTopics: true)), Is.False);
                Assert.That(policy.IsAllowed(Context("attacker/topic", usesTopics: false)), Is.True);
                Assert.That(policy.IsAllowed(Context(null, usesTopics: true)), Is.True);
                Assert.That(policy.IsAllowed(Context(string.Empty, usesTopics: true)), Is.True);
            });
        }

        [Test]
        public void AllowAll_PermitsAnyAddress()
        {
            PubSubResponseAddressPolicy policy = PubSubResponseAddressPolicy.AllowAll;
            Assert.That(policy.IsAllowed(Context("anything/at/all", usesTopics: true)), Is.True);
        }

        [Test]
        public void Matching_HonorsWildcardPatterns()
        {
            PubSubResponseAddressPolicy policy =
                PubSubResponseAddressPolicy.Matching("responses/*", "exact/topic");
            Assert.Multiple(() =>
            {
                Assert.That(policy.IsAllowed(Context("responses/writer5", usesTopics: true)), Is.True);
                Assert.That(policy.IsAllowed(Context("exact/topic", usesTopics: true)), Is.True);
                Assert.That(policy.IsAllowed(Context("responses", usesTopics: true)), Is.False);
                Assert.That(policy.IsAllowed(Context("other/topic", usesTopics: true)), Is.False);
                Assert.That(policy.IsAllowed(Context("other/topic", usesTopics: false)), Is.True);
            });
        }

        [Test]
        public void Matching_WithNullPatterns_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PubSubResponseAddressPolicy.Matching(null!));
        }

        [Test]
        public void Create_WithNullPredicate_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => PubSubResponseAddressPolicy.Create("custom", null!));
        }
    }
}
