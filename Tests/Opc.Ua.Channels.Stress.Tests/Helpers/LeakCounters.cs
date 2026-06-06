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
using System.Globalization;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Channels.Stress.Tests.Helpers
{
    /// <summary>
    /// Captures channel-manager and certificate leak counters for stress tests.
    /// </summary>
    public static class LeakCounters
    {
        /// <summary>
        /// Captures the current leak-counter snapshot.
        /// </summary>
        /// <param name="manager">The channel manager to inspect.</param>
        /// <returns>The captured snapshot.</returns>
        public static Snapshot Capture(IClientChannelManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            int activeEntries = 0;
            int totalRefcount = 0;
            int totalParticipants = 0;
            foreach (ManagedChannelDiagnostic diagnostic in manager.GetChannelDiagnostics())
            {
                activeEntries++;
                totalRefcount += diagnostic.Refcount;
                totalParticipants += diagnostic.ParticipantCount;
            }

            return new Snapshot(
                activeEntries,
                totalRefcount,
                totalParticipants,
                Certificate.InstancesCreated,
                Certificate.InstancesDisposed);
        }

        /// <summary>
        /// Asserts that the after snapshot did not leak entries, references, participants or certificates.
        /// </summary>
        /// <param name="before">The baseline snapshot.</param>
        /// <param name="after">The snapshot captured after the test scope.</param>
        /// <param name="scope">The diagnostic scope name.</param>
        /// <param name="tolerance">Allowed channel-manager entry/refcount/participant growth.</param>
        public static void AssertNoLeaks(
            Snapshot before,
            Snapshot after,
            string scope,
            int tolerance = 0)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }
            if (after == null)
            {
                throw new ArgumentNullException(nameof(after));
            }
            if (scope == null)
            {
                throw new ArgumentNullException(nameof(scope));
            }
            if (tolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            }

            long maxActiveEntries = (long)before.ActiveEntries + tolerance;
            long maxTotalRefcount = (long)before.TotalRefcount + tolerance;
            long maxTotalParticipants = (long)before.TotalParticipants + tolerance;
            long beforeLeakedCertificates = before.CertificatesCreated - before.CertificatesDisposed;
            long afterLeakedCertificates = after.CertificatesCreated - after.CertificatesDisposed;
            Assert.Multiple(() =>
            {
                Assert.That(
                    after.ActiveEntries,
                    Is.LessThanOrEqualTo(maxActiveEntries),
                    CreateMessage(scope, "active entries", before.ActiveEntries, after.ActiveEntries, tolerance));
                Assert.That(
                    after.TotalRefcount,
                    Is.LessThanOrEqualTo(maxTotalRefcount),
                    CreateMessage(scope, "total refcount", before.TotalRefcount, after.TotalRefcount, tolerance));
                Assert.That(
                    after.TotalParticipants,
                    Is.LessThanOrEqualTo(maxTotalParticipants),
                    CreateMessage(scope, "total participants", before.TotalParticipants, after.TotalParticipants, tolerance));
                Assert.That(
                    afterLeakedCertificates,
                    Is.LessThanOrEqualTo(beforeLeakedCertificates + tolerance),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{scope}: certificate leak count grew from {beforeLeakedCertificates} " +
                        $"to {afterLeakedCertificates} (tolerance={tolerance}). " +
                        $"Created={after.CertificatesCreated}, " +
                        $"Disposed={after.CertificatesDisposed}."));
            });
        }

        /// <summary>
        /// A point-in-time leak-counter snapshot.
        /// </summary>
        /// <param name="ActiveEntries">The number of active diagnostics entries.</param>
        /// <param name="TotalRefcount">The sum of diagnostics refcounts.</param>
        /// <param name="TotalParticipants">The sum of diagnostics participant counts.</param>
        /// <param name="CertificatesCreated">The total certificate instances created.</param>
        /// <param name="CertificatesDisposed">The total certificate instances disposed.</param>
        public sealed record Snapshot(
            int ActiveEntries,
            int TotalRefcount,
            int TotalParticipants,
            long CertificatesCreated,
            long CertificatesDisposed);

        private static string CreateMessage(
            string scope,
            string counterName,
            int before,
            int after,
            int tolerance)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{scope}: {counterName} grew from {before} to {after} " +
                $"with tolerance {tolerance}.");
        }
    }
}
