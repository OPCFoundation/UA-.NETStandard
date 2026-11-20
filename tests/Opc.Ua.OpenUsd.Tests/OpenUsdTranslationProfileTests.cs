/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Pins which source values the connector's §5.8 translation profile accepts.
    /// </summary>
    /// <remarks>
    /// This matters more than it looks. The profile fails closed - an unaccepted value
    /// leaves the target unresolved with no error anywhere - so a server publishing a
    /// position in a shape the profile does not take produces a viewport that silently
    /// never moves the prim, while every subscription counter says the data is flowing.
    /// </remarks>
    [TestFixture]
    [Category("OpenUsd")]
    [Parallelizable]
    public sealed class OpenUsdTranslationProfileTests
    {
        /// <summary>
        /// Verifies structured Cartesian coordinates produce the exact three-axis translation vector.
        /// </summary>
        [Test]
        public void StructuredCartesianCoordinatesAreAccepted()
        {
            var binding = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Translation,
                Scale = 1.0,
                Offset = 0.0
            };
            var value = new Variant(new ExtensionObject(new ThreeDCartesianCoordinates
            {
                X = 1.5,
                Y = -2.5,
                Z = 3.5
            }));

            Variant converted = OpenUsdConnector.Convert(binding, value);

            Assert.That(converted.IsNull, Is.False,
                "A structured 3D coordinate is the source shape the translation profile is " +
                "defined for; leaving it unresolved would stop any prim following it.");
            Assert.That(converted.TryGetValue(out ArrayOf<double> coordinates), Is.True);
            Assert.That(coordinates.ToArray(), Is.EqualTo(new[] { 1.5, -2.5, 3.5 }));
        }

        [Test]
        public void PlainDoubleArrayIsNotAccepted()
        {
            var binding = new OpenUsdConnector.BindingInfo
            {
                Kind = OpenUsdRenderTargetKind.Translation,
                Scale = 1.0,
                Offset = 0.0
            };

            Variant converted = OpenUsdConnector.Convert(binding, new Variant(new[] { 1.5, -2.5, 3.5 }));

            Assert.That(converted.IsNull, Is.True,
                "A bare double[3] is not a structured 3D source. This is the behaviour that " +
                "silently stopped the bin-picking parts moving, so it is pinned here to " +
                "make the requirement visible rather than surprising.");
        }
    }
}
