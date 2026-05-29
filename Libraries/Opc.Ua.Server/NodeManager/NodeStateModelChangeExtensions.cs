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
using System.Globalization;
using System.Threading;

namespace Opc.Ua.Server.NodeManager
{
    /// <summary>
    /// Helpers for the <c>NodeVersion</c> property defined in OPC UA
    /// Part 5 §9.32.2. The spec requires that only nodes carrying a
    /// <c>NodeVersion</c> property may trigger a <c>ModelChangeEvent</c>,
    /// that every emission bumps the property, and that any write to
    /// the property fires a model-change event.
    /// </summary>
    public static class NodeStateModelChangeExtensions
    {
        /// <summary>
        /// AsyncLocal guard set by <see cref="BumpNodeVersion"/> so that
        /// the <c>OnWriteValue</c> handler installed by
        /// <see cref="EnableModelChangeTracking"/> does not re-enter and
        /// double-fire a model-change event for framework-driven bumps.
        /// </summary>
        private static readonly AsyncLocal<bool> s_isFrameworkBump = new();

        /// <summary>
        /// True when execution is inside a framework-driven NodeVersion
        /// bump (the framework already emitted the appropriate
        /// <c>GeneralModelChangeEvent</c> and the OnWriteValue hook must
        /// not echo it as a <c>BaseModelChangeEvent</c>).
        /// </summary>
        public static bool IsInsideFrameworkNodeVersionBump => s_isFrameworkBump.Value;

        /// <summary>
        /// Returns <c>true</c> when <paramref name="node"/> has a child
        /// property with <c>BrowseName == BrowseNames.NodeVersion</c>.
        /// </summary>
        public static bool HasNodeVersion(this NodeState node)
        {
            return GetNodeVersionProperty(node) != null;
        }

        /// <summary>
        /// Returns the <c>NodeVersion</c> property of
        /// <paramref name="node"/> if present, otherwise <c>null</c>.
        /// </summary>
        public static PropertyState<string>? GetNodeVersionProperty(this NodeState node)
        {
            if (node == null)
            {
                return null;
            }
            BaseInstanceState? child = node.FindChild(null!, new QualifiedName(BrowseNames.NodeVersion));
            return child as PropertyState<string>;
        }

        /// <summary>
        /// Increments the <c>NodeVersion</c> of <paramref name="node"/>.
        /// Parses the current value as <see cref="ulong"/> (treating
        /// missing / non-numeric as 0), increments, and writes back as
        /// the decimal string. No-op when the node has no
        /// <c>NodeVersion</c> property.
        /// </summary>
        /// <remarks>
        /// The framework calls this from
        /// <c>AsyncCustomNodeManager.EmitModelChange</c> after recording
        /// a change for the node. The internal AsyncLocal flag
        /// <see cref="IsInsideFrameworkNodeVersionBump"/> is set while
        /// the underlying write executes so the OnWriteValue hook
        /// installed by <see cref="EnableModelChangeTracking"/> does not
        /// echo the change as a <c>BaseModelChangeEvent</c>.
        /// </remarks>
        public static void BumpNodeVersion(this NodeState node, ISystemContext context)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            PropertyState<string>? nodeVersion = GetNodeVersionProperty(node);
            if (nodeVersion == null)
            {
                return;
            }

            ulong current = 0;
            if (!string.IsNullOrEmpty(nodeVersion.Value)
                && !ulong.TryParse(nodeVersion.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out current))
            {
                current = 0;
            }

            unchecked
            {
                current++;
                if (current == 0)
                {
                    current = 1;
                }
            }

            bool previous = s_isFrameworkBump.Value;
            s_isFrameworkBump.Value = true;
            try
            {
                nodeVersion.Value = current.ToString(CultureInfo.InvariantCulture);
                nodeVersion.Timestamp = DateTime.UtcNow;
                nodeVersion.StatusCode = StatusCodes.Good;
                nodeVersion.ClearChangeMasks(context, false);
            }
            finally
            {
                s_isFrameworkBump.Value = previous;
            }
        }

        /// <summary>
        /// Marks <paramref name="node"/> as eligible to trigger
        /// <c>ModelChangeEvents</c> by attaching a <c>NodeVersion</c>
        /// property in namespace <paramref name="namespaceIndex"/> if
        /// none is present, and wiring an <c>OnWriteValue</c> handler
        /// that posts a <c>BaseModelChangeEvent</c> through
        /// <paramref name="raiseBaseModelChangeEvent"/> whenever the
        /// property is written by an external client. Framework-driven
        /// bumps (see <see cref="BumpNodeVersion"/>) are suppressed via
        /// an AsyncLocal guard so they do not echo.
        /// </summary>
        /// <param name="node">The node to enable tracking on.</param>
        /// <param name="namespaceIndex">
        /// Namespace index for the new NodeVersion property when one is
        /// attached.
        /// </param>
        /// <param name="raiseBaseModelChangeEvent">
        /// Callback invoked when an external client writes the
        /// NodeVersion. Typically wired to
        /// <c>AsyncCustomNodeManager.RaiseBaseModelChangeEvent</c>.
        /// </param>
        /// <returns>
        /// The existing or newly attached NodeVersion property. Always
        /// non-null.
        /// </returns>
        public static PropertyState<string> EnableModelChangeTracking(
            this NodeState node,
            ushort namespaceIndex,
            Action<ISystemContext, NodeState>? raiseBaseModelChangeEvent = null)
        {
            if (node == null)
            {
                throw new ArgumentNullException(nameof(node));
            }

            PropertyState<string>? nodeVersion = GetNodeVersionProperty(node);
            if (nodeVersion == null)
            {
                nodeVersion = new PropertyState<string>.Implementation<VariantBuilder>(node)
                {
                    SymbolicName = BrowseNames.NodeVersion,
                    BrowseName = new QualifiedName(BrowseNames.NodeVersion, 0),
                    DisplayName = new LocalizedText(BrowseNames.NodeVersion),
                    DataType = DataTypeIds.String,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                    Value = "1",
                    ReferenceTypeId = ReferenceTypeIds.HasProperty,
                    TypeDefinitionId = VariableTypeIds.PropertyType,
                    NodeId = new NodeId(Guid.NewGuid(), namespaceIndex)
                };
                node.AddChild(nodeVersion);
            }

            if (raiseBaseModelChangeEvent != null)
            {
                NodeState owner = node;
                nodeVersion.OnWriteValue = (
                    ISystemContext ctx,
                    NodeState target,
                    NumericRange indexRange,
                    QualifiedName dataEncoding,
                    ref Variant value,
                    ref StatusCode statusCode,
                    ref DateTimeUtc timestamp) =>
                {
                    if (target is PropertyState<string> property
                        && value.TryGetValue(out string? newValue))
                    {
                        property.Value = newValue;
                    }

                    if (!IsInsideFrameworkNodeVersionBump)
                    {
                        raiseBaseModelChangeEvent(ctx, owner);
                    }

                    return ServiceResult.Good;
                };
            }

            return nodeVersion;
        }
    }
}
