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

using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Tests.Providers
{
    /// <summary>
    /// Verifies that the loss-aware conversions round-trip every standard Job
    /// Control V1/V2 job-order and job-response field, including work masters and
    /// the personnel, equipment, physical-asset and material requirement and
    /// actual collections, within a version and across versions.
    /// </summary>
    [TestFixture]
    public class Isa95JobControlConversionsTests
    {
        private static V2.ISA95ParameterDataType V2Param(string id, string value, string unit)
        {
            return new V2.ISA95ParameterDataType
            {
                ID = id,
                Value = new Variant(value),
                Description = new[] { new LocalizedText("d-" + id) }.ToArrayOf(),
                EngineeringUnits = new EUInformation { DisplayName = new LocalizedText(unit), UnitId = 42 },
                Subparameters = new[]
                {
                    new V2.ISA95ParameterDataType { ID = id + ".sub", Value = new Variant(7) }
                }.ToArrayOf()
            };
        }

        private static V2.ISA95PropertyDataType V2Prop(string id, string value)
        {
            return new V2.ISA95PropertyDataType
            {
                ID = id,
                Value = new Variant(value),
                Description = new[] { new LocalizedText("pd-" + id) }.ToArrayOf(),
                EngineeringUnits = new EUInformation { DisplayName = new LocalizedText("u-" + id) }
            };
        }

        private static V2.ISA95JobOrderDataType FullV2Order(string id)
        {
            return new V2.ISA95JobOrderDataType
            {
                JobOrderID = id,
                Description = new[] { new LocalizedText("en", "order"), new LocalizedText("de", "auftrag") }.ToArrayOf(),
                Priority = 7,
                StartTime = DateTimeUtc.From(new System.DateTime(2026, 1, 2, 3, 4, 5, System.DateTimeKind.Utc)),
                EndTime = DateTimeUtc.From(new System.DateTime(2026, 1, 3, 3, 4, 5, System.DateTimeKind.Utc)),
                JobOrderParameters = new[] { V2Param("p1", "v1", "kg") }.ToArrayOf(),
                WorkMasterID = new[]
                {
                    new V2.ISA95WorkMasterDataType
                    {
                        ID = "wm1",
                        Description = new LocalizedText("work master"),
                        Parameters = new[] { V2Param("wmp", "wv", "m") }.ToArrayOf()
                    }
                }.ToArrayOf(),
                PersonnelRequirements = new[]
                {
                    new V2.ISA95PersonnelDataType
                    {
                        ID = "per1",
                        Description = new[] { new LocalizedText("welder") }.ToArrayOf(),
                        PersonnelUse = "assembly",
                        Quantity = "2",
                        EngineeringUnits = new EUInformation { DisplayName = new LocalizedText("count") },
                        Properties = new[] { V2Prop("perProp", "pv") }.ToArrayOf()
                    }
                }.ToArrayOf(),
                EquipmentRequirements = new[]
                {
                    new V2.ISA95EquipmentDataType
                    {
                        ID = "eq1",
                        Description = new[] { new LocalizedText("robot") }.ToArrayOf(),
                        EquipmentUse = "weld",
                        Quantity = "1",
                        EngineeringUnits = new EUInformation { DisplayName = new LocalizedText("ea") },
                        Properties = new[] { V2Prop("eqProp", "ev") }.ToArrayOf()
                    }
                }.ToArrayOf(),
                PhysicalAssetRequirements = new[]
                {
                    new V2.ISA95PhysicalAssetDataType
                    {
                        ID = "pa1",
                        Description = new[] { new LocalizedText("fixture") }.ToArrayOf(),
                        PhysicalAssetUse = "hold",
                        Quantity = "3",
                        EngineeringUnits = new EUInformation { DisplayName = new LocalizedText("pa") },
                        Properties = new[] { V2Prop("paProp", "av") }.ToArrayOf()
                    }
                }.ToArrayOf(),
                MaterialRequirements = new[]
                {
                    new V2.ISA95MaterialDataType
                    {
                        MaterialClassID = "mc",
                        MaterialDefinitionID = "md",
                        MaterialLotID = "ml",
                        MaterialSublotID = "ms",
                        Description = new[] { new LocalizedText("steel") }.ToArrayOf(),
                        MaterialUse = "consume",
                        Quantity = "10",
                        EngineeringUnits = new EUInformation { DisplayName = new LocalizedText("kg") },
                        Properties = new[] { V2Prop("matProp", "mv") }.ToArrayOf()
                    }
                }.ToArrayOf()
            };
        }

        [Test]
        public void V2JobOrderRoundTripsEveryFieldWithinVersion()
        {
            V2.ISA95JobOrderDataType original = FullV2Order("job-v2");

            Isa95JobOrder neutral = Isa95JobControlConversions.FromV2Order(original);
            V2.ISA95JobOrderDataType result = Isa95JobControlConversions.ToV2Order(neutral);

            Assert.That(result.JobOrderID, Is.EqualTo("job-v2"));
            Assert.That(result.Description.Count, Is.EqualTo(2));
            Assert.That(result.Description[1].Text, Is.EqualTo("auftrag"));
            Assert.That(result.Priority, Is.EqualTo((short)7));
            Assert.That(result.StartTime, Is.EqualTo(original.StartTime));
            Assert.That(result.EndTime, Is.EqualTo(original.EndTime));

            Assert.That(result.JobOrderParameters.Count, Is.EqualTo(1));
            Assert.That(result.JobOrderParameters[0].ID, Is.EqualTo("p1"));
            Assert.That(result.JobOrderParameters[0].EngineeringUnits.DisplayName.Text, Is.EqualTo("kg"));
            Assert.That(result.JobOrderParameters[0].EngineeringUnits.UnitId, Is.EqualTo(42));
            Assert.That(result.JobOrderParameters[0].Subparameters.Count, Is.EqualTo(1));
            Assert.That(result.JobOrderParameters[0].Subparameters[0].ID, Is.EqualTo("p1.sub"));

            Assert.That(result.WorkMasterID.Count, Is.EqualTo(1));
            Assert.That(result.WorkMasterID[0].ID, Is.EqualTo("wm1"));
            Assert.That(result.WorkMasterID[0].Description.Text, Is.EqualTo("work master"));
            Assert.That(result.WorkMasterID[0].Parameters[0].ID, Is.EqualTo("wmp"));

            Assert.That(result.PersonnelRequirements[0].PersonnelUse, Is.EqualTo("assembly"));
            Assert.That(result.PersonnelRequirements[0].Quantity, Is.EqualTo("2"));
            Assert.That(result.PersonnelRequirements[0].Properties[0].ID, Is.EqualTo("perProp"));
            Assert.That(result.EquipmentRequirements[0].EquipmentUse, Is.EqualTo("weld"));
            Assert.That(result.PhysicalAssetRequirements[0].PhysicalAssetUse, Is.EqualTo("hold"));

            Assert.That(result.MaterialRequirements[0].MaterialClassID, Is.EqualTo("mc"));
            Assert.That(result.MaterialRequirements[0].MaterialDefinitionID, Is.EqualTo("md"));
            Assert.That(result.MaterialRequirements[0].MaterialLotID, Is.EqualTo("ml"));
            Assert.That(result.MaterialRequirements[0].MaterialSublotID, Is.EqualTo("ms"));
            Assert.That(result.MaterialRequirements[0].MaterialUse, Is.EqualTo("consume"));
            Assert.That(result.MaterialRequirements[0].Quantity, Is.EqualTo("10"));
            Assert.That(result.MaterialRequirements[0].EngineeringUnits.DisplayName.Text, Is.EqualTo("kg"));
        }

        [Test]
        public void V2JobOrderMaterializedWithEncodingMaskForPopulatedFields()
        {
            Isa95JobOrder neutral = Isa95JobControlConversions.FromV2Order(FullV2Order("job-mask"));
            V2.ISA95JobOrderDataType result = Isa95JobControlConversions.ToV2Order(neutral);

            uint mask = result.EncodingMask;
            Assert.That(mask & (uint)V2.ISA95JobOrderDataTypeFields.Description, Is.Not.Zero);
            Assert.That(mask & (uint)V2.ISA95JobOrderDataTypeFields.WorkMasterID, Is.Not.Zero);
            Assert.That(mask & (uint)V2.ISA95JobOrderDataTypeFields.Priority, Is.Not.Zero);
            Assert.That(mask & (uint)V2.ISA95JobOrderDataTypeFields.JobOrderParameters, Is.Not.Zero);
            Assert.That(mask & (uint)V2.ISA95JobOrderDataTypeFields.PersonnelRequirements, Is.Not.Zero);
            Assert.That(mask & (uint)V2.ISA95JobOrderDataTypeFields.MaterialRequirements, Is.Not.Zero);
        }

        [Test]
        public void V1JobOrderRoundTripsEveryFieldWithinVersion()
        {
            var original = new V1.ISA95JobOrderDataType
            {
                ID = "job-v1",
                Description = "the order",
                Priority = 5,
                StartTime = DateTimeUtc.From(new System.DateTime(2026, 5, 6, 7, 8, 9, System.DateTimeKind.Utc)),
                EndTime = DateTimeUtc.From(new System.DateTime(2026, 5, 7, 7, 8, 9, System.DateTimeKind.Utc)),
                JobOrderParameters = new[]
                {
                    new V1.ISA95ParameterDataType
                    {
                        ID = "p1",
                        Value = new Variant("v"),
                        Description = "pd",
                        UoM = "kg",
                        Subparameters = new[]
                        {
                            new V1.ISA95ParameterDataType { ID = "p1.sub", Value = new Variant(3) }
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                WorkMasterID = new[]
                {
                    new V1.ISA95WorkMasterDataType { ID = "wm1", Description = "wm", Parameters = [] }
                }.ToArrayOf(),
                PersonnelRequirements = new[]
                {
                    new V1.ISA95PersonnelDataType
                    {
                        ID = "per1",
                        Description = "welder",
                        PersonnelUse = "assembly",
                        Quantity = "2",
                        UoM = "count",
                        Properties = new[]
                        {
                            new V1.ISA95PropertyDataType { ID = "pp", Value = new Variant("x"), UoM = "u" }
                        }.ToArrayOf()
                    }
                }.ToArrayOf(),
                EquipmentRequirements = new[]
                {
                    new V1.ISA95EquipmentDataType { ID = "eq1", EquipmentUse = "weld", Quantity = "1", UoM = "ea" }
                }.ToArrayOf(),
                PhysicalAssetRequirements = new[]
                {
                    new V1.ISA95PhysicalAssetDataType { ID = "pa1", PhysicalAssetUse = "hold", Quantity = "3" }
                }.ToArrayOf(),
                MaterialRequirements = new[]
                {
                    new V1.ISA95MaterialDataType
                    {
                        MaterialClassID = "mc",
                        MaterialLotID = "ml",
                        MaterialUse = "consume",
                        Quantity = "10",
                        UoM = "kg"
                    }
                }.ToArrayOf()
            };

            Isa95JobOrder neutral = Isa95JobControlConversions.FromV1Order(original);
            V1.ISA95JobOrderDataType result = Isa95JobControlConversions.ToV1Order(neutral);

            Assert.That(result.ID, Is.EqualTo("job-v1"));
            Assert.That(result.Description, Is.EqualTo("the order"));
            Assert.That(result.Priority, Is.EqualTo((short)5));
            Assert.That(result.StartTime, Is.EqualTo(original.StartTime));
            Assert.That(result.JobOrderParameters[0].UoM, Is.EqualTo("kg"));
            Assert.That(result.JobOrderParameters[0].Subparameters[0].ID, Is.EqualTo("p1.sub"));
            Assert.That(result.WorkMasterID[0].ID, Is.EqualTo("wm1"));
            Assert.That(result.WorkMasterID[0].Description, Is.EqualTo("wm"));
            Assert.That(result.PersonnelRequirements[0].PersonnelUse, Is.EqualTo("assembly"));
            Assert.That(result.PersonnelRequirements[0].UoM, Is.EqualTo("count"));
            Assert.That(result.PersonnelRequirements[0].Properties[0].ID, Is.EqualTo("pp"));
            Assert.That(result.EquipmentRequirements[0].UoM, Is.EqualTo("ea"));
            Assert.That(result.PhysicalAssetRequirements[0].PhysicalAssetUse, Is.EqualTo("hold"));
            Assert.That(result.MaterialRequirements[0].MaterialClassID, Is.EqualTo("mc"));
            Assert.That(result.MaterialRequirements[0].MaterialLotID, Is.EqualTo("ml"));
            Assert.That(result.MaterialRequirements[0].UoM, Is.EqualTo("kg"));
        }

        [Test]
        public void V2JobResponseRoundTripsActualsWithinVersion()
        {
            var original = new V2.ISA95JobResponseDataType
            {
                JobResponseID = "r1",
                JobOrderID = "job1",
                Description = new LocalizedText("response"),
                JobState = Isa95V2StateMachine.ToStateArray(Isa95JobCanonicalState.Completed),
                JobResponseData = new[] { V2Param("rp", "rv", "s") }.ToArrayOf(),
                PersonnelActuals = new[]
                {
                    new V2.ISA95PersonnelDataType { ID = "pa", PersonnelUse = "did", Quantity = "1" }
                }.ToArrayOf(),
                EquipmentActuals = new[]
                {
                    new V2.ISA95EquipmentDataType { ID = "ea", EquipmentUse = "used" }
                }.ToArrayOf(),
                PhysicalAssetActuals = new[]
                {
                    new V2.ISA95PhysicalAssetDataType { ID = "paa", PhysicalAssetUse = "held" }
                }.ToArrayOf(),
                MaterialActuals = new[]
                {
                    new V2.ISA95MaterialDataType { MaterialLotID = "lot", Quantity = "9" }
                }.ToArrayOf()
            };

            Isa95JobResponse neutral = Isa95JobControlConversions.FromV2Response(original);
            V2.ISA95JobResponseDataType result = Isa95JobControlConversions.ToV2Response(neutral);

            Assert.That(result.JobResponseID, Is.EqualTo("r1"));
            Assert.That(result.JobOrderID, Is.EqualTo("job1"));
            Assert.That(result.Description.Text, Is.EqualTo("response"));
            Assert.That(Isa95V2StateMachine.FromStateArray(result.JobState),
                Is.EqualTo(Isa95JobCanonicalState.Completed));
            Assert.That(result.JobResponseData[0].ID, Is.EqualTo("rp"));
            Assert.That(result.PersonnelActuals[0].PersonnelUse, Is.EqualTo("did"));
            Assert.That(result.EquipmentActuals[0].ID, Is.EqualTo("ea"));
            Assert.That(result.PhysicalAssetActuals[0].ID, Is.EqualTo("paa"));
            Assert.That(result.MaterialActuals[0].MaterialLotID, Is.EqualTo("lot"));
            Assert.That(result.MaterialActuals[0].Quantity, Is.EqualTo("9"));
        }

        [Test]
        public void V1ResponseLoadedAndErrorStatesArePreserved()
        {
            foreach (V1.ISA95JobOrderStateEnum state in new[]
            {
                V1.ISA95JobOrderStateEnum.Loaded,
                V1.ISA95JobOrderStateEnum.Error
            })
            {
                var original = new V1.ISA95JobResponseDataType
                {
                    ID = "r",
                    JobOrderID = "job1",
                    JobState = state
                };

                Isa95JobResponse neutral = Isa95JobControlConversions.FromV1Response(original);
                V1.ISA95JobResponseDataType result = Isa95JobControlConversions.ToV1Response(neutral);

                Assert.That(result.JobState, Is.EqualTo(state));
            }
        }

        [Test]
        public void V1RequirementsAreVisibleAcrossToV2()
        {
            var v1 = new V1.ISA95JobOrderDataType
            {
                ID = "cross",
                Description = "d",
                JobOrderParameters = new[]
                {
                    new V1.ISA95ParameterDataType { ID = "p", Value = new Variant("v"), UoM = "kg" }
                }.ToArrayOf(),
                PersonnelRequirements = new[]
                {
                    new V1.ISA95PersonnelDataType { ID = "per", PersonnelUse = "use", UoM = "count" }
                }.ToArrayOf()
            };

            Isa95JobOrder neutral = Isa95JobControlConversions.FromV1Order(v1);
            V2.ISA95JobOrderDataType v2 = Isa95JobControlConversions.ToV2Order(neutral);

            Assert.That(v2.JobOrderID, Is.EqualTo("cross"));
            Assert.That(v2.Description[0].Text, Is.EqualTo("d"));
            Assert.That(v2.JobOrderParameters[0].EngineeringUnits.DisplayName.Text, Is.EqualTo("kg"));
            Assert.That(v2.PersonnelRequirements[0].PersonnelUse, Is.EqualTo("use"));
            Assert.That(v2.PersonnelRequirements[0].EngineeringUnits.DisplayName.Text, Is.EqualTo("count"));
        }

        [Test]
        public void V2RequirementsAreVisibleAcrossToV1()
        {
            Isa95JobOrder neutral = Isa95JobControlConversions.FromV2Order(FullV2Order("cross2"));
            V1.ISA95JobOrderDataType v1 = Isa95JobControlConversions.ToV1Order(neutral);

            Assert.That(v1.ID, Is.EqualTo("cross2"));
            Assert.That(v1.Description, Is.EqualTo("order"));
            Assert.That(v1.JobOrderParameters[0].UoM, Is.EqualTo("kg"));
            Assert.That(v1.PersonnelRequirements[0].PersonnelUse, Is.EqualTo("assembly"));
            Assert.That(v1.PersonnelRequirements[0].UoM, Is.EqualTo("count"));
            Assert.That(v1.MaterialRequirements[0].MaterialClassID, Is.EqualTo("mc"));
        }
    }
}
