// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// AOT integration tests for server diagnostics read operations.
    /// </summary>
    [ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
    public class DiagnosticsAotTests(AotTestFixture fixture)
    {
        [Test]
        public async Task ReadServerStatusAsync()
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
        public async Task ReadServerDiagnosticsAsync()
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
        public async Task ReadNamespaceArrayAsync()
        {
            DataValue nsArray = await fixture.Session.ReadValueAsync(
                VariableIds.Server_NamespaceArray,
                CancellationToken.None).ConfigureAwait(false);

            await Assert.That(StatusCode.IsGood(nsArray.StatusCode)).IsTrue();
            await Assert.That(nsArray.WrappedValue.IsNull).IsFalse();

            // Verify through the session namespace table
            await Assert.That(fixture.Session.NamespaceUris.Count)
                .IsGreaterThan(0);
            string opcUaNamespace = fixture.Session.NamespaceUris.GetString(0) ??
                throw new InvalidOperationException("The base namespace is missing.");
            await Assert.That(opcUaNamespace).Contains("opcfoundation.org");
        }

        [Test]
        public async Task ReadServerArrayAsync()
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
