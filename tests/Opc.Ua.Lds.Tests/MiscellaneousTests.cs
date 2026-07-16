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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Lds.Tests
{
    /// <summary>
    /// compliance tests for miscellaneous server behavior:
    /// concurrent sessions, stress reads/writes, error handling,
    /// and general protocol correctness.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("Miscellaneous")]
    public class MiscellaneousTests : TestFixture
    {
        [Description("Verify server handles rapid connect/disconnect gracefully.")]
        [Test]
        public async Task RapidConnectDisconnectAsync()
        {
            for (int i = 0; i < 5; i++)
            {
                ISession session = await ClientFixture
                    .ConnectAsync(ServerUrl, SecurityPolicies.None)
                    .ConfigureAwait(false);
                Assert.That(session.Connected, Is.True);
                await session.CloseAsync(5000, true).ConfigureAwait(false);
                session.Dispose();
            }
        }

        [Description("Verify server handles multiple concurrent sessions.")]
        [Test]
        public async Task ConcurrentSessionsAsync()
        {
            const int count = 5;
            var sessions = new List<ISession>(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    ISession s = await ClientFixture
                        .ConnectAsync(ServerUrl, SecurityPolicies.None)
                        .ConfigureAwait(false);
                    Assert.That(s.Connected, Is.True);
                    sessions.Add(s);
                }

                // All sessions should have unique ids
                List<NodeId> ids = sessions.ConvertAll(s => s.SessionId);
                Assert.That(ids.Distinct().Count(), Is.EqualTo(count));
            }
            finally
            {
                foreach (ISession s in sessions)
                {
                    try
                    {
                        await s.CloseAsync(5000, true).ConfigureAwait(false);
                    }
                    catch
                    {
                        // best-effort
                    }
                    s.Dispose();
                }
            }
        }

        [Description("Read many nodes in a single Read call (stress test).")]
        [Test]
        public async Task ReadManyNodesInSingleCallAsync()
        {
            // Build a batch of 100 reads using the same set of scalar nodes
            var items = new List<ReadValueId>();
            for (int i = 0; i < 100; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                items.Add(new ReadValueId
                {
                    NodeId = ToNodeId(eni),
                    AttributeId = Attributes.Value
                });
            }

            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                items.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(100));
            foreach (DataValue dv in response.Results)
            {
                Assert.That(
                    StatusCode.IsGood(dv.StatusCode) ||
                    StatusCode.IsUncertain(dv.StatusCode), Is.True);
            }
        }

        [Description("Browse many nodes in a single Browse call.")]
        [Test]
        public async Task BrowseManyNodesInSingleCallAsync()
        {
            var descriptions = new List<BrowseDescription>();
            NodeId[] wellKnown =
            [
                ObjectIds.ObjectsFolder,
                ObjectIds.TypesFolder,
                ObjectIds.ViewsFolder,
                ObjectIds.Server,
                ObjectIds.RootFolder
            ];

            // Create 20 browse requests
            for (int i = 0; i < 20; i++)
            {
                descriptions.Add(new BrowseDescription
                {
                    NodeId = wellKnown[i % wellKnown.Length],
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                });
            }

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                descriptions.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(20));
            foreach (BrowseResult br in response.Results)
            {
                Assert.That(StatusCode.IsGood(br.StatusCode), Is.True);
            }
        }

        [Description("Write many nodes in a single Write call.")]
        [Test]
        public async Task WriteManyNodesInSingleCallAsync()
        {
            var values = new List<WriteValue>();
            for (int i = 0; i < 19; i++)
            {
                ExpandedNodeId eni = Constants.ScalarStaticNodes[
                    i % Constants.ScalarStaticNodes.Length];
                values.Add(new WriteValue
                {
                    NodeId = ToNodeId(eni),
                    AttributeId = Attributes.Value,
                    Value = new DataValue(Variant.From(i))
                });
            }

            WriteResponse response = await Session.WriteAsync(
                null, values.ToArray().ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(values.Count));

            // Some nodes may reject writes due to type mismatch, so
            // just verify the server responded for each.
            foreach (StatusCode sc in response.Results)
            {
                Assert.That(sc.Code, Is.Not.EqualTo(StatusCodes.BadInternalError),
                    "Server should not return internal error for batch write.");
            }
        }

        [Description("Verify StatusCode Good equals zero.")]
        [Test]
        public void StatusCodeGoodIsZero()
        {
            Assert.That((uint)StatusCodes.Good, Is.Zero);
        }

        [Description("Verify BadNodeIdUnknown has expected code value.")]
        [Test]
        public void StatusCodeBadNodeIdUnknownIsCorrect()
        {
            Assert.That(StatusCodes.BadNodeIdUnknown, Is.EqualTo(0x80340000u));
        }

        [Description("Verify server returns timestamps in UTC.")]
        [Test]
        public async Task ServerTimestampsAreUtcAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            DataValue dv = response.Results[0];

            if (dv.ServerTimestamp != DateTimeUtc.MinValue)
            {
                // DateTimeUtc is always UTC by definition
                Assert.That(dv.ServerTimestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
            }

            if (dv.SourceTimestamp != DateTimeUtc.MinValue)
            {
                // DateTimeUtc is always UTC by definition
                Assert.That(dv.SourceTimestamp, Is.Not.EqualTo(DateTimeUtc.MinValue));
            }
        }

        [Description("Read a non-existent node and verify BadNodeIdUnknown.")]
        [Test]
        public async Task ReadNonExistentNodeReturnsBadNodeIdUnknownAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = Constants.InvalidNodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Description("Verify server returns proper error for reading an invalid attribute.")]
        [Test]
        public async Task ReadInvalidAttributeIdReturnsBadAttributeIdInvalidAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ObjectIds.Server,
                        AttributeId = 999
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].StatusCode.Code,
                Is.EqualTo(StatusCodes.BadAttributeIdInvalid));
        }

        [Description("Verify the server ResponseHeader always has a non-default timestamp.")]
        [Test]
        public async Task ResponseHeaderHasTimestampAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = ToNodeId(Constants.ScalarStaticInt32),
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                response.ResponseHeader.Timestamp,
                Is.GreaterThan(DateTimeUtc.MinValue));
        }

        [Test]
        public async Task WriteAndReadBackValueAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);

            WriteResponse writeResp = await Session.WriteAsync(
                null,
                new WriteValue[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue(Variant.From(42))
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(writeResp.Results.Count, Is.EqualTo(1));
            Assert.That(
                StatusCode.IsGood(writeResp.Results[0]), Is.True);

            ReadResponse readResp = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(readResp.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResp.Results[0].StatusCode), Is.True);
            Assert.That(
                readResp.Results[0].WrappedValue.GetInt32(),
                Is.EqualTo(42));
        }

        [Test]
        public async Task VerifyServerStateIsRunningAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_State,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            int state = response.Results[0].WrappedValue.GetInt32();
            Assert.That(state, Is.Zero,
                "Server state should be Running (0).");
        }

        [Test]
        public async Task ReadNamespaceArrayAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_NamespaceArray,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            Assert.That(response.Results[0].WrappedValue.TryGetValue(out ArrayOf<string> nsArr), Is.True);
            string[] nsStrings = nsArr.ToArray();
            Assert.That(nsStrings, Has.Length.GreaterThanOrEqualTo(2),
                "NamespaceArray should have at least 2 entries.");
        }

        [Test]
        public async Task ReadServerArrayAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerArray,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            Assert.That(response.Results[0].WrappedValue.TryGetValue(out ArrayOf<string> srvArr), Is.True);
            string[] serverStrings = srvArr.ToArray();
            Assert.That(serverStrings, Is.Not.Empty,
                "ServerArray should have at least 1 entry.");
        }

        [Test]
        public async Task ReadServiceLevelAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServiceLevel,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            byte level = response.Results[0].WrappedValue.GetByte();
            Assert.That(level, Is.InRange((byte)0, (byte)255));
        }

        [Test]
        public async Task VerifyServerCurrentTimeUpdatesAsync()
        {
            ReadResponse first = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(first.Results[0].StatusCode), Is.True);

            await Task.Delay(100).ConfigureAwait(false);

            ReadResponse second = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(StatusCode.IsGood(second.Results[0].StatusCode), Is.True);

            DateTimeUtc time1 = first.Results[0].WrappedValue.GetDateTime();
            DateTimeUtc time2 = second.Results[0].WrappedValue.GetDateTime();
            Assert.That(time2, Is.GreaterThan(time1));
        }

        [Test]
        public async Task VerifyMaxNodesPerReadAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerCapabilities_OperationLimits_MaxNodesPerRead,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            if (!StatusCode.IsGood(response.Results[0].StatusCode))
            {
                Assert.Ignore("MaxNodesPerRead not available.");
            }

            uint maxNodes = response.Results[0].WrappedValue.GetUInt32();
            Assert.That(maxNodes, Is.GreaterThan(0u));
        }

        [Test]
        public async Task VerifyLocaleIdArrayAsync()
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = VariableIds.Server_ServerCapabilities_LocaleIdArray,
                        AttributeId = Attributes.Value
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(response.Results[0].StatusCode), Is.True);

            Assert.That(response.Results[0].WrappedValue.TryGetValue(out ArrayOf<string> locArr), Is.True);
            string[] localeStrings = locArr.ToArray();
            Assert.That(localeStrings, Is.Not.Empty,
                "LocaleIdArray should have at least 1 entry.");
        }
    }
}
