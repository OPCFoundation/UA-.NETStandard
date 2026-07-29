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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.OpenUsd.Client;
using OpenUsd;
using OpenUsd.Geom;

namespace Opc.Ua.OpenUsd.Connector.Viewer
{
    /// <summary>
    /// Authors connector values straight into a scheduler-owned USD stage, so a running
    /// viewport shows live OPC UA data instead of a file that has to be reloaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IUsdSink"/> is synchronous while the stage scheduler is asynchronous, so
    /// calls never touch the stage directly. A single background pump ticks at a fixed
    /// display rate and applies everything pending in one <c>EditAsync</c>.
    /// </para>
    /// <para>
    /// Numeric values are <em>interpolated</em> rather than stepped. A subscription
    /// delivers a new joint angle a few tens of times a second while the viewport draws
    /// far more often, so stepping makes the twin visibly stutter. Each numeric target
    /// instead moves from its previous value towards the newest one across roughly the
    /// interval at which samples are arriving, which decouples how smooth the twin looks
    /// from how fast the server publishes, at the cost of about one sample of lag.
    /// Interpolation is linear because the server already eases between its key poses;
    /// easing again here would distort the motion.
    /// </para>
    /// <para>
    /// Tokens, colours, composition and history samples are not interpolated - there is no
    /// meaningful value between "visible" and "invisible" - so they are applied on the
    /// next tick and then left alone.
    /// </para>
    /// <para>
    /// Structured <c>double3</c> transform ops cannot be authored directly, because the
    /// managed data API has no <c>double3</c> setter. They are instead accumulated per
    /// prim and written as a composed local transform. Components the server does not
    /// bind default to no rotation and unit scale, and translation is seeded from the
    /// prim's existing local transform so a rotation-only binding does not move the prim
    /// to the origin.
    /// </para>
    /// </remarks>
    internal sealed class UsdStageSink : IUsdSink, IAsyncDisposable
    {
        private const string TranslateOp = "xformOp:translate";
        private const string RotateOp = "xformOp:rotateXYZ";
        private const string ScaleOp = "xformOp:scale";

        /// <summary>
        /// Pump period, about 60 Hz.
        /// </summary>
        private static readonly TimeSpan s_tick = TimeSpan.FromMilliseconds(16);

        /// <summary>
        /// Shortest interpolation window, so a burst cannot cause a visible jump.
        /// </summary>
        private static readonly long s_minWindow = Stopwatch.Frequency / 50;

        /// <summary>
        /// Longest interpolation window, so a stalled feed settles instead of crawling.
        /// </summary>
        private static readonly long s_maxWindow = Stopwatch.Frequency / 2;

        private static readonly DateTime s_epoch =
            new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Targets already reported as unwritable, so a persistent type mismatch
        /// is logged once instead of on every frame.
        /// </summary>
        private static readonly ConcurrentDictionary<string, bool> s_rejectedTargets =
            new(StringComparer.Ordinal);

        private readonly UsdStageScheduler m_scheduler;
        private readonly Action<Exception> m_onError;
        private readonly Lock m_gate = new();
        private readonly CancellationTokenSource m_lifetime = new();
        private readonly Task m_pump;

        private readonly Dictionary<string, PrimComposition> m_prims =
            new(StringComparer.Ordinal);

        private readonly Dictionary<AttributeKey, Variant> m_discrete = [];
        private readonly Dictionary<TimeSampleKey, Variant> m_timeSamples = [];
        private readonly Dictionary<AttributeKey, ScalarTrack> m_scalars = [];

        private readonly Dictionary<string, TransformTracks> m_transforms =
            new(StringComparer.Ordinal);

        private int m_batchDepth;

        /// <summary>
        /// Creates a sink over <paramref name="scheduler"/>.
        /// </summary>
        /// <param name="scheduler">The scheduler that owns the rendered stage.</param>
        /// <param name="onError">Invoked when applying an update fails.</param>
        /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
        public UsdStageSink(UsdStageScheduler scheduler, Action<Exception> onError)
        {
            m_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            m_onError = onError ?? throw new ArgumentNullException(nameof(onError));
            m_pump = PumpAsync(m_lifetime.Token);
        }

