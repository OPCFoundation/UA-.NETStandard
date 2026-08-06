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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Gpos;
using Opc.Ua.Positioning;

namespace Robotics
{
    /// <summary>
    /// Deterministic sample provider that publishes a global location per robot.
    /// </summary>
    public sealed class MobileRobotPositionProvider : IGeoLocationProvider
    {
        /// <summary>
        /// The EPSG code of WGS84, which the cell's GlobalLocation Variables
        /// are configured for.
        /// </summary>
        private const uint WGS84EpsgCode = 4326;

        private readonly MobileRobotPositionOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly DateTimeOffset m_startedAt;

        public MobileRobotPositionProvider(
            IOptions<MobileRobotPositionOptions> options,
            RobotPositioningScenario scenario,
            CellChoreographer choreographer)
            : this(options.Value, scenario, TimeProvider.System)
        {
            Choreographer = choreographer ??
                throw new ArgumentNullException(nameof(choreographer));
        }

        public MobileRobotPositionProvider(
            MobileRobotPositionOptions options,
            RobotPositioningScenario scenario,
            TimeProvider timeProvider)
        {
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            m_timeProvider = timeProvider ??
                throw new ArgumentNullException(nameof(timeProvider));
            m_startedAt = m_timeProvider.GetUtcNow();
        }

        public RobotPositioningScenario Scenario { get; }

        /// <summary>
        /// The choreographer whose agents own the robot poses, when the provider is hosted.
        /// </summary>
        public CellChoreographer? Choreographer { get; }

        /// <inheritdoc/>
        public bool SupportsPush => true;

        /// <inheritdoc/>
        public ValueTask<GeoLocationSample> ReadAsync(
            string sourceId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan elapsed = m_timeProvider.GetUtcNow() - m_startedAt;
            return new ValueTask<GeoLocationSample>(
                CreateSample(sourceId, elapsed));
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<GeoLocationSample> WatchAsync(
            string sourceId,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Choreographer == null && GetOptions(sourceId).Mode == RobotMotionMode.Fixed)
            {
                yield break;
            }

            var interval = TimeSpan.FromMilliseconds(
                Math.Max(20, m_options.UpdateIntervalMilliseconds));
            while (!cancellationToken.IsCancellationRequested)
            {
#if NET8_0_OR_GREATER
                await Task.Delay(
                    interval,
                    m_timeProvider,
                    cancellationToken).ConfigureAwait(false);
#else
                await Task.Delay(
                    interval,
                    cancellationToken).ConfigureAwait(false);
#endif
                TimeSpan elapsed = m_timeProvider.GetUtcNow() - m_startedAt;
                yield return CreateSample(sourceId, elapsed);
            }
        }

        internal ThreeDFrame EvaluateLocalFrame(
            string sourceId,
            TimeSpan elapsed)
        {
            // The choreographer owns the pose whenever the provider is hosted: the two
            // robots have to agree on where each other is, which independent parametric
            // paths could never do. The options-driven path below stays for tests that
            // exercise the provider on its own.
            if (Choreographer != null)
            {
                foreach (RobotAgent agent in Choreographer.Robots)
                {
                    if (!string.Equals(agent.Id, sourceId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    return new ThreeDFrame
                    {
                        CartesianCoordinates = new ThreeDCartesianCoordinates
                        {
                            X = agent.X,
                            Y = agent.Y,
                            Z = 0.0
                        },
                        Orientation = new ThreeDOrientation
                        {
                            A = 0.0,
                            B = 0.0,
                            C = agent.HeadingDegrees
                        }
                    };
                }
            }

            RobotMotionOptions options = GetOptions(sourceId);
            double period = Math.Max(0.1, options.PeriodSeconds);
            double theta = 2.0 *
                Math.PI *
                (elapsed.TotalSeconds + options.PhaseSeconds) /
                period;
            double x = options.OriginX;
            double y = options.OriginY;
            double dx = 1.0;
            double dy = 0.0;

            switch (options.Mode)
            {
                case RobotMotionMode.FigureEight:
                    x += options.AmplitudeX * Math.Sin(theta);
                    y += options.AmplitudeY * Math.Sin(2.0 * theta);
                    dx = options.AmplitudeX * Math.Cos(theta);
                    dy = 2.0 * options.AmplitudeY * Math.Cos(2.0 * theta);
                    break;
                case RobotMotionMode.Circle:
                    x += options.Radius * Math.Cos(theta);
                    y += options.Radius * Math.Sin(theta);
                    dx = -options.Radius * Math.Sin(theta);
                    dy = options.Radius * Math.Cos(theta);
                    break;
                case RobotMotionMode.Shuttle:
                    x += options.ShuttleDistance * Math.Sin(theta);
                    dx = options.ShuttleDistance * Math.Cos(theta);
                    break;
                case RobotMotionMode.Fixed:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported robot motion mode '{options.Mode}'.");
            }

            double heading = options.HeadingFollowsPath &&
                options.Mode != RobotMotionMode.Fixed
                ? Math.Atan2(dy, dx) * (180.0 / Math.PI)
                : options.FixedHeadingDegrees;
            return new ThreeDFrame
            {
                CartesianCoordinates = new ThreeDCartesianCoordinates
                {
                    X = x,
                    Y = y,
                    Z = options.OriginZ
                },
                Orientation = new ThreeDOrientation
                {
                    A = 0.0,
                    B = 0.0,
                    C = heading
                }
            };
        }

        private GeoLocationSample CreateSample(
            string sourceId,
            TimeSpan elapsed)
        {
            ThreeDFrame local = EvaluateLocalFrame(sourceId, elapsed);
            S3DGeographicCoordinateDataType geographic = Scenario.Fit.LocalToGlobal(
                local.CartesianCoordinates,
                AngleUnit.Degrees);
            bool hasElevation = (geographic.EncodingMask &
                (uint)S3DGeographicCoordinateDataTypeFields.Elevation) != 0;
            var position = new GeoPosition(
                geographic.Latitude,
                geographic.Longitude,
                hasElevation ? geographic.Elevation : null,
                Accuracy: 0.05,
                EpsgCode: WGS84EpsgCode);
            var orientation = new GeoOrientation(
                local.Orientation.A,
                local.Orientation.B,
                local.Orientation.C);
            return new GeoLocationSample(
                position,
                orientation,
                default,
                StatusCodes.Good,
                new DateTimeUtc(m_timeProvider.GetUtcNow()),
                sourceId);
        }

        private RobotMotionOptions GetOptions(string sourceId)
        {
            return sourceId switch
            {
                "R1" => m_options.R1,
                "R2" => m_options.R2,
                _ => throw new ArgumentException(
                    $"Unknown robot positioning source '{sourceId}'.",
                    nameof(sourceId))
            };
        }
    }
}
