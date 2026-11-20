// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// compliance tests for Session Multiple.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SessionServices")]
    public class SessionMultipleTests : TestFixture
    {
        [Description("Create two sessions, activate them, and then close them.")]
        [Test]
        public async Task TwoActiveSessionsCanBeCreatedAndClosedAsync()
        {
            using ISession session2 = await ClientFixture.ConnectAsync(
                ServerUrl, SecurityPolicies.None).ConfigureAwait(false);
            Assert.That(session2, Is.Not.Null);
            Assert.That(session2.Connected, Is.True);
            await session2.CloseAsync(5000, true).ConfigureAwait(false);
        }

        [Description("Check that a session can't be closed from a different SecureChannel")]
        [Test]
        public async Task SessionCannotBeClosedFromDifferentSecureChannelAsync()
        {
            Assert.Ignore("Multiple sessions error requires session limit config.");
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
