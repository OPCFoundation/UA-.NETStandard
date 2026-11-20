/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using Opc.Ua.Gpos;
using Opc.Ua.ISA95.Server.Builders;
using Opc.Ua.Positioning.Server;

namespace Opc.Ua.Aot.Tests
{
    /// <summary>
    /// Proves the point of the shared <see cref="IGeoLocationProvider"/> seam:
    /// a single provider implementation drives both the OPC 10000-211 (GPOS)
    /// coordinate model and the OPC 10030 (ISA-95) text model, with no
    /// per-model provider code.
    /// </summary>
    public sealed class SharedGeoLocationProviderTests
    {
        private const string SourceId = "reactor-1";
        private static readonly string[] s_zurichPlant = ["Building 4, Zurich Plant"];
        private static readonly string[] s_detroitPlant = ["Building 4, Detroit Plant"];

        [Test]
        public async Task OneProviderDrivesBothTheGposAndIsa95ModelsAsync()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(
                SourceId,
                new GeoLocationSample(
                    new GeoPosition(47.3769, 8.5417, 408.0, EpsgCode: 4326),
                    new GeoOrientation(0.0, 0.0, 90.0),
                    s_zurichPlant.ToArrayOf(),
                    StatusCodes.Good,
                    DateTimeUtc.Now));

            // GPOS consumes the structured position.
            GeoLocationSample sample = await provider
                .ReadAsync(SourceId, CancellationToken.None).ConfigureAwait(false);
            await Assert.That(sample.Position.HasValue).IsTrue();
            await Assert.That(sample.Position!.Value.Height).IsEqualTo(408.0);
            await Assert.That(sample.Orientation!.Value.C).IsEqualTo(90.0);

            // ISA-95 consumes the projected text, from the very same sample.
            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);
            await Assert.That(literals.Count).IsEqualTo(2);
            await Assert.That(literals[0])
                .IsEqualTo("SRID=4326;POINT Z (8.5417 47.3769 408)");
            await Assert.That(literals[1]).IsEqualTo("Building 4, Zurich Plant");
        }

        [Test]
        public async Task ATextOnlySourceIsRejectedByGposButServesIsa95Async()
        {
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(
                SourceId,
                GeoLocationSample.Good(
                    s_detroitPlant.ToArrayOf()));

            GeoLocationSample sample = await provider
                .ReadAsync(SourceId, CancellationToken.None).ConfigureAwait(false);

            // No coordinates, so a GPOS Variable cannot publish it...
            await Assert.That(sample.Position.HasValue).IsFalse();

            // ...but ISA-95 publishes the literal unchanged.
            ArrayOf<string> literals = WktGeoLocationTextFormatter.Instance
                .Format(sample);
            await Assert.That(literals.Count).IsEqualTo(1);
            await Assert.That(literals[0]).IsEqualTo("Building 4, Detroit Plant");
        }

        [Test]
        public async Task TheSharedContractDoesNotLiveInACompanionModelAsync()
        {
            // Both companion server assemblies bind the same contract type,
            // which must sit below them so neither depends on the other.
            await Assert.That(
                typeof(PositioningAddressSpaceBuilder).Assembly ==
                    typeof(Isa95GeoSpatialLocationBinder).Assembly).IsFalse();
            await Assert.That(
                typeof(IGeoLocationProvider).Assembly ==
                    typeof(GlobalLocationDataType).Assembly).IsFalse();
        }
    }
}