        /// <inheritdoc/>
        public void SetAttribute(string primPath, string propertyName, Variant value)
        {
            if (!UsdNames.IsValidPrimPath(primPath) ||
                !UsdNames.IsValidPropertyName(propertyName))
            {
                return;
            }
            long now = Stopwatch.GetTimestamp();
            lock (m_gate)
            {
                if (TryUpdateTransform(primPath, propertyName, value, now) ||
                    TryUpdateScalar(primPath, propertyName, value, now))
                {
                    return;
                }
                m_discrete[new AttributeKey(primPath, propertyName)] = value;
            }
        }

        /// <inheritdoc/>
        public void SetTimeSample(string primPath, string propertyName, DateTime time, Variant value)
        {
            if (!UsdNames.IsValidPrimPath(primPath) ||
                !UsdNames.IsValidPropertyName(propertyName))
            {
                return;
            }
            double frame = (time.ToUniversalTime() - s_epoch).TotalSeconds;
            lock (m_gate)
            {
                m_timeSamples[new TimeSampleKey(primPath, propertyName, frame)] = value;
            }
        }

        /// <inheritdoc/>
        public void ComposePrim(string primPath, OpenUsdCompositionArc arc,
            string? assetReference, bool active)
        {
            if (!UsdNames.IsValidPrimPath(primPath))
            {
                return;
            }
            lock (m_gate)
            {
                m_prims[primPath] = new PrimComposition(arc, assetReference, active);
            }
        }

        /// <inheritdoc/>
        public IDisposable BeginBatch()
        {
            lock (m_gate)
            {
                m_batchDepth++;
            }
            return new BatchScope(this);
        }

        /// <summary>
        /// Stops the pump and applies whatever is still pending.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            await m_lifetime.CancelAsync().ConfigureAwait(false);
            try
            {
                await m_pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            m_lifetime.Dispose();
        }

        private void EndBatch()
        {
            lock (m_gate)
            {
                if (m_batchDepth > 0)
                {
                    m_batchDepth--;
                }
            }
        }

        /// <summary>
        /// Applies pending work at a fixed display rate until cancelled. One iteration is
        /// at most one stage edit, so neither a fast subscription nor the interpolation
        /// can queue unbounded scheduler work.
        /// </summary>
        private async Task PumpAsync(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(s_tick);
            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ApplyPendingAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutting down.
            }
            // Let values that arrived during shutdown land, so a short run still leaves
            // the stage in its final state.
            await ApplyPendingAsync(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task ApplyPendingAsync(CancellationToken cancellationToken)
        {
            Snapshot snapshot;
            lock (m_gate)
            {
                if (m_batchDepth > 0)
                {
                    return;
                }
                snapshot = Snapshot.Take(
                    Stopwatch.GetTimestamp(),
                    m_prims,
                    m_discrete,
                    m_timeSamples,
                    m_scalars,
                    m_transforms);
            }
            if (snapshot.IsEmpty)
            {
                return;
            }
            try
            {
                await m_scheduler.EditAsync(
                    stage => Apply(stage, snapshot),
                    snapshot.Invalidation,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The viewport closed while an edit was in flight.
            }
            catch (ObjectDisposedException)
            {
                // The stage was torn down while an edit was in flight.
            }
#pragma warning disable CA1031 // A malformed server value must not kill the pump.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                m_onError(exception);
            }
        }

        private static void Apply(UsdStage stage, Snapshot snapshot)
        {
            foreach (KeyValuePair<string, PrimComposition> entry in snapshot.Prims)
            {
                ApplyComposition(stage, entry.Key, entry.Value);
            }
            foreach (TransformSample entry in snapshot.Transforms)
            {
                ApplyTransform(stage, entry);
            }
            foreach (ScalarSample entry in snapshot.Scalars)
            {
                UsdPrim prim = stage.GetPrim(entry.PrimPath);
                if (prim.Exists())
                {
                    prim.SetDouble(entry.PropertyName, entry.Value);
                }
            }
            foreach (KeyValuePair<AttributeKey, Variant> entry in snapshot.Discrete)
            {
                ApplyValueIsolated(
                    stage.GetPrim(entry.Key.PrimPath),
                    entry.Key.PrimPath,
                    entry.Key.PropertyName,
                    entry.Value,
                    timeCode: null);
            }
            foreach (KeyValuePair<TimeSampleKey, Variant> entry in snapshot.TimeSamples)
            {
                ApplyValueIsolated(
                    stage.GetPrim(entry.Key.PrimPath),
                    entry.Key.PrimPath,
                    entry.Key.PropertyName,
                    entry.Value,
                    entry.Key.Frame);
            }
        }

        /// <summary>
        /// Applies one attribute, containing any failure to that attribute. A
        /// single unwritable target (for example a type the stage rejects) must
        /// not discard the whole frame, which would freeze every other binding.
        /// Each distinct target is reported once so a persistent mismatch cannot
        /// flood the console.
        /// </summary>
        private static void ApplyValueIsolated(
            UsdPrim prim, string primPath, string propertyName, Variant value, double? timeCode)
        {
            try
            {
                ApplyValue(prim, propertyName, value, timeCode);
            }
#pragma warning disable CA1031 // One rejected attribute must not discard the frame.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                string target = string.Concat(primPath, ".", propertyName);
                if (s_rejectedTargets.TryAdd(target, true))
                {
                    Console.Error.WriteLine(
                        $"A stage update failed for {target}: {exception.Message} " +
                        "Further failures for this target are suppressed.");
                }
            }
        }

