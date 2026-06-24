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

using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Adapter.Session;

namespace Opc.Ua.PubSub.Adapter.Tests.Unit
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
