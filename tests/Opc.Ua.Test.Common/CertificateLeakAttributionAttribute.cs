/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Tests
{
    /// <summary>
    /// Records certificate counter deltas for each test fixture.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class CertificateLeakAttributionAttribute : Attribute, ITestAction
    {
        /// <inheritdoc/>
        public ActionTargets Targets => ActionTargets.Suite;

        /// <inheritdoc/>
        public void BeforeTest(ITest test)
        {
            s_baseline.Value = new Baseline(
                Certificate.InstancesCreated,
                Certificate.InstancesDisposed,
                test.FullName,
                s_baseline.Value);
            Certificate.LeakTrackingScope = test.FullName;
        }

        /// <inheritdoc/>
        public void AfterTest(ITest test)
        {
            if (s_baseline.Value is not Baseline baseline)
            {
                return;
            }

            s_baseline.Value = baseline.Parent;
            Certificate.LeakTrackingScope = baseline.Parent?.FixtureName;
            CertificateLeakAttribution.Record(
                baseline.FixtureName,
                Certificate.InstancesCreated - baseline.Created,
                Certificate.InstancesDisposed - baseline.Disposed);
        }

        private sealed class Baseline
        {
            public Baseline(
                long created,
                long disposed,
                string fixtureName,
                Baseline parent)
            {
                Created = created;
                Disposed = disposed;
                FixtureName = fixtureName;
                Parent = parent;
            }

            public long Created { get; }

            public long Disposed { get; }

            public string FixtureName { get; }

            public Baseline Parent { get; }
        }

        private static readonly AsyncLocal<Baseline> s_baseline = new();
    }

    /// <summary>
    /// Aggregates per-fixture certificate activity for leak diagnostics.
    /// </summary>
    public static class CertificateLeakAttribution
    {
        /// <summary>
        /// Builds the per-fixture activity summary appended to a leak failure.
        /// </summary>
        internal static string BuildSummary()
        {
            var entries = s_fixtureActivity
                .Select(entry => new
                {
                    entry.Key,
                    entry.Value.Created,
                    entry.Value.Disposed,
                    Net = entry.Value.Created - entry.Value.Disposed
                })
                .Where(entry => entry.Net != 0)
                .OrderByDescending(entry => entry.Net)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .ToArray();

            if (entries.Length == 0)
            {
                return string.Empty;
            }

            var summary = new StringBuilder();
            summary.AppendLine(
                "Overlapping per-fixture counter windows (diagnostic hints only):");
            foreach (var entry in entries)
            {
                summary.AppendFormat(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "  net={0}, created={1}, disposed={2}: {3}",
                    entry.Net,
                    entry.Created,
                    entry.Disposed,
                    entry.Key);
                summary.AppendLine();
            }
            return summary.ToString();
        }

        /// <summary>
        /// Clears recorded fixture activity.
        /// </summary>
        internal static void Reset()
        {
            s_fixtureActivity.Clear();
        }

        /// <summary>
        /// Records one fixture's certificate counter deltas.
        /// </summary>
        internal static void Record(string fixtureName, long created, long disposed)
        {
            if (created == 0 && disposed == 0)
            {
                return;
            }

            s_fixtureActivity.AddOrUpdate(
                fixtureName,
                (created, disposed),
                (_, previous) => (
                    previous.Created + created,
                    previous.Disposed + disposed));
        }

        private static readonly ConcurrentDictionary<string, (long Created, long Disposed)>
            s_fixtureActivity = new();
    }
}
