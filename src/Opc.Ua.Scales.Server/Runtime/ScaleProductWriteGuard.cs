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
using System.Threading.Tasks;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Composes product session-owner access and native cache writes without replacing application value handlers.
    /// </summary>
    internal sealed class ScaleProductWriteGuard
    {
        /// <summary>
        /// Captures the existing callbacks and watches for subsequently materialised children.
        /// </summary>
        private ScaleProductWriteGuard(
            ScaleRuntimeServices services,
            ProductState? product,
            ScaleDeviceState? scaleRoot,
            NodeState node,
            ScaleWriteGate? writeGate)
        {
            m_services = services;
            m_product = product;
            m_scaleRoot = scaleRoot;
            m_writeGate = writeGate;
            m_stateChanged = node.OnStateChanged;
            node.OnStateChanged = OnStateChanged;
            if (product != null && node is BaseVariableState variable)
            {
                m_readUserAccessLevel = variable.OnReadUserAccessLevel;
                variable.OnReadUserAccessLevel = ReadUserAccessLevel;
            }
        }

        /// <summary>
        /// Binds every product descendant in a subtree, including a separately registered optional member.
        /// </summary>
        internal static void Bind(ScaleRuntimeServices services, NodeState node, ScaleWriteGate? writeGate)
        {
            ProductState? product = null;
            ScaleDeviceState? scaleRoot = null;
            for (NodeState? ancestor = node; ancestor is BaseInstanceState instance; ancestor = instance.Parent)
            {
                if (instance is ProductState owner)
                {
                    product = owner;
                }
                if (instance is ScaleDeviceState scale)
                {
                    scaleRoot = scale;
                    break;
                }
            }
            Bind(services, node, product, scaleRoot, writeGate);
        }

        /// <summary>
        /// Composes request write access with the product lock while leaving the manager's own context trusted.
        /// </summary>
        private ServiceResult ReadUserAccessLevel(ISystemContext context, NodeState node, ref byte value)
        {
            ServiceResult result = m_readUserAccessLevel?.Invoke(context, node, ref value) ?? ServiceResult.Good;
            if (ServiceResult.IsBad(result) ||
                ((value & AccessLevels.CurrentWrite) != 0 &&
                    m_product is { } product &&
                    ServiceResult.IsBad(ScaleValues.CheckLock(m_services, context, product.NodeId))))
            {
                value = (byte)(value & ~AccessLevels.CurrentWrite);
            }
            return result;
        }

        /// <summary>
        /// Protects newly materialised members before forwarding their parent's change notification.
        /// </summary>
        private void OnStateChanged(ISystemContext context, NodeState node, NodeStateChangeMasks changeMasks)
        {
            if ((changeMasks & NodeStateChangeMasks.Children) != 0)
            {
                m_services.BindProductWriteGuards(node, m_writeGate);
            }
            m_stateChanged?.Invoke(context, node, changeMasks);
        }

        /// <summary>
        /// Walks a subtree while retaining its nearest product owner.
        /// </summary>
        private static void Bind(
            ScaleRuntimeServices services,
            NodeState node,
            ProductState? product,
            ScaleDeviceState? scaleRoot,
            ScaleWriteGate? writeGate)
        {
            product = node as ProductState ?? product;
            scaleRoot = node as ScaleDeviceState ?? scaleRoot;
            writeGate ??= FindWriteGate(services, node, product, scaleRoot);
            if (writeGate != null)
            {
                node.BindWriteOperationExecutor(writeGate.NodeWriteExecutor);
                if (node is BaseVariableState variable &&
                    (variable.AccessLevel & AccessLevels.CurrentWrite) != 0 &&
                    variable.OnWriteValueAsync == null &&
                    variable.OnSimpleWriteValueAsync == null &&
                    variable.OnWriteValue == null &&
                    variable.OnSimpleWriteValue == null)
                {
                    if (IsOrdinaryCacheVariable(variable))
                    {
                        variable.OnWriteValueAsync = WriteCachedValueAsync;
                    }
                    else if (IsNativeEnumerationVariable(variable))
                    {
                        variable.OnWriteValueAsync = MultiStateValueDiscreteState.WriteCachedValueAsync;
                    }
                }
            }
            if ((product != null || writeGate != null) &&
                (node.OnStateChanged?.Target is not ScaleProductWriteGuard guard ||
                    guard.m_services != services ||
                    guard.m_product != product ||
                    guard.m_scaleRoot != scaleRoot ||
                    guard.m_writeGate != writeGate))
            {
                _ = new ScaleProductWriteGuard(services, product, scaleRoot, node, writeGate);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(services.Context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is ScaleDeviceState childScale &&
                    scaleRoot != null &&
                    !ReferenceEquals(childScale, scaleRoot))
                {
                    continue;
                }
                Bind(services, child, product, scaleRoot, writeGate);
            }
        }

        /// <summary>
        /// Limits automatic cache policy to exact audited stock types, never their derived application types.
        /// </summary>
        private static bool IsOrdinaryCacheVariable(BaseVariableState variable)
        {
            Type type = variable.GetType();
            if (type == typeof(BaseDataVariableState) ||
                type == typeof(PropertyState) ||
                type == typeof(DataItemState) ||
                type == typeof(AnalogItemState) ||
                type == typeof(AnalogUnitState) ||
                type == typeof(AnalogUnitRangeState) ||
                type == typeof(TwoStateDiscreteState) ||
                type == typeof(TargetItemState) ||
                type == typeof(MeasuredItemState) ||
                type == typeof(WeightItemState))
            {
                return true;
            }
            if (!type.IsGenericType)
            {
                return false;
            }
            Type definition = type.GetGenericTypeDefinition();
            return definition == typeof(BaseDataVariableState<>.Implementation<>) ||
                definition == typeof(PropertyState<>.Implementation<>) ||
                definition == typeof(DataItemState<>.Implementation<>) ||
                definition == typeof(AnalogItemState<>.Implementation<>) ||
                definition == typeof(AnalogUnitState<>.Implementation<>) ||
                definition == typeof(AnalogUnitRangeState<>.Implementation<>) ||
                definition == typeof(TargetItemState<>.Implementation<>) ||
                definition == typeof(MeasuredItemState<>.Implementation<>);
        }

        /// <summary>
        /// Selects only the stock semantic type whose sparse enum and dependent text writes are native.
        /// </summary>
        private static bool IsNativeEnumerationVariable(BaseVariableState variable)
        {
            Type type = variable.GetType();
            return type == typeof(MultiStateValueDiscreteState) ||
                (type.IsGenericType &&
                    type.GetGenericTypeDefinition() == typeof(MultiStateValueDiscreteState<>.Implementation<>));
        }

        /// <summary>
        /// Accepts the Scales cache-writing policy without adapting synchronous application callbacks.
        /// </summary>
        /// <remarks>
        /// The native BaseVariableState async core validates access, status, timestamp and type before this
        /// callback and commits the value, including copy policy and indexed range rejection, afterward.
        /// This callback does not mutate the cache. A synchronous handler installed after binding requires
        /// its own native async implementation;
        /// accepting its write here would otherwise bypass the application's write semantics. Variables with
        /// SelectionList and MultiStateDiscrete overrides and all unknown types are excluded by the exact
        /// type list. MultiStateValueDiscrete uses its own native enum policy instead.
        /// </remarks>
        private static ValueTask<AttributeWriteResult> WriteCachedValueAsync(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            Variant value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variable = (BaseVariableState)node;
            ServiceResult result = variable.OnWriteValue != null || variable.OnSimpleWriteValue != null
                ? new ServiceResult(StatusCodes.BadNotSupported)
                : ServiceResult.Good;
            return new ValueTask<AttributeWriteResult>(
                new AttributeWriteResult(result) { UseOrdinaryCacheWrite = true });
        }

        /// <summary>
        /// Retains an existing product gate when a manager registers an already bound subtree.
        /// </summary>
        private static ScaleWriteGate? FindWriteGate(
            ScaleRuntimeServices services,
            NodeState node,
            ProductState? product,
            ScaleDeviceState? scaleRoot)
        {
            if (product == null && scaleRoot == null)
            {
                return null;
            }

            for (NodeState? ancestor = node;
                ancestor is BaseInstanceState instance;
                ancestor = instance.Parent)
            {
                if (instance.OnStateChanged?.Target is ScaleProductWriteGuard guard &&
                    guard.m_services == services &&
                    guard.m_product == product &&
                    guard.m_scaleRoot == scaleRoot &&
                    guard.m_writeGate is { } existingGate)
                {
                    return existingGate;
                }
            }
            return null;
        }

        /// <summary>The runtime supplying the DI lock service.</summary>
        private readonly ScaleRuntimeServices m_services;

        /// <summary>The product whose lock restricts these values.</summary>
        private readonly ProductState? m_product;

        /// <summary>
        /// The scale that owns this subtree, when present.
        /// </summary>
        private readonly ScaleDeviceState? m_scaleRoot;

        /// <summary>
        /// The executor for the owning scale.
        /// </summary>
        private readonly ScaleWriteGate? m_writeGate;

        /// <summary>The existing access-level policy, which must never be broadened.</summary>
        private readonly NodeAttributeEventHandler<byte>? m_readUserAccessLevel;

        /// <summary>The existing state-change callback.</summary>
        private readonly NodeStateChangedHandler? m_stateChanged;
    }
}
