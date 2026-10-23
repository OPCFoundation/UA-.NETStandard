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

using System.Collections.Generic;

namespace Opc.Ua.Scales.Server.Runtime
{
    /// <summary>
    /// Adds the product's session-owner restriction without replacing its existing value handlers.
    /// </summary>
    internal sealed class ScaleProductWriteGuard
    {
        /// <summary>
        /// Captures the existing callbacks and watches for subsequently materialised children.
        /// </summary>
        private ScaleProductWriteGuard(ScaleRuntimeServices services, ProductState product, NodeState node)
        {
            m_services = services;
            m_product = product;
            m_stateChanged = node.OnStateChanged;
            node.OnStateChanged = OnStateChanged;
            if (node is BaseVariableState variable)
            {
                m_readUserAccessLevel = variable.OnReadUserAccessLevel;
                variable.OnReadUserAccessLevel = ReadUserAccessLevel;
            }
        }

        /// <summary>
        /// Binds every product descendant in a subtree, including a separately registered optional member.
        /// </summary>
        internal static void Bind(ScaleRuntimeServices services, NodeState node)
        {
            ProductState? product = null;
            for (NodeState? ancestor = node; ancestor is BaseInstanceState instance; ancestor = instance.Parent)
            {
                if (instance is ProductState owner)
                {
                    product = owner;
                    break;
                }
            }
            Bind(services, node, product);
        }

        /// <summary>
        /// Composes request write access with the product lock while leaving the manager's own context trusted.
        /// </summary>
        private ServiceResult ReadUserAccessLevel(ISystemContext context, NodeState node, ref byte value)
        {
            ServiceResult result = m_readUserAccessLevel?.Invoke(context, node, ref value) ?? ServiceResult.Good;
            if (ServiceResult.IsBad(result) ||
                ((value & AccessLevels.CurrentWrite) != 0 &&
                    ServiceResult.IsBad(ScaleValues.CheckLock(m_services, context, m_product.NodeId))))
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
                m_services.BindProductWriteGuards(node);
            }
            m_stateChanged?.Invoke(context, node, changeMasks);
        }

        /// <summary>
        /// Walks a subtree while retaining its nearest product owner.
        /// </summary>
        private static void Bind(ScaleRuntimeServices services, NodeState node, ProductState? product)
        {
            product = node as ProductState ?? product;
            if (product != null &&
                (node.OnStateChanged?.Target is not ScaleProductWriteGuard guard ||
                    guard.m_services != services ||
                    guard.m_product != product))
            {
                _ = new ScaleProductWriteGuard(services, product, node);
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(services.Context, children);
            foreach (BaseInstanceState child in children)
            {
                Bind(services, child, product);
            }
        }

        /// <summary>The runtime supplying the DI lock service.</summary>
        private readonly ScaleRuntimeServices m_services;

        /// <summary>The product whose lock restricts these values.</summary>
        private readonly ProductState m_product;

        /// <summary>The existing access-level policy, which must never be broadened.</summary>
        private readonly NodeAttributeEventHandler<byte>? m_readUserAccessLevel;

        /// <summary>The existing state-change callback.</summary>
        private readonly NodeStateChangedHandler? m_stateChanged;
    }
}
