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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Aas.Tests.Values
{
    /// <summary>
    /// Tests the clause 6.3.1 xsd type assignment. The properties asserted
    /// here are the ones losslessness rests on: every xsd type has a DataType,
    /// no DataType is shared, and the declared type is recoverable from the
    /// value node.
    /// </summary>
    [TestFixture]
    [Category("Aas")]
    public class AasXsdTypeMapTests
    {
        [Test]
        public void EveryDeclaredXsdTypeIsAssignedADataType()
        {
#if NET5_0_OR_GREATER
            AASDataTypeDefXsdDataType[] all = Enum.GetValues<AASDataTypeDefXsdDataType>();
#else
            var all = (AASDataTypeDefXsdDataType[])Enum
                .GetValues(typeof(AASDataTypeDefXsdDataType));
#endif

            Assert.Multiple(() =>
            {
                Assert.That(all, Has.Length.EqualTo(30));
                foreach (AASDataTypeDefXsdDataType valueType in all)
                {
                    Assert.That(
                        AasXsdTypeMap.TryGetDataTypeId(valueType, out _),
                        Is.True,
                        $"{valueType} has no assigned DataType.");
                }
            });
        }

        [Test]
        public void NoDataTypeIsAssignedToTwoXsdTypes()
        {
            // This is the property that lets a serializer read the declared
            // type back off the value node. Without it clause 6.4 could not
            // hold.
            List<ExpandedNodeId> assigned = AasXsdTypeMap.ValueTypes
                .Select(AasXsdTypeMap.ToDataTypeId)
                .ToList();

            Assert.That(assigned.Distinct().Count(), Is.EqualTo(assigned.Count));
        }

        [Test]
        public void EveryAssignmentRoundTripsThroughTheDataType()
        {
            Assert.Multiple(() =>
            {
                foreach (AASDataTypeDefXsdDataType valueType in AasXsdTypeMap.ValueTypes)
                {
                    ExpandedNodeId dataTypeId = AasXsdTypeMap.ToDataTypeId(valueType);

                    Assert.That(
                        AasXsdTypeMap.TryGetValueType(
                            dataTypeId, out AASDataTypeDefXsdDataType recovered),
                        Is.True);
                    Assert.That(recovered, Is.EqualTo(valueType));
                }
            });
        }

        [TestCase(AASDataTypeDefXsdDataType.Boolean, Opc.Ua.DataTypes.Boolean)]
        [TestCase(AASDataTypeDefXsdDataType.Byte, Opc.Ua.DataTypes.SByte)]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedByte, Opc.Ua.DataTypes.Byte)]
        [TestCase(AASDataTypeDefXsdDataType.Short, Opc.Ua.DataTypes.Int16)]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedShort, Opc.Ua.DataTypes.UInt16)]
        [TestCase(AASDataTypeDefXsdDataType.Int, Opc.Ua.DataTypes.Int32)]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedInt, Opc.Ua.DataTypes.UInt32)]
        [TestCase(AASDataTypeDefXsdDataType.Long, Opc.Ua.DataTypes.Int64)]
        [TestCase(AASDataTypeDefXsdDataType.UnsignedLong, Opc.Ua.DataTypes.UInt64)]
        [TestCase(AASDataTypeDefXsdDataType.Float, Opc.Ua.DataTypes.Float)]
        [TestCase(AASDataTypeDefXsdDataType.Double, Opc.Ua.DataTypes.Double)]
        [TestCase(AASDataTypeDefXsdDataType.Decimal, Opc.Ua.DataTypes.Decimal)]
        [TestCase(AASDataTypeDefXsdDataType.Integer, Opc.Ua.DataTypes.Integer)]
        [TestCase(AASDataTypeDefXsdDataType.NonNegativeInteger, Opc.Ua.DataTypes.UInteger)]
        [TestCase(AASDataTypeDefXsdDataType.String, Opc.Ua.DataTypes.String)]
        [TestCase(AASDataTypeDefXsdDataType.DateTime, Opc.Ua.DataTypes.DateTime)]
        [TestCase(AASDataTypeDefXsdDataType.Base64Binary, Opc.Ua.DataTypes.ByteString)]
        public void ABuiltInDataTypeIsUsedWhereItDenotesTheXsdTypeOnItsOwn(
            AASDataTypeDefXsdDataType valueType,
            uint expected)
        {
            ExpandedNodeId dataTypeId = AasXsdTypeMap.ToDataTypeId(valueType);

            Assert.Multiple(() =>
            {
                Assert.That(dataTypeId.NamespaceUri, Is.EqualTo(Opc.Ua.Namespaces.OpcUa));
                Assert.That(IdentifierOf(dataTypeId), Is.EqualTo(expected));
            });
        }

        [Test]
        public void DurationTakesDurationStringRatherThanDuration()
        {
            // Duration (i=290) is a Double count of milliseconds, and
            // xs:duration has year and month components that are not a fixed
            // number of them: P1M is not thirty days.
            ExpandedNodeId dataTypeId =
                AasXsdTypeMap.ToDataTypeId(AASDataTypeDefXsdDataType.Duration);

            Assert.Multiple(() =>
            {
                Assert.That(IdentifierOf(dataTypeId), Is.EqualTo(Opc.Ua.DataTypes.DurationString));
                Assert.That(IdentifierOf(dataTypeId), Is.Not.EqualTo(Opc.Ua.DataTypes.Duration));
            });
        }

        [Test]
        public void DateAndTimeTakeStringSubtypesRatherThanDateTime()
        {
            // A DateTime is an instant; a date is a day and a time is a
            // time-of-day. Assigning either DateTime would require inventing
            // the missing component.
            Assert.Multiple(() =>
            {
                Assert.That(
                    IdentifierOf(AasXsdTypeMap.ToDataTypeId(AASDataTypeDefXsdDataType.Date)),
                    Is.EqualTo(Opc.Ua.DataTypes.DateString));
                Assert.That(
                    IdentifierOf(AasXsdTypeMap.ToDataTypeId(AASDataTypeDefXsdDataType.Time)),
                    Is.EqualTo(Opc.Ua.DataTypes.TimeString));
            });
        }

        [TestCase(AASDataTypeDefXsdDataType.AnyUri, Opc.Ua.Aas.V3.DataTypes.AASAnyUri)]
        [TestCase(AASDataTypeDefXsdDataType.HexBinary, Opc.Ua.Aas.V3.DataTypes.AASHexBinary)]
        [TestCase(AASDataTypeDefXsdDataType.NonPositiveInteger, Opc.Ua.Aas.V3.DataTypes.AASNonPositiveInteger)]
        [TestCase(AASDataTypeDefXsdDataType.NegativeInteger, Opc.Ua.Aas.V3.DataTypes.AASNegativeInteger)]
        [TestCase(AASDataTypeDefXsdDataType.PositiveInteger, Opc.Ua.Aas.V3.DataTypes.AASPositiveInteger)]
        [TestCase(AASDataTypeDefXsdDataType.GYear, Opc.Ua.Aas.V3.DataTypes.AASGYear)]
        [TestCase(AASDataTypeDefXsdDataType.GYearMonth, Opc.Ua.Aas.V3.DataTypes.AASGYearMonth)]
        [TestCase(AASDataTypeDefXsdDataType.GMonth, Opc.Ua.Aas.V3.DataTypes.AASGMonth)]
        [TestCase(AASDataTypeDefXsdDataType.GMonthDay, Opc.Ua.Aas.V3.DataTypes.AASGMonthDay)]
        [TestCase(AASDataTypeDefXsdDataType.GDay, Opc.Ua.Aas.V3.DataTypes.AASGDay)]
        public void ASubtypeIsDefinedWhereTwoXsdTypesWouldShareOneBuiltIn(
            AASDataTypeDefXsdDataType valueType,
            uint expected)
        {
            ExpandedNodeId dataTypeId = AasXsdTypeMap.ToDataTypeId(valueType);

            Assert.Multiple(() =>
            {
                Assert.That(dataTypeId.NamespaceUri, Is.EqualTo(Opc.Ua.Aas.V3.Namespaces.AasV3));
                Assert.That(IdentifierOf(dataTypeId), Is.EqualTo(expected));
                Assert.That(AasXsdTypeMap.IsAasDefinedSubtype(dataTypeId), Is.True);
            });
        }

        [Test]
        public void ExactlyTenSubtypesAreDefinedByThisNamespace()
        {
            int defined = AasXsdTypeMap.ValueTypes
                .Select(AasXsdTypeMap.ToDataTypeId)
                .Count(AasXsdTypeMap.IsAasDefinedSubtype);

            Assert.That(defined, Is.EqualTo(10));
        }

        [Test]
        public void ABuiltInDataTypeIsNotReportedAsAnAasSubtype()
        {
            Assert.That(
                AasXsdTypeMap.IsAasDefinedSubtype(
                    AasXsdTypeMap.ToDataTypeId(AASDataTypeDefXsdDataType.String)),
                Is.False);
        }

        [Test]
        public void TheDeclaredTypeIsRecoveredFromASessionLocalNodeId()
        {
            var namespaceUris = new NamespaceTable();
            ushort index = namespaceUris.GetIndexOrAppend(Opc.Ua.Aas.V3.Namespaces.AasV3);
            var local = new NodeId(Opc.Ua.Aas.V3.DataTypes.AASHexBinary, index);

            Assert.That(
                AasXsdTypeMap.TryGetValueType(
                    local, namespaceUris, out AASDataTypeDefXsdDataType valueType),
                Is.True);
            Assert.That(valueType, Is.EqualTo(AASDataTypeDefXsdDataType.HexBinary));
        }

        [Test]
        public void ANullNodeIdCarriesNoDeclaredType()
        {
            Assert.That(
                AasXsdTypeMap.TryGetValueType(NodeId.Null, new NamespaceTable(), out _),
                Is.False);
        }

        [Test]
        public void ANullExpandedNodeIdCarriesNoDeclaredType()
        {
            Assert.That(AasXsdTypeMap.TryGetValueType(ExpandedNodeId.Null, out _), Is.False);
        }

        [Test]
        public void AnUnassignedDataTypeCarriesNoDeclaredType()
        {
            var foreign = new ExpandedNodeId(
                Opc.Ua.DataTypes.LocalizedText, 0, Opc.Ua.Namespaces.OpcUa, 0);

            Assert.That(AasXsdTypeMap.TryGetValueType(foreign, out _), Is.False);
        }

        [Test]
        public void AnUndefinedXsdValueIsRejectedRatherThanDroppedSilently()
        {
            // Clause 6.3.3: the enumerations are closed, and a value outside
            // them cannot round-trip.
            const AASDataTypeDefXsdDataType undefined = (AASDataTypeDefXsdDataType)999;

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => AasXsdTypeMap.ToDataTypeId(undefined),
                    Throws.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(AasXsdTypeMap.TryGetDataTypeId(undefined, out _), Is.False);
            });
        }

        [Test]
        public void TryGetValueTypeRejectsANullNamespaceTable()
        {
            Assert.That(
                () => AasXsdTypeMap.TryGetValueType(NodeId.Null, null!, out _),
                Throws.ArgumentNullException);
        }

        private static uint IdentifierOf(ExpandedNodeId dataTypeId)
        {
            Assert.That(dataTypeId.TryGetValue(out uint identifier), Is.True);
            return identifier;
        }
    }
}
