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

using NUnit.Framework;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Unit tests for <see cref="OpenUsdConnector.Convert"/>: the pure mapping from a
    /// source <see cref="Variant"/> to the USD-side value for each render-target kind.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorConvertTests
    {
        private static OpenUsdConnector.BindingInfo Binding(
            OpenUsdRenderTargetKind kind, double scale = 1.0, double offset = 0.0)
        {
            return new() { Kind = kind, Scale = scale, Offset = offset };
        }

        [TestCase(OpenUsdRenderTargetKind.Translation)]
        [TestCase(OpenUsdRenderTargetKind.Rotation)]
        [TestCase(OpenUsdRenderTargetKind.Scale)]
        [TestCase(OpenUsdRenderTargetKind.Opacity)]
        [TestCase(OpenUsdRenderTargetKind.Custom)]
        public void ScalarKindsApplyScaleAndOffset(OpenUsdRenderTargetKind kind)
        {
            OpenUsdConnector.BindingInfo b = Binding(kind, scale: 2.0, offset: 1.0);

            Variant result = OpenUsdConnector.Convert(b, new Variant(3.0));

            Assert.That(result.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(7.0).Within(1e-9));
        }

        [Test]
        public void IntegerSourceIsCoercedToDouble()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation, scale: 1.0, offset: 0.0);

            Variant result = OpenUsdConnector.Convert(b, new Variant(5));

            Assert.That(result.TryGetValue(out double value), Is.True);
            Assert.That(value, Is.EqualTo(5.0).Within(1e-9));
        }

        [TestCase(20.0, 0f, 0f, 1f)]
        [TestCase(100.0, 1f, 0f, 0f)]
        [TestCase(-50.0, 0f, 0f, 1f)]
        [TestCase(500.0, 1f, 0f, 0f)]
        public void DisplayColorMapsAndClamps(double raw, float r, float g, float bl)
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.DisplayColor);

            Variant result = OpenUsdConnector.Convert(b, new Variant(raw));

            Assert.That(result.TryGetValue(out ArrayOf<float> colour), Is.True);
            Assert.That(colour.Count, Is.EqualTo(3));
            Assert.That(colour[0], Is.EqualTo(r).Within(1e-4));
            Assert.That(colour[1], Is.EqualTo(g).Within(1e-4));
            Assert.That(colour[2], Is.EqualTo(bl).Within(1e-4));
        }

        [Test]
        public void EmissiveColorMapsPressure()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.EmissiveColor);

            Variant hot = OpenUsdConnector.Convert(b, new Variant(6.0));
            Assert.That(hot.TryGetValue(out ArrayOf<float> hotColour), Is.True);
            Assert.That(hotColour[0], Is.EqualTo(0.1f).Within(1e-4));
            Assert.That(hotColour[1], Is.EqualTo(1f).Within(1e-4));
            Assert.That(hotColour[2], Is.EqualTo(0.2f).Within(1e-4));

            Variant cold = OpenUsdConnector.Convert(b, new Variant(0.0));
            Assert.That(cold.TryGetValue(out ArrayOf<float> coldColour), Is.True);
            Assert.That(coldColour[0], Is.EqualTo(0f).Within(1e-4));
            Assert.That(coldColour[1], Is.EqualTo(0f).Within(1e-4));
            Assert.That(coldColour[2], Is.EqualTo(0f).Within(1e-4));
        }

        [TestCase(1.0, "inherited")]
        [TestCase(0.0, "invisible")]
        public void VisibilityMapsToToken(double raw, string expected)
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Visibility);

            Variant result = OpenUsdConnector.Convert(b, new Variant(raw));

            Assert.That(result.TryGetValue(out string token), Is.True);
            Assert.That(token, Is.EqualTo(expected));
        }

        [Test]
        public void NullSourceReturnsNullVariant()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation);

            Variant result = OpenUsdConnector.Convert(b, default);

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ThreeDCartesianCoordinatesTranslationReturnsDoubleArray()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation);
            var coordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(coordinates)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector.Count, Is.EqualTo(3));
            Assert.That(vector[0], Is.EqualTo(1.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(2.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(3.0).Within(1e-9));
        }

        [Test]
        public void ThreeDCartesianCoordinatesTranslationAppliesScaleAndOffsetElementWise()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation, scale: 2.0, offset: 1.0);
            var coordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(coordinates)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(3.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(5.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(7.0).Within(1e-9));
        }

        [Test]
        public void ThreeDOrientationRotationAppliesNoUnitConversionWhenUnitsAreUndeclared()
        {
            // H6/§5.8: the angle factor is step (1) of the conversion, but it is only
            // applied when the binding actually declares an AngleUnit. With no declared
            // units the source value is authored unchanged — this preserves a server
            // that already publishes degrees.
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            var orientation = new ThreeDOrientation { A = 0.1, B = 0.2, C = 0.3 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(orientation)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector.Count, Is.EqualTo(3));
            Assert.That(vector[0], Is.EqualTo(0.1).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(0.2).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(0.3).Within(1e-9));
        }

        /// <summary>
        /// UNECE common code "C81" (radian) packed into an EUInformation UnitId.
        /// </summary>
        private static EUInformation Radians()
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = ('C' << 16) | ('8' << 8) | '1',
                DisplayName = new LocalizedText("rad")
            };
        }

        /// <summary>
        /// UNECE common code "DD" (degree) packed into an EUInformation UnitId.
        /// </summary>
        private static EUInformation Degrees()
        {
            return new EUInformation
            {
                NamespaceUri = "http://www.opcfoundation.org/UA/units/un/cefact",
                UnitId = ('D' << 8) | 'D',
                DisplayName = new LocalizedText("°")
            };
        }

        [Test]
        public void RadianSourceIsConvertedToDegreesForRotationTargets()
        {
            // §5.8: "USD rotation ops are in degrees; convert an AngleUnit of radians
            // with x 180/pi".
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            b.SourceEngineeringUnits = Radians();

            Variant result = OpenUsdConnector.Convert(b, new Variant(0.1));

            Assert.That(result.TryGetValue(out double degrees), Is.True);
            Assert.That(degrees, Is.EqualTo(5.729577951308232).Within(1e-9));
        }

        [Test]
        public void RadianSourceIsConvertedElementWiseForStructuredRotation()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            b.SourceEngineeringUnits = Radians();
            b.TargetEngineeringUnits = Degrees();
            var orientation = new ThreeDOrientation { A = 0.1, B = 0.2, C = 0.3 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(orientation)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(5.729577951308232).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(11.459155902616464).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(17.188733853924695).Within(1e-9));
        }

        [Test]
        public void DeclaredDegreeSourceIsNotConverted()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            b.SourceEngineeringUnits = Degrees();

            Variant result = OpenUsdConnector.Convert(b, new Variant(90.0));

            Assert.That(result.TryGetValue(out double degrees), Is.True);
            Assert.That(degrees, Is.EqualTo(90.0).Within(1e-9));
        }

        [Test]
        public void UnhonourableAngleUnitFailsClosed()
        {
            // A declared AngleUnit the connector has no factor for must leave the target
            // unresolved rather than author the raw value 1:1.
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            b.SourceEngineeringUnits = new EUInformation
            {
                UnitId = ('M' << 16) | ('I' << 8) | 'K',
                DisplayName = new LocalizedText("mil")
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(0.1));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void TransformKindFailsClosedForStructuredSource()
        {
            // C1/§5.8: the matrix4d/quaternion transform profile is not implemented, so
            // the target is left unresolved (no update) rather than fabricated as 0.0.
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Transform);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 },
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = 0.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void TransformKindFailsClosedForScalarSource()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Transform);

            Variant result = OpenUsdConnector.Convert(b, new Variant(1.0));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void UnknownRenderTargetKindFailsClosed()
        {
            // C1: a RenderTargetKind introduced by a later revision of the companion
            // specification is never guessed at — no value is authored.
            OpenUsdConnector.BindingInfo b = Binding((OpenUsdRenderTargetKind)99);

            Variant result = OpenUsdConnector.Convert(b, new Variant(3.0));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void UndecodableStructuredSourceFailsClosedInsteadOfAuthoringOffset()
        {
            // C1: a structured source ToDouble cannot decode used to fall through to a
            // fabricated 0.0, so the connector authored Offset. It must now be unresolved.
            OpenUsdConnector.BindingInfo b = Binding(
                OpenUsdRenderTargetKind.Opacity, scale: 1.0, offset: 0.25);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 },
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = 0.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GeoreferenceLatitudeComponentIsAuthoredInDegrees()
        {
            // §5.8 geospatial profile: latitude/longitude are authored as decimal
            // degrees on the target anchor component, never as a raw xformOp.
            var b = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Georeference,
                Scale = 1.0,
                Offset = 0.0,
                PropertyName = "cesium:anchor:latitude"
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(47.3769));

            Assert.That(result.TryGetValue(out double latitude), Is.True);
            Assert.That(latitude, Is.EqualTo(47.3769).Within(1e-9));
        }

        [Test]
        public void GeoreferenceHeightComponentAppliesScaleAndOffset()
        {
            var b = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Georeference,
                Scale = 0.001,
                Offset = 5.0,
                PropertyName = "cesium:anchor:height"
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(408000.0));

            Assert.That(result.TryGetValue(out double height), Is.True);
            Assert.That(height, Is.EqualTo(413.0).Within(1e-9));
        }

        [Test]
        public void GeoreferenceWithUnknownComponentFailsClosed()
        {
            // The single most dangerous fabrication the audit found: an unresolvable
            // georeference target used to author Offset (usually 0.0), teleporting the
            // asset to lat/lon 0,0. §5.8 requires it to stay unresolved.
            var b = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Georeference,
                Scale = 1.0,
                Offset = 0.0,
                PropertyName = "xformOp:translate"
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(47.3769));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void GeoreferenceWithStructuredSourceFailsClosed()
        {
            // §5.8: "an unmapped or unsupported CRS shall leave the target unresolved
            // (no update) rather than author an unprojected value."
            var b = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Georeference,
                Scale = 1.0,
                Offset = 0.0,
                PropertyName = "cesium:anchor:latitude"
            };
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 1.0, Y = 2.0, Z = 3.0 },
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = 0.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.IsNull, Is.True);
        }

        [Test]
        public void ClientRenderTargetKindMirrorsGeoreference()
        {
            // The hand-maintained client mirror must track DataType i=3002 in the
            // NodeSet, where Georeference = 9 was added in Release 0.3.0.
            Assert.That((int)OpenUsdRenderTargetKind.Georeference, Is.EqualTo(9));
        }

        [TestCase(1.0, 0.0, 42.5, 42.5)]
        [TestCase(2.0, 1.0, 7.0, 3.0)]
        [TestCase(0.5, -1.0, 2.0, 6.0)]
        public void InverseConversionUndoesScaleAndOffset(
            double scale, double offset, double usdValue, double expected)
        {
            // §5.10: the trigger value is converted back through the inverse of §5.8.
            OpenUsdConnector.BindingInfo b = Binding(
                OpenUsdRenderTargetKind.Custom, scale: scale, offset: offset);

            Assert.That(OpenUsdConnector.TryInvertConversion(b, usdValue, out double uaValue), Is.True);
            Assert.That(uaValue, Is.EqualTo(expected).Within(1e-9));
        }

        [Test]
        public void InverseConversionUndoesTheAngleUnitFactor()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            b.SourceEngineeringUnits = Radians();

            Assert.That(
                OpenUsdConnector.TryInvertConversion(b, 5.729577951308232, out double radians), Is.True);
            Assert.That(radians, Is.EqualTo(0.1).Within(1e-9));
        }

        [Test]
        public void InverseConversionRefusesAZeroScale()
        {
            OpenUsdConnector.BindingInfo b = Binding(
                OpenUsdRenderTargetKind.Custom, scale: 0.0, offset: 1.0);

            Assert.That(OpenUsdConnector.TryInvertConversion(b, 5.0, out _), Is.False);
        }

        [Test]
        public void InverseConversionRefusesAnUnhonourableUnit()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            b.SourceEngineeringUnits = new EUInformation
            {
                UnitId = ('M' << 16) | ('I' << 8) | 'K',
                DisplayName = new LocalizedText("mil")
            };

            Assert.That(OpenUsdConnector.TryInvertConversion(b, 5.0, out _), Is.False);
        }

        [Test]
        public void DisabledCommandBindingIsNotSelectedForActuation()
        {
            // §5.4: "Enabled | Boolean | M | false is a tombstone suppressing an
            // inherited binding." A suppressed command binding must never be actuated.
            var rep = new OpenUsdConnector.RepresentationInfo { PrimPath = "/Plant/Pumps/P101" };
            rep.Bindings.Add(new OpenUsdConnector.BindingInfo
            {
                Intent = OpenUsdIntentProfile.UsdToUaCommand,
                SignalRole = OpenUsdSignalRole.Controllable,
                CommandTargetNodeId = new NodeId(9999u, 3),
                Enabled = false
            });

            Assert.That(OpenUsdConnector.SelectCommandBinding([rep]), Is.Null);
        }

        [Test]
        public void EnabledCommandBindingIsSelectedForActuation()
        {
            var rep = new OpenUsdConnector.RepresentationInfo { PrimPath = "/Plant/Pumps/P101" };
            rep.Bindings.Add(new OpenUsdConnector.BindingInfo
            {
                Intent = OpenUsdIntentProfile.UsdToUaCommand,
                SignalRole = OpenUsdSignalRole.Controllable,
                CommandTargetNodeId = new NodeId(9999u, 3)
            });

            Assert.That(OpenUsdConnector.SelectCommandBinding([rep]), Is.Not.Null);
        }

        [Test]
        public void DisabledCommandBindingDoesNotShadowAnEnabledOne()
        {
            var rep = new OpenUsdConnector.RepresentationInfo { PrimPath = "/Plant/Pumps/P101" };
            rep.Bindings.Add(new OpenUsdConnector.BindingInfo
            {
                Intent = OpenUsdIntentProfile.UsdToUaCommand,
                SignalRole = OpenUsdSignalRole.Controllable,
                CommandTargetNodeId = new NodeId(1u, 3),
                Enabled = false
            });
            rep.Bindings.Add(new OpenUsdConnector.BindingInfo
            {
                Intent = OpenUsdIntentProfile.UsdToUaCommand,
                SignalRole = OpenUsdSignalRole.Controllable,
                CommandTargetNodeId = new NodeId(2u, 3)
            });

            OpenUsdConnector.BindingInfo? selected = OpenUsdConnector.SelectCommandBinding([rep]);
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.CommandTargetNodeId, Is.EqualTo(new NodeId(2u, 3)));
        }

        [Test]
        public void ObservableCommandBindingIsNotSelectedForActuation()
        {
            // §5.9: only a Controllable binding may be actuated.
            var rep = new OpenUsdConnector.RepresentationInfo { PrimPath = "/Plant/Pumps/P101" };
            rep.Bindings.Add(new OpenUsdConnector.BindingInfo
            {
                Intent = OpenUsdIntentProfile.UsdToUaCommand,
                SignalRole = OpenUsdSignalRole.Observable,
                CommandTargetNodeId = new NodeId(9999u, 3)
            });

            Assert.That(OpenUsdConnector.SelectCommandBinding([rep]), Is.Null);
        }

        [Test]
        public void CommandBindingWithOnlyAMethodIdIsSelectable()
        {
            // §5.10: "If CommandMethodId is present, the connector Calls that Method …;
            // otherwise it Writes the converted value to CommandTargetNodeId."
            var rep = new OpenUsdConnector.RepresentationInfo { PrimPath = "/Plant/Pumps/P101" };
            rep.Bindings.Add(new OpenUsdConnector.BindingInfo
            {
                Intent = OpenUsdIntentProfile.UsdToUaCommand,
                SignalRole = OpenUsdSignalRole.Controllable,
                CommandMethodId = new NodeId(4242u, 3)
            });

            OpenUsdConnector.BindingInfo? selected = OpenUsdConnector.SelectCommandBinding([rep]);
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.CommandMethodId, Is.EqualTo(new NodeId(4242u, 3)));
        }

        [TestCase("/Plant/Pumps/P101", "Impeller", "/Plant/Pumps/P101/Impeller")]
        [TestCase("/Plant/Pumps/P101", "Body/Impeller", "/Plant/Pumps/P101/Body/Impeller")]
        [TestCase("/Plant/Pumps/P101", "", "/Plant/Pumps/P101")]
        [TestCase("/Plant/Pumps/P101", "/Absolute/Prim", "/Absolute/Prim")]
        [TestCase("", "Impeller", "/Impeller")]
        public void RelativeTargetPrimPathIsJoinedToTheRepresentationPrimPath(
            string basePath, string target, string expected)
        {
            // §5.7: "a relative path shall be joined to the representation PrimPath,
            // never authored at the layer root."
            Assert.That(OpenUsdConnector.JoinPrimPath(basePath, target), Is.EqualTo(expected));
        }

        [Test]
        public void ThreeDOrientationRotationAppliesScaleAndOffsetElementWise()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation, scale: 10.0, offset: 1.0);
            var orientation = new ThreeDOrientation { A = 1.0, B = 2.0, C = 3.0 };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(orientation)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(11.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(21.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(31.0).Within(1e-9));
        }

        [Test]
        public void ThreeDFrameTranslationReturnsCartesianCoordinatesArray()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 4.0, Y = 5.0, Z = 6.0 },
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = 0.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(4.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(5.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(6.0).Within(1e-9));
        }

        [Test]
        public void ThreeDFrameRotationReturnsOrientationArray()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Rotation);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 0.0, Y = 0.0, Z = 0.0 },
                Orientation = new ThreeDOrientation { A = 7.0, B = 8.0, C = 9.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.EqualTo(7.0).Within(1e-9));
            Assert.That(vector[1], Is.EqualTo(8.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(9.0).Within(1e-9));
        }

        [Test]
        public void ThreeDFrameTranslationAppliesScaleAndOffsetElementWise()
        {
            OpenUsdConnector.BindingInfo b = Binding(OpenUsdRenderTargetKind.Translation, scale: 0.5, offset: -1.0);
            var frame = new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates { X = 2.0, Y = 4.0, Z = 6.0 },
                Orientation = new ThreeDOrientation { A = 0.0, B = 0.0, C = 0.0 }
            };

            Variant result = OpenUsdConnector.Convert(b, new Variant(new ExtensionObject(frame)));

            Assert.That(result.TryGetValue(out ArrayOf<double> vector), Is.True);
            Assert.That(vector[0], Is.Zero);
            Assert.That(vector[1], Is.EqualTo(1.0).Within(1e-9));
            Assert.That(vector[2], Is.EqualTo(2.0).Within(1e-9));
        }

        // ---------------------------------------------------------------------
        // H3 — §5.15: "verif[y] each digest" / "shall not silently mix unverified
        // delivered bytes into the stage". An asset delivered *without* a digest is
        // unverifiable and must never be reported as verified.
        // ---------------------------------------------------------------------

        [Test]
        public void DeliveredAssetWithoutDigestIsRefusedWhenDigestsAreRequired()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("#usda 1.0\n");

            Assert.That(
                () => OpenUsdConnector.VerifyDeliveredAsset(
                    "orphan.usda", bytes, default, OpenUsdDigestAlgorithm.Sha256, requireDigests: true),
                Throws.InstanceOf<System.InvalidOperationException>());
        }

        [Test]
        public void DeliveredAssetWithoutDigestIsNotReportedVerified()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("#usda 1.0\n");

            bool verified = OpenUsdConnector.VerifyDeliveredAsset(
                "orphan.usda", bytes, default, OpenUsdDigestAlgorithm.Sha256, requireDigests: false);

            Assert.That(verified, Is.False);
        }

        [Test]
        public void DeliveredAssetWithMatchingDigestIsVerified()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("#usda 1.0\n");
            byte[] digest;
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
#pragma warning disable CA1850
                digest = sha.ComputeHash(bytes);
#pragma warning restore CA1850
            }

            bool verified = OpenUsdConnector.VerifyDeliveredAsset(
                "root.usda", bytes, new ByteString(digest), OpenUsdDigestAlgorithm.Sha256, requireDigests: true);

            Assert.That(verified, Is.True);
        }

        [Test]
        public void DeliveredAssetWithMismatchedDigestIsRefused()
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("#usda 1.0\n");
            var wrong = new ByteString(new byte[32]);

            Assert.That(
                () => OpenUsdConnector.VerifyDeliveredAsset(
                    "root.usda", bytes, wrong, OpenUsdDigestAlgorithm.Sha256, requireDigests: true),
                Throws.InstanceOf<System.InvalidOperationException>());
        }
    }
}
