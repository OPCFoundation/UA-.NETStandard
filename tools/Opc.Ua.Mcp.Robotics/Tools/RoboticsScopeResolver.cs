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
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Resolves scoped name references (frames, tools, locations, outputs,
    /// programs) against the lookup tables one controller publishes.
    /// The resolver is a pure projection over a single lookup snapshot: it
    /// never reads from the server, never submits work, and never requests
    /// command authority.
    /// </summary>
    internal sealed class RoboticsScopeResolver
    {
        /// <summary>
        /// Initializes the resolver over one lookup snapshot.
        /// </summary>
        public RoboticsScopeResolver(RobotIntentLookups lookups)
        {
            Lookups = lookups ?? RobotIntentLookups.Empty;
        }

        /// <summary>
        /// Gets the lookup snapshot the resolver projects.
        /// </summary>
        public RobotIntentLookups Lookups { get; }

        /// <summary>
        /// Resolves a frame name or NodeId through the Frames lookup.
        /// </summary>
        public NodeId ResolveFrame(string? nameOrNodeId)
        {
            return RoboticsControllerResolver.ResolveScopedResource(
                nameOrNodeId, Lookups.Frames, "frame");
        }

        /// <summary>
        /// Resolves a pose or force frame selector to the FrameId string the
        /// controller publishes.
        /// </summary>
        /// <remarks>
        /// A value that already matches a published FrameId is returned as-is.
        /// A frame Name or NodeId is resolved through the Frames lookup and
        /// mapped back to the published FrameId. Anything else is rejected so
        /// that a mistyped selector never reaches the server as a silently
        /// unscoped string.
        /// </remarks>
        public string ResolveFrameId(string? frameId)
        {
            if (string.IsNullOrWhiteSpace(frameId))
            {
                return string.Empty;
            }

            string trimmed = frameId.Trim();
            for (int i = 0; i < Lookups.FramesByFrameId.Count; i++)
            {
                if (string.Equals(Lookups.FramesByFrameId[i].Name, trimmed, StringComparison.Ordinal))
                {
                    return Lookups.FramesByFrameId[i].Name;
                }
            }

            if (Lookups.Frames.Count == 0 && Lookups.FramesByFrameId.Count == 0)
            {
                return trimmed;
            }

            NodeId resolved = RoboticsControllerResolver.ResolveScopedResource(
                trimmed, Lookups.Frames, "frame");
            for (int i = 0; i < Lookups.FramesByFrameId.Count; i++)
            {
                if (Lookups.FramesByFrameId[i].NodeId == resolved)
                {
                    return Lookups.FramesByFrameId[i].Name;
                }
            }

            return trimmed;
        }

        /// <summary>
        /// Resolves a tool name or NodeId through the Tools lookup.
        /// </summary>
        public NodeId ResolveTool(string? nameOrNodeId)
        {
            return RoboticsControllerResolver.ResolveScopedResource(
                nameOrNodeId, Lookups.Tools, "tool");
        }

        /// <summary>
        /// Resolves a location name or NodeId through the Locations lookup.
        /// </summary>
        public NodeId ResolveLocation(string? nameOrNodeId)
        {
            return RoboticsControllerResolver.ResolveScopedResource(
                nameOrNodeId, Lookups.Locations, "location");
        }

        /// <summary>
        /// Resolves an output or Boolean signal name or NodeId through the
        /// Outputs lookup. A full NodeId is always accepted so that a signal
        /// the controller does not publish as an output can still be named;
        /// the server validates the scope.
        /// </summary>
        public NodeId ResolveOutput(string? nameOrNodeId)
        {
            return RoboticsControllerResolver.ResolveScopedResource(
                nameOrNodeId, Lookups.Outputs, "output");
        }

        /// <summary>
        /// Resolves a program name or NodeId through the Programs lookup.
        /// </summary>
        public NodeId ResolveProgram(string? nameOrNodeId)
        {
            return RoboticsControllerResolver.ResolveScopedResource(
                nameOrNodeId, Lookups.Programs, "program");
        }

        /// <summary>
        /// Resolves a required tool selector.
        /// </summary>
        public NodeId ResolveRequiredTool(string? nameOrNodeId, string parameterName)
        {
            return Require(ResolveTool(nameOrNodeId), parameterName);
        }

        /// <summary>
        /// Resolves a required location selector.
        /// </summary>
        public NodeId ResolveRequiredLocation(string? nameOrNodeId, string parameterName)
        {
            return Require(ResolveLocation(nameOrNodeId), parameterName);
        }

        /// <summary>
        /// Resolves a required output selector.
        /// </summary>
        public NodeId ResolveRequiredOutput(string? nameOrNodeId, string parameterName)
        {
            return Require(ResolveOutput(nameOrNodeId), parameterName);
        }

        /// <summary>
        /// Resolves a required program selector.
        /// </summary>
        public NodeId ResolveRequiredProgram(string? nameOrNodeId, string parameterName)
        {
            return Require(ResolveProgram(nameOrNodeId), parameterName);
        }

        private static NodeId Require(NodeId resolved, string parameterName)
        {
            if (resolved.IsNull)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"'{parameterName}' is required."),
                    parameterName);
            }

            return resolved;
        }
    }
}