        private static void ApplyComposition(
            UsdStage stage, string primPath, PrimComposition composition)
        {
            UsdPrim prim = stage.GetPrim(primPath);
            if (!prim.Exists())
            {
                prim = stage.DefinePrim(primPath, "Xform");
            }
            if (!string.IsNullOrEmpty(composition.AssetReference) &&
                TryParseReference(composition.AssetReference!, out string asset, out string? target))
            {
                switch (composition.Arc)
                {
                    case OpenUsdCompositionArc.Reference:
                    case OpenUsdCompositionArc.Instance:
                        prim.ClearReferences();
                        prim.AddReference(asset, target);
                        prim.SetInstanceable(
                            composition.Arc == OpenUsdCompositionArc.Instance);
                        break;
                    case OpenUsdCompositionArc.Payload:
                        prim.ClearPayloads();
                        prim.AddPayload(asset, target);
                        break;
                    default:
                        break;
                }
            }
            prim.SetActive(composition.Active);
        }

        /// <summary>
        /// Splits a USD reference literal such as <c>@robot.usda@&lt;/Robot&gt;</c> into the
        /// asset path and the optional target prim path. The connector hands sinks the
        /// literal because the file sink writes it straight into a layer; authoring
        /// through the data API needs the two parts separately.
        /// </summary>
        private static bool TryParseReference(
            string reference, out string assetPath, out string? targetPrimPath)
        {
            assetPath = string.Empty;
            targetPrimPath = null;
            string value = reference.Trim();
            if (value.Length < 3 || value[0] != '@')
            {
                return false;
            }
            int close = value.IndexOf('@', 1);
            if (close <= 1)
            {
                return false;
            }
            assetPath = value.Substring(1, close - 1);
            string rest = value[(close + 1)..].Trim();
            if (rest.Length == 0)
            {
                return true;
            }
            if (rest.Length < 3 || rest[0] != '<' || rest[^1] != '>')
            {
                return false;
            }
            targetPrimPath = rest[1..^1];
            return UsdNames.IsValidPrimPath(targetPrimPath);
        }

