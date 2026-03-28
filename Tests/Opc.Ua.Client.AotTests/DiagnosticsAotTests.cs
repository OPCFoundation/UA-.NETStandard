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

using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Client.AotTests
{
    /// <summary>
    /// AOT integration tests for server diagnostics read operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class DiagnosticsAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task ReadServerStatus()
        {
            DataValue stateValue = await fixture.Session.ReadValueAsync(
                VariableIds.Server_ServerStatus_State,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(stateValue.StatusCode))
                .IsTrue();

            // ServerState.Running == 0
            await Assert.That(
                Convert.ToInt32(
                    stateValue.WrappedValue.AsBoxedObject(),
                    System.Globalization.CultureInfo.InvariantCulture))
                .IsEqualTo(0);
        }

        [Test]
        public async Task ReadServerDiagnostics()
        {
            try
            {
                DataValue diagnostics =
                    await fixture.Session.ReadValueAsync(
                        VariableIds
                            .Server_ServerDiagnostics_ServerDiagnosticsSummary,
                        CancellationToken.None).ConfigureAwait(false);

                await Assert.That(
                    StatusCode.IsGood(diagnostics.StatusCode)).IsTrue();
                await Assert.That(
                    diagnostics.WrappedValue.AsBoxedObject()).IsNotNull();
            }
            catch (ServiceResultException ex)
                when (ex.StatusCode == StatusCodes.BadUserAccessDenied)
            {
                // Anonymous user may not access diagnostics — test passes
            }
        }

        [Test]
        public async Task ReadNamespaceArray()
        {
            DataValue nsArray = await fixture.Session.ReadValueAsync(
                VariableIds.Server_NamespaceArray,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(nsArray.StatusCode)).IsTrue();
            await Assert.That(nsArray.WrappedValue.AsBoxedObject())
                .IsNotNull();

            // Verify through the session namespace table
            await Assert.That(fixture.Session.NamespaceUris.Count)
                .IsGreaterThan(0);
            string opcUaNamespace = fixture.Session.NamespaceUris.GetString(0);
            await Assert.That(opcUaNamespace).Contains("opcfoundation.org");
        }

        [Test]
        public async Task ReadServerArray()
        {
            DataValue serverArray = await fixture.Session.ReadValueAsync(
                VariableIds.Server_ServerArray,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(serverArray.StatusCode))
                .IsTrue();
            await Assert.That(serverArray.WrappedValue.AsBoxedObject())
                .IsNotNull();
        }
    }
}
