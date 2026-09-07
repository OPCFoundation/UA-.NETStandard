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
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Conformance
{
    /// <summary>
    /// Validates the machine-readable inventory of every released UACore
    /// 1.05 Historical Access profile in
    /// <see cref="HistoricalAccessProfileCatalog"/>.
    /// </summary>
    [TestFixture]
    [Category("Conformance")]
    [Parallelizable]
    public class HistoricalAccessProfileCatalogTests
    {
        /// <summary>
        /// Expected number of catalogued profiles per functional family,
        /// derived from OPC UA Part 11 (37 total: 15 Server, 22 Client).
        /// </summary>
        private static readonly Dictionary<HistoricalAccessProfileFamily, int> s_expectedFamilyCounts = new()
        {
            [HistoricalAccessProfileFamily.RawAndServerTimestamp] = 3,
            [HistoricalAccessProfileFamily.Modified] = 2,
            [HistoricalAccessProfileFamily.AtTime] = 2,
            [HistoricalAccessProfileFamily.Aggregate] = 2,
            [HistoricalAccessProfileFamily.Annotation] = 2,
            [HistoricalAccessProfileFamily.Structured] = 8,
            [HistoricalAccessProfileFamily.RawUpdates] = 8,
            [HistoricalAccessProfileFamily.Events] = 10
        };

        /// <summary>
        /// Verifies that the historical-access catalog contains 37 profiles.
        /// </summary>
        [Test]
        public void CatalogContainsThirtySevenProfiles()
        {
            Assert.That(HistoricalAccessProfileCatalog.AllProfiles.Count, Is.EqualTo(37));
        }

        /// <summary>
        /// Verifies that the historical-access catalog contains 15 server profiles.
        /// </summary>
        [Test]
        public void CatalogContainsFifteenServerProfiles()
        {
            ArrayOf<HistoricalAccessProfileDescriptor> serverProfiles =
                HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileSide.Server);

            Assert.That(serverProfiles.Count, Is.EqualTo(15));
        }

        /// <summary>
        /// Verifies that the historical-access catalog contains 22 client profiles.
        /// </summary>
        [Test]
        public void CatalogContainsTwentyTwoClientProfiles()
        {
            ArrayOf<HistoricalAccessProfileDescriptor> clientProfiles =
                HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileSide.Client);

            Assert.That(clientProfiles.Count, Is.EqualTo(22));
        }

        /// <summary>
        /// Verifies that server and client profile counts account for the entire catalog.
        /// </summary>
        [Test]
        public void ServerAndClientProfileCountsSumToCatalogTotal()
        {
            int server = HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileSide.Server).Count;
            int client = HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileSide.Client).Count;

            Assert.That(server + client, Is.EqualTo(HistoricalAccessProfileCatalog.AllProfiles.Count));
        }

        /// <summary>
        /// Verifies that every historical-access profile URI is unique.
        /// </summary>
        [Test]
        public void AllProfileUrisAreUnique()
        {
            var uris = new HashSet<string>(StringComparer.Ordinal);
            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                Assert.That(uris.Add(profile.ProfileUri), Is.True, $"Duplicate profile URI: {profile.ProfileUri}");
            }
        }

        /// <summary>
        /// Verifies that every historical-access profile name is unique.
        /// </summary>
        [Test]
        public void AllProfileNamesAreUnique()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                Assert.That(names.Add(profile.Name), Is.True, $"Duplicate profile name: {profile.Name}");
            }
        }

        /// <summary>
        /// Verifies that each profile name identifies historical functionality.
        /// </summary>
        [Test]
        public void EveryProfileNameContainsHistorical()
        {
            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                Assert.That(profile.Name, Does.Contain("Historical"), $"Profile name missing 'Historical': {profile.Name}");
            }
        }

        /// <summary>
        /// Verifies that each profile URI agrees with its declared client or server side.
        /// </summary>
        [Test]
        public void EveryProfileUriMatchesItsDeclaredSide()
        {
            const string serverPrefix = "http://opcfoundation.org/UA-Profile/Server/";
            const string clientPrefix = "http://opcfoundation.org/UA-Profile/Client/";

            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                string expectedPrefix = profile.Side == HistoricalAccessProfileSide.Server ? serverPrefix : clientPrefix;
                Assert.That(
                    profile.ProfileUri,
                    Does.StartWith(expectedPrefix),
                    $"Profile URI does not match its declared side: {profile.Name}");
            }
        }

        /// <summary>
        /// Verifies that every historical-access profile declares at least one mandatory conformance unit.
        /// </summary>
        [Test]
        public void EveryProfileHasAtLeastOneMandatoryConformanceUnit()
        {
            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                Assert.That(
                    profile.MandatoryConformanceUnits.Count,
                    Is.GreaterThan(0),
                    $"Profile has no mandatory conformance units: {profile.Name}");
            }
        }

        /// <summary>
        /// Verifies that mandatory conformance units have nonempty names.
        /// </summary>
        [Test]
        public void MandatoryConformanceUnitNamesAreNonEmpty()
        {
            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                foreach (string unit in profile.MandatoryConformanceUnits)
                {
                    Assert.That(string.IsNullOrWhiteSpace(unit), Is.False, $"Empty conformance unit name on {profile.Name}");
                }
            }
        }

        /// <summary>
        /// Verifies that the catalog represents all eight historical-access profile families.
        /// </summary>
        [Test]
        public void AllEightFamiliesAreRepresented()
        {
            var families = new HashSet<HistoricalAccessProfileFamily>();
            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                families.Add(profile.Family);
            }

