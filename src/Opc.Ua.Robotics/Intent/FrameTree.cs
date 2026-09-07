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
using System.Threading;

namespace Opc.Ua.RobotIntent
{
    /// <summary>
    /// Resolves Robot Intent coordinate-frame transforms through a named frame tree.
    /// </summary>
    public sealed class FrameTree
    {
        /// <summary>
        /// Adds a named frame and its transform within its parent frame.
        /// </summary>
        /// <param name="frameId">
        /// The unique frame identifier.
        /// </param>
        /// <param name="parentFrameId">
        /// The parent frame identifier, or an empty string for the tree root.
        /// </param>
        /// <param name="transform">
        /// The transform expressing the frame within its parent.
        /// </param>
        /// <param name="role">
        /// The frame role.
        /// </param>
        /// <param name="error">
        /// The failure reason, or <c>null</c> when the frame is added.
        /// </param>
        /// <returns>
        /// <c>true</c> if the frame was added.
        /// </returns>
        public bool TryAdd(
            string frameId,
            string parentFrameId,
            Pose3DDataType transform,
            FrameRoleEnum role,
            out string? error)
        {
            if (frameId is null)
            {
                throw new ArgumentNullException(nameof(frameId));
            }
            if (parentFrameId is null)
            {
                throw new ArgumentNullException(nameof(parentFrameId));
            }
            if (transform is null)
            {
                throw new ArgumentNullException(nameof(transform));
            }

            lock (m_lock)
            {
                if (frameId.Length == 0)
                {
                    return Fail("The frame identifier must not be empty.", out error);
                }
                if (m_frames.ContainsKey(frameId))
                {
                    return Fail($"The frame '{frameId}' already exists.", out error);
                }
                if (StringComparer.Ordinal.Equals(frameId, parentFrameId))
                {
                    return Fail("A frame cannot be its own parent.", out error);
                }
                if (parentFrameId.Length != 0 && !m_frames.ContainsKey(parentFrameId))
                {
                    return Fail($"The parent frame '{parentFrameId}' is unknown.", out error);
                }
                if (WouldCreateCycle(frameId, parentFrameId))
                {
                    return Fail("The frame would create a cycle.", out error);
                }
                if (!PoseMath.TryValidate(transform, 1e-6, out string? validationError))
                {
                    return Fail(validationError ?? "The frame transform is invalid.", out error);
                }

                m_frames.Add(frameId, new FrameEntry(parentFrameId, PoseMath.CopyPose(transform), role));
                error = null;
                return true;
            }
        }

        /// <summary>
        /// Resolves a frame transform to the root frame.
        /// </summary>
        /// <param name="frameId">
        /// The frame identifier to resolve, or an empty string for the root identity.
        /// </param>
        /// <param name="transform">
        /// The transform from the requested frame to the root frame.
        /// </param>
        /// <param name="error">
        /// The failure reason, or <c>null</c> when the frame is resolved.
        /// </param>
        /// <returns>
        /// <c>true</c> if the frame was resolved.
        /// </returns>
        public bool TryResolveToRoot(string frameId, out Pose3DDataType transform, out string? error)
        {
            if (frameId is null)
            {
                throw new ArgumentNullException(nameof(frameId));
            }

            lock (m_lock)
            {
                return TryResolveToRootLocked(frameId, out transform, out _, out error);
            }
        }

