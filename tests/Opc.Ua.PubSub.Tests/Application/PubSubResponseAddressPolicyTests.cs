/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
            var policy =
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