#if NET8_0_OR_GREATER
            int familyCount =
                Enum.GetValues<HistoricalAccessProfileFamily>().Length;
#else
            int familyCount = Enum.GetValues(
                typeof(HistoricalAccessProfileFamily)).Length;
#endif
            Assert.That(families, Has.Count.EqualTo(familyCount));
            Assert.That(families, Has.Count.EqualTo(8));
        }

        /// <summary>
        /// Verifies that historical-access profile counts match the expected distribution across families.
        /// </summary>
        [Test]
        public void FamilyCountsMatchExpectedDistribution()
        {
            foreach (KeyValuePair<HistoricalAccessProfileFamily, int> expected in s_expectedFamilyCounts)
            {
                int actual = HistoricalAccessProfileCatalog.GetProfiles(expected.Key).Count;
                Assert.That(actual, Is.EqualTo(expected.Value), $"Unexpected count for family {expected.Key}");
            }
        }

        /// <summary>
        /// Verifies that family counts account for every profile in the catalog.
        /// </summary>
        [Test]
        public void FamilyCountsSumToCatalogTotal()
        {
            int sum = s_expectedFamilyCounts.Values.Sum();
            Assert.That(sum, Is.EqualTo(HistoricalAccessProfileCatalog.AllProfiles.Count));
        }

        /// <summary>
        /// Verifies that server profiles are verified and client profiles are not advertised as server claims.
        /// </summary>
        [Test]
        public void ServerProfilesAreVerifiedAndClientProfilesAreNotAdvertised()
        {
            foreach (HistoricalAccessProfileDescriptor profile in HistoricalAccessProfileCatalog.AllProfiles)
            {
                if (profile.Side ==
                    HistoricalAccessProfileSide.Server)
                {
                    Assert.That(
                        profile.IsAdvertised,
                        Is.True,
                        $"Server profile is not verified: {profile.Name}");
                    Assert.That(
                        profile.Prerequisite,
                        Is.Empty);
                }
                else
                {
                    Assert.That(
                        profile.IsAdvertised,
                        Is.False,
                        $"Client profile cannot be advertised by a Server: {profile.Name}");
                    Assert.That(
                        profile.Prerequisite,
                        Does.Contain("not published through a Server"));
                }
            }
        }

        /// <summary>
        /// Verifies that each historical-access profile links production, test, and sample evidence.
        /// </summary>
        [Test]
        public void EveryProfileHasProductionTestAndSampleEvidence()
        {
            Assert.That(
                HistoricalAccessProfileEvidenceCatalog.All.Count,
                Is.EqualTo(
                    HistoricalAccessProfileCatalog.AllProfiles.Count));
            var profileUris = new HashSet<string>(
                StringComparer.Ordinal);
            foreach (HistoricalAccessProfileEvidence evidence in
                HistoricalAccessProfileEvidenceCatalog.All)
            {
                Assert.That(
                    profileUris.Add(evidence.ProfileUri),
                    Is.True,
                    $"Duplicate profile evidence: {evidence.ProfileUri}");
                Assert.That(
                    evidence.ProductionModules,
                    Is.Not.Empty,
                    $"Missing production evidence: {evidence.ProfileUri}");
                Assert.That(
                    evidence.AutomatedTests,
                    Is.Not.Empty,
                    $"Missing test evidence: {evidence.ProfileUri}");
                Assert.That(
                    evidence.Samples,
                    Is.Not.Empty,
                    $"Missing sample evidence: {evidence.ProfileUri}");
                Assert.That(
                    HistoricalAccessProfileCatalog.TryGetProfile(
                        evidence.ProfileUri,
                        out _),
                    Is.True);
            }
        }

        /// <summary>
        /// Verifies that querying profiles by family excludes profiles from other families.
        /// </summary>
        [Test]
        public void GetProfilesByFamilyOnlyReturnsThatFamily()
        {
            ArrayOf<HistoricalAccessProfileDescriptor> aggregateProfiles =
                HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileFamily.Aggregate);

            Assert.That(aggregateProfiles.Count, Is.EqualTo(2));
            foreach (HistoricalAccessProfileDescriptor profile in aggregateProfiles)
            {
                Assert.That(profile.Family, Is.EqualTo(HistoricalAccessProfileFamily.Aggregate));
            }
        }

        /// <summary>
        /// Verifies that a known historical-access profile can be found by its URI.
        /// </summary>
        [Test]
        public void TryGetProfileFindsAKnownProfileByUri()
        {
            bool found = HistoricalAccessProfileCatalog.TryGetProfile(
                "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022",
                out HistoricalAccessProfileDescriptor descriptor);

            Assert.That(found, Is.True);
            Assert.That(descriptor, Is.Not.Null);
            Assert.That(descriptor.Name, Is.EqualTo("Historical Aggregate 2022 Server Facet"));
        }

        /// <summary>
        /// Verifies that lookup returns false for an unknown historical-access profile URI.
        /// </summary>
        [Test]
        public void TryGetProfileReturnsFalseForAnUnknownUri()
        {
            bool found = HistoricalAccessProfileCatalog.TryGetProfile(
                "http://opcfoundation.org/UA-Profile/Server/DoesNotExist",
                out HistoricalAccessProfileDescriptor descriptor);

            Assert.That(found, Is.False);
            Assert.That(descriptor, Is.Null);
        }
    }
}
