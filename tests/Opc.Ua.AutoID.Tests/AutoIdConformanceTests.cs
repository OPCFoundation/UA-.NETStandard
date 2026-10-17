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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.AutoID.Server;

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
                fixture.Name("Verifier"), new InMemoryOpticalVerifierProvider()).ConfigureAwait(false);
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
