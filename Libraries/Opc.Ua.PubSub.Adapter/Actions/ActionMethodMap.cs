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
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;

namespace Opc.Ua.PubSub.Adapter.Actions
{
    /// <summary>
    /// Maps a <see cref="PubSubActionTarget"/> to the external OPC UA object
    /// and method an <see cref="ServerActionHandler"/> invokes. A
    /// target is identified by its <see cref="PubSubActionTarget.DataSetWriterId"/>
    /// and <see cref="PubSubActionTarget.ActionTargetId"/> pair, or by its
    /// <see cref="PubSubActionTarget.ActionName"/>. Resolution prefers the
    /// writer/target pair and falls back to the action name. The fluent
    /// <c>Add</c> overloads return the same instance so multiple targets can be
    /// registered in a single expression.
    /// </summary>
    public sealed class ActionMethodMap
    {
        private readonly Dictionary<(ushort, ushort), ActionMethodBinding> m_byTargetId
            = [];
        private readonly Dictionary<string, ActionMethodBinding> m_byActionName
            = new(StringComparer.Ordinal);

        /// <summary>
        /// Maps a DataSetWriter/ActionTarget pair to the external object and
        /// method to call.
        /// </summary>
        /// <param name="dataSetWriterId">
        /// The DataSetWriterId that owns the action metadata.
        /// </param>
        /// <param name="actionTargetId">
        /// The ActionTargetId unique within the action metadata.
        /// </param>
        /// <param name="objectId">
        /// The external object that provides the method to call.
        /// </param>
        /// <param name="methodId">
        /// The external method invoked for the action.
        /// </param>
        /// <param name="outputFieldNames">
        /// Optional output field names applied, in order, to the method's
        /// output arguments.
        /// </param>
        /// <returns>
        /// This instance, to allow fluent registration of multiple targets.
        /// </returns>
        public ActionMethodMap Add(
            ushort dataSetWriterId,
            ushort actionTargetId,
            NodeId objectId,
            NodeId methodId,
            ArrayOf<string> outputFieldNames = default)
        {
            m_byTargetId[(dataSetWriterId, actionTargetId)] =
                new ActionMethodBinding(objectId, methodId, outputFieldNames);
            return this;
        }

        /// <summary>
        /// Maps an action name to the external object and method to call.
        /// </summary>
        /// <param name="actionName">
        /// The action name used to resolve the target.
        /// </param>
        /// <param name="objectId">
        /// The external object that provides the method to call.
        /// </param>
        /// <param name="methodId">
        /// The external method invoked for the action.
        /// </param>
        /// <param name="outputFieldNames">
        /// Optional output field names applied, in order, to the method's
        /// output arguments.
        /// </param>
        /// <returns>
        /// This instance, to allow fluent registration of multiple targets.
        /// </returns>
        public ActionMethodMap Add(
            string actionName,
            NodeId objectId,
            NodeId methodId,
            ArrayOf<string> outputFieldNames = default)
        {
            if (string.IsNullOrEmpty(actionName))
            {
                throw new ArgumentException(
                    "Action name must be specified.", nameof(actionName));
            }
            m_byActionName[actionName] =
                new ActionMethodBinding(objectId, methodId, outputFieldNames);
            return this;
        }

        /// <summary>
        /// Maps an action name to an external object and method addressed by
        /// relative <em>browse paths</em> (for example <c>/2:Demo</c> and
        /// <c>/2:Demo/2:ResetCounters</c>) instead of concrete node ids. The
        /// paths are resolved against the server the first time the action is
        /// invoked. See <see cref="NodeBrowsePath"/> for the supported syntax.
        /// </summary>
        /// <param name="actionName">
        /// The action name used to resolve the target.
        /// </param>
        /// <param name="objectBrowsePath">
        /// The relative browse path of the external object that provides the
        /// method to call.
        /// </param>
        /// <param name="methodBrowsePath">
        /// The relative browse path of the external method invoked for the
        /// action.
        /// </param>
        /// <param name="outputFieldNames">
        /// Optional output field names applied, in order, to the method's
        /// output arguments.
        /// </param>
        /// <returns>
        /// This instance, to allow fluent registration of multiple targets.
        /// </returns>
        public ActionMethodMap Add(
            string actionName,
            string objectBrowsePath,
            string methodBrowsePath,
            ArrayOf<string> outputFieldNames = default)
        {
            return Add(
                actionName,
                NodeBrowsePath.ToNodeId(objectBrowsePath),
                NodeBrowsePath.ToNodeId(methodBrowsePath),
                outputFieldNames);
        }

        /// <summary>
        /// Resolves the external object/method binding for the supplied action
        /// target. The DataSetWriter/ActionTarget pair is tried first, then the
        /// action name.
        /// </summary>
        /// <param name="target">
        /// The action target to resolve.
        /// </param>
        /// <param name="binding">
        /// When this method returns <c>true</c>, the resolved binding;
        /// otherwise the default value.
        /// </param>
        /// <returns>
        /// <c>true</c> when a binding was found; otherwise <c>false</c>.
        /// </returns>
        public bool TryResolve(
            PubSubActionTarget target,
            out ActionMethodBinding binding)
        {
            if (target is not null)
            {
                if (m_byTargetId.TryGetValue(
                    (target.DataSetWriterId, target.ActionTargetId), out binding))
                {
                    return true;
                }
                if (!string.IsNullOrEmpty(target.ActionName)
                    && m_byActionName.TryGetValue(target.ActionName, out binding))
                {
                    return true;
                }
            }
            binding = default;
            return false;
        }
    }
}
