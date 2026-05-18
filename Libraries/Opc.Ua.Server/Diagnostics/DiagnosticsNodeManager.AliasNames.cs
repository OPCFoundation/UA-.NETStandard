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
using Opc.Ua.Server.AliasNames;

namespace Opc.Ua.Server
{
    /// <summary>
    /// OPC UA Part 17 (Alias Names) integration for the standard
    /// well-known nodes loaded by <c>DiagnosticsNodeManager</c> from
    /// <c>Opc.Ua.NodeSet2.xml</c>.
    /// </summary>
    /// <remarks>
    /// The standard NodeSet ships three <c>AliasNameCategoryType</c>
    /// instances under the Server object (Part 17 §9):
    /// <list type="bullet">
    ///   <item><description><c>Aliases (i=23470)</c> — with mandatory
    ///   <c>FindAlias (i=23476)</c> and a <c>LastChange (i=32852)</c>
    ///   property;</description></item>
    ///   <item><description><c>TagVariables (i=23479)</c> — with mandatory
    ///   <c>FindAlias (i=23485)</c>;</description></item>
    ///   <item><description><c>Topics (i=23488)</c> — with mandatory
    ///   <c>FindAlias (i=23494)</c>.</description></item>
    /// </list>
    /// None of the optional Part 17 children
    /// (<c>FindAliasVerbose</c>/<c>AddAliasesToCategory</c>/<c>DeleteAliasesFromCategory</c>)
    /// are instantiated by the standard NodeSet on these well-known nodes,
    /// so this binder only wires <c>FindAlias</c> and (for
    /// <c>Aliases</c>) <c>LastChange</c>. Optional methods on
    /// application-defined categories are wired by
    /// <see cref="AliasNameNodeManager"/> instead.
    /// </remarks>
    public partial class DiagnosticsNodeManager
    {
        /// <summary>
        /// Resolves the server's <see cref="IAliasNameStoreRegistry"/>
        /// (via the optional <see cref="IAliasNameStoreRegistryProvider"/>
        /// interface) and wires the standard well-known
        /// <c>Aliases</c>/<c>TagVariables</c>/<c>Topics</c> <c>FindAlias</c>
        /// methods (plus <c>Aliases.LastChange</c>) to dispatch through
        /// it. The wiring is "live" — the registry is queried at each
        /// call rather than snapshotted at load time — so stores
        /// registered after this point are also reachable.
        /// </summary>
        private void WireStandardAliasMethods()
        {
            IAliasNameStoreRegistry? registry =
                (Server as IAliasNameStoreRegistryProvider)?.AliasNameStoreRegistry;
            if (registry == null)
            {
                return;
            }

            m_aliasRegistry = registry;
            m_aliasRegistry.Changed += OnAliasRegistryChanged;

            WireStandardCategory(ObjectIds.Aliases, includeLastChange: true);
            WireStandardCategory(ObjectIds.TagVariables, includeLastChange: false);
            WireStandardCategory(ObjectIds.Topics, includeLastChange: false);
        }

        private void WireStandardCategory(NodeId categoryId, bool includeLastChange)
        {
            AliasNameCategoryState? category =
                FindPredefinedNode<AliasNameCategoryState>(categoryId);
            if (category == null)
            {
                return;
            }

            if (category.FindAlias != null)
            {
                category.FindAlias.OnCallAsync = (ctx, method, objId, pattern, refType, ct) =>
                    AliasNameMethodDispatcher.FindAliasAsync(
                        m_aliasRegistry!,
                        Server.TypeTree,
                        objId.IsNull ? categoryId : objId,
                        pattern,
                        refType,
                        ct);
            }

            if (includeLastChange && category.LastChange != null)
            {
                uint? seed = null;
                foreach (IAliasNameStore store in m_aliasRegistry!.Stores)
                {
                    uint? v = store.GetLastChange(categoryId);
                    if (v.HasValue)
                    {
                        seed = v;
                        break;
                    }
                }
                if (seed.HasValue)
                {
                    category.LastChange.Value = seed.Value;
                    category.LastChange.ClearChangeMasks(SystemContext, false);
                }
                m_aliasesLastChangeNode = category.LastChange;
            }
        }

        private void OnAliasRegistryChanged(object? sender, AliasStoreChangedEventArgs e)
        {
            // Only the standard Aliases (i=23470) node carries a LastChange
            // property in the shipped NodeSet; mirror its store value.
            if (m_aliasesLastChangeNode != null
                && e.CategoryId == ObjectIds.Aliases)
            {
                m_aliasesLastChangeNode.Value = e.LastChange;
                m_aliasesLastChangeNode.ClearChangeMasks(SystemContext, false);
            }
        }

        private IAliasNameStoreRegistry? m_aliasRegistry;
        private PropertyState<uint>? m_aliasesLastChangeNode;
    }
}
