/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
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
        public async Task OrganizesReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.Organizes).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task HasComponentReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasComponent).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task HasPropertyReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasProperty).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task HasSubtypeReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task HasTypeDefinitionReferenceTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                ReferenceTypeIds.HasTypeDefinition).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task BooleanDataTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                DataTypeIds.Boolean).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
        public async Task Int32DataTypeExistsAsync()
        {
            DataValue result = await ReadBrowseNameAsync(
                DataTypeIds.Int32).ConfigureAwait(false);
            Assert.That(StatusCode.IsGood(result.StatusCode), Is.True);
        }

        [Test]
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
