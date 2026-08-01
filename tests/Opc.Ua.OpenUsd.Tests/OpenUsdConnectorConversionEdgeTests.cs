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

using System;
using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Fail-closed edges of the §5.8 conversion profile and of the §5.2 stage-digest
    /// guards that do not require a session.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorConversionEdgeTests
    {
        private static EUInformation Unit(string code)
        {
            int id = 0;
            foreach (char c in code)
            {
                id = (id << 8) | (byte)c;
            }
            return new EUInformation { UnitId = id, DisplayName = new LocalizedText(code) };
        }

        private static OpenUsdConnector.BindingInfo Binding(
            OpenUsdRenderTargetKind kind, string? source, string? target)
        {
            return new OpenUsdConnector.BindingInfo
            {
                Kind = kind,
                PrimPath = "/World/Robot",
                PropertyName = "value",
                SourceEngineeringUnits = source == null ? null : Unit(source),
                TargetEngineeringUnits = target == null ? null : Unit(target)
            };
        }

        [Test]
        public void ConvertLeavesDisplayColorUnresolvedForANonScalarSource()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.DisplayColor, null, null);

            Variant result = OpenUsdConnector.Convert(b, new Variant("warm"));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ConvertLeavesEmissiveColorUnresolvedForANonScalarSource()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.EmissiveColor, null, null);

            Variant result = OpenUsdConnector.Convert(b, new Variant("bright"));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ConvertLeavesTranslationUnresolvedForAnUnhonourableLengthUnit()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation, "MTR", "FOT");

            Variant result = OpenUsdConnector.Convert(b, new Variant(1.0));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ConvertLeavesGeoreferenceHeightUnresolvedForAnUnhonourableLengthUnit()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Georeference, "MTR", "FOT");
            b.PropertyName = "cesium:anchor:height";

            Variant result = OpenUsdConnector.Convert(b, new Variant(12.0));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ConvertLeavesGeoreferenceUnresolvedWhenTheTargetAttributeIsUnnamed()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Georeference, null, null);
            b.PropertyName = string.Empty;

            Variant result = OpenUsdConnector.Convert(b, new Variant(12.0));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ConvertScalesRadiansToDegreesForAScalarTarget()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Custom, "C81", "DD");

            Variant result = OpenUsdConnector.Convert(b, new Variant(Math.PI));

            Assert.That(result.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(180.0).Within(1e-9));
        }

        [Test]
        public void ConvertScalesDegreesToRadiansForAScalarTarget()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Custom, "DD", "C81");

            Variant result = OpenUsdConnector.Convert(b, new Variant(180.0));

            Assert.That(result.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(Math.PI).Within(1e-9));
        }

        [Test]
        public void ConvertLeavesAScalarTargetUnresolvedForAnUnknownUnitPair()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Custom, "MTR", "FOT");

            Variant result = OpenUsdConnector.Convert(b, new Variant(1.0));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void UnitCodeFallsBackToTheDisplayNameWhenNoUnitIdIsSet()
        {
            var units = new EUInformation { UnitId = 0, DisplayName = new LocalizedText("rad") };

            string code = OpenUsdConnector.UnitCode(units);

            Assert.That(code, Is.EqualTo("rad"));
        }

        [Test]
        public void UnitCodeIsEmptyForAnUndeclaredUnit()
        {
            Assert.That(OpenUsdConnector.UnitCode(null), Is.Empty);
        }

        [Test]
        public void VerifyStageDigestRejectsARepresentationWithoutADigest()
        {
            var rep = new OpenUsdConnector.RepresentationInfo();

            bool verified = OpenUsdConnector.VerifyStageDigest(rep, [1, 2, 3]);

            Assert.That(verified, Is.False);
        }

        [Test]
        public void VerifyStageDigestRejectsARepresentationWithoutADigestAlgorithm()
        {
            var rep = new OpenUsdConnector.RepresentationInfo
            {
                RootLayerDigest = new ByteString(new byte[] { 1, 2, 3 }),
                DigestAlgorithm = OpenUsdDigestAlgorithm.None
            };

            bool verified = OpenUsdConnector.VerifyStageDigest(rep, [1, 2, 3]);

            Assert.That(verified, Is.False);
        }

        [Test]
        public void VerifyStageDigestRejectsNullContent()
        {
            var rep = new OpenUsdConnector.RepresentationInfo
            {
                RootLayerDigest = new ByteString(new byte[] { 1, 2, 3 }),
                DigestAlgorithm = OpenUsdDigestAlgorithm.Sha256
            };

            bool verified = OpenUsdConnector.VerifyStageDigest(rep, null!);

            Assert.That(verified, Is.False);
        }

        [Test]
        public void TryInvertConversionRejectsAZeroScale()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Custom, null, null);
            b.Scale = 0.0;

            bool inverted = OpenUsdConnector.TryInvertConversion(b, 1.0, out double uaValue);

            Assert.That(inverted, Is.False);
            Assert.That(uaValue, Is.Zero);
        }

        [Test]
        public void TryInvertConversionRejectsAnUnhonourableUnitPair()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Custom, "MTR", "FOT");

            bool inverted = OpenUsdConnector.TryInvertConversion(b, 1.0, out _);

            Assert.That(inverted, Is.False);
        }

        [Test]
        public void TryInvertConversionUndoesTheAngleFactorForARotationTarget()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation, "C81", "DD");

            bool inverted = OpenUsdConnector.TryInvertConversion(b, 180.0, out double uaValue);

            Assert.That(inverted, Is.True);
            Assert.That(uaValue, Is.EqualTo(Math.PI).Within(1e-9));
        }

        [Test]
        public void SelectCommandBindingIgnoresABindingWithoutATargetOrMethod()
        {
            var rep = new OpenUsdConnector.RepresentationInfo();
            rep.Bindings.Add(new OpenUsdConnector.BindingInfo
            {
                Intent = OpenUsdIntentProfile.UsdToUaCommand,
                SignalRole = OpenUsdSignalRole.Controllable
            });

            OpenUsdConnector.BindingInfo? selected = OpenUsdConnector.SelectCommandBinding([rep]);

            Assert.That(selected, Is.Null);
        }
    }
}
