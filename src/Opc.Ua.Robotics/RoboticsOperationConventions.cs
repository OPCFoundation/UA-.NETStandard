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

namespace Opc.Ua.Robotics.Operations
{
    /// <summary>
    /// Describes how a release operation should transfer the held object.
    /// </summary>
    public enum RoboticsReleaseMode
    {
        /// <summary>
        /// Release the object without controlled placement.
        /// </summary>
        Drop,

        /// <summary>
        /// Place the object at the requested or current target.
        /// </summary>
        Place,

        /// <summary>
        /// Hold the object for a handover and release when the application decides it is safe.
        /// </summary>
        Handover
    }

    /// <summary>
    /// Describes the approach direction convention used by grasping and placement operations.
    /// </summary>
    public enum RoboticsApproach
    {
        /// <summary>
        /// The server or application chooses the approach.
        /// </summary>
        Default,

        /// <summary>
        /// Approach along the tool Z axis.
        /// </summary>
        ToolZ,

        /// <summary>
        /// Approach from above in the active work coordinate system.
        /// </summary>
        Top,

        /// <summary>
        /// Approach from the side in the active work coordinate system.
        /// </summary>
        Side
    }

    /// <summary>
    /// Requests a non-normative convention MoveTo operation.
    /// </summary>
    public sealed record MoveToRequest(
        ThreeDFrame TargetFrame,
        double? SpeedFraction = null,
        double? BlendRadius = null,
        EUInformation? BlendRadiusUnits = null);

    /// <summary>
    /// Requests a non-normative convention joint move.
    /// </summary>
    public sealed record JointMoveRequest(
        ArrayOf<double> JointTargets,
        EUInformation JointUnits,
        double? SpeedFraction = null);

    /// <summary>
    /// Requests a non-normative convention linear move.
    /// </summary>
    public sealed record LinearMoveRequest(
        ThreeDFrame TargetFrame,
        double LinearSpeed,
        EUInformation LinearSpeedUnits,
        double? Acceleration = null,
        EUInformation? AccelerationUnits = null);

    /// <summary>
    /// Requests a non-normative convention grasp operation.
    /// </summary>
    public sealed record GraspRequest(
        double? ForceNewtons = null,
        double? Width = null,
        EUInformation? WidthUnits = null,
        RoboticsApproach Approach = RoboticsApproach.Default);

    /// <summary>
    /// Requests a non-normative convention release operation.
    /// </summary>
    public sealed record ReleaseRequest(
        RoboticsReleaseMode Mode,
        ThreeDFrame? TargetFrame = null);

    /// <summary>
    /// Requests a non-normative convention pick or place operation.
    /// </summary>
    public sealed record PickPlaceRequest(
        string StationOrLocationIdentifier,
        string ObjectClass,
        ArrayOf<KeyValuePair> Attributes,
        double? ForceNewtons = null);

    /// <summary>
    /// Requests a non-normative convention tool-change operation.
    /// </summary>
    public sealed record ToolChangeRequest(string ToolIdentifier, string? DockStation = null);

    /// <summary>
    /// Requests a non-normative convention output write operation.
    /// </summary>
    public sealed record OutputRequest(string OutputLineIdentifier, Variant Value);

    /// <summary>
    /// Requests a non-normative convention program call operation.
    /// </summary>
    public sealed record ProgramCallRequest(string ProgramName, ArrayOf<Variant> Arguments);

    /// <summary>
    /// Describes the result of a non-normative convention Robotics operation.
    /// </summary>
    public sealed record RoboticsOperationResult(
        ServiceResult ServiceResult,
        string? Message = null,
        ArrayOf<Variant>? Outputs = null)
    {
        /// <summary>
        /// Gets a successful operation result.
        /// </summary>
        public static RoboticsOperationResult Good { get; } = new(ServiceResult.Good);
    }
}
