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
using Opc.Ua.Gds.Client;

namespace Opc.Ua.MigrationHelpers.Tests
{
    /// <summary>
    /// Runtime tests for <see cref="GlobalDiscoveryServerClientShim"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="GlobalDiscoveryServerClient"/> is <c>sealed</c> and has
    /// non-virtual <c>RegisterApplicationAsync</c> / <c>UnregisterApplicationAsync</c>
    /// methods, so neither Moq nor a hand-rolled subclass can intercept the
    /// shim's forwarded call. Exercising the shim end-to-end requires a
    /// live GDS endpoint (full server + secure channel bootstrap), which
    /// belongs to the integration test suite (<c>Opc.Ua.Gds.Tests</c>), not
    /// a unit-level runtime check of the shim wiring.
    /// </remarks>
    [TestFixture]
    [Category("Shim")]
    public class GlobalDiscoveryServerClientShimTests
    {
        /// <summary>
        /// Placeholder for the shim invocation test. Requires a full GDS
        /// server bootstrap to exercise.
        /// </summary>
        [Test]
        [Ignore("Requires GDS server bootstrap: GlobalDiscoveryServerClient " +
            "is sealed and RegisterApplicationAsync is non-virtual, so the " +
            "shim cannot be exercised via Moq. Integration coverage lives " +
            "in Opc.Ua.Gds.Tests.")]
        public Task RegisterApplicationCallsRegisterApplicationAsyncAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Placeholder for the unregister shim invocation test. Requires a
        /// full GDS server bootstrap to exercise.
        /// </summary>
        [Test]
        [Ignore("Requires GDS server bootstrap: GlobalDiscoveryServerClient " +
            "is sealed and UnregisterApplicationAsync is non-virtual, so the " +
            "shim cannot be exercised via Moq. Integration coverage lives " +
            "in Opc.Ua.Gds.Tests.")]
        public Task UnregisterApplicationCallsUnregisterApplicationAsyncAsync()
        {
            return Task.CompletedTask;
        }
    }
}
