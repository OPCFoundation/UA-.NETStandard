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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;
using Opc.Ua.AutoID.Server.Builders;

namespace Opc.Ua.AutoID.Tests
{
    /// <summary>
    /// The server advertises the conformance units and facets of OPC 30010 10.2 only for what the
    /// hosted readers implement.
    /// </summary>
    [TestFixture]
    [Category("AutoID")]
    public sealed class AutoIdConformanceTests
    {
        [Test]
        public async Task CapabilitiesGrowWithTheReadersAndTheirProvidersAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            ushort ns = (ushort)fixture.Context.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri);

            string[] emptyProfiles = AutoIdProfilesOf(fixture);
            string[] emptyUnits = UnitsOf(fixture, ns);

            await fixture.Runtime.CreateReaderAsync(fixture.Name("Passive"), new MinimalProvider(AutoIdReaderKind.Optical))
                .ConfigureAwait(false);
            string[] passiveProfiles = AutoIdProfilesOf(fixture);
            string[] passiveUnits = UnitsOf(fixture, ns);

            await fixture.Runtime.CreateReaderAsync(
                fixture.Name("AsyncOptical"), new AutoIdParameterControlTests.ParameterProvider
                {
                    Kind = AutoIdReaderKind.Optical
                }).ConfigureAwait(false);
            string[] asyncProfiles = AutoIdProfilesOf(fixture);
            string[] asyncUnits = UnitsOf(fixture, ns);

            await fixture.Runtime.CreateReaderAsync(fixture.Name("Rfid"), new InMemoryRfidReaderProvider())
                .ConfigureAwait(false);
            string[] syncProfiles = AutoIdProfilesOf(fixture);
            string[] syncUnits = UnitsOf(fixture, ns);

