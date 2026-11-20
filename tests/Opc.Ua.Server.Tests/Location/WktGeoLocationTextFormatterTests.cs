// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
