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

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Deployment monitoring requirements evaluated against actual configured server quotas.
    /// These do not configure or increase quotas. Defaults represent one subscription in one session.
    /// </summary>
    public sealed record MachineToolMonitoringRequirements
    {
        /// <summary>
        /// Gets the required session count, from one through 1024.
        /// </summary>
        public uint Sessions { get; init; } = 1;

        /// <summary>
        /// Gets the required subscriptions per session, from one through 1024.
        /// </summary>
        public uint SubscriptionsPerSession { get; init; } = 1;

        /// <summary>
        /// Rejects nonpositive or unbounded deployment requirements.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Validate()
        {
            if (Sessions is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(Sessions));
            }
            if (SubscriptionsPerSession is < 1 or > 1024)
            {
                throw new ArgumentOutOfRangeException(nameof(SubscriptionsPerSession));
            }
        }
    }

    /// <summary>
    /// Describes the actual monitorable graph, configured quotas, and resulting section 14 monitoring claims.
    /// Zero monitored-item quotas mean no configured item limit, not an invented finite capacity.
    /// </summary>
    /// <param name="PotentialMonitoredItems">All actual exposed variables and event notifiers.</param>
    /// <param name="MaximumSessions">The configured server session ceiling.</param>
    /// <param name="MaximumSubscriptions">The configured server-wide subscription ceiling.</param>
    /// <param name="MaximumSubscriptionsPerSession">The published per-session ceiling.</param>
    /// <param name="MaximumMonitoredItems">The server-wide item ceiling; zero means no imposed limit.</param>
    /// <param name="MaximumMonitoredItemsPerSubscription">The per-subscription ceiling; zero means no limit.</param>
    /// <param name="RequiredSessions">The declared deployment session requirement.</param>
    /// <param name="RequiredSubscriptionsPerSession">The declared per-session subscription requirement.</param>
    /// <param name="SupportsMinimum">All actual items and at least 20 items fit one subscription and session.</param>
    /// <param name="SupportsAll">All items fit half, rounded up, of both declared deployment requirements.</param>
    public sealed record MachineToolMonitoringCapacity(
        uint PotentialMonitoredItems, uint MaximumSessions, uint MaximumSubscriptions,
        uint MaximumSubscriptionsPerSession, uint MaximumMonitoredItems, uint MaximumMonitoredItemsPerSubscription,
        uint RequiredSessions, uint RequiredSubscriptionsPerSession, bool SupportsMinimum, bool SupportsAll);

    /// <summary>
    /// Describes supported behavior for one actually published machine, not its package or namespace.
    /// Contains MachineTool claims only; WMTP and GMS have independent conformance contributions.
    /// </summary>
    /// <param name="MachineId">The actual machine instance.</param>
    /// <param name="ConformanceUnits">Literal released OPC 40501-1 section 14.1 CU names.</param>
    /// <param name="ServerProfiles">Exact released Table 121 profile URIs whose prerequisites are satisfied.</param>
    /// <param name="Monitoring">Actual graph and quota evidence for monitoring claims.</param>
    public sealed record MachineToolCapabilities(
        NodeId MachineId, ArrayOf<QualifiedName> ConformanceUnits,
        ArrayOf<string> ServerProfiles, MachineToolMonitoringCapacity Monitoring);

    /// <summary>
    /// Reads an optional application-owned capability declaration while preserving its original sample metadata.
    /// A server without the declaration returns DeclarationAvailable=false, not fabricated conformance.
    /// </summary>
    /// <param name="DeclarationAvailable">Whether the actual machine exposes the complete runtime declaration.</param>
    /// <param name="Capabilities">The decoded declaration, null when absent.</param>
    /// <param name="Samples">Raw samples in MachineToolCapabilityNames.Fields order.</param>
    public sealed record MachineToolCapabilityReadSnapshot(
        bool DeclarationAvailable, MachineToolCapabilities? Capabilities, ArrayOf<DataValue> Samples);

    /// <summary>
    /// Identifies this library's read-only instance declaration, not new standard MachineTool nodes or methods.
    /// All declaration NodeIds remain application-owned.
    /// </summary>
    public static class MachineToolCapabilityNames
    {
        /// <summary>The library-owned extension namespace, deliberately distinct from the released standard.</summary>
        public const string NamespaceUri = "urn:opcfoundation:netstandard:machinetool:capabilities";

        /// <summary>The application-qualified declaration object's browse name.</summary>
        public const string Capabilities = "Capabilities";

        /// <summary>
        /// Gets the fixed version-one declaration field order used by the native OPC UA read workflow.
        /// </summary>
        public static ArrayOf<string> Fields =>
        [
            "ConformanceUnits", "ServerProfiles", "PotentialMonitoredItems", "MaximumSessions",
            "MaximumSubscriptions", "MaximumSubscriptionsPerSession", "MaximumMonitoredItems",
            "MaximumMonitoredItemsPerSubscription", "RequiredSessions", "RequiredSubscriptionsPerSession",
            "SupportsMinimum", "SupportsAll"
        ];
    }
}