        /// <summary>
        /// Re-expresses a pose in another frame.
        /// </summary>
        /// <param name="pose">
        /// The pose to re-express. Its <see cref="Pose3DDataType.FrameId"/> identifies the source frame.
        /// </param>
        /// <param name="targetFrameId">
        /// The target frame identifier, or an empty string for the root frame.
        /// </param>
        /// <param name="result">
        /// The pose expressed in the target frame.
        /// </param>
        /// <param name="error">
        /// The failure reason, or <c>null</c> when the pose is re-expressed.
        /// </param>
        /// <returns>
        /// <c>true</c> if both frames were resolved and the pose was re-expressed.
        /// </returns>
        public bool TryExpress(
            in Pose3DDataType pose,
            string targetFrameId,
            out Pose3DDataType result,
            out string? error)
        {
            if (pose is null)
            {
                throw new ArgumentNullException(nameof(pose));
            }
            if (targetFrameId is null)
            {
                throw new ArgumentNullException(nameof(targetFrameId));
            }

            lock (m_lock)
            {
                if (!PoseMath.TryValidate(pose, 1e-6, out string? validationError))
                {
                    result = PoseMath.Identity(targetFrameId);
                    return Fail(validationError ?? "The pose is invalid.", out error);
                }

                string sourceFrameId = pose.FrameId ?? string.Empty;
                if (StringComparer.Ordinal.Equals(sourceFrameId, targetFrameId))
                {
                    result = PoseMath.CopyPose(pose);
                    result.FrameId = targetFrameId;
                    error = null;
                    return true;
                }

                if (!TryResolveToRootLocked(
                    sourceFrameId,
                    out Pose3DDataType sourceToRoot,
                    out string sourceRootFrameId,
                    out error))
                {
                    result = PoseMath.Identity(targetFrameId);
                    return false;
                }
                if (!TryResolveToRootLocked(
                    targetFrameId,
                    out Pose3DDataType targetToRoot,
                    out string targetRootFrameId,
                    out error))
                {
                    result = PoseMath.Identity(targetFrameId);
                    return false;
                }
                if (sourceRootFrameId.Length != 0 &&
                    targetRootFrameId.Length != 0 &&
                    !StringComparer.Ordinal.Equals(sourceRootFrameId, targetRootFrameId))
                {
                    result = PoseMath.Identity(targetFrameId);
                    return Fail(
                        $"The source frame '{sourceFrameId}' and target frame '{targetFrameId}' " +
                        "do not share a common root.",
                        out error);
                }

                Pose3DDataType poseInRoot = PoseMath.Compose(sourceToRoot, PoseMath.CopyPose(pose));
                Pose3DDataType rootToTarget = PoseMath.Invert(targetToRoot);
                result = PoseMath.Compose(rootToTarget, poseInRoot);
                result.FrameId = targetFrameId;
                error = null;
                return true;
            }
        }

        private bool TryResolveToRootLocked(
            string frameId,
            out Pose3DDataType transform,
            out string rootFrameId,
            out string? error)
        {
            transform = PoseMath.Identity(string.Empty);
            rootFrameId = string.Empty;
            if (frameId.Length == 0)
            {
                error = null;
                return true;
            }
            if (!m_frames.TryGetValue(frameId, out FrameEntry? entry))
            {
                return Fail($"The frame '{frameId}' is unknown.", out error);
            }
            if (entry.ParentFrameId.Length == 0)
            {
                rootFrameId = frameId;
                error = null;
                return true;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            string currentFrameId = frameId;
            Pose3DDataType current = PoseMath.CopyPose(entry.Transform);

            while (entry.ParentFrameId.Length != 0)
            {
                if (!visited.Add(currentFrameId))
                {
                    transform = PoseMath.Identity(string.Empty);
                    rootFrameId = string.Empty;
                    return Fail("The frame tree contains a cycle.", out error);
                }
                if (!m_frames.TryGetValue(entry.ParentFrameId, out FrameEntry? parent))
                {
                    transform = PoseMath.Identity(string.Empty);
                    rootFrameId = string.Empty;
                    return Fail($"The parent frame '{entry.ParentFrameId}' is unknown.", out error);
                }

                if (parent.ParentFrameId.Length != 0)
                {
                    current = PoseMath.Compose(parent.Transform, current);
                }
                currentFrameId = entry.ParentFrameId;
                entry = parent;
            }

            rootFrameId = currentFrameId;
            current.FrameId = string.Empty;
            transform = current;
            error = null;
            return true;
        }

        private bool WouldCreateCycle(string frameId, string parentFrameId)
        {
            string currentFrameId = parentFrameId;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (currentFrameId.Length != 0)
            {
                if (!visited.Add(currentFrameId))
                {
                    return true;
                }
                if (StringComparer.Ordinal.Equals(currentFrameId, frameId))
                {
                    return true;
                }
                if (!m_frames.TryGetValue(currentFrameId, out FrameEntry? entry))
                {
                    return false;
                }

                currentFrameId = entry.ParentFrameId;
            }

            return false;
        }

        private static bool Fail(string failure, out string? error)
        {
            error = failure;
            return false;
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<string, FrameEntry> m_frames = new(StringComparer.Ordinal);

        private sealed class FrameEntry
        {
            public FrameEntry(string parentFrameId, Pose3DDataType transform, FrameRoleEnum role)
            {
                ParentFrameId = parentFrameId;
                Transform = transform;
                Role = role;
            }

            public string ParentFrameId { get; }

            public Pose3DDataType Transform { get; }

            public FrameRoleEnum Role { get; }
        }
    }
}
