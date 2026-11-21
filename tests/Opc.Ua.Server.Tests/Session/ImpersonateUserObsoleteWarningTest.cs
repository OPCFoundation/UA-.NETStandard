// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Reflection;
using NUnit.Framework;
using Opc.Ua.Identity;

namespace Opc.Ua.Server.Tests.Session
{
    /// <summary>
    /// Asserts the public obsolete metadata that produces CS0618 when callers
    /// subscribe to the legacy event in normal project compilation.
    /// </summary>
    public sealed class ImpersonateUserObsoleteWarningTest
    {
        [Test]
        public void SessionManagerImpersonateUserEventIsObsolete()
        {
            EventInfo eventInfo = typeof(SessionManager).GetEvent("ImpersonateUser")!;

            ObsoleteAttribute attribute = eventInfo.GetCustomAttribute<ObsoleteAttribute>()!;

            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.Message, Does.Contain(nameof(IUserTokenAuthenticator)));
            Assert.That(attribute.Message, Does.Contain(nameof(IServerIdentityRegistry)));
        }
    }
}
