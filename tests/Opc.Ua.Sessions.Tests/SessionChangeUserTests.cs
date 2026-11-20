/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Sessions.Tests
{
    /// <summary>
    /// compliance tests for Session Change User.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("SessionServices")]
    public class SessionChangeUserTests : TestFixture
    {
        [Description("Specify different user credentials when activating an already active session. Call Publish. */")]
        [Test]
        public async Task ChangeUserCredentialsOnActiveSessionAsync()
        {
            Assert.Ignore("Change user requires multiple user configs.");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Description("Specify the same user credentials on a session that the user already owns, using the same channel. */")]
        [Test]
        public async Task ReactivateSessionWithSameCredentialsAsync()
        {
            Assert.Ignore("Change user requires multiple user configs.");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Description("Change the owner of a session but specify invalid/incorrect credentials (e.g. good username, empty password).*/")]
        [Test]
        public async Task ChangeUserWithInvalidCredentialsFailsAsync()
        {
            Assert.Ignore("Change user requires multiple user configs.");
            await Task.CompletedTask.ConfigureAwait(false);
        }

        [Description("Activate an already active session while specifying different user login credentials.")]
        [Test]
        public async Task ActivateSessionWithDifferentUserCredentialsAsync()
        {
            Assert.Ignore("Change user requires multiple user configs.");
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
