// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;
using Opc.Ua.PubSub.Security.Sks;

namespace Opc.Ua.PubSub.Tests.Security.Sks
{
    /// <summary>
    /// Tests for <see cref="SksKeyRequest"/>.
    /// </summary>
    [TestFixture]
    [TestSpec("8.3.2")]
    public class SksKeyRequestTests
    {
        [Test]
        public void Constructor_RecordsAllFields()
        {
            var request = new SksKeyRequest("group-1", 5U, 3U);
            Assert.That(request.SecurityGroupId, Is.EqualTo("group-1"));
            Assert.That(request.StartingTokenId, Is.EqualTo(5U));
            Assert.That(request.RequestedKeyCount, Is.EqualTo(3U));
        }

        [Test]
        public void Equality_TreatsRequestsWithSameFieldsAsEqual()
        {
            var a = new SksKeyRequest("g", 1U, 2U);
            var b = new SksKeyRequest("g", 1U, 2U);
            var c = new SksKeyRequest("g", 1U, 3U);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void Defaults_AreZeroValuedRecord()
        {
            SksKeyRequest empty = default;
            Assert.That(empty.SecurityGroupId, Is.Null);
            Assert.That(empty.StartingTokenId, Is.Zero);
            Assert.That(empty.RequestedKeyCount, Is.Zero);
        }
    }
}
