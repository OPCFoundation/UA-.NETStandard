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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using ISession = Opc.Ua.Client.ISession;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// Dedicated fixture for the tests that toggle
    /// <see cref="ServerDiagnosticsState.EnabledFlag"/> (Part 5 §6.3.3,
    /// CTT Base Info Diagnostics 018-1/018-2/018-3).
    /// </summary>
    /// <remarks>
    /// The fixture gets its own in-process server so that disabling the
    /// diagnostics collection does not affect neighboring diagnostics tests.
    /// </remarks>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfo")]
    [Category("DiagnosticsEnabledFlag")]
    [NonParallelizable]
    public class BaseInfoEnabledFlagToggleTests : TestFixture
    {
        [Test]
        public async Task Diagnostics015VerifyEnabledFlagToggleAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Ignore("The server has no UserName endpoint for the administrator.");
            }

            try
            {
                DataValue dv = await ReadValueAsync(
                    admin,
                    VariableIds.Server_ServerDiagnostics_EnabledFlag).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(dv.StatusCode), Is.True,
                    "EnabledFlag must be readable.");

                bool original = dv.GetValue(false);
                bool toggled = !original;

                StatusCode writeResult = await WriteEnabledFlagAsync(admin, toggled).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(writeResult), Is.True,
                    $"EnabledFlag write must succeed (got {writeResult}).");

                DataValue after = await ReadValueAsync(
                    admin,
                    VariableIds.Server_ServerDiagnostics_EnabledFlag).ConfigureAwait(false);
                Assert.That(after.GetValue(original), Is.EqualTo(toggled),
                    "EnabledFlag must reflect the toggled value after write.");

                writeResult = await WriteEnabledFlagAsync(admin, original).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(writeResult), Is.True);
            }
            finally
            {
                await CloseAsync(admin).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task EnabledFlagIsNotWritableWithoutAdministratorAccessAsync()
        {
            DataValue userAccessLevel = await ReadAttributeAsync(
                Session,
                VariableIds.Server_ServerDiagnostics_EnabledFlag,
                Attributes.UserAccessLevel).ConfigureAwait(false);
            Assert.That(
                userAccessLevel.GetValue<byte>(0) & AccessLevels.CurrentWrite,
                Is.Zero,
                "The anonymous session must not get write access to EnabledFlag.");

            StatusCode writeResult = await WriteEnabledFlagAsync(Session, false).ConfigureAwait(false);
            Assert.That(writeResult.Code, Is.EqualTo(StatusCodes.BadUserAccessDenied));

            DataValue flag = await ReadValueAsync(
                Session,
                VariableIds.Server_ServerDiagnostics_EnabledFlag).ConfigureAwait(false);
            Assert.That(flag.GetValue(false), Is.True);
        }

        [Test]
        public async Task DisabledDiagnosticsAreNotReadableAndRestoredWhenEnabledAsync()
        {
            ISession admin = await ConnectAsSysAdminAsync().ConfigureAwait(false);
            if (admin == null)
            {
                Assert.Ignore("The server has no UserName endpoint for the administrator.");
            }

            try
            {
                NodeId[] staticNodes =
                [
                    VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary,
                    VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount,
                    VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CurrentSubscriptionCount,
                    VariableIds.Server_ServerDiagnostics_SubscriptionDiagnosticsArray,
                    VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray
                ];

                DataValue cumulated = await ReadValueAsync(
                    admin,
                    VariableIds.Server_ServerDiagnostics_ServerDiagnosticsSummary_CumulatedSessionCount)
                    .ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(cumulated.StatusCode), Is.True);
                uint cumulatedBefore = cumulated.GetValue<uint>(0);
                Assert.That(
                    await CountSessionNodesAsync(admin).ConfigureAwait(false),
                    Is.GreaterThanOrEqualTo(2),
                    "The fixture session and the administrator session must be listed.");

                StatusCode writeResult = await WriteEnabledFlagAsync(admin, false).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(writeResult), Is.True, writeResult.ToString());

                try
                {
                    // static diagnostic variables return Bad_NotReadable without a value.
                    ArrayOf<DataValue> disabled = await ReadValuesAsync(admin, staticNodes).ConfigureAwait(false);
                    for (int ii = 0; ii < staticNodes.Length; ii++)
                    {
                        Assert.That(disabled[ii].StatusCode.Code, Is.EqualTo(StatusCodes.BadNotReadable),
                            staticNodes[ii].ToString());
                        Assert.That(disabled[ii].WrappedValue.IsNull, Is.True, staticNodes[ii].ToString());
                    }

                    // the flag itself stays readable.
                    DataValue flag = await ReadValueAsync(
                        admin,
                        VariableIds.Server_ServerDiagnostics_EnabledFlag).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(flag.StatusCode), Is.True);
                    Assert.That(flag.GetValue(true), Is.False);

                    // dynamic session nodes are removed from the address space.
                    Assert.That(await CountSessionNodesAsync(admin).ConfigureAwait(false), Is.Zero);
                    DataValue ownSession = await ReadAttributeAsync(
                        admin,
                        admin.SessionId,
                        Attributes.BrowseName).ConfigureAwait(false);
                    Assert.That(ownSession.StatusCode.Code, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                }
                finally
                {
                    writeResult = await WriteEnabledFlagAsync(admin, true).ConfigureAwait(false);
                    Assert.That(StatusCode.IsGood(writeResult), Is.True, writeResult.ToString());
                }

                // enabling restores readable values, the live sessions and cumulative counters.
                ArrayOf<DataValue> enabled = await ReadValuesAsync(admin, staticNodes).ConfigureAwait(false);
                for (int ii = 0; ii < staticNodes.Length; ii++)
                {
                    Assert.That(StatusCode.IsGood(enabled[ii].StatusCode), Is.True,
                        $"{staticNodes[ii]}: {enabled[ii].StatusCode}");
                }
                Assert.That(enabled[1].GetValue<uint>(0), Is.EqualTo(cumulatedBefore),
                    "Toggling the flag must not reset or increment the cumulative counters.");

                Assert.That(
                    await CountSessionNodesAsync(admin).ConfigureAwait(false),
                    Is.GreaterThanOrEqualTo(2),
                    "The diagnostics nodes of the live sessions must be restored.");
                DataValue sessionName = await ReadAttributeAsync(
                    admin,
                    admin.SessionId,
                    Attributes.BrowseName).ConfigureAwait(false);
                Assert.That(StatusCode.IsGood(sessionName.StatusCode), Is.True,
                    "The session diagnostics node keeps its NodeId.");

                ArrayOf<DataValue> sessionArray = await ReadValuesAsync(
                    admin,
                    [VariableIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary_SessionDiagnosticsArray])
                    .ConfigureAwait(false);
                Assert.That(sessionArray[0].WrappedValue.TryGetValue(out ArrayOf<ExtensionObject> sessions), Is.True);
                Assert.That(
                    sessions.ToArray().Select(s => s.TryGetValue(out SessionDiagnosticsDataType d) ? d.SessionId : default),
                    Does.Contain(admin.SessionId));
            }
            finally
            {
                await CloseAsync(admin).ConfigureAwait(false);
            }
        }

        private async Task<int> CountSessionNodesAsync(ISession session)
        {
            BrowseResponse response = await session.BrowseAsync(
                null,
                null,
                0,
                new BrowseDescription[]
                {
                    new()
                    {
                        NodeId = ObjectIds.Server_ServerDiagnostics_SessionsDiagnosticsSummary,
                        BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent,
                        IncludeSubtypes = false,
                        NodeClassMask = (uint)NodeClass.Object,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);
            return response.Results[0].References.Count;
        }

        private static async Task<StatusCode> WriteEnabledFlagAsync(ISession session, bool enabled)
        {
            WriteResponse response = await session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new()
                    {
                        NodeId = VariableIds.Server_ServerDiagnostics_EnabledFlag,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(new Variant(enabled))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }

        private static Task<DataValue> ReadValueAsync(ISession session, NodeId nodeId)
        {
            return ReadAttributeAsync(session, nodeId, Attributes.Value);
        }

        private static async Task<DataValue> ReadAttributeAsync(
            ISession session,
            NodeId nodeId,
            uint attributeId)
        {
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() { NodeId = nodeId, AttributeId = attributeId }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            return response.Results[0];
        }

        private static async Task<ArrayOf<DataValue>> ReadValuesAsync(ISession session, NodeId[] nodeIds)
        {
            ReadResponse response = await session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                nodeIds.Select(n => new ReadValueId { NodeId = n, AttributeId = Attributes.Value }).ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            return response.Results;
        }

        private static async Task CloseAsync(ISession session)
        {
            if (session == null)
            {
                return;
            }
            try
            {
                await session.CloseAsync(5000, true).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
            session.Dispose();
        }
    }
}
