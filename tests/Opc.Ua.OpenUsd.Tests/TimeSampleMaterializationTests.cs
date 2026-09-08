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
using Opc.Ua.OpenUsd.Scene;
using Opc.Ua.OpenUsd.Server.Scene;

namespace Opc.Ua.OpenUsd.Tests
{
    /// <summary>
    /// Address-space wiring of USD time samples (§7.1 step 3, §7.2, §9): a sampled attribute
    /// materializes its authored default as <c>Value</c>, becomes <c>Historizing</c> with the
    /// HistoryRead access bit set, and exposes its ordered samples through the materialization
    /// result's <see cref="UsdMaterializationResult.HistoricalAccessByPath"/> surface. The samples
    /// round-trip out through the exporter, and the §9 time-code → wall-clock mapping is explicit
    /// and fails closed when no epoch is declared.
    /// </summary>
    [TestFixture]
    public class TimeSampleMaterializationTests
    {
        // ---- materialization: default + historizing ------------------------------------

        [Test]
        public void SampledAttribute_MaterializesDefaultAsValue_AndHistorizes()
        {
            var attr = new UsdAttribute("angle", "double") { Value = UsdValue.From(5.0) };
            attr.TimeSamples[0.0] = UsdValue.From(0.0);
            attr.TimeSamples[24.0] = UsdValue.From(90.0);
            MaterializedScene ms = MaterializeAttr(attr);

            UsdAttributeState node = ms.Attr("/P.angle");
            // §7.1 step 3: the authored default is materialized as the live Value.
            Assert.That(node.BoxedValue(), Is.EqualTo(5.0));
            // The samples are the retained timeline, so the node historizes independently of Live.
            Assert.That(node.Historizing, Is.True);
            Assert.That(node.AccessLevel & Opc.Ua.AccessLevels.CurrentRead, Is.Not.Zero);
            Assert.That(node.AccessLevel & Opc.Ua.AccessLevels.HistoryRead, Is.Not.Zero);
        }

        [Test]
        public void SampledAttribute_WithoutDefault_HasNoValue_ButStillHistorizes()
        {
            var attr = new UsdAttribute("angle", "double");
            attr.TimeSamples[0.0] = UsdValue.From(0.0);
            attr.TimeSamples[24.0] = UsdValue.From(90.0);
            MaterializedScene ms = MaterializeAttr(attr);

            UsdAttributeState node = ms.Attr("/P.angle");
            // No authored default: Value stays unset (fail closed) but the samples still historize.
            Assert.That(node.BoxedValue(), Is.Null);
            Assert.That(node.Historizing, Is.True);
            Assert.That(node.AccessLevel & Opc.Ua.AccessLevels.HistoryRead, Is.Not.Zero);
        }

        [Test]
        public void HistoricalAccess_ExposesOrderedSamples_KeyedByComposedPath()
        {
            var attr = new UsdAttribute("angle", "double") { Value = UsdValue.From(5.0) };
            attr.TimeSamples[48.0] = UsdValue.From(180.0);
            attr.TimeSamples[0.0] = UsdValue.From(0.0);
            attr.TimeSamples[24.0] = UsdValue.From(90.0);
            MaterializedScene ms = MaterializeAttr(attr);

            Assert.That(ms.Result.HistoricalAccessByPath.ContainsKey("/P.angle"), Is.True);
            UsdHistoricalAccess ha = ms.Result.HistoricalAccessByPath["/P.angle"];
            Assert.That(ReferenceEquals(ha.Node, ms.Attr("/P.angle")), Is.True);
            Assert.That(ha.AttributePath, Is.EqualTo("/P.angle"));
            // Samples are ordered by ascending time code (USD composed sample order).
            Assert.That(
                ha.Samples.Select(s => s.TimeCode), Is.EqualTo(s_doubleValues1));
            Assert.That(
                ha.Samples.Select(s => s.Value),
                Is.EqualTo(new[] { UsdValue.From(0.0), UsdValue.From(90.0), UsdValue.From(180.0) }));
        }

