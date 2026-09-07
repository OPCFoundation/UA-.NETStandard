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
        internal static void RegisterCreatedNode(
            INodeManagerBuilder builder,
            NodeState node)
        {
            if (builder.NodeManager is AsyncCustomNodeManager manager)
            {
                manager.AddPredefinedNodeSynchronously(node);
            }
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
            if (firstSource != null &&
                builder.NodeManager is AsyncCustomNodeManager manager)
            {
                // Only claim ownership when this alarm actually inserted the
                // registration. AddRootNotifierSynchronously is an upsert, so claiming
                // it unconditionally would have teardown remove a pre-existing root
                // notifier — along with its event callback and HasNotifier reference.
                bool alreadyRegistered = manager.IsRootNotifier(firstSource.NodeId);
                manager.AddRootNotifierSynchronously(firstSource);
                rootNotifier = alreadyRegistered ? null : firstSource;
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
