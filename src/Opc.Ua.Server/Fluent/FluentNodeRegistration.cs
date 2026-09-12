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

using System.Collections.Generic;

namespace Opc.Ua.Server.Fluent
{
    internal static class FluentNodeRegistration
    {
        internal static NodeBuilder GetHandlerBuilder(INodeManagerBuilder builder, NodeState node)
        {
            NodeManagerBuilder owner = builder as NodeManagerBuilder ??
                FluentNodeManagerBase.TryResolveAttachedBuilder(builder) ??
                throw ServiceResultException.Create(StatusCodes.BadConfigurationError,
                    "The node manager does not expose a fluent handler dispatcher.");
            owner.ThrowIfSealed();
            return new NodeBuilder(owner, node);
        }

        internal static void EnsureGraphAuthoringOpen(INodeManagerBuilder builder)
        {
            NodeManagerBuilder? owner = builder as NodeManagerBuilder ??
                FluentNodeManagerBase.TryResolveAttachedBuilder(builder);
            owner?.ThrowIfSealed();
        }

        /// <summary>
        /// Mints the NodeId for a node the fluent surface just created.
        /// </summary>
        /// <remarks>
        /// Routed through the owning NodeManager so that a fluent graph
        /// obeys the same <see cref="NodeIdAssignmentMode"/> as the rest of
        /// the manager, instead of the ambiguous
        /// <c>{parentIdentifier}_{browseName}</c> concatenation the fluent
        /// builders used to each spell out for themselves. The node is
        /// created here and there is nothing to preserve, so the NodeId is
        /// cleared first to say so.
        /// </remarks>
        /// <param name="builder">The builder that owns the node.</param>
        /// <param name="node">The freshly created node.</param>
        internal static void AssignNodeId(
            INodeManagerBuilder builder,
            NodeState node)
        {
            node.NodeId = NodeId.Null;
            node.NodeId = builder.NodeManager.New(builder.Context, node);
        }

        internal static void RegisterCreatedNode(
            INodeManagerBuilder builder,
            NodeState node)
        {
            builder.NodeManager.AddNode(node);
        }

        /// <summary>
        /// Promotes the notifier chain above an alarm source and registers the topmost
        /// object as a root notifier.
        /// </summary>
        /// <returns>
        /// Exactly what was changed, so that teardown can undo it. Nodes that already
        /// carried SubscribeToEvents are not reported: they were not ours to clear.
        /// </returns>
        internal static AlarmEventSourceRegistration RegisterAlarmEventSource(
            INodeManagerBuilder builder,
            NodeState source)
        {
            BaseObjectState? firstSource = null;
            var promoted = new List<BaseObjectState>();
            for (NodeState? current = source; current != null;)
            {
                if (current is BaseObjectState notifier)
                {
                    firstSource ??= notifier;
                    if ((notifier.EventNotifier & EventNotifiers.SubscribeToEvents) == 0)
                    {
                        notifier.EventNotifier |= EventNotifiers.SubscribeToEvents;
                        promoted.Add(notifier);
                    }
                }

                current = current is BaseInstanceState instance ? instance.Parent : null;
            }

            BaseObjectState? rootNotifier = null;
            if (firstSource != null)
            {
                // Only claim ownership when this alarm actually inserted the
                // registration. AddRootNotifier is an upsert, so claiming it
                // unconditionally would have teardown remove a pre-existing root
                // notifier — along with its event callback and HasNotifier reference.
                //
                // The probe needs the concrete manager. Where it is unavailable we
                // claim nothing: leaving a registration behind is the lesser harm
                // against tearing down one that was never ours.
                bool owned =
                    builder.NodeManager is AsyncCustomNodeManager manager &&
                    !manager.IsRootNotifier(firstSource.NodeId);

                builder.NodeManager.AddRootNotifier(firstSource);
                rootNotifier = owned ? firstSource : null;
            }

            return new AlarmEventSourceRegistration(promoted, rootNotifier);
        }
    }

    /// <summary>
    /// Records what registering an alarm event source changed in the address space.
    /// </summary>
    internal sealed class AlarmEventSourceRegistration
    {
        public AlarmEventSourceRegistration(
            List<BaseObjectState> promotedNotifiers,
            BaseObjectState? rootNotifier)
        {
            PromotedNotifiers = promotedNotifiers;
            RootNotifier = rootNotifier;
        }

        /// <summary>
        /// Gets the nodes whose EventNotifier this registration turned on.
        /// </summary>
        public List<BaseObjectState> PromotedNotifiers { get; }

        /// <summary>
        /// Gets the node registered as a root notifier, if any.
        /// </summary>
        public BaseObjectState? RootNotifier { get; }
    }
}