        private static void ApplyTransform(UsdStage stage, TransformSample sample)
        {
            UsdPrim prim = stage.GetPrim(sample.PrimPath);
            if (!prim.Exists() || !UsdGeomXformable.TryWrap(prim, out UsdGeomXformable xformable))
            {
                return;
            }
            UsdVec3d translation = sample.Translate ??
                xformable.GetLocalTransform().ExtractTranslation();
            UsdVec3d rotation = sample.Rotate ?? new UsdVec3d(0, 0, 0);
            UsdVec3d scale = sample.Scale ?? new UsdVec3d(1, 1, 1);
            // Author the matrix op directly rather than through SetLocalTransform. That
            // helper rewrites xformOpOrder, which fails whenever the asset declares an op
            // order in a layer weaker than the one being edited: the opinion there cannot
            // be cleared from here. Bound prims therefore declare a single
            // xformOp:transform, which only has to be set.
            prim.SetMatrix4d("xformOp:transform", Compose(translation, rotation, scale));
        }

        /// <summary>
        /// Builds the row-vector local transform for scale, then rotation (XYZ degrees),
        /// then translation - the composition OpenUSD's
        /// <c>["xformOp:translate", "xformOp:rotateXYZ", "xformOp:scale"]</c> op order
        /// denotes.
        /// </summary>
        private static UsdMatrix4d Compose(UsdVec3d translate, UsdVec3d rotate, UsdVec3d scale)
        {
            const double toRadians = Math.PI / 180.0;
            double cx = Math.Cos(rotate.X * toRadians);
            double sx = Math.Sin(rotate.X * toRadians);
            double cy = Math.Cos(rotate.Y * toRadians);
            double sy = Math.Sin(rotate.Y * toRadians);
            double cz = Math.Cos(rotate.Z * toRadians);
            double sz = Math.Sin(rotate.Z * toRadians);

            // Row-vector rotation R = Rx * Ry * Rz.
            double r00 = cy * cz;
            double r01 = cy * sz;
            double r02 = -sy;
            double r10 = (sx * sy * cz) - (cx * sz);
            double r11 = (sx * sy * sz) + (cx * cz);
            double r12 = sx * cy;
            double r20 = (cx * sy * cz) + (sx * sz);
            double r21 = (cx * sy * sz) - (sx * cz);
            double r22 = cx * cy;

            return new UsdMatrix4d(
                scale.X * r00, scale.X * r01, scale.X * r02, 0,
                scale.Y * r10, scale.Y * r11, scale.Y * r12, 0,
                scale.Z * r20, scale.Z * r21, scale.Z * r22, 0,
                translate.X, translate.Y, translate.Z, 1);
        }

        private static bool IsColorProperty(string propertyName)
        {
            return propertyName.EndsWith("Color", StringComparison.Ordinal) ||
                propertyName.EndsWith("color", StringComparison.Ordinal);
        }

        private static void ApplyValue(
            UsdPrim prim, string propertyName, Variant value, double? timeCode)
        {
            if (!prim.Exists())
            {
                return;
            }
            if (value.TryGetValue(out ArrayOf<float> floats) && floats.Count == 3)
            {
                var vector = new UsdVec3f(floats[0], floats[1], floats[2]);
                if (propertyName.EndsWith("displayColor", StringComparison.Ordinal))
                {
                    ReadOnlySpan<UsdVec3f> single = [vector];
                    if (timeCode is { } colorTime)
                    {
                        prim.SetVec3fArray(propertyName, single, colorTime);
                    }
                    else
                    {
                        prim.SetVec3fArray(propertyName, single);
                    }
                    return;
                }
                // Shader colour inputs are authored as color3f, and the native
                // setters match the attribute type exactly -- writing them as a
                // plain float3 is rejected.
                if (IsColorProperty(propertyName))
                {
                    if (timeCode is { } shaderColorTime)
                    {
                        prim.SetColor3f(propertyName, vector, shaderColorTime);
                    }
                    else
                    {
                        prim.SetColor3f(propertyName, vector);
                    }
                    return;
                }
                if (timeCode is { } vectorTime)
                {
                    prim.SetVec3f(propertyName, vector, vectorTime);
                }
                else
                {
                    prim.SetVec3f(propertyName, vector);
                }
                return;
            }
            if (value.TryGetValue(out string? text) && text is not null)
            {
                if (string.Equals(propertyName, "visibility", StringComparison.Ordinal))
                {
                    prim.SetVisibility(text);
                    return;
                }
                if (timeCode is { } tokenTime)
                {
                    prim.SetToken(propertyName, text, tokenTime);
                }
                else
                {
                    prim.SetToken(propertyName, text);
                }
                return;
            }
            if (value.TryGetValue(out bool flag))
            {
                if (timeCode is { } boolTime)
                {
                    prim.SetBool(propertyName, flag, boolTime);
                }
                else
                {
                    prim.SetBool(propertyName, flag);
                }
                return;
            }
            if (VariantConversions.TryGetDouble(value, out double number))
            {
                if (timeCode is { } numberTime)
                {
                    prim.SetDouble(propertyName, number, numberTime);
                }
                else
                {
                    prim.SetDouble(propertyName, number);
                }
            }
        }

