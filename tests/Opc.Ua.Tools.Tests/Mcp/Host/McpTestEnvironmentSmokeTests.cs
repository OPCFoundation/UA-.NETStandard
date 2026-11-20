/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if NET10_0_OR_GREATER
using NUnit.Framework;

namespace Opc.Ua.Tools.Tests.Mcp
{
    [TestFixture]
    public sealed class McpTestEnvironmentSmokeTests
    {
        [Test]
        public void SharedSessionManagerHasConnectedSession()
        {
            Assert.That(McpTestEnvironment.SessionManager, Is.Not.Null);
            Assert.That(McpTestEnvironment.SessionManager.IsConnected, Is.True);
            Assert.That(
                McpTestEnvironment.SessionManager.GetSessionOrThrow(McpTestEnvironment.SessionName),
                Is.Not.Null);
        }
    }
}
#endif
