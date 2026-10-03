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

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;
using Opc.Ua.Server;

namespace Opc.Ua.Di.Tests
{
    /// <summary>
    /// Verifies that the DI node manager advertises only the conformance
    /// units it meets, and a facet only with all its mandatory units.
    /// </summary>
    [TestFixture]
    [Category("DI")]
    [Category("Compliance")]
    public sealed class DiNodeManagerConformanceTests
    {
        private const string DeviceIntegrationHost =
            "http://opcfoundation.org/UA-Profile/DI/Server/DeviceIntegrationHost";

        private const string SoftwareUpdateBase =
            "http://opcfoundation.org/UA-Profile/DI/Server/SoftwareUpdateBase";

        private const string FileSystemLoading =
            "http://opcfoundation.org/UA-Profile/DI/Server/FileSystemLoading";

        private const string DirectLoading =
            "http://opcfoundation.org/UA-Profile/DI/Server/DirectLoading";

        private const string CachedLoading =
            "http://opcfoundation.org/UA-Profile/DI/Server/CachedLoading";

        /// <summary>
        /// The mandatory units of the DI 1.05 server facets in the OPC
        /// Foundation profile database, those of included facets folded in.
        /// </summary>
        private static readonly Dictionary<string, string[]> s_mandatoryUnits = new()
        {
            [DeviceIntegrationHost] = ["DI DeviceTopology", "DI Offline"],
            ["http://opcfoundation.org/UA-Profile/DI/Server/Locking"] = ["DI Locking"],
            [SoftwareUpdateBase] = ["DI SU Software Update"],
            [FileSystemLoading] =
            [
                "DI SU Software Update",
                "DI SU FileSystem Loading",
                "DI SU Installation for File System"
            ],
            [DirectLoading] = ["DI SU Software Update", "DI SU DirectLoading", "DI SU UpdateStatus"],
            [CachedLoading] =
            [
                "DI SU Software Update",
                "DI SU CachedLoading",
                "DI SU Installation for Cached Loading",
                "DI SU UpdateStatus"
            ]
        };

        private DiServerFixture m_fixture = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new DiServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        [Test]
        public void DeviceSetAloneDoesNotClaimTheOfflineUnitOrTheHostFacet()
        {
            string[] units = Units();

            Assert.That(units, Does.Contain("DI DeviceTopology"));
            Assert.That(units, Does.Not.Contain("DI Offline"));
            Assert.That(m_fixture.Manager.ServerProfiles.ToList(), Does.Not.Contain(DeviceIntegrationHost));
        }

        [TestCase(SoftwareLoadingMode.Package)]
        [TestCase(SoftwareLoadingMode.Direct)]
        [TestCase(SoftwareLoadingMode.Cached)]
        public async Task SoftwareUpdateClaimsOnlyWhatItBuildsAsync(SoftwareLoadingMode mode)
        {
            await CreateSoftwareUpdateAsync("Device" + mode, mode).ConfigureAwait(false);

            string[] units = Units();
            Assert.That(units, Does.Contain("DI SU Software Update"));
            Assert.That(units, Does.Contain("DI SU PrepareForUpdate"));
            Assert.That(units, Does.Contain("DI SU Resume Update"));
            Assert.That(units, Does.Not.Contain("DI SU UpdateStatus"));
            Assert.That(units, Does.Not.Contain("DI SU FileSystem Loading"));
            Assert.That(units, Does.Not.Contain("DI SU Installation for File System"));
            Assert.That(units, Does.Not.Contain("DI SU Installation for Cached Loading"));
            Assert.That(units.Contains("DI SU DirectLoading"), Is.EqualTo(mode == SoftwareLoadingMode.Direct));
            Assert.That(units.Contains("DI SU CachedLoading"), Is.EqualTo(mode == SoftwareLoadingMode.Cached));

            List<string> profiles = m_fixture.Manager.ServerProfiles.ToList();
            Assert.That(profiles, Does.Contain(SoftwareUpdateBase));
            Assert.That(profiles, Does.Not.Contain(FileSystemLoading));
            Assert.That(profiles, Does.Not.Contain(DirectLoading));
            Assert.That(profiles, Does.Not.Contain(CachedLoading));
        }

        [Test]
        public async Task EveryAdvertisedFacetCarriesItsMandatoryUnitsAsync()
        {
            await CreateSoftwareUpdateAsync("PackageDevice", SoftwareLoadingMode.Package).ConfigureAwait(false);
            await CreateSoftwareUpdateAsync("DirectDevice", SoftwareLoadingMode.Direct).ConfigureAwait(false);
            await CreateSoftwareUpdateAsync("CachedDevice", SoftwareLoadingMode.Cached).ConfigureAwait(false);

            string[] units = Units();
            foreach (string profile in m_fixture.Manager.ServerProfiles)
            {
                Assert.That(s_mandatoryUnits.ContainsKey(profile), Is.True, profile);
                Assert.That(units, Is.SupersetOf(s_mandatoryUnits[profile]), profile);
            }
        }

        [Test]
        public async Task FacetFollowsAMandatoryUnitAnOverrideAddsAsync()
        {
            DiServerFixture fixture = new((server, configuration) =>
                new OfflineNodeManager(server, configuration));
            try
            {
                await fixture.StartAsync().ConfigureAwait(false);

                Assert.That(Units(fixture.Manager), Is.SupersetOf(s_mandatoryUnits[DeviceIntegrationHost]));
                Assert.That(
                    fixture.Manager.ServerProfiles.ToList(),
                    Does.Contain(DeviceIntegrationHost),
                    "The facet follows the units the override advertises.");
            }
            finally
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
        }

        private string[] Units()
        {
            return Units(m_fixture.Manager);
        }

        private static string[] Units(DiNodeManager manager)
        {
            return [.. manager.ConformanceUnits.ToList().Select(unit => unit.Name ?? string.Empty)];
        }

        private async Task CreateSoftwareUpdateAsync(string deviceName, SoftwareLoadingMode mode)
        {
            IDeviceBuilder<DeviceState> device = await m_fixture.Manager
                .CreateDeviceAsync(new QualifiedName(deviceName, m_fixture.Manager.DiNamespaceIndex))
                .ConfigureAwait(false);
            device.WithSoftwareUpdate(new MemoryPackageStore(), su =>
            {
                switch (mode)
                {
                    case SoftwareLoadingMode.Direct:
                        su.UseDirectLoading();
                        break;
                    case SoftwareLoadingMode.Cached:
                        su.UseCachedLoading();
                        break;
                    default:
                        su.UsePackageLoading();
                        break;
                }
            });
        }

        /// <summary>
        /// A companion manager that builds the offline representation the
        /// DI Offline unit asks for and so appends that unit to the base set.
        /// </summary>
        private sealed class OfflineNodeManager : DiNodeManager
        {
            /// <summary>
            /// Creates the manager for <paramref name="server"/>.
            /// </summary>
            public OfflineNodeManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            /// <inheritdoc/>
            public override ArrayOf<QualifiedName> ConformanceUnits
            {
                get
                {
                    var units = new List<QualifiedName>(base.ConformanceUnits.ToList())
                    {
                        new("DI Offline")
                    };
                    return units.ToArrayOf();
                }
            }
        }
    }
}
