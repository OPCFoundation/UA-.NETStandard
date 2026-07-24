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
using Opc.Ua.Server.Fluent;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Fluent configurator for a single DI <c>FunctionalGroupType</c>
    /// instance attached to a device (or sub-component).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A functional group aggregates related parameters, methods, or
    /// observations by topic. The DI spec defines eight well-known
    /// groups (see <see cref="WellKnownFunctionalGroups"/>); custom
    /// groups are added via
    /// <see cref="TopologyElementBuilderExtensions.WithFunctionalGroup{TElement}(ITopologyElementBuilder{TElement}, QualifiedName, Action{IFunctionalGroupBuilder})"/>.
    /// </para>
    /// <para>
    /// The builder exposes a typed <see cref="Group"/> handle for direct
    /// state manipulation plus an <see cref="Node"/> view that plugs into
    /// the standard fluent extensions (<c>WithProperty</c>,
    /// <c>Organizes</c>, <c>CreateInstance</c>, <c>CreateLimitAlarm</c>,
    /// ...).
    /// </para>
    /// </remarks>
    public interface IFunctionalGroupBuilder
    {
        /// <summary>
        /// The functional-group state being configured.
        /// </summary>
        FunctionalGroupState Group { get; }

        /// <summary>
        /// A standard <see cref="INodeBuilder"/> view of the group, for
        /// composition with the rest of the fluent surface.
        /// </summary>
        INodeBuilder Node { get; }

        /// <summary>
        /// The owning system context.
        /// </summary>
        ISystemContext Context { get; }

        /// <summary>
        /// Adds a HasComponent/Organizes reference from the group to
        /// <paramref name="target"/>. Functional groups typically use
        /// <c>Organizes</c> to aggregate parameters without owning them.
        /// </summary>
        IFunctionalGroupBuilder Organizes(NodeState target);

        /// <summary>
        /// Adds an <c>Organizes</c> reference to a node identified by
        /// <paramref name="targetNodeId"/>. Use this for cross-manager
        /// references.
        /// </summary>
        IFunctionalGroupBuilder Organizes(NodeId targetNodeId);

        /// <summary>
        /// Invokes <paramref name="configure"/> with the
        /// <see cref="Node"/> view for further fluent wiring.
        /// </summary>
        IFunctionalGroupBuilder Configure(Action<INodeBuilder> configure);
    }
}
