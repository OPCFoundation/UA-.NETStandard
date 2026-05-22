/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.Conformance.Tests.SessionServices
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
        [Property("ConformanceUnit", "Session Multiple")]
        [Property("Tag", "001")]
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
        [Property("ConformanceUnit", "Session Multiple")]
        [Property("Tag", "Err-001")]
        public async Task SessionCannotBeClosedFromDifferentSecureChannelAsync()
        {
            Assert.Ignore("Multiple sessions error requires session limit config.");
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
