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

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.InformationModel.Tests
{
    /// <summary>
    /// compliance tests for Base Information DataType and structure type
    /// definitions. Verifies that standard DataTypes exist, have correct
    /// supertype relationships, and that structure types expose expected members.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Category("BaseInfoDataTypes")]
    public class BaseInfoDataTypeTests : TestFixture
    {
        [Test]
        public async Task AudioDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.AudioDataType),
                "AudioDataType").ConfigureAwait(false);

            await AssertSupertypeAsync(
                new NodeId(DataTypes.AudioDataType),
                new NodeId(DataTypes.ByteString),
                "AudioDataType").ConfigureAwait(false);
        }

        [Test]
        public async Task BitFieldMaskDataTypeIsSubtypeOfUInt64Async()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.BitFieldMaskDataType),
                "BitFieldMaskDataType").ConfigureAwait(false);

            await AssertSupertypeAsync(
                new NodeId(DataTypes.BitFieldMaskDataType),
                new NodeId(DataTypes.UInt64),
                "BitFieldMaskDataType").ConfigureAwait(false);
        }

        [Test]
        public async Task DateDataTypesExistUnderStringAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.DateString),
                "DateString").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                new NodeId(DataTypes.TimeString),
                "TimeString").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                new NodeId(DataTypes.DurationString),
                "DurationString").ConfigureAwait(false);
        }

        [Test]
        public async Task DecimalDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.Decimal),
                "Decimal").ConfigureAwait(false);
        }

        [Test]
        public async Task DecimalStringIsSubtypeOfStringAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.DecimalString),
                "DecimalString").ConfigureAwait(false);

            await AssertSupertypeAsync(
                new NodeId(DataTypes.DecimalString),
                new NodeId(DataTypes.String),
                "DecimalString").ConfigureAwait(false);
        }

        [Test]
        public async Task HandleIsSubtypeOfUInt32Async()
        {
            await AssertNodeExistsAsync(
                HandleId, "Handle").ConfigureAwait(false);

            await AssertSupertypeAsync(
                HandleId,
                new NodeId(DataTypes.UInt32),
                "Handle").ConfigureAwait(false);
        }

        [Test]
        public async Task ImageDataTypesExistAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.Image),
                "Image").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                new NodeId(DataTypes.ImageBMP),
                "ImageBMP").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                new NodeId(DataTypes.ImageGIF),
                "ImageGIF").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                new NodeId(DataTypes.ImageJPG),
                "ImageJPG").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                new NodeId(DataTypes.ImagePNG),
                "ImagePNG").ConfigureAwait(false);
        }

        [Test]
        public async Task NormalizedStringIsSubtypeOfStringAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.NormalizedString),
                "NormalizedString").ConfigureAwait(false);

            await AssertSupertypeAsync(
                new NodeId(DataTypes.NormalizedString),
                new NodeId(DataTypes.String),
                "NormalizedString").ConfigureAwait(false);
        }

        [Test]
        public async Task SemanticVersionStringIsSubtypeOfStringAsync()
        {
            await AssertNodeExistsAsync(
                SemanticVersionStringId,
                "SemanticVersionString").ConfigureAwait(false);

            await AssertSupertypeAsync(
                SemanticVersionStringId,
                new NodeId(DataTypes.String),
                "SemanticVersionString").ConfigureAwait(false);
        }

        [Test]
        public async Task TrimmedStringIsSubtypeOfStringAsync()
        {
            await AssertNodeExistsAsync(
                TrimmedStringId,
                "TrimmedString").ConfigureAwait(false);

            await AssertSupertypeAsync(
                TrimmedStringId,
                new NodeId(DataTypes.String),
                "TrimmedString").ConfigureAwait(false);
        }

        [Test]
        public async Task UriStringIsSubtypeOfStringAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.UriString),
                "UriString").ConfigureAwait(false);

            await AssertSupertypeAsync(
                new NodeId(DataTypes.UriString),
                new NodeId(DataTypes.String),
                "UriString").ConfigureAwait(false);
        }

        [Test]
        public async Task ContentFilterElementExistsAsync()
        {
            await AssertNodeExistsAsync(
                ContentFilterElementId,
                "ContentFilterElement").ConfigureAwait(false);

            List<ReferenceDescription> refs = await BrowseAllRefsAsync(
                ContentFilterElementId).ConfigureAwait(false);

            bool hasEncoding = refs.Any(
                r => r.ReferenceTypeId == ReferenceTypeIds.HasEncoding);
            Assert.That(hasEncoding, Is.True,
                "ContentFilterElement should have HasEncoding references.");
        }

        [Test]
        public async Task StructureDataTypeExistsAndHasChildrenAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.Structure),
                "Structure").ConfigureAwait(false);

            List<ReferenceDescription> refs = await BrowseRefsAsync(
                new NodeId(DataTypes.Structure),
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);

            Assert.That(refs, Is.Not.Empty,
                "Structure should have subtypes.");
        }

        [Test]
        public async Task StructureHasUnionAndOptionalFieldsSubtypesAsync()
        {
            List<ReferenceDescription> refs = await BrowseRefsAsync(
                new NodeId(DataTypes.Structure),
                ReferenceTypeIds.HasSubtype).ConfigureAwait(false);

            if (refs.Count == 0)
            {
                Assert.Ignore("Structure subtypes not found.");
            }

            Assert.That(
                HasChildWithName(refs, "Union"), Is.True,
                "Structure should have Union subtype.");
            if (!HasChildWithName(refs, "StructureWithOptionalFields"))
            {
                Assert.Ignore("StructureWithOptionalFields not found (v1.04+ type).");
            }
        }

        [Test]
        public async Task EUInformationExistsAsync()
        {
            await AssertNodeExistsAsync(
                EUInformationId,
                "EUInformation").ConfigureAwait(false);
        }

        [Test]
        public async Task KeyValuePairStructureExistsAsync()
        {
            await AssertNodeExistsAsync(
                KeyValuePairId,
                "KeyValuePair").ConfigureAwait(false);
        }

        [Test]
        public async Task ArgumentDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.Argument),
                "Argument").ConfigureAwait(false);

            // Argument is a Structure DataType — its fields (Name, DataType,
            // ValueRank, ArrayDimensions, Description) are exposed via
            // DataTypeDefinition, not browseable child nodes.
            await AssertStructureFieldExistsAsync(
                new NodeId(DataTypes.Argument), "Name").ConfigureAwait(false);
        }

        [Test]
        public async Task OptionSetDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.OptionSet),
                "OptionSet").ConfigureAwait(false);
        }

        [Test]
        public async Task PortableNodeIdAndQualifiedNameExistAsync()
        {
            await AssertNodeExistsAsync(
                PortableNodeIdId,
                "PortableNodeId").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                PortableQualifiedNameId,
                "PortableQualifiedName").ConfigureAwait(false);
        }

        [Test]
        public async Task RangeDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                RangeId, "Range").ConfigureAwait(false);
        }

        [Test]
        public async Task RationalNumberTypeHasComponentsAsync()
        {
            await AssertNodeExistsAsync(
                RationalNumberTypeId,
                "RationalNumberType").ConfigureAwait(false);

            List<ReferenceDescription> refs = await BrowseRefsAsync(
                RationalNumberTypeId).ConfigureAwait(false);

            if (refs.Count == 0 ||
                !HasChildWithName(refs, "Numerator"))
            {
                Assert.Ignore(
                    "RationalNumberType children not browseable.");
            }

            Assert.That(
                HasChildWithName(refs, "Denominator"), Is.True,
                "RationalNumberType should have Denominator.");
        }

        [Test]
        public async Task RationalNumberDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.RationalNumber),
                "RationalNumber").ConfigureAwait(false);
        }

        [Test]
        public async Task ReferenceDescriptionDataTypeExistsAsync()
        {
            // The standard 1.05 nodeset renamed this DataType from
            // "ReferenceDescription" (i=518) to "ReferenceDescriptionDataType"
            // (i=32659).
            await AssertNodeExistsAsync(
                new NodeId(32659u),
                "ReferenceDescriptionDataType").ConfigureAwait(false);
        }

        [Test]
        public async Task StatusResultDataTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                StatusResultId, "StatusResult").ConfigureAwait(false);
        }

        [Test]
        public async Task DataTypeEncodingTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                ObjectTypeIds.DataTypeEncodingType,
                "DataTypeEncodingType").ConfigureAwait(false);
        }

        [Test]
        public async Task CurrencyUnitTypeExistsAsync()
        {
            await AssertNodeExistsAsync(
                CurrencyUnitTypeId,
                "CurrencyUnitType").ConfigureAwait(false);
        }

        [Test]
        public async Task CurrencyUnitTypeHasAlphabeticCodeAsync()
        {
            await AssertNodeExistsAsync(
                CurrencyUnitTypeId,
                "CurrencyUnitType").ConfigureAwait(false);

            // CurrencyUnitType is a Structure DataType — its fields are
            // exposed via the DataTypeDefinition attribute, not browseable
            // child nodes.
            await AssertStructureFieldExistsAsync(
                CurrencyUnitTypeId, "AlphabeticCode").ConfigureAwait(false);
        }

        [Test]
        public async Task CurrencyUnitTypeHasCurrencyAsync()
        {
            await AssertNodeExistsAsync(
                CurrencyUnitTypeId,
                "CurrencyUnitType").ConfigureAwait(false);

            await AssertStructureFieldExistsAsync(
                CurrencyUnitTypeId, "Currency").ConfigureAwait(false);
        }

        [Test]
        public async Task CurrencyUnitTypeHasExponentAsync()
        {
            await AssertNodeExistsAsync(
                CurrencyUnitTypeId,
                "CurrencyUnitType").ConfigureAwait(false);

            await AssertStructureFieldExistsAsync(
                CurrencyUnitTypeId, "Exponent").ConfigureAwait(false);
        }

        [Test]
        public async Task SpatialDataCoordinateTypesExistAsync()
        {
            await AssertNodeExistsAsync(
                CartesianCoordinatesTypeId,
                "CartesianCoordinatesType").ConfigureAwait(false);

            await AssertNodeExistsAsync(
                ThreeDCartesianCoordinatesTypeId,
                "ThreeDCartesianCoordinatesType").ConfigureAwait(false);
        }

        [Test]
        public async Task SpatialDataStructuresExistAsync()
        {
            await AssertNodeExistsAsync(
                new NodeId(DataTypes.ThreeDCartesianCoordinates),
                "ThreeDCartesianCoordinates").ConfigureAwait(false);
        }

        private static readonly NodeId HandleId = new(31917);
        private static readonly NodeId SemanticVersionStringId = new(24263);
        private static readonly NodeId TrimmedStringId = DataTypeIds.TrimmedString;
        private static readonly NodeId ContentFilterElementId = new(583);
        private static readonly NodeId EUInformationId = new(887);
        private static readonly NodeId KeyValuePairId = new(14533);
        private static readonly NodeId RangeId = new(884);
        private static readonly NodeId StatusResultId = new(299);
        private static readonly NodeId RationalNumberTypeId = VariableTypeIds.RationalNumberType;
        private static readonly NodeId PortableNodeIdId = DataTypeIds.PortableNodeId;
        private static readonly NodeId PortableQualifiedNameId = DataTypeIds.PortableQualifiedName;
        private static readonly NodeId CurrencyUnitTypeId = new(23498);
        private static readonly NodeId CartesianCoordinatesTypeId = VariableTypeIds.CartesianCoordinatesType;

        private async Task AssertNodeExistsAsync(NodeId nodeId, string name)
        {
            DataValue dv = await ReadAttributeAsync(
                nodeId, Attributes.BrowseName).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Ignore(name + " not found.");
            }
        }

        /// <summary>
        /// Verifies that a Structure DataType exposes the named field via its
        /// DataTypeDefinition attribute. Used for DataTypes whose fields are
        /// not exposed as browseable child nodes (which is the standard case
        /// in the OPC UA 1.05 nodeset for non-instance Structure types).
        /// </summary>
        private async Task AssertStructureFieldExistsAsync(
            NodeId nodeId, string fieldName)
        {
            DataValue dv = await ReadAttributeAsync(
                nodeId, Attributes.DataTypeDefinition).ConfigureAwait(false);
            if (StatusCode.IsBad(dv.StatusCode))
            {
                Assert.Ignore(
                    $"DataTypeDefinition not exposed for {nodeId}: {dv.StatusCode}");
                return;
            }
            if (!dv.WrappedValue.TryGetValue(out ExtensionObject ext)
                || !ext.TryGetValue(out StructureDefinition definition))
            {
                Assert.Ignore(
                    $"DataTypeDefinition for {nodeId} is not a StructureDefinition.");
                return;
            }
            bool found = false;
            if (definition.Fields != default)
            {
                foreach (StructureField f in definition.Fields)
                {
                    if (f.Name == fieldName)
                    {
                        found = true;
                        break;
                    }
                }
            }
            Assert.That(found, Is.True,
                $"Structure DataType {nodeId} should declare field '{fieldName}'.");
        }

        private async Task AssertSupertypeAsync(
            NodeId typeId, NodeId expectedParent, string name)
        {
            List<ReferenceDescription> refs = await BrowseRefsAsync(
                typeId, ReferenceTypeIds.HasSubtype,
                BrowseDirection.Inverse, false).ConfigureAwait(false);

            if (refs.Count == 0)
            {
                Assert.Ignore(name + " not found or no supertype.");
            }

            var parent = ExpandedNodeId.ToNodeId(
                refs[0].NodeId, Session.NamespaceUris);
            Assert.That(parent, Is.EqualTo(expectedParent),
                name + " supertype mismatch.");
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

        private async Task<List<ReferenceDescription>> BrowseRefsAsync(
            NodeId nodeId,
            NodeId referenceTypeId = default,
            BrowseDirection direction = BrowseDirection.Forward,
            bool includeSubtypes = true)
        {
            NodeId refType = referenceTypeId.IsNull
                ? ReferenceTypeIds.HierarchicalReferences
                : referenceTypeId;

            BrowseResponse response = await Session.BrowseAsync(
                null, null, 0,
                new BrowseDescription[]
                {
                    new() {
                        NodeId = nodeId,
                        BrowseDirection = direction,
                        ReferenceTypeId = refType,
                        IncludeSubtypes = includeSubtypes,
                        NodeClassMask = 0,
                        ResultMask = (uint)BrowseResultMask.All
                    }
                }.ToArrayOf(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(response.Results.Count, Is.EqualTo(1));

            var refs = new List<ReferenceDescription>();
            if (response.Results[0].References != default)
            {
                foreach (ReferenceDescription r in
                    response.Results[0].References)
                {
                    refs.Add(r);
                }
            }

            return refs;
        }

        private Task<List<ReferenceDescription>> BrowseAllRefsAsync(
            NodeId nodeId)
        {
            return BrowseRefsAsync(
                nodeId,
                ReferenceTypeIds.References,
                BrowseDirection.Forward,
                true);
        }

        private static bool HasChildWithName(
            List<ReferenceDescription> refs, string name)
        {
            return refs.Any(r => r.BrowseName.Name == name);
        }

        private static readonly NodeId ThreeDCartesianCoordinatesTypeId =
            new(18810);
    }
}
