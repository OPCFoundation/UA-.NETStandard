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

namespace Opc.Ua.Server.Tests.Location
{
    /// <summary>
    /// Tests for <see cref="WktGeoLocationTextFormatter"/>, which projects a
    /// structured sample into the text a location Variable carries.
    /// </summary>
    [TestFixture]
    [Category("GeoLocation")]
    public class WktGeoLocationTextFormatterTests
    {
        private static readonly string[] s_building4 = ["Building 4"];
        private static readonly string[] s_detroitPlant = ["Building 4, Detroit Plant"];

        [Test]
        public void APositionWithHeightBecomesAThreeDimensionalPoint()
        {
            var sample = GeoLocationSample.Good(
                new GeoPosition(47.3769, 8.5417, 408.0));

            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);

            Assert.That(literals.Count, Is.EqualTo(1));
            Assert.That(literals[0], Is.EqualTo("POINT Z (8.5417 47.3769 408)"));
        }

        [Test]
        public void APositionWithoutHeightBecomesATwoDimensionalPoint()
        {
            var sample = GeoLocationSample.Good(new GeoPosition(47.3769, 8.5417));

            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);

            Assert.That(literals.Count, Is.EqualTo(1));
            Assert.That(literals[0], Is.EqualTo("POINT (8.5417 47.3769)"));
        }

        [Test]
        public void AnEpsgCodeBecomesAnSridPrefix()
        {
            var sample = GeoLocationSample.Good(
                new GeoPosition(47.3769, 8.5417, EpsgCode: 4326));

            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);

            Assert.That(literals.Count, Is.EqualTo(1));
            Assert.That(literals[0], Is.EqualTo("SRID=4326;POINT (8.5417 47.3769)"));
        }

        [Test]
        public void CoordinatesUseTheInvariantCulture()
        {
            var sample = GeoLocationSample.Good(new GeoPosition(-0.5, 1.25));

            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);

            Assert.That(literals[0], Does.Contain("1.25"));
            Assert.That(literals[0], Does.Not.Contain(","));
        }

        [Test]
        public void LabelsFollowTheGeometry()
        {
            var sample = new GeoLocationSample(
                new GeoPosition(47.3769, 8.5417),
                null,
                s_building4.ToArrayOf(),
                StatusCodes.Good,
                DateTimeUtc.Now);

            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);

            Assert.That(literals.Count, Is.EqualTo(2));
            Assert.That(literals[0], Is.EqualTo("POINT (8.5417 47.3769)"));
            Assert.That(literals[1], Is.EqualTo("Building 4"));
        }

        [Test]
        public void ASampleWithoutAPositionKeepsOnlyItsLabels()
        {
            var sample = GeoLocationSample.Good(
                s_detroitPlant.ToArrayOf());

            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);

            Assert.That(literals.Count, Is.EqualTo(1));
            Assert.That(literals[0], Is.EqualTo("Building 4, Detroit Plant"));
        }

        [Test]
        public void AnEmptySampleProducesNoLiterals()
        {
            var sample = GeoLocationSample.Unavailable(StatusCodes.BadNoDataAvailable);

            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);

            Assert.That(literals.Count, Is.Zero);
        }
    }
}