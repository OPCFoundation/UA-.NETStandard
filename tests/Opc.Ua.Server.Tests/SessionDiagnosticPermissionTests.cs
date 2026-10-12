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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies diagnostic permission validation through an activated server session.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class SessionDiagnosticPermissionTests
    {
        /// <summary>
        /// Starts the server that owns the sessions under test.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartServerAsync()
        {
            m_fixture = new ServerFixture<StandardServer>(telemetry => new StandardServer(telemetry));
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the server and releases its sessions and certificate stores.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopServerAsync()
        {
            await m_fixture.StopAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Verifies that only server-authorized, requested additional information survives mask validation.
        /// </summary>
        /// <param name="role">Zero for an ordinary user, one for SecurityAdmin, or two for ConfigureAdmin.</param>
        /// <param name="requested">Whether the request asks for additional information.</param>
        /// <param name="forged">Whether the client supplies the internal permission flag.</param>
        [TestCase(0, false, true)]
        [TestCase(0, true, true)]
        [TestCase(0, true, false)]
        [TestCase(1, false, true)]
        [TestCase(1, true, true)]
        [TestCase(1, true, false)]
        [TestCase(2, false, true)]
        [TestCase(2, true, true)]
        [TestCase(2, true, false)]
        public async Task DiagnosticPermissionCannotBeForgedAndRequiresAnAdditionalInfoRequest(
            int role, bool requested, bool forged)
        {
            StandardServer server = m_fixture.Server;
            (RequestHeader header, SecureChannelContext channel) =
                await server.CreateAndActivateSessionAsync("DiagnosticPermission").ConfigureAwait(false);
            try
            {
                ISession session = server.CurrentInstance.SessionManager.GetSession(header.AuthenticationToken)!;
                var identity = new Mock<IUserIdentity>();
                ArrayOf<NodeId> roles = role switch
                {
                    0 => [ObjectIds.WellKnownRole_AuthenticatedUser],
                    1 => [ObjectIds.WellKnownRole_SecurityAdmin],
                    2 => [ObjectIds.WellKnownRole_ConfigureAdmin],
                    _ => throw new ArgumentOutOfRangeException(nameof(role))
                };
                identity.Setup(value => value.GrantedRoleIds).Returns(roles);
                session.RefreshEffectiveIdentity(identity.Object);
                DiagnosticsMasks mask = DiagnosticsMasks.ServiceSymbolicId | DiagnosticsMasks.OperationSymbolicId;
                if (requested)
                {
                    mask |= DiagnosticsMasks.ServiceAdditionalInfo | DiagnosticsMasks.OperationAdditionalInfo;
                }
                header.ReturnDiagnostics = (uint)(mask |
                    (forged ? DiagnosticsMasks.UserPermissionAdditionalInfo : DiagnosticsMasks.None));

                session.ValidateDiagnosticInfo(header);

                bool authorized = role != 0 && requested;
                var validated = (DiagnosticsMasks)header.ReturnDiagnostics;
                Assert.That((validated & DiagnosticsMasks.UserPermissionAdditionalInfo) != 0,
                    Is.EqualTo(authorized));
                Assert.That(validated & ~DiagnosticsMasks.UserPermissionAdditionalInfo, Is.EqualTo(mask));
                var result = new ServiceResult(
                    "urn:device", StatusCodes.BadOutOfRange, LocalizedText.Null, "limit: 100", innerResult: null);
                foreach (bool serviceLevel in new[] { false, true })
                {
                    var diagnostic = new DiagnosticInfo(
                        result, validated, serviceLevel, new StringTable(), Mock.Of<ILogger>());
                    Assert.That(diagnostic.AdditionalInfo, Is.EqualTo(authorized ? "limit: 100" : null));
                }
            }
            finally
            {
                await server.CloseSessionAsync(channel, header, true, RequestLifetime.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Verifies that a null diagnostic request header is rejected explicitly.
        /// </summary>
        [Test]
        public async Task DiagnosticValidationRejectsANullRequestHeader()
        {
            StandardServer server = m_fixture.Server;
            (RequestHeader header, SecureChannelContext channel) =
                await server.CreateAndActivateSessionAsync("NullDiagnosticHeader").ConfigureAwait(false);
            try
            {
                ISession session = server.CurrentInstance.SessionManager.GetSession(header.AuthenticationToken)!;
                Assert.Throws<ArgumentNullException>(() => session.ValidateDiagnosticInfo(null!));
            }
            finally
            {
                await server.CloseSessionAsync(channel, header, true, RequestLifetime.None).ConfigureAwait(false);
            }
        }

        /// <summary>The server fixture used to create and activate real sessions.</summary>
        private ServerFixture<StandardServer> m_fixture = null!;
    }
}
