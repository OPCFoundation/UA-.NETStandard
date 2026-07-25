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
using Opc.Ua.Positioning.Server;

namespace Robotics
{
    /// <summary>
    /// Deterministic sample provider that publishes a global location per robot.
    /// </summary>
    public sealed class MobileRobotPositionProvider : IGlobalPositionProvider
    {
        private readonly RobotMobilityOptions m_options;
        private readonly TimeProvider m_timeProvider;
        private readonly DateTimeOffset m_startedAt;

        public MobileRobotPositionProvider(
            IOptions<RobotMobilityOptions> options,
            RobotPositioningScenario scenario)
            : this(options.Value, scenario, TimeProvider.System)
        {
        }

        public MobileRobotPositionProvider(
            RobotMobilityOptions options,
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

        public ValueTask<GlobalPositionSample> ReadAsync(
            string sourceId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TimeSpan elapsed = m_timeProvider.GetUtcNow() - m_startedAt;
            return new ValueTask<GlobalPositionSample>(
                CreateSample(sourceId, elapsed));
        }

        public async IAsyncEnumerable<GlobalPositionSample> WatchAsync(
            string sourceId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            RobotMotionOptions options = GetOptions(sourceId);
            if (options.Mode == RobotMotionMode.Fixed)
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

        private GlobalPositionSample CreateSample(
            string sourceId,
            TimeSpan elapsed)
        {
            ThreeDFrame local = EvaluateLocalFrame(sourceId, elapsed);
            S3DGeographicCoordinateDataType geographic =
                Scenario.Fit.LocalToGlobal(
                    local.CartesianCoordinates,
                    AngleUnit.Degrees);
            var position = new GlobalPositionDataType
            {
                EncodingMask =
                    geographic.EncodingMask |
                    (uint)GlobalPositionDataTypeFields.Accuracy,
                Longitude = geographic.Longitude,
                Latitude = geographic.Latitude,
                Elevation = geographic.Elevation,
                Accuracy = 0.05
            };
            var location = new GlobalLocationDataType
            {
                EncodingMask =
                    (uint)GlobalLocationDataTypeFields.Orientation,
                Position = position,
                Orientation = local.Orientation
            };
            return new GlobalPositionSample(
                sourceId,
                location,
                StatusCodes.Good,
                new DateTimeUtc(m_timeProvider.GetUtcNow()));
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
