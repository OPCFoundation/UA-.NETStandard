/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests
{
    /// <summary>
    /// Tests value equality for <see cref="ServerConnectionOptions"/>.
    /// </summary>
    [TestFixture]
    public sealed class ServerConnectionOptionsTests
    {
        [Test]
        public void EqualsReturnsTrueForEqualConnectionIdentity()
        {
            ServerConnectionOptions left = CreateIdentity();
            ServerConnectionOptions right = CreateIdentity();

            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentEndpoint()
        {
            ServerConnectionOptions left = CreateIdentity();
            ServerConnectionOptions right = CreateIdentity();
            right.EndpointUrl = "opc.tcp://other:4840";

            Assert.That(left, Is.Not.EqualTo(right));
        }

        [Test]
        public void EqualsIncludesCredentialsAndIgnoresApplicationConfiguration()
        {
            ServerConnectionOptions left = CreateIdentity();
            left.ApplicationConfiguration = new ApplicationConfiguration();

            ServerConnectionOptions right = CreateIdentity();
            right.ApplicationConfiguration = new ApplicationConfiguration();

            Assert.That(left, Is.EqualTo(right));
            Assert.That(left.GetHashCode(), Is.EqualTo(right.GetHashCode()));

            right.Password = "rotated";

            Assert.That(left, Is.Not.EqualTo(right));

            right.Password = left.Password;
            left.UserIdentity = new Mock<IUserIdentity>().Object;
            right.UserIdentity = new Mock<IUserIdentity>().Object;

            Assert.That(left, Is.Not.EqualTo(right));
        }

        private static ServerConnectionOptions CreateIdentity()
        {
            return new ServerConnectionOptions
            {
                EndpointUrl = "opc.tcp://host:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                UserName = "user1",
                SessionName = "Session1",
                SessionTimeout = 60000,
                ApplicationName = "Application1"
            };
        }
    }
}