        /// <summary>
        /// Retargets a numeric attribute so it eases towards the newest sample instead of
        /// stepping onto it. Returns <c>false</c> for anything that is not a plain number.
        /// </summary>
        private bool TryUpdateScalar(
            string primPath, string propertyName, in Variant value, long now)
        {
            if (value.TryGetValue(out string? _) || value.TryGetValue(out bool _) ||
                value.TryGetValue(out ArrayOf<float> _) ||
                !VariantConversions.TryGetDouble(value, out double number))
            {
                return false;
            }
            var key = new AttributeKey(primPath, propertyName);
            if (m_scalars.TryGetValue(key, out ScalarTrack? track))
            {
                track.Retarget(number, now);
            }
            else
            {
                m_scalars[key] = ScalarTrack.Starting(number, now);
            }
            return true;
        }

        /// <summary>
        /// Routes a structured <c>double3</c> transform op into the per-prim transform
        /// tracks. Returns <c>false</c> for anything that is authorable directly, such as
        /// the scalar <c>xformOp:rotateZ</c> a robot joint uses.
        /// </summary>
        private bool TryUpdateTransform(
            string primPath, string propertyName, in Variant value, long now)
        {
            if (!string.Equals(propertyName, TranslateOp, StringComparison.Ordinal) &&
                !string.Equals(propertyName, RotateOp, StringComparison.Ordinal) &&
                !string.Equals(propertyName, ScaleOp, StringComparison.Ordinal))
            {
                return false;
            }
            if (!TryGetVec3d(value, out UsdVec3d vector))
            {
                return false;
            }
            if (!m_transforms.TryGetValue(primPath, out TransformTracks? tracks))
            {
                tracks = new TransformTracks();
                m_transforms[primPath] = tracks;
            }
            tracks.Retarget(propertyName, vector, now);
            return true;
        }

        private static bool TryGetVec3d(in Variant value, out UsdVec3d result)
        {
            if (value.TryGetValue(out ArrayOf<double> doubles) && doubles.Count == 3)
            {
                result = new UsdVec3d(doubles[0], doubles[1], doubles[2]);
                return true;
            }
            if (value.TryGetValue(out ArrayOf<float> floats) && floats.Count == 3)
            {
                result = new UsdVec3d(floats[0], floats[1], floats[2]);
                return true;
            }
            result = default;
            return false;
        }

        /// <summary>
        /// One numeric target easing from the value it had when the newest sample arrived
        /// towards that sample, across roughly the observed sample interval.
        /// </summary>
        private sealed class ScalarTrack
        {
            private double m_from;
            private double m_to;
            private long m_start;
            private long m_window;
            private double m_lastApplied;
            private bool m_applied;

            public static ScalarTrack Starting(double value, long now) =>
                new() { m_from = value, m_to = value, m_start = now, m_window = 0 };

            public void Retarget(double value, long now)
            {
                m_from = ValueAt(now);
                m_to = value;
                m_window = Math.Clamp(now - m_start, s_minWindow, s_maxWindow);
                m_start = now;
            }