        [Test]
        public void CoauthoredDefaultAndSamples_AreIndependent()
        {
            var attr = new UsdAttribute("angle", "double") { Value = UsdValue.From(42.0) };
            attr.TimeSamples[0.0] = UsdValue.From(7.0);
            MaterializedScene ms = MaterializeAttr(attr);

            // The default and the first sample differ, proving Value is the default, not sample[0].
            Assert.That(ms.Attr("/P.angle").BoxedValue(), Is.EqualTo(42.0));
            Assert.That(
                ms.Result.HistoricalAccessByPath["/P.angle"].Samples.Single().Value,
                Is.EqualTo(UsdValue.From(7.0)));
        }

        [Test]
        public void NegativeAndFractionalTimeCodes_ArePreserved()
        {
            var attr = new UsdAttribute("angle", "double");
            attr.TimeSamples[-12.0] = UsdValue.From(-1.0);
            attr.TimeSamples[0.5] = UsdValue.From(5.0);
            attr.TimeSamples[2.25] = UsdValue.From(7.5);
            MaterializedScene ms = MaterializeAttr(attr);

            IReadOnlyList<UsdTimeSample> samples =
                ms.Result.HistoricalAccessByPath["/P.angle"].Samples;
            Assert.That(samples.Select(s => s.TimeCode), Is.EqualTo(new[] { -12.0, 0.5, 2.25 }));
            UsdTestHelpers.AssertDouble(samples[0].Value, -1.0);
        }

        [Test]
        public void UnknownValueType_Samples_AreHistorized_AndPreservedOpaquely()
        {
            // §8.4: an unrecognized SdfValueTypeName is carried opaquely. Its samples must still be
            // recorded verbatim — the materializer never guesses at or drops an unknown value.
            var attr = new UsdAttribute("mystery", "customType");
            attr.TimeSamples[0.0] = UsdValue.FromString("opaque-a");
            attr.TimeSamples[10.0] = UsdValue.FromString("opaque-b");
            MaterializedScene ms = MaterializeAttr(attr);

            UsdAttributeState node = ms.Attr("/P.mystery");
            Assert.That(node.Historizing, Is.True);
            IReadOnlyList<UsdTimeSample> samples =
                ms.Result.HistoricalAccessByPath["/P.mystery"].Samples;
            Assert.That(
                samples.Select(s => s.Value),
                Is.EqualTo(new[] { UsdValue.FromString("opaque-a"), UsdValue.FromString("opaque-b") }));
        }

        // ---- regression: unsampled attributes are untouched ----------------------------

        [Test]
        public void UnsampledAttribute_IsNotHistorizing_AndAbsentFromHistoricalAccess()
        {
            var attr = new UsdAttribute("angle", "double") { Value = UsdValue.From(5.0) };
            MaterializedScene ms = MaterializeAttr(attr);

            Assert.That(ms.Attr("/P.angle").Historizing, Is.False);
            Assert.That(ms.Result.HistoricalAccessByPath, Is.Empty);
        }

        // ---- §9: explicit, fail-closed time-code -> wall-clock mapping ------------------

        [Test]
        public void ResolveUtc_WithoutEpoch_ReturnsNull()
        {
            var attr = new UsdAttribute("angle", "double");
            attr.TimeSamples[24.0] = UsdValue.From(90.0);
            // No epoch option and stage declares TimeCodesPerSecond: the timeline is Server-defined.
            MaterializedScene ms = MaterializeAttr(attr, tcps: 24.0, epochUtc: null);

            UsdHistoricalAccess ha = ms.Result.HistoricalAccessByPath["/P.angle"];
            Assert.That(ha.EpochUtc, Is.Null);
            Assert.That(ha.ResolveUtc(24.0), Is.Null, "no epoch means no wall-clock mapping (§9)");
        }

