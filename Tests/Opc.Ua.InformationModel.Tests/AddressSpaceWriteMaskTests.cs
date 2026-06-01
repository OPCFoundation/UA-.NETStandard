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
