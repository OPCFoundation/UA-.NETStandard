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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Server;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// Dedicated fixture for tests that require the
    /// <see cref="ServerDiagnosticsState.EnabledFlag"/> to be writable.
    ///
    /// <para>
    /// The shared <see cref="TestFixture"/> deliberately leaves the
    /// EnabledFlag <see cref="AccessLevels.CurrentRead"/>-only because
    /// the in-process <c>ServerInternalData.SetDiagnosticsEnabled</c>
    /// implementation deletes the diagnostic child nodes when the flag
    /// is set to <c>false</c>, and re-enabling the flag does not
    /// recreate them — toggling on the shared server breaks ~5
    /// neighboring diagnostic tests.
    /// </para>
    ///
    /// <para>
    /// This fixture inherits <see cref="TestFixture"/> (so it gets a
    /// fresh in-process server, client session, and PKI store) and
    /// flips the <c>EnabledFlag.AccessLevel</c> to read+write
    /// <em>after</em> server startup. The fixture is intentionally
    /// minimal — only the EnabledFlag-toggle-related tests live here
    /// — so the side-effects of toggling are confined to this class.
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfo")]
    [Category("DiagnosticsEnabledFlag")]
    [NonParallelizable]
    public class BaseInfoEnabledFlagToggleTests : TestFixture
    {
        [OneTimeSetUp]
        public Task EnableEnabledFlagWriteAsync()
        {
            // Allow the EnabledFlag to be written. The
            // OnSimpleWriteValue hook (registered in
            // ServerInternalData.CreateServerObject) delegates to
            // DiagnosticsNodeManager.SetDiagnosticsEnabledAsync, which
            // is the spec-conformant toggle path.
            ServerDiagnosticsState diag =
                ReferenceServer.CurrentInstance?.ServerObject?.ServerDiagnostics;
            if (diag != null)
            {
                diag.EnabledFlag.AccessLevel = AccessLevels.CurrentReadOrWrite;
                diag.EnabledFlag.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            }
            return Task.CompletedTask;
        }

        [Test]
        public async Task Diagnostics015VerifyEnabledFlagToggleAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            try
            {
                ISession session = admin ?? Session;
                DataValue dv = await ReadAttributeAsync(
                    VariableIds.Server_ServerDiagnostics_EnabledFlag,
                    Attributes.Value, session)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                    "EnabledFlag must be readable.");

                bool original = dv.GetValue(false);
                bool toggled = !original;

                WriteResponse writeResp = await session.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = VariableIds.Server_ServerDiagnostics_EnabledFlag,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(toggled))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);

                Assert.That(StatusCode.IsGood(writeResp.Results[0]), Is.True,
                    $"EnabledFlag write must succeed (got {writeResp.Results[0]}).");

                DataValue after = await ReadAttributeAsync(
                    VariableIds.Server_ServerDiagnostics_EnabledFlag,
                    Attributes.Value, session)
                    .ConfigureAwait(false);
                Assert.That(after.GetValue(original), Is.EqualTo(toggled),
                    "EnabledFlag must reflect the toggled value after write.");

                // Restore original — note: the
                // SetDiagnosticsEnabledAsync(false)/(true) round trip
                // recreates child diagnostic nodes only on the
                // false→true transition for the FIRST setup; this
                // restore is best-effort to leave a tidy fixture state.
                await session.WriteAsync(
                    null,
                    new WriteValue[]
                    {
                        new() {
                            NodeId = VariableIds.Server_ServerDiagnostics_EnabledFlag,
                            AttributeId = Attributes.Value,
                            Value = new DataValue(new Variant(original))
                        }
                    }.ToArrayOf(),
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                if (admin != null)
                {
                    try
                    {
                        await admin.CloseAsync(5000, true).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort
                    }
                    admin.Dispose();
                }
            }
        }

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId,
            uint attributeId,
            ISession session = null)
        {
            ReadResponse response = await (session ?? Session).ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }
    }
}
