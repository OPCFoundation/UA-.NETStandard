// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
