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
using Opc.Ua.Server;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Supplies one live owner for direct, in-process result-transfer calls.
    /// </summary>
    internal sealed class MachineryTestSession
    {
        /// <summary>
        /// Registers the owner in the session manager returned by the server factory.
        /// </summary>
        public MachineryTestSession()
        {
            var session = new Mock<ISession>();
            session.SetupGet(value => value.Id).Returns(new NodeId("machinery-test-session", 1));
            m_session = session.Object;
            var sessions = new Mock<ISessionManager>();
            sessions.Setup(value => value.GetSessions()).Returns(() => [m_session]);
            var factory = new Mock<ISessionManagerFactory>();
            factory.SetReturnsDefault(sessions.Object);
            Factory = factory.Object;
        }

        /// <summary>
        /// Gets the factory that exposes the owner as a live server session.
        /// </summary>
        public ISessionManagerFactory Factory { get; }

        /// <summary>
        /// Copies the node manager context with the registered session as its owner.
        /// </summary>
        public ServerSystemContext CreateContext(ServerSystemContext context)
        {
            return context.Copy(m_session);
        }

        /// <summary>The registered transfer owner.</summary>
        private readonly ISession m_session;
    }
}
