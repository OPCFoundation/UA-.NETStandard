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

namespace Opc.Ua.Server.Historian
{
    /// <summary>
    /// Gates the static, spec-derived <see cref="HistoricalAccessProfileCatalog"/>
    /// inventory (<c>Opc.Ua.Core</c>) against what a concrete server-side
    /// <see cref="IHistorianProvider"/> actually implements and currently
    /// advertises, so a Historical Access Server profile is only ever
    /// reported as supported when <em>every</em> one of the following
    /// holds:
    /// <list type="number">
    ///   <item>the catalog entry's
    ///     <see cref="HistoricalAccessProfileDescriptor.IsAdvertised"/>
    ///     flag has been verified <see langword="true"/> — the baseline
    ///     catalog ships with this <see langword="false"/> for every
    ///     profile;</item>
    ///   <item>the supplied provider implements the capability
    ///     interface the profile's family requires (see
    ///     <see cref="IHistorianDataProvider"/>,
    ///     <see cref="IHistorianModifiedProvider"/>,
    ///     <see cref="IHistorianAnnotationProvider"/>,
    ///     <see cref="IHistorianStructuredDataProvider"/>,
    ///     <see cref="IHistorianEventProvider"/>); and</item>
    ///   <item>the provider's rolled-up <see cref="HistorianNodeCapabilities"/>
    ///     — typically obtained via
    ///     <see cref="IHistorianProvider.GetCapabilitiesAsync"/> called
    ///     with <see cref="NodeId.Null"/> — actually enables the
    ///     corresponding behavior for at least one registered node.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Gating on the interface alone is not sufficient: a provider can
    /// implement, say, <see cref="IHistorianDataProvider"/> while every
    /// registered node advertises read-only capabilities, in which case
    /// the "raw update" family must not be reported as supported even
    /// though the interface is present. Conversely, gating on flags alone
    /// is not sufficient either — <see cref="IHistorianAtTimeProvider"/>
    /// and <see cref="IHistorianProcessedProvider"/> are optional
    /// accelerants with a framework-level fallback through
    /// <see cref="IHistorianDataProvider"/>, so their families require
    /// only the base data-provider interface, not the specialised one.
    /// Capability gating is per <em>profile</em>, not just per family: a
    /// family with several sibling Server profiles (Events, raw updates)
    /// checks each profile's own specific flag rather than an
    /// across-the-board OR of every flag in the family, so e.g. an
    /// insert-only provider is never reported as satisfying the Delete
    /// profile. Events-family profiles are gated most strictly of all:
    /// see <see cref="SatisfiesEventFacet"/>. Because the provider-wide
    /// <see cref="NodeId.Null"/> capability rollup never carries
    /// per-notifier data (<c>EventTypes</c> / <c>MandatoryEventFields</c>
    /// are notifier-specific, not something a global union can
    /// meaningfully aggregate), Events-family profiles can only ever be
    /// reported supported when callers pass the capabilities of an actual
    /// historized notifier — obtained via
    /// <see cref="IHistorianProvider.GetCapabilitiesAsync"/> for that
    /// notifier's own <see cref="NodeId"/> — not the global rollup.
    /// </remarks>
    public static class HistorianProfileCatalog
    {
        /// <summary>
        /// Returns <see langword="true"/> when <paramref name="profile"/>
        /// may honestly be reported as supported by <paramref name="provider"/>
        /// given its rolled-up <paramref name="capabilities"/>.
        /// </summary>
        /// <param name="profile">The catalogued profile to evaluate.</param>
        /// <param name="provider">The concrete historian provider instance.</param>
        /// <param name="capabilities">
        /// The capability set to evaluate the profile against. For every
        /// family except Events, this is typically the provider-wide
        /// rollup returned by
        /// <see cref="IHistorianProvider.GetCapabilitiesAsync"/> called
        /// with <see cref="NodeId.Null"/>. Events-family profiles instead
        /// require the capabilities of one specific historized notifier
        /// (its own <see cref="NodeId"/>), because event conformance is
        /// inherently per-notifier — see <see cref="SatisfiesEventFacet"/>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="profile"/>, <paramref name="provider"/>, or
        /// <paramref name="capabilities"/> is <see langword="null"/>.
        /// </exception>
        public static bool IsSupportedByProvider(
            HistoricalAccessProfileDescriptor profile,
            IHistorianProvider provider,
            HistorianNodeCapabilities capabilities)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }

