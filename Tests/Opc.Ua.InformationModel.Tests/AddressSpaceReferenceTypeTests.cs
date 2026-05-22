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
    /// compliance tests verifying that fundamental ReferenceTypes
    /// and DataTypes exist in the address space.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("AddressSpaceReferenceTypes")]
    public class AddressSpaceReferenceTypeTests : TestFixture
    {
        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task OrganizesReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task HasComponentReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasComponent).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task HasPropertyReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task HasSubtypeReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task HasTypeDefinitionReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task BooleanDataTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                DataTypeIds.Boolean).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task Int32DataTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                DataTypeIds.Int32).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        [Property("ConformanceUnit", "Address Space Base")]
        [Property("Tag", "001")]
        public async Task StringDataTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                DataTypeIds.String).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        private async Task<DataValue> ReadBrowseNameAsync(NodeId nodeId)
        {
            ReadResponse response = await Session.ReadAsync(
                null, 0, TimestampsToReturn.Both,
                new ReadValueId[]
                {
                    new() {
                        NodeId = nodeId,
                        AttributeId = Attributes.BrowseName
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));
            return response.Results[0];
        }
    }
}