        [Test]
        public void ResolveUtc_WithEpochAndTimeCodesPerSecond_MapsToWallClock()
        {
            var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var attr = new UsdAttribute("angle", "double");
            attr.TimeSamples[48.0] = UsdValue.From(180.0);
            MaterializedScene ms = MaterializeAttr(attr, tcps: 24.0, epochUtc: epoch);

            UsdHistoricalAccess ha = ms.Result.HistoricalAccessByPath["/P.angle"];
            Assert.That(ha.EpochUtc, Is.EqualTo(epoch));
            Assert.That(ha.TimeCodesPerSecond, Is.EqualTo(24.0));
            // 48 time codes at 24 codes/second is 2 seconds after the epoch.
            Assert.That(ha.ResolveUtc(48.0), Is.EqualTo(epoch.AddSeconds(2.0)));
        }

        [Test]
        public void ResolveUtc_WithEpochButNoTimeCodesPerSecond_ReturnsNull()
        {
            var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var attr = new UsdAttribute("angle", "double");
            attr.TimeSamples[24.0] = UsdValue.From(90.0);
            // Epoch declared but the stage has no TimeCodesPerSecond: the rate is unknown, so the
            // mapping stays undefined rather than assuming a rate (fail closed).
            MaterializedScene ms = MaterializeAttr(attr, tcps: null, epochUtc: epoch);

            Assert.That(ms.Result.HistoricalAccessByPath["/P.angle"].ResolveUtc(24.0), Is.Null);
        }

        // ---- §7.2: samples recovered by the exporter -----------------------------------

        [Test]
        public void SampledAttribute_RoundTripsThroughExport()
        {
            var attr = new UsdAttribute("angle", "double") { Value = UsdValue.From(5.0) };
            attr.TimeSamples[-6.0] = UsdValue.From(-1.0);
            attr.TimeSamples[0.0] = UsdValue.From(0.0);
            attr.TimeSamples[24.0] = UsdValue.From(90.0);
            MaterializedScene ms = MaterializeAttr(attr);

            UsdStage exported = ms.Context.ExportUsdStage(ms.Result);
            UsdAttribute exportedAttr = exported.Find("/P")!.Attributes.Single();

            UsdTestHelpers.AssertDouble(exportedAttr.Value, 5.0);
            Assert.That(exportedAttr.TimeSamples.Keys, Is.EqualTo(new[] { -6.0, 0.0, 24.0 }));
            UsdTestHelpers.AssertDouble(exportedAttr.TimeSamples[24.0], 90.0);
        }

        [Test]
        public void Exporter_WithoutSampleMap_KeepsDefault_ButOmitsSamples()
        {
            // Exporting from the stage node alone (no samples map) cannot recover the samples —
            // they live on the result, not the node — but the authored default still round-trips.
            var attr = new UsdAttribute("angle", "double") { Value = UsdValue.From(5.0) };
            attr.TimeSamples[0.0] = UsdValue.From(0.0);
            MaterializedScene ms = MaterializeAttr(attr);

            UsdStage exported = ms.Context.ExportUsdStage(ms.Stage);
            UsdAttribute exportedAttr = exported.Find("/P")!.Attributes.Single();

            UsdTestHelpers.AssertDouble(exportedAttr.Value, 5.0);
            Assert.That(exportedAttr.TimeSamples, Is.Empty);
        }

        // ---- helpers -------------------------------------------------------------------

        private static MaterializedScene MaterializeAttr(
            UsdAttribute attribute, double? tcps = null, DateTime? epochUtc = null)
        {
            var stage = new UsdStage("TS") { DefaultPrim = "P", TimeCodesPerSecond = tcps };
            var prim = new UsdPrim("P", "Xform");
            prim.Attributes.Add(attribute);
            stage.AddRootPrim(prim);

            UsdMaterializationOptions? options =
                epochUtc == null ? null : new UsdMaterializationOptions { TimeCodeEpochUtc = epochUtc };
            return MaterializationHarness.Materialize(stage, options);
        }

        private static readonly double[] s_doubleValues1 = [0.0, 24.0, 48.0];
    }
}
