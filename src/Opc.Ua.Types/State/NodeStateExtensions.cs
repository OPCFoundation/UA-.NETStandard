/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Node state extension methods
    /// </summary>
    public static class NodeStateExtensions
    {
        /// <summary>
        /// Get all children
        /// </summary>
        /// <param name="state"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public static IEnumerable<BaseInstanceState> EnumerateChildren(
            this NodeState state,
            SystemContext context)
        {
            List<BaseInstanceState> list = [];
            state.GetChildren(context, list);
            return list;
        }

        /// <summary>
        /// Get all references
        /// </summary>
        /// <param name="state"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public static IEnumerable<IReference> EnumerateReferences(
            this NodeState state,
            SystemContext context)
        {
            List<IReference> list = [];
            state.GetReferences(context, list);
            return list;
        }
    }
}
