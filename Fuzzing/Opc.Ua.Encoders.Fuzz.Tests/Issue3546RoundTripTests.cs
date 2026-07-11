/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.IO;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Regression coverage for https://github.com/OPCFoundation/UA-.NETStandard/issues/3546.
    /// <para>
    /// The pipeline-only failure reported in #3546 is: a serialized buffer that round-trips
    /// byte-for-byte through encode -> decode -> encode still decodes into two
    /// <see cref="DataTypeNode"/> / <see cref="VariableNode"/> instances that compare unequal
    /// via <see cref="Utils.IsEqual(object?, object?)"/>. The CI libfuzz pipeline feeds a zip
    /// of corpus inputs sequentially, so any state that drifts between consecutive decodes can
    /// surface this; the local NUnit runner did not catch it because no seed in
    /// <c>Testcases.Binary</c> populated the implicated fields.
    /// </para>
    /// <para>
    /// These tests construct populated <see cref="DataTypeNode"/> and <see cref="VariableNode"/>
    /// instances and drive them through <see cref="FuzzableCode.FuzzBinaryEncoderIndempotentCore"/>
    /// (the exact method whose 3rd-gen IsEqual gate trips in the pipeline) to attempt
    /// reproduction without depending on the libfuzz corpus.
    /// </para>
    /// </summary>
    [TestFixture]
    [Category("Fuzzing")]
    [Category("Issue3546")]
    public class Issue3546RoundTripTests
    {
        private static readonly uint[] s_arrayDimensions = [1u, 2u];

        [SetUp]
        public void Setup()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            FuzzableCode.MessageContext = ServiceMessageContext.Create(telemetry);
        }

        [Test]
        public void DataTypeNodeWithReferencesRoundTripsCleanly()
        {
            DataTypeNode original = BuildPopulatedDataTypeNode();
            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            // FuzzBinaryEncoderIndempotentCore performs: decode -> encode -> decode -> IsEqual.
            // If the pipeline-only failure is hit here, this throws InvalidOperationException
            // with message "Idempotent 3rd gen decoding failed. Type=DataTypeNode."
            Assert.That(
                () => FuzzableCode.FuzzBinaryEncoderIndempotentCore(serialized, original),
                Throws.Nothing,
                "DataTypeNode populated with non-empty References, RolePermissions, " +
                "and a non-empty DataTypeDefinition ExtensionObject body should round-trip " +
                "cleanly through the fuzz idempotent target.");
        }

        [Test]
        public void DataTypeNodeWithEmptyDataTypeDefinitionRoundTripsCleanly()
        {
            DataTypeNode original = BuildPopulatedDataTypeNode();
            original.DataTypeDefinition = ExtensionObject.Null;

            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            Assert.That(
                () => FuzzableCode.FuzzBinaryEncoderIndempotentCore(serialized, original),
                Throws.Nothing,
                "DataTypeNode with a null DataTypeDefinition must round-trip cleanly.");
        }

        [Test]
        public void VariableNodeWithReferencesRoundTripsCleanly()
        {
            VariableNode original = BuildPopulatedVariableNode();
            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            Assert.That(
                () => FuzzableCode.FuzzBinaryEncoderIndempotentCore(serialized, original),
                Throws.Nothing,
                "VariableNode populated with non-empty References, RolePermissions, " +
                "and a populated Value Variant should round-trip cleanly through the " +
                "fuzz idempotent target.");
        }

        [Test]
        public void VariableNodeWithEmptyArrayDimensionsRoundTripsCleanly()
        {
            VariableNode original = BuildPopulatedVariableNode();
            original.ArrayDimensions = [];

            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            Assert.That(
                () => FuzzableCode.FuzzBinaryEncoderIndempotentCore(serialized, original),
                Throws.Nothing,
                "VariableNode with explicitly empty ArrayDimensions must round-trip cleanly.");
        }

        [Test]
        public void VariableNodeWithNullArrayDimensionsRoundTripsCleanly()
        {
            VariableNode original = BuildPopulatedVariableNode();
            original.ArrayDimensions = ArrayOf<uint>.Null;

            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            Assert.That(
                () => FuzzableCode.FuzzBinaryEncoderIndempotentCore(serialized, original),
                Throws.Nothing,
                "VariableNode with null ArrayDimensions must round-trip cleanly. " +
                "If the binary decoder canonicalizes 'absent' to either Empty or Null, " +
                "both rounds of decode must agree on the shape.");
        }

        [Test]
        public void VariableNodeValueExtensionObjectBodyRoundTripsCleanly()
        {
            VariableNode original = BuildPopulatedVariableNode();

            // ExtensionObject body inside a Variant exercises the ExtensionObject.Equals path
            // that depends on Encoding-match. If the EncodeableFactory cannot resolve the
            // TypeId, one decode produces a ByteString body while another (after the factory
            // is populated) produces an IEncodeable body, making the two decoded objects
            // compare unequal even though their bytes match.
            var argument = new Argument
            {
                Name = "Sample",
                DataType = DataTypeIds.UInt32,
                ValueRank = ValueRanks.Scalar,
                Description = new LocalizedText("en", "fuzz #3546 sample")
            };
            original.Value = Variant.From(new ExtensionObject(argument));

            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            Assert.That(
                () => FuzzableCode.FuzzBinaryEncoderIndempotentCore(serialized, original),
                Throws.Nothing,
                "VariableNode with a Variant carrying an ExtensionObject body must round-trip " +
                "cleanly. Failure here points at ExtensionObject.Equals dropping into the " +
                "non-matching-Encoding fall-through (ByteString vs IEncodeable bodies).");
        }

        [Test]
        public void DecodeDecodeIsEqualForBinaryEncodedDataTypeNode()
        {
            // Direct two-decode IsEqual test bypassing the three-round-trip orchestration:
            // confirms the pure decode-decode-IsEqual contract for DataTypeNode.
            DataTypeNode original = BuildPopulatedDataTypeNode();
            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            IEncodeable a = DecodeBinary(serialized);
            IEncodeable b = DecodeBinary(serialized);

            Assert.That(
                Utils.IsEqual(a, b),
                Is.True,
                "Decoding the same binary buffer twice must produce IsEqual instances. " +
                "Field-level mismatches identify the broken IsEqual override.");
        }

        [Test]
        public void DecodeDecodeIsEqualForBinaryEncodedVariableNode()
        {
            VariableNode original = BuildPopulatedVariableNode();
            byte[] serialized = BinaryEncoder.EncodeMessage(original, FuzzableCode.MessageContext);

            IEncodeable a = DecodeBinary(serialized);
            IEncodeable b = DecodeBinary(serialized);

            Assert.That(
                Utils.IsEqual(a, b),
                Is.True,
                "Decoding the same binary buffer twice must produce IsEqual instances. " +
                "Field-level mismatches identify the broken IsEqual override.");
        }

        private static IEncodeable DecodeBinary(byte[] serialized)
        {
            using var memory = new MemoryStream(serialized);
            using var decoder = new BinaryDecoder(memory, FuzzableCode.MessageContext);
            return decoder.DecodeMessage<IEncodeable>();
        }

        private static DataTypeNode BuildPopulatedDataTypeNode()
        {
            return new DataTypeNode
            {
                NodeId = new NodeId(1234, 2),
                NodeClass = NodeClass.DataType,
                BrowseName = new QualifiedName("FuzzSample", 2),
                DisplayName = new LocalizedText("en", "Fuzz Sample"),
                Description = new LocalizedText("en", "Issue 3546 reproducer"),
                WriteMask = 0,
                UserWriteMask = 0,
                AccessRestrictions = 0,
                IsAbstract = false,
                DataTypeDefinition = new ExtensionObject(
                    new StructureDefinition
                    {
                        DefaultEncodingId = new NodeId(5678, 2),
                        BaseDataType = DataTypeIds.Structure,
                        StructureType = StructureType.Structure,
                        Fields =
                        [
                            new StructureField
                            {
                                Name = "Field0",
                                DataType = DataTypeIds.UInt32,
                                ValueRank = ValueRanks.Scalar,
                                IsOptional = false
                            },
                            new StructureField
                            {
                                Name = "Field1",
                                DataType = DataTypeIds.String,
                                ValueRank = ValueRanks.Scalar,
                                IsOptional = false
                            }
                        ]
                    }),
                References =
                [
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasSubtype,
                        IsInverse = true,
                        TargetId = (ExpandedNodeId)DataTypeIds.Structure
                    },
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                        IsInverse = false,
                        TargetId = new ExpandedNodeId(new NodeId(5678, 2))
                    }
                ],
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)PermissionType.Read
                    }
                ],
                UserRolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_AuthenticatedUser,
                        Permissions = (uint)PermissionType.Read
                    }
                ]
            };
        }

        private static VariableNode BuildPopulatedVariableNode()
        {
            return new VariableNode
            {
                NodeId = new NodeId(4321, 2),
                NodeClass = NodeClass.Variable,
                BrowseName = new QualifiedName("FuzzVariable", 2),
                DisplayName = new LocalizedText("en", "Fuzz Variable"),
                Description = new LocalizedText("en", "Issue 3546 variable reproducer"),
                WriteMask = 0,
                UserWriteMask = 0,
                AccessRestrictions = 0,
                Value = Variant.From("hello"),
                DataType = DataTypeIds.String,
                ValueRank = ValueRanks.Scalar,
                ArrayDimensions = new ArrayOf<uint>(s_arrayDimensions),
                AccessLevel = AccessLevels.CurrentRead | AccessLevels.CurrentWrite,
                UserAccessLevel = AccessLevels.CurrentRead,
                MinimumSamplingInterval = 0,
                Historizing = false,
                AccessLevelEx = 0,
                References =
                [
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition,
                        IsInverse = false,
                        TargetId = (ExpandedNodeId)VariableTypeIds.BaseDataVariableType
                    },
                    new ReferenceNode
                    {
                        ReferenceTypeId = ReferenceTypeIds.Organizes,
                        IsInverse = true,
                        TargetId = (ExpandedNodeId)ObjectIds.ObjectsFolder
                    }
                ],
                RolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)PermissionType.Read
                    }
                ],
                UserRolePermissions =
                [
                    new RolePermissionType
                    {
                        RoleId = ObjectIds.WellKnownRole_Anonymous,
                        Permissions = (uint)PermissionType.Read
                    }
                ]
            };
        }
    }
}
