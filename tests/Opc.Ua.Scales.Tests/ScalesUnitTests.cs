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

using System;
using NUnit.Framework;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// The weighing arithmetic of OPC 40200 §9.3 and the zero and tare
    /// methods of §7.4.4 - §7.4.8, without an address space.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    public sealed class ScaleWeighingEngineTests
    {
        private static ScaleWeighingEngine TwoRangeScale()
        {
            // A dual-range scale: 0..3 kg with d = 1 g, 0..6 kg with d = 2 g.
            return new ScaleWeighingEngine(
                [
                    new WeighingRangeDefinition(0, 6, 0.002, 0.002),
                    new WeighingRangeDefinition(0, 3, 0.001, 0.001)
                ],
                zeroSettingRange: 0.04);
        }

        [Test]
        public void ConstructorRejectsAScaleWithoutRange()
        {
            Assert.That(() => new ScaleWeighingEngine([], 0.04), Throws.ArgumentException);
        }

        [Test]
        public void RangesAreOrderedByCapacity()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            Assert.That(engine.Ranges[0].High, Is.EqualTo(3));
            Assert.That(engine.Capacity, Is.EqualTo(6));
        }

        [Test]
        public void WeightIsRoundedToTheIntervalOfItsRange()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            engine.RawLoad = 1.23456;
            WeighingResult low = engine.Evaluate();
            Assert.That(low.Gross, Is.EqualTo(1.235).Within(1e-9));
            Assert.That(low.RangeIndex, Is.Zero);
            Assert.That(low.HighResolutionGross, Is.EqualTo(1.23456).Within(1e-12));

            engine.RawLoad = 4.3333;
            WeighingResult high = engine.Evaluate();
            Assert.That(high.Gross, Is.EqualTo(4.334).Within(1e-9));
            Assert.That(high.RangeIndex, Is.EqualTo(1));
        }

        [Test]
        public void LegalForTradeRoundsToTheVerificationInterval()
        {
            var engine = new ScaleWeighingEngine([new WeighingRangeDefinition(0, 30, 0.001, 0.01)], 0.04)
            {
                LegalForTrade = true,
                RawLoad = 1.234
            };
            Assert.That(engine.Evaluate().Gross, Is.EqualTo(1.23).Within(1e-9));
            engine.LegalForTrade = false;
            Assert.That(engine.Evaluate().Gross, Is.EqualTo(1.234).Within(1e-9));
        }

        [Test]
        public void OverloadAndUnderloadFollowCapacityAndZeroBand()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            engine.RawLoad = 6.5;
            Assert.That(engine.Evaluate().Overload, Is.True);
            engine.RawLoad = -0.1;
            WeighingResult slightlyNegative = engine.Evaluate();
            Assert.That(slightlyNegative.Underload, Is.False, "within the zero band");
            Assert.That(slightlyNegative.GrossNegative, Is.True);
            engine.RawLoad = -1;
            Assert.That(engine.Evaluate().Underload, Is.True);
        }

        [Test]
        public void SetZeroAcceptsLoadsInsideTheZeroBandOnly()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            engine.RawLoad = 0.2;
            Assert.That(engine.Evaluate().InsideZero, Is.True);
            Assert.That(ServiceResult.IsGood(engine.SetZero()), Is.True);
            WeighingResult zeroed = engine.Evaluate();
            Assert.That(zeroed.Gross, Is.Zero);
            Assert.That(zeroed.CenterOfZero, Is.True);

            engine.RawLoad = 1.0;
            ServiceResult outside = engine.SetZero();
            Assert.That(outside.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
        }

        [Test]
        public void UnstableWeightCannotBeZeroedTaredOrRegistered()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            engine.RawLoad = 0.1;
            engine.Stable = false;
            Assert.That(engine.SetZero().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(engine.SetTare().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(engine.CanRegister().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public void SetTareTakesTheGrossAndClearTareRemovesIt()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            engine.RawLoad = 0.5;
            Assert.That(ServiceResult.IsGood(engine.SetTare()), Is.True);
            Assert.That(engine.TareMode, Is.EqualTo(TareMode.MeasuredTare_1));
            engine.RawLoad = 2.0;
            WeighingResult withTare = engine.Evaluate();
            Assert.That(withTare.Net, Is.EqualTo(1.5).Within(1e-9));
            Assert.That(withTare.Tare, Is.EqualTo(0.5).Within(1e-9));

            engine.ClearTare();
            Assert.That(engine.TareMode, Is.EqualTo(TareMode.None_0));
            Assert.That(engine.Evaluate().Net, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void SetTareRejectsOverloadAndNegativeGross()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            engine.RawLoad = 7;
            Assert.That(engine.SetTare().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            engine.RawLoad = -0.01;
            Assert.That(engine.SetTare().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
        }

        [TestCase(-1.0)]
        [TestCase(7.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void PresetTareOutsideTheCapacityIsRejected(double presetTare)
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            Assert.That(engine.SetPresetTare(presetTare).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadOutOfRange));
        }

        [Test]
        public void PresetTareIsAppliedWithItsMode()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            Assert.That(ServiceResult.IsGood(engine.SetPresetTare(0.25)), Is.True);
            Assert.That(engine.TareMode, Is.EqualTo(TareMode.PresetTare_2));
            engine.RawLoad = 1.0;
            Assert.That(engine.Evaluate().Net, Is.EqualTo(0.75).Within(1e-9));
        }

        [Test]
        public void RegistrationNeedsAWeightInsideTheRange()
        {
            ScaleWeighingEngine engine = TwoRangeScale();
            engine.RawLoad = 1;
            Assert.That(ServiceResult.IsGood(engine.CanRegister()), Is.True);
            engine.RawLoad = 10;
            Assert.That(engine.CanRegister().StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public void WeighingRangeDefinitionValidatesItsLimits()
        {
            Assert.That(() => new WeighingRangeDefinition(5, 1, 0.1, 0.1).Validate(), Throws.ArgumentException);
            Assert.That(() => new WeighingRangeDefinition(0, 1, 0, 0.1).Validate(), Throws.ArgumentException);
            Assert.That(() => new WeighingRangeDefinition(double.NaN, 1, 0.1, 0.1).Validate(), Throws.ArgumentException);
        }
    }

    /// <summary>
    /// The UNECE unit handling of <see cref="ScaleUnits"/>.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    public sealed class ScaleUnitsTests
    {
        [Test]
        public void UnitIdPacksTheCommonCodeBigEndian()
        {
            Assert.That(ScaleUnits.UnitIdOf("KGM"), Is.EqualTo(4933453));
            Assert.That(ScaleUnits.Kilogram.UnitId, Is.EqualTo(4933453));
            Assert.That(ScaleUnits.Kilogram.NamespaceUri, Is.EqualTo(ScaleUnits.UneceNamespaceUri));
        }

        [TestCase("")]
        [TestCase("ABCD")]
        public void UnitIdRejectsInvalidCodes(string code)
        {
            Assert.That(() => ScaleUnits.UnitIdOf(code), Throws.ArgumentException);
        }

        [Test]
        public void MassUnitsConvertThroughTheKilogram()
        {
            Assert.That(ScaleUnits.TryConvertMass(1500, ScaleUnits.Gram, ScaleUnits.Kilogram, out double kg), Is.True);
            Assert.That(kg, Is.EqualTo(1.5).Within(1e-12));
            Assert.That(ScaleUnits.TryConvertMass(1, ScaleUnits.Pound, ScaleUnits.Gram, out double g), Is.True);
            Assert.That(g, Is.EqualTo(453.59237).Within(1e-9));
            Assert.That(ScaleUnits.TryConvertMass(2, ScaleUnits.Tonne, ScaleUnits.Kilogram, out double t), Is.True);
            Assert.That(t, Is.EqualTo(2000).Within(1e-9));
            Assert.That(ScaleUnits.TryConvertMass(1, ScaleUnits.Percent, ScaleUnits.Kilogram, out _), Is.False);
            Assert.That(ScaleUnits.TryConvertMass(3, ScaleUnits.Percent, ScaleUnits.Percent, out double same), Is.True);
            Assert.That(same, Is.EqualTo(3));
        }

        [Test]
        public void SiMassUnitsAreRecognised()
        {
            Assert.That(ScaleUnits.IsSiMass(ScaleUnits.Kilogram), Is.True);
            Assert.That(ScaleUnits.IsSiMass(ScaleUnits.Milligram), Is.True);
            Assert.That(ScaleUnits.IsSiMass(ScaleUnits.Pound), Is.False);
            Assert.That(ScaleUnits.IsSiMass(ScaleUnits.Tonne), Is.False);
            Assert.That(ScaleUnits.IsSiMass(null), Is.False);
        }

        [Test]
        public void SameUnitComparesIdentityNotText()
        {
            EUInformation renamed = ScaleUnits.Create("KGM", "Kilo", "whatever");
            Assert.That(ScaleUnits.SameUnit(ScaleUnits.Kilogram, renamed), Is.True);
            Assert.That(ScaleUnits.SameUnit(ScaleUnits.Kilogram, ScaleUnits.Gram), Is.False);
            Assert.That(ScaleUnits.SameUnit(null, null), Is.True);
            Assert.That(ScaleUnits.SameUnit(ScaleUnits.Kilogram, null), Is.False);
        }
    }

    /// <summary>
    /// The server/client-independent model facts of <see cref="ScalesModel"/>
    /// and <see cref="ScalesProfiles"/>.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    public sealed class ScalesModelTests
    {
        [Test]
        public void EveryKindMapsToAConcreteTypeAndBack()
        {
            Assert.That(ScalesModel.AllKinds.Count, Is.EqualTo(14));
            foreach (ScaleKind kind in ScalesModel.AllKinds)
            {
                uint typeId = ScalesModel.ObjectTypeOf(kind);
                Assert.That(ScalesModel.KindOf(typeId), Is.EqualTo(kind));
                Assert.That(ScalesModel.ProductTypeOf(kind), Is.Not.Zero);
                Assert.That(ScalesProfiles.FacetOf(kind), Does.StartWith("http://opcfoundation.org/UA-Profile/External/Scales/V2/"));
            }
            Assert.That(ScalesModel.KindOf(ObjectTypes.ScaleDeviceType), Is.Null, "the abstract base is not a kind");
        }

        [Test]
        public void ProductTypesFollowTheInheritedRestriction()
        {
            Assert.That(ScalesModel.ProductTypeOf(ScaleKind.Laboratory), Is.EqualTo(ObjectTypes.SimpleProductType));
            Assert.That(ScalesModel.ProductTypeOf(ScaleKind.LossInWeight), Is.EqualTo(ObjectTypes.ContinuousProductType));
            Assert.That(ScalesModel.ProductTypeOf(ScaleKind.Checkweigher), Is.EqualTo(ObjectTypes.CheckweigherProductType));
            Assert.That(ScalesModel.ProductTypeOf(ScaleKind.Vehicle), Is.EqualTo(ObjectTypes.VehicleProductType));
        }

        [Test]
        public void InvalidKindsAreRejected()
        {
            Assert.That(() => ScalesModel.ObjectTypeOf((ScaleKind)99), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => ScalesModel.ProductTypeOf((ScaleKind)99), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => ScalesProfiles.FacetOf((ScaleKind)99), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [TestCase(ScaleNotificationId.GeneralOtherFault, ScaleNotificationCategory.Others, "GENERAL_OTHER_FAULT")]
        [TestCase(ScaleNotificationId.EmergencyStop, ScaleNotificationCategory.Process, "EMERGENCY_STOP")]
        [TestCase(ScaleNotificationId.LoginFault, ScaleNotificationCategory.System, "LOGIN_FAULT")]
        [TestCase(ScaleNotificationId.GeneralMemoryFault, ScaleNotificationCategory.Memory, "GENERAL_MEMORY_FAULT")]
        [TestCase(ScaleNotificationId.FeederNotRunning, ScaleNotificationCategory.Component, "FEEDER_NOT_RUNNING")]
        [TestCase(ScaleNotificationId.GeneralCommunicationFault, ScaleNotificationCategory.Communication, "GENERAL_COMMUNICATION_FAULT")]
        [TestCase(ScaleNotificationId.OverloadFault, ScaleNotificationCategory.WeighingModule, "OVERLOAD_FAULT")]
        [TestCase(ScaleNotificationId.AirPressureFault, ScaleNotificationCategory.Environment, "AIR_PRESSURE_FAULT")]
        public void NotificationIdsResolveToTheirAnnexCCategoryAndName(
            ScaleNotificationId id,
            ScaleNotificationCategory category,
            string text)
        {
            Assert.That(ScalesModel.CategoryOf(id), Is.EqualTo(category));
            Assert.That(ScalesModel.TextOf(id), Is.EqualTo(text));
        }

        [Test]
        public void EveryDefinedNotificationIdHasAName()
        {
#if NET8_0_OR_GREATER
            ScaleNotificationId[] ids = Enum.GetValues<ScaleNotificationId>();
#else
            var ids = (ScaleNotificationId[])Enum.GetValues(typeof(ScaleNotificationId));
#endif
            foreach (ScaleNotificationId id in ids)
            {
                Assert.That(ScalesModel.TextOf(id), Does.Match("^[A-Z_]+$"), id.ToString());
            }
            Assert.That(ScalesModel.TextOf((ScaleNotificationId)5001), Is.EqualTo("5001"));
            Assert.That(ScalesModel.CategoryOf((ScaleNotificationId)5001), Is.EqualTo(ScaleNotificationCategory.Others));
        }

        [Test]
        public void EveryCategoryHasItsTableName()
        {
            string[] expected = ["OTHERS", "PROCESS", "SYSTEM", "MEMORY", "COMPONENT", "COMMUNICATION", "WEIGHING_MODULE", "ENVIRONMENT"];
            for (uint ii = 0; ii < expected.Length; ii++)
            {
                Assert.That(ScalesModel.TextOf((ScaleNotificationCategory)ii), Is.EqualTo(expected[ii]));
            }
        }

        [Test]
        public void ProfileUrisAreReproducedVerbatimIncludingSpaces()
        {
            Assert.That(ScalesProfiles.LossInWeightScale, Does.EndWith("/Server/Scales_LossInWeight Scale"));
            Assert.That(ScalesProfiles.WeighingBridge, Does.EndWith("/Server/Scales_Weighing Bridge"));
            Assert.That(ScalesProfiles.TotalizingHopperScale, Does.EndWith("/Server/Scales_Totalizing_Hopper Scale"));
            Assert.That(ScalesProfiles.BaseScale, Is.EqualTo("http://opcfoundation.org/UA-Profile/External/Scales/V2/Scales_Base_Scale"));
        }
    }
}