            public double ValueAt(long now)
            {
                if (m_window <= 0)
                {
                    return m_to;
                }
                double u = (now - m_start) / (double)m_window;
                if (u >= 1.0)
                {
                    return m_to;
                }
                return u <= 0.0 ? m_from : m_from + ((m_to - m_from) * u);
            }

            /// <summary>
            /// Returns the value to author, or <c>null</c> when the target has settled and
            /// the stage already carries it, so a still scene costs no stage edits.
            /// </summary>
            public double? Pending(long now)
            {
                double value = ValueAt(now);
                if (m_applied && value == m_lastApplied)
                {
                    return null;
                }
                m_lastApplied = value;
                m_applied = true;
                return value;
            }
        }

        /// <summary>
        /// The translate, rotate and scale tracks of one prim's composed local transform.
        /// </summary>
        private sealed class TransformTracks
        {
            private Vec3Track? m_translate;
            private Vec3Track? m_rotate;
            private Vec3Track? m_scale;

            public void Retarget(string op, UsdVec3d value, long now)
            {
                switch (op)
                {
                    case TranslateOp:
                        Retarget(ref m_translate, value, now);
                        break;
                    case RotateOp:
                        Retarget(ref m_rotate, value, now);
                        break;
                    default:
                        Retarget(ref m_scale, value, now);
                        break;
                }
            }

            public bool TryTake(string primPath, long now, out TransformSample sample)
            {
                bool changed =
                    (m_translate?.HasPending(now) ?? false) |
                    (m_rotate?.HasPending(now) ?? false) |
                    (m_scale?.HasPending(now) ?? false);
                sample = new TransformSample(
                    primPath,
                    m_translate?.ValueAt(now),
                    m_rotate?.ValueAt(now),
                    m_scale?.ValueAt(now));
                return changed;
            }

            private static void Retarget(ref Vec3Track? track, UsdVec3d value, long now)
            {
                if (track is null)
                {
                    track = Vec3Track.Starting(value, now);
                    return;
                }
                track.Retarget(value, now);
            }
        }

        private sealed class Vec3Track
        {
            private readonly ScalarTrack m_x = ScalarTrack.Starting(0, 0);
            private readonly ScalarTrack m_y = ScalarTrack.Starting(0, 0);
            private readonly ScalarTrack m_z = ScalarTrack.Starting(0, 0);

            public static Vec3Track Starting(UsdVec3d value, long now)
            {
                var track = new Vec3Track();
                track.Retarget(value, now);
                return track;
            }

            public void Retarget(UsdVec3d value, long now)
            {
                m_x.Retarget(value.X, now);
                m_y.Retarget(value.Y, now);
                m_z.Retarget(value.Z, now);
            }

            public UsdVec3d ValueAt(long now) =>
                new(m_x.ValueAt(now), m_y.ValueAt(now), m_z.ValueAt(now));

            public bool HasPending(long now)
            {
                // Evaluate all three so each track records what it last authored.
                bool x = m_x.Pending(now).HasValue;
                bool y = m_y.Pending(now).HasValue;
                bool z = m_z.Pending(now).HasValue;
                return x || y || z;
            }
        }

        private readonly record struct AttributeKey(string PrimPath, string PropertyName);

        private readonly record struct TimeSampleKey(
            string PrimPath, string PropertyName, double Frame);

        private readonly record struct PrimComposition(
            OpenUsdCompositionArc Arc, string? AssetReference, bool Active);

        private readonly record struct ScalarSample(
            string PrimPath, string PropertyName, double Value);

        private readonly record struct TransformSample(
            string PrimPath, UsdVec3d? Translate, UsdVec3d? Rotate, UsdVec3d? Scale);

