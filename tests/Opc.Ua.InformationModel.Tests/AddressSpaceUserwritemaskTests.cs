// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Address Space UserWriteMask.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpaceModel")]
    public class AddressSpaceUserwritemaskTests : TestFixture
    {
        [Description("Write to the Value attribute of a Variable, where the AccessLevel == CurrentWriteService. */")]
        [Test]
        public async Task WriteValueAttributeWithCurrentWriteAccessLevelAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() { NodeId = ObjectIds.Server, AttributeId = Attributes.UserWriteMask }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Write to a node whose AccessLevel does not contain write capabilities. */")]
        [Test]
        public async Task WriteToNodeWithoutWriteAccessLevelFailsAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() { NodeId = ObjectIds.Server, AttributeId = Attributes.UserWriteMask }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Write a valid value to each attribute that can be written to as determined by the value of the WriteMask and/or UserWriteMask attributes.")]
        [Test]
        public async Task WriteAttributesPerWriteMaskCapabilitiesAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() { NodeId = ObjectIds.Server, AttributeId = Attributes.UserWriteMask }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }

        [Description("Write to a node whose UserAccessLevel does not contain write capabilities. */")]
        [Test]
        public async Task WriteToNodeWithoutUserWriteAccessLevelFailsAsync()
        {
            ReadResponse readResponse = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                new ReadValueId[]
                {
                    new() { NodeId = ObjectIds.Server, AttributeId = Attributes.UserWriteMask }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(readResponse.Results.Count, Is.EqualTo(1));
            Assert.That(StatusCode.IsGood(readResponse.Results[0].StatusCode), Is.True);
        }
    }
}