            Assert.Multiple(() =>
            {
                Assert.That(emptyProfiles, Is.Empty, "no reader, no facet");
                Assert.That(emptyUnits, Is.Empty);
                Assert.That(
                    passiveUnits,
                    Is.EquivalentTo(
                    [
                        AutoIdProfiles.DeviceTypeUnit, AutoIdProfiles.SimpleSyncAccessUnit, AutoIdProfiles.AsyncAccessUnit
                    ]));
                Assert.That(
                    passiveProfiles,
                    Is.EquivalentTo([AutoIdProfiles.SimpleSyncServer, AutoIdProfiles.BaseAsyncServer]));
                Assert.That(asyncUnits, Does.Contain(AutoIdProfiles.AsyncAccessControlUnit));
                Assert.That(asyncUnits, Does.Not.Contain(AutoIdProfiles.SyncAccessUnit), "no Scan method");
                Assert.That(asyncProfiles, Does.Not.Contain(AutoIdProfiles.BaseSyncServer));
                Assert.That(syncUnits, Does.Contain(AutoIdProfiles.SyncAccessUnit));
                Assert.That(
                    syncProfiles,
                    Is.EquivalentTo(
                    [
                        AutoIdProfiles.SimpleSyncServer, AutoIdProfiles.BaseSyncServer, AutoIdProfiles.BaseAsyncServer
                    ]));
                Assert.That(syncProfiles, Does.Not.Contain(AutoIdProfiles.FullServer));
            });
        }

        [Test]
        public async Task TheOwningManagerPublishesTheContributionTogetherWithDeviceIntegrationAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await fixture.Runtime.CreateReaderAsync(fixture.Name("Rfid"), new InMemoryRfidReaderProvider())
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(fixture.Manager.ServerProfiles.ToArray(), Does.Contain(AutoIdProfiles.BaseSyncServer));
                Assert.That(
                    fixture.Manager.ConformanceUnits.ToArray()!.Select(unit => unit.Name),
                    Does.Contain(AutoIdProfiles.SyncAccessUnit));
                Assert.That(
                    fixture.Manager.ConformanceUnits.ToArray()!.Select(unit => unit.Name),
                    Does.Contain("DI DeviceTopology"),
                    "the Device Integration units stay");
            });
        }

        /// <summary>
        /// Requires every mandatory reader capability on one reader, independently of optional reader-kind methods.
        /// </summary>
        [Test]
        public async Task FullFacetRequiresAllMandatoryCapabilitiesOnOneReaderAsync(
            [Values] bool synchronous, [Values] bool asynchronous, [Values] bool parameters)
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var provider = new Mock<IAutoIdReaderProvider>();
            provider.SetupGet(value => value.Kind).Returns(AutoIdReaderKind.Optical);
            if (synchronous)
            {
                provider.As<IAutoIdScanProvider>();
            }
            if (asynchronous)
            {
                provider.As<IAutoIdAsyncScanProvider>();
            }
            if (parameters)
            {
                Mock<IAutoIdScanParametersProvider> configuration = provider.As<IAutoIdScanParametersProvider>();
                ArrayOf<string> codes = ["RAW:BYTES"];
                configuration.SetupGet(value => value.SupportedCodeTypes).Returns(codes);
                configuration.Setup(value => value.ReadParametersAsync(It.IsAny<CancellationToken>()))
                    .Returns(() => new ValueTask<AutoIdScanParameters>(
                        new AutoIdScanParameters { CodeType = "RAW:BYTES", CodeTypes = [0] }));
            }
            IAutoIdReaderBuilder builder = fixture.Runtime.Reader(fixture.Name("Candidate"), provider.Object);
            if (parameters)
            {
                builder.WithRuntimeParameters();
            }
            await using AutoIdReader reader = await builder.BuildAsync().ConfigureAwait(false);
            bool expected = synchronous && asynchronous && parameters;
            Assert.That(AutoIdProfilesOf(fixture).Contains(AutoIdProfiles.FullServer), Is.EqualTo(expected));
            Assert.That(fixture.Manager.ServerProfiles.ToArray()!.Contains(AutoIdProfiles.FullServer),
                Is.EqualTo(expected));
            Assert.That(reader.Device.ScanActive, Is.Null, "Table 82 makes variable scan control optional");
        }

        /// <summary>
        /// Does not combine parameter and method capabilities from different readers into a Full facet.
        /// </summary>
        [Test]
        public async Task FullFacetCannotCombineCapabilitiesOfSeparateReadersAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using AutoIdReader scans = await fixture.Runtime.Reader(
                fixture.Name("Scans"), new InMemoryRfidReaderProvider())
                .OwnsProvider().BuildAsync().ConfigureAwait(false);
            await using AutoIdReader parameters = await fixture.Runtime.Reader(
                fixture.Name("Parameters"), new AutoIdParameterControlTests.ParameterProvider())
                .WithRuntimeParameters().BuildAsync().ConfigureAwait(false);
            Assert.That(AutoIdProfilesOf(fixture), Does.Not.Contain(AutoIdProfiles.FullServer));
        }

        /// <summary>
        /// Advertises Full for the in-memory RFID provider when parameters are enabled.
        /// </summary>
        [Test]
        public async Task ParameterEnabledRfidReaderAdvertisesFullFacetAsync()
        {
            await using var fixture = new AutoIdManagerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using AutoIdReader reader = await fixture.Runtime.Reader(
                fixture.Name("RfidFull"), new InMemoryRfidReaderProvider())
                .OwnsProvider().WithRuntimeParameters(true).BuildAsync().ConfigureAwait(false);
            Assert.That(AutoIdProfilesOf(fixture), Does.Contain(AutoIdProfiles.FullServer));
            Assert.That(fixture.Manager.ServerProfiles.ToArray(), Does.Contain(AutoIdProfiles.FullServer));
        }

        private static string[] AutoIdProfilesOf(AutoIdManagerFixture fixture)
        {
            return fixture.Runtime.ServerProfiles.ToArray()!;
        }

        private static string[] UnitsOf(AutoIdManagerFixture fixture, ushort ns)
        {
            QualifiedName[] units = fixture.Runtime.ConformanceUnits.ToArray()!;
            Assert.That(units.All(unit => unit.NamespaceIndex == ns), Is.True, "units live in the AutoID namespace");
            return [.. units.Select(unit => unit.Name!)];
        }
    }
}