        /// <summary>
        /// One tick's worth of work, detached from the sink's mutable state so the stage
        /// callback never touches a lock.
        /// </summary>
        private sealed class Snapshot
        {
            private Snapshot(
                Dictionary<string, PrimComposition> prims,
                Dictionary<AttributeKey, Variant> discrete,
                Dictionary<TimeSampleKey, Variant> timeSamples,
                List<ScalarSample> scalars,
                List<TransformSample> transforms)
            {
                Prims = prims;
                Discrete = discrete;
                TimeSamples = timeSamples;
                Scalars = scalars;
                Transforms = transforms;
            }

            public Dictionary<string, PrimComposition> Prims { get; }

            public Dictionary<AttributeKey, Variant> Discrete { get; }

            public Dictionary<TimeSampleKey, Variant> TimeSamples { get; }

            public List<ScalarSample> Scalars { get; }

            public List<TransformSample> Transforms { get; }

            public bool IsEmpty =>
                Prims.Count == 0 && Discrete.Count == 0 &&
                TimeSamples.Count == 0 && Scalars.Count == 0 && Transforms.Count == 0;

            /// <summary>
            /// Composing a prim changes the stage's composition; everything else only
            /// changes property values, which is far cheaper for the renderer to absorb.
            /// </summary>
            public UsdStageInvalidationKind Invalidation => Prims.Count > 0
                ? UsdStageInvalidationKind.Composition
                : UsdStageInvalidationKind.Property;

            public static Snapshot Take(
                long now,
                Dictionary<string, PrimComposition> prims,
                Dictionary<AttributeKey, Variant> discrete,
                Dictionary<TimeSampleKey, Variant> timeSamples,
                Dictionary<AttributeKey, ScalarTrack> scalars,
                Dictionary<string, TransformTracks> transforms)
            {
                var scalarSamples = new List<ScalarSample>();
                foreach (KeyValuePair<AttributeKey, ScalarTrack> entry in scalars)
                {
                    if (entry.Value.Pending(now) is { } value)
                    {
                        scalarSamples.Add(
                            new ScalarSample(entry.Key.PrimPath, entry.Key.PropertyName, value));
                    }
                }

                var transformSamples = new List<TransformSample>();
                foreach (KeyValuePair<string, TransformTracks> entry in transforms)
                {
                    if (entry.Value.TryTake(entry.Key, now, out TransformSample sample))
                    {
                        transformSamples.Add(sample);
                    }
                }

                var snapshot = new Snapshot(
                    new Dictionary<string, PrimComposition>(prims, StringComparer.Ordinal),
                    new Dictionary<AttributeKey, Variant>(discrete),
                    new Dictionary<TimeSampleKey, Variant>(timeSamples),
                    scalarSamples,
                    transformSamples);
                prims.Clear();
                discrete.Clear();
                timeSamples.Clear();
                // Interpolation tracks are retained: they keep easing across ticks and
                // report nothing once they have settled.
                return snapshot;
            }
        }

        private sealed class BatchScope : IDisposable
        {
            private UsdStageSink? m_owner;

            public BatchScope(UsdStageSink owner)
            {
                m_owner = owner;
            }

            public void Dispose()
            {
                UsdStageSink? owner = Interlocked.Exchange(ref m_owner, null);
                owner?.EndBatch();
            }
        }
    }

    /// <summary>
    /// Prim-path and property-name validation. The binding model comes from the server,
    /// which the connector treats as untrusted, so a malformed or hostile name is dropped
    /// rather than passed to the USD runtime.
    /// </summary>
    internal static class UsdNames
    {
        public static bool IsValidPrimPath(string? path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/' || path.Length == 1)
            {
                return false;
            }
            foreach (string segment in path.Split('/'))
            {
                if (segment.Length == 0)
                {
                    continue;
                }
                if (!IsValidIdentifier(segment))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool IsValidPropertyName(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            foreach (string segment in name.Split(':'))
            {
                if (!IsValidIdentifier(segment))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IsValidIdentifier(string segment)
        {
            if (segment.Length == 0)
            {
                return false;
            }
            if (segment[0] != '_' && !char.IsLetter(segment[0]))
            {
                return false;
            }
            foreach (char character in segment)
            {
                if (character != '_' &&
                    !char.IsLetterOrDigit(character))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
