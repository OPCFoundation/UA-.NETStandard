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

#nullable enable

using System;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    public sealed class SessionBindingContextTests
    {
        [TestCase(UserTokenType.Anonymous, null)]
        [TestCase(UserTokenType.UserName, "user")]
        [TestCase(UserTokenType.Certificate, "certificate")]
        [TestCase(UserTokenType.IssuedToken, "issuer:subject")]
        public void SnapshotPreservesValidatedValues(UserTokenType tokenType, string? userId)
        {
            var context = new SessionBindingContext(
                new NodeId(1), "channel", 7, tokenType, userId,
                SecurityPolicies.Basic256Sha256, MessageSecurityMode.SignAndEncrypt);
            Assert.That(context.SessionId, Is.EqualTo(new NodeId(1)));
            Assert.That(context.SecureChannelId, Is.EqualTo("channel"));
            Assert.That(context.ActivationSequence, Is.EqualTo(7));
            Assert.That(context.UserTokenType, Is.EqualTo(tokenType));
            Assert.That(context.ClientUserId, Is.EqualTo(userId));
            Assert.That(context.SecurityPolicyUri, Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(context.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
        }

        [TestCase("sessionId")]
        [TestCase("secureChannelId")]
        [TestCase("activationSequence")]
        [TestCase("userTokenType")]
        [TestCase("clientUserId")]
        [TestCase("securityPolicyUri")]
        [TestCase("securityMode")]
        public void SnapshotRejectsInvalidState(string parameter)
        {
            Assert.That(() => new SessionBindingContext(
                parameter == "sessionId" ? NodeId.Null : new NodeId(1),
                parameter == "secureChannelId" ? string.Empty : "channel",
                parameter == "activationSequence" ? 0 : 1,
                parameter == "userTokenType" ? (UserTokenType)99 : UserTokenType.Anonymous,
                parameter == "clientUserId" ? "unvalidated-user" : null,
                parameter == "securityPolicyUri" ? string.Empty : SecurityPolicies.None,
                parameter == "securityMode" ? MessageSecurityMode.Invalid : MessageSecurityMode.None),
                Throws.InstanceOf<ArgumentException>().With.Property("ParamName").EqualTo(parameter));
        }

        [Test]
        public void AuthenticatedSnapshotRequiresContinuityKey()
        {
            Assert.That(() => new SessionBindingContext(
                new NodeId(1), "channel", 1, UserTokenType.UserName, null,
                SecurityPolicies.None, MessageSecurityMode.None),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("clientUserId"));
        }
    }
}
