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

namespace Opc.Ua
{
    /// <summary>
    /// A machine-readable inventory of every released OPC UA Part 11
    /// (Historical Access) profile whose name contains "Historical" in
    /// the UACore 1.05 profile group — 15 Server facets and 22 Client
    /// facets, 37 in total.
    /// </summary>
    /// <remarks>
    /// Released Server facets are marked as verified for capability-gated
    /// advertisement. Client facets remain non-advertised because
    /// <c>ServerProfileArray</c> is a Server surface; their implementation
    /// evidence is tracked separately by
    /// <see cref="HistoricalAccessProfileEvidenceCatalog"/>.
    /// </remarks>
    public static class HistoricalAccessProfileCatalog
    {
        /// <summary>
        /// Reason recorded on every baseline entry, until verified
        /// otherwise.
        /// </summary>
        public const string PendingConformanceVerification =
            "Baseline inventory only: end-to-end coverage of every mandatory " +
            "conformance unit has not been verified yet, so the profile must not " +
            "be advertised until a conformance test run confirms it.";

        private const string ClientProfileNonAdvertisement =
            "Client implementation evidence is verified separately. Client facets " +
            "are not published through a Server's ServerProfileArray.";

        /// <summary>
        /// Every released UACore 1.05 Historical Access profile (facet),
        /// 37 entries in total (15 Server, 22 Client).
        /// </summary>
        public static ArrayOf<HistoricalAccessProfileDescriptor> AllProfiles { get; } =
            MarkVerifiedServerProfiles(
            [
            // Server facets (15) — http://opcfoundation.org/UA-Profile/Server/*
            new(
                "Base Historical Event 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Events,
                [
                    "Attribute Historical Read",
                    "Base Info History ReadEvents Capabilities",
                    "Historical Access Events"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Access Modified Data 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalModifiedData2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Modified,
                [
                    "Attribute Historical Read",
                    "Base Info History Read Capabilities",
                    "Base Info History ReadData Capabilities",
                    "Historical Access Modified Values"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Access Structured Data 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalStructuredData2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Structured,
                [
                    "Base Info History Read Capabilities",
                    "Base Info History ReadData Capabilities",
                    "Base Info History UpdateData Capabilities",
                    "Historical Access Structured Data Read Raw"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Aggregate 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Aggregate,
                [
                    "Aggregate Master Configuration",
                    "Attribute Historical Read",
                    "Base Info History Read Capabilities",
                    "Base Info History ReadData Capabilities",
                    "Historical Access Aggregates"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Annotation 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalAnnotation2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Annotation,
                [
                    "Attribute Historical Read",
                    "Attribute Historical Update",
                    "Base Info History Read Capabilities",
                    "Base Info History ReadData Capabilities",
                    "Base Info History UpdateData Capabilities",
                    "Historical Access Annotations"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data AtTime 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataAtTime2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.AtTime,
                [
                    "Attribute Historical Read",
                    "Base Info History Read Capabilities",
                    "Base Info History ReadData Capabilities",
                    "Historical Access Time Instance"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Delete 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataDelete2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.RawUpdates,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateData Capabilities",
                    "Historical Access Delete Value"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Insert 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.RawUpdates,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateData Capabilities",
                    "Historical Access Insert Value"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Replace 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataReplace2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.RawUpdates,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateData Capabilities",
                    "Historical Access Replace Value"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Update 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataUpdate2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.RawUpdates,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateData Capabilities",
                    "Historical Access Update Value"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Delete 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventDelete2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Events,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateEvents Capabilities",
                    "Historical Access Delete Event"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Insert 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventInsert2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Events,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateEvents Capabilities",
                    "Historical Access Insert Event"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Replace 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventReplace2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Events,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateEvents Capabilities",
                    "Historical Access Replace Event"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Update 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventUpdate2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.Events,
                [
                    "Attribute Historical Update",
                    "Base Info History UpdateEvents Capabilities",
                    "Historical Access Update Event"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Raw Data 2022 Server Facet",
                "http://opcfoundation.org/UA-Profile/Server/HistoricalRawData2022",
                HistoricalAccessProfileSide.Server,
                HistoricalAccessProfileFamily.RawAndServerTimestamp,
                [
                    "Attribute Historical Read",
                    "Base Info History Read Capabilities",
                    "Base Info History ReadData Capabilities",
                    "Historical Access Read Raw"
                ],
                false,
                PendingConformanceVerification),

            // Client facets (22) — http://opcfoundation.org/UA-Profile/Client/*
            new(
                "Historical Access Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalAccess",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.RawAndServerTimestamp,
                [
                    "Attribute Client Historical Read",
                    "Historical Access Client Browse",
                    "Historical Access Client Read Raw"
                ],
                false,
                PendingConformanceVerification),
            new(
                "Historical Access Client Server Timestamp Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalServerTimeStamp",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.RawAndServerTimestamp,
                ["Historical Access Client Server Timestamp"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Access Modified Data Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalAccessModifiedData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Modified,
                ["Historical Access Client Read Modified"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Aggregate Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalAccessAggregate",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Aggregate,
                ["Aggregate \u2013 Client Usage", "Historical Access Client Read Aggregates"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Annotation Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalAnnotation",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Annotation,
                ["Historical Access Client Annotations"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data AtTime Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalAccessAtTime",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.AtTime,
                ["Historical Access Client Time Instance"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Delete Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalDeleteData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.RawUpdates,
                ["Attribute Client Historical Updates", "Historical Access Client Data Delete"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Insert Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalInsertData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.RawUpdates,
                ["Attribute Client Historical Updates", "Historical Access Client Data Insert"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Replace Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalReplaceData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.RawUpdates,
                ["Attribute Client Historical Updates", "Historical Access Client Data Replace"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Data Update Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalUpdateData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.RawUpdates,
                ["Attribute Client Historical Updates", "Historical Access Client Data Update"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Delete Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalDeleteEvents",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Events,
                ["Attribute Client Historical Updates", "Historical Access Client Event Deletes"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Insert Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalInsertEvents",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Events,
                ["Attribute Client Historical Updates", "Historical Access Client Event Inserts"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Replace Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalReplaceEvents",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Events,
                ["Attribute Client Historical Updates", "Historical Access Client Event Replaces"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Event Update Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalUpdateEvents",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Events,
                ["Attribute Client Historical Updates", "Historical Access Client Event Updates"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Events Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalEvents",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Events,
                ["Attribute Client Historical Read", "Historical Access Client Read Events"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Structured Data Access Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalAccessStructuredData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Structured,
                ["Historical Access Client Structure Data Raw"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Structured Data AtTime Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalAtTimeStructuredData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Structured,
                ["Historical Access Client Structure Data Time Instance"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Structured Data Delete Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalDeleteStructuredData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Structured,
                ["Historical Access Client Structure Data Delete"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Structured Data Insert Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalInsertStructuredData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Structured,
                ["Historical Access Client Structure Data Insert"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Structured Data Modified Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalModifiedStructuredData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Structured,
                ["Historical Access Client Structure Data Read Modified"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Structured Data Replace Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalReplaceStructuredData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Structured,
                ["Historical Access Client Structure Data Replace"],
                false,
                PendingConformanceVerification),
            new(
                "Historical Structured Data Update Client Facet",
                "http://opcfoundation.org/UA-Profile/Client/HistoricalUpdateStructuredData",
                HistoricalAccessProfileSide.Client,
                HistoricalAccessProfileFamily.Structured,
                ["Historical Access Client Structure Data Update"],
                false,
                PendingConformanceVerification)
            ]);

        /// <summary>
        /// Returns every catalogued profile for the given side (Server
        /// or Client).
        /// </summary>
        public static ArrayOf<HistoricalAccessProfileDescriptor> GetProfiles(HistoricalAccessProfileSide side)
        {
            return Filter(profile => profile.Side == side);
        }

        /// <summary>
        /// Returns every catalogued profile belonging to the given
        /// functional family.
        /// </summary>
        public static ArrayOf<HistoricalAccessProfileDescriptor> GetProfiles(HistoricalAccessProfileFamily family)
        {
            return Filter(profile => profile.Family == family);
        }

        /// <summary>
        /// Looks up a catalogued profile by its profile URI.
        /// </summary>
        /// <param name="profileUri">The profile URI to look up.</param>
        /// <param name="descriptor">
        /// The matching descriptor, or <see langword="null"/> if none of
        /// the catalogued profiles has this URI.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if a matching profile was found.
        /// </returns>
        public static bool TryGetProfile(string profileUri, out HistoricalAccessProfileDescriptor? descriptor)
        {
            foreach (HistoricalAccessProfileDescriptor profile in AllProfiles)
            {
                if (string.Equals(profile.ProfileUri, profileUri, StringComparison.Ordinal))
                {
                    descriptor = profile;
                    return true;
                }
            }
            descriptor = null;
            return false;
        }

        private static ArrayOf<HistoricalAccessProfileDescriptor> Filter(
            Func<HistoricalAccessProfileDescriptor, bool> predicate)
        {
            var results = new List<HistoricalAccessProfileDescriptor>();
            foreach (HistoricalAccessProfileDescriptor profile in AllProfiles)
            {
                if (predicate(profile))
                {
                    results.Add(profile);
                }
            }
            return results.ToArrayOf();
        }

        private static ArrayOf<HistoricalAccessProfileDescriptor>
            MarkVerifiedServerProfiles(
                ArrayOf<HistoricalAccessProfileDescriptor> profiles)
        {
            var verified =
                new HistoricalAccessProfileDescriptor[profiles.Count];
            for (int i = 0; i < profiles.Count; i++)
            {
                HistoricalAccessProfileDescriptor profile = profiles[i];
                verified[i] = profile.Side ==
                    HistoricalAccessProfileSide.Server
                    ? profile with
                    {
                        IsAdvertised = true,
                        Prerequisite = string.Empty
                    }
                    : profile with
                    {
                        Prerequisite = ClientProfileNonAdvertisement
                    };
            }
            return verified;
        }
    }
}
