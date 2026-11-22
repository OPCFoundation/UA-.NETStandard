// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for WriteMask, AccessLevel, and related
    /// variable attributes.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpaceWriteMask")]
    public class AddressSpaceWriteMaskTests : TestFixture
    {
        [Test]
        public async Task ReadWriteMaskOnVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.WriteMask).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadUserWriteMaskOnVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.UserWriteMask).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadAccessLevelOnWritableVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.AccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            byte accessLevel =
                result.WrappedValue.GetByte();
            Assert.That(
                accessLevel & AccessLevels.CurrentRead,
                Is.Not.Zero,
                "CurrentRead should be set.");
            Assert.That(
                accessLevel & AccessLevels.CurrentWrite,
                Is.Not.Zero,
                "CurrentWrite should be set.");
        }

        [Test]
        public async Task ReadUserAccessLevelOnWritableVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.UserAccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadAccessLevelOnServerStateVariableAsync()
        {
            DataValue result = await ReadAttributeAsync(
                VariableIds.Server_ServerStatus_State,
                Attributes.AccessLevel).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);

            byte accessLevel =
                result.WrappedValue.GetByte();
            Assert.That(
                accessLevel & AccessLevels.CurrentWrite,
                Is.Zero,
                "ServerState should not be writable.");
        }

        [Test]
        public async Task ReadMinimumSamplingIntervalOnVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.MinimumSamplingInterval)
                .ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task ReadHistorizingOnVariableAsync()
        {
            NodeId nodeId = ToNodeId(Constants.ScalarStaticInt32);
            DataValue result = await ReadAttributeAsync(
                nodeId, Attributes.Historizing).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task WriteWriteMaskWritableAttributeIsNotAccessDeniedAsync()
        {
            // The node advertises Description (and DisplayName) as writable via its
            // WriteMask, and it carries explicit RolePermissions. Writing a
            // WriteMask-writable non-Value attribute must be governed by the
            // WriteMask (Good), not rejected with BadUserAccessDenied because the
            // RolePermissions omit WriteAttribute (OPC UA Part 3 §4.8.3 / §5.2.10).
            // This mirrors the CTT "Address Space WriteMask" conformance unit.
            ArrayOf<WriteValue> wv = new WriteValue[]
            {
                new()
                {
                    NodeId = ToNodeId(Constants.ScalarStaticInt32),
                    AttributeId = Attributes.Description,
                    Value = new DataValue(
                        new Variant(new LocalizedText("en", "CTT WriteMask test")))
                }
            }.ToArrayOf();

            WriteResponse response = await Session.WriteAsync(
                null, wv, CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            Assert.That(
                response.Results[0].Code,
                Is.Not.EqualTo(StatusCodes.BadUserAccessDenied),
                "Writing a WriteMask-writable attribute must not be BadUserAccessDenied.");
            Assert.That(
                StatusCode.IsGood(response.Results[0]),
                Is.True,
                $"Expected Good for the Description attribute advertised as writable, got {response.Results[0]}.");
        }

        private async Task<DataValue> ReadAttributeAsync(
            NodeId nodeId, uint attributeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = attributeId
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
