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

namespace Opc.Ua.Conformance.Tests.AddressSpaceModel
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
        [Property("ConformanceUnit", "Address Space UserWriteMask")]
        [Property("Tag", "004")]
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
        [Property("ConformanceUnit", "Address Space UserWriteMask")]
        [Property("Tag", "Err-001")]
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

        [Description("Write a valid value to each attribute that can be written to as determined by the value of the WriteMask and/or UserWriteMask attributes. */ include( &quot;./library/Base/NodeTypeAttrib")]
        [Test]
        [Property("ConformanceUnit", "Address Space UserWriteMask")]
        [Property("Tag", "Err-002")]
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
        [Property("ConformanceUnit", "Address Space UserWriteMask")]
        [Property("Tag", "Err-004")]
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