            if (profile.Side != HistoricalAccessProfileSide.Server || !profile.IsAdvertised)
            {
                return false;
            }

            return ImplementsRequiredInterface(profile.Family, provider) &&
                SatisfiesCapabilityFlags(profile, capabilities);
        }

        /// <summary>
        /// Returns every catalogued Server profile that
        /// <paramref name="provider"/> may honestly claim, given the
        /// supplied <paramref name="capabilities"/>. Pass a specific
        /// notifier's capabilities (not the <see cref="NodeId.Null"/>
        /// rollup) if the Events family should be considered — see
        /// <see cref="IsSupportedByProvider"/>.
        /// </summary>
        public static ArrayOf<HistoricalAccessProfileDescriptor> GetSupportedProfiles(
            IHistorianProvider provider,
            HistorianNodeCapabilities capabilities)
        {
            return GetSupportedProfiles(
                provider,
                capabilities,
                capabilities);
        }

        /// <summary>
        /// Returns every supported Server profile by combining provider-wide
        /// data capabilities with one concrete historical event notifier's
        /// capabilities.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="eventNotifierCapabilities"/> is <c>null</c>.</exception>
        public static ArrayOf<HistoricalAccessProfileDescriptor> GetSupportedProfiles(
            IHistorianProvider provider,
            HistorianNodeCapabilities capabilities,
            HistorianNodeCapabilities eventNotifierCapabilities)
        {
            if (eventNotifierCapabilities == null)
            {
                throw new ArgumentNullException(
                    nameof(eventNotifierCapabilities));
            }
            var results = new List<HistoricalAccessProfileDescriptor>();
            foreach (HistoricalAccessProfileDescriptor profile in
                HistoricalAccessProfileCatalog.GetProfiles(HistoricalAccessProfileSide.Server))
            {
                HistorianNodeCapabilities effectiveCapabilities =
                    profile.Family == HistoricalAccessProfileFamily.Events
                        ? eventNotifierCapabilities
                        : capabilities;
                if (IsSupportedByProvider(
                    profile,
                    provider,
                    effectiveCapabilities))
                {
                    results.Add(profile);
                }
            }
            return results.ToArrayOf();
        }

        /// <summary>
        /// Whether <paramref name="provider"/> implements the capability
        /// interface required by <paramref name="family"/>. AtTime and
        /// Aggregate only require the base
        /// <see cref="IHistorianDataProvider"/> because the framework
        /// falls back to raw reads plus interpolation / the central
        /// aggregate calculator when the specialised interface is absent.
        /// </summary>
        private static bool ImplementsRequiredInterface(
            HistoricalAccessProfileFamily family,
            IHistorianProvider provider)
        {
            return family switch
            {
                HistoricalAccessProfileFamily.RawAndServerTimestamp => provider is IHistorianDataProvider,
                HistoricalAccessProfileFamily.Modified => provider is IHistorianModifiedProvider,
                HistoricalAccessProfileFamily.AtTime => provider is IHistorianDataProvider,
                HistoricalAccessProfileFamily.Aggregate => provider is IHistorianDataProvider,
                HistoricalAccessProfileFamily.Annotation => provider is IHistorianAnnotationProvider,
                HistoricalAccessProfileFamily.Structured => provider is IHistorianStructuredDataProvider,
                HistoricalAccessProfileFamily.RawUpdates => provider is IHistorianDataProvider,
                HistoricalAccessProfileFamily.Events => provider is IHistorianEventProvider,
                _ => false
            };
        }

        /// <summary>
        /// Whether the rolled-up <paramref name="capabilities"/> actually
        /// enable the behavior <paramref name="profile"/> requires.
        /// </summary>
        /// <remarks>
        /// Events-family profiles route through
        /// <see cref="SatisfiesEventFacet"/> instead of a bare flag check:
        /// a specific update/read flag being <see langword="true"/> is not
        /// enough on its own to honestly claim event conformance — the
        /// capabilities must also describe an actual historized notifier
        /// (<see cref="HistorianNodeCapabilities.EventTypes"/>) with its
        /// mandatory BaseEventType field configuration asserted
        /// (<see cref="HistorianNodeCapabilities.MandatoryEventFields"/>).
        /// </remarks>
        private static bool SatisfiesCapabilityFlags(
            HistoricalAccessProfileDescriptor profile,
            HistorianNodeCapabilities capabilities)
        {
            return profile.ProfileUri switch
            {
                "http://opcfoundation.org/UA-Profile/Server/BaseHistoricalEvent2022" =>
                    SatisfiesEventFacet(capabilities, capabilities.ReadEventHistory),
                "http://opcfoundation.org/UA-Profile/Server/HistoricalModifiedData2022" =>
                    capabilities.ReadModifiedData,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalStructuredData2022" =>
                    capabilities.ReadStructuredData &&
                    capabilities.ReadModifiedStructuredData &&
                    capabilities.ReadAtTimeStructuredData &&
                    capabilities.InsertStructuredData &&
                    capabilities.ReplaceStructuredData &&
                    capabilities.UpdateStructuredData &&
                    capabilities.DeleteStructuredData,
                "http://opcfoundation.org/UA-Profile/Server/AggregateHistorical2022" =>
                    capabilities.ReadProcessedData,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalAnnotation2022" =>
                    capabilities.InsertAnnotation,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataAtTime2022" =>
                    capabilities.ReadAtTime,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataDelete2022" =>
                    capabilities.DeleteRaw &&
                    capabilities.DeleteAtTime,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataInsert2022" =>
                    capabilities.InsertData,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataReplace2022" =>
                    capabilities.ReplaceData,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalDataUpdate2022" =>
                    capabilities.UpdateData,
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventDelete2022" =>
                    SatisfiesEventFacet(capabilities, capabilities.DeleteEvent),
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventInsert2022" =>
                    SatisfiesEventFacet(capabilities, capabilities.InsertEvent),
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventReplace2022" =>
                    SatisfiesEventFacet(capabilities, capabilities.ReplaceEvent),
                "http://opcfoundation.org/UA-Profile/Server/HistoricalEventUpdate2022" =>
                    SatisfiesEventFacet(capabilities, capabilities.UpdateEvent),
                "http://opcfoundation.org/UA-Profile/Server/HistoricalRawData2022" =>
                    capabilities.ReadRawData,
                _ => false
            };
        }

        /// <summary>
        /// Whether an Events-family profile's specific read/update flag
        /// may be honestly trusted. A bare global flag (e.g.
        /// <see cref="HistorianNodeCapabilities.ReadEventHistory"/>) is
        /// not sufficient on its own: it can be <see langword="true"/>
        /// for a provider-wide rollup even when no node is actually a
        /// historized event notifier. This additionally requires:
        /// <list type="bullet">
        ///   <item>
        ///     <see cref="HistorianNodeCapabilities.EventTypes"/> is not
        ///     empty — there is a historized notifier that declares which
        ///     event types it historizes (the
        ///     <c>HistoricalEventConfigurationType</c>'s mandatory
        ///     <c>EventTypes</c> folder would otherwise install empty,
        ///     Part 11 §5.3); and
        ///   </item>
        ///   <item>
        ///     <see cref="HistorianNodeCapabilities.MandatoryEventFields"/>
        ///     is not empty — the notifier's mandatory BaseEventType field
        ///     configuration has actually been asserted (<c>EventType</c>
        ///     and <c>Time</c> are always mandatory under Part 11, but a
        ///     concrete per-notifier configuration must exist rather than
        ///     be assumed from the flag alone).
        ///   </item>
        /// </list>
        /// </summary>
        private static bool SatisfiesEventFacet(
            HistorianNodeCapabilities capabilities,
            bool specificFlag)
        {
            return specificFlag &&
                !capabilities.EventTypes.IsEmpty &&
                !capabilities.MandatoryEventFields.IsEmpty;
        }
    }
}
