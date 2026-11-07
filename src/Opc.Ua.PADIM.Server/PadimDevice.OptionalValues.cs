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

namespace Opc.Ua.PADIM.Server
{
    public sealed partial class PadimDevice
    {
        /// <summary>
        /// Materializes and binds a declared optional PA-DIM variable while preserving its model metadata.
        /// </summary>
        public async ValueTask<PadimValueBinding> AddOptionalValueAsync(
            BaseInstanceState parent,
            QualifiedName browseName,
            string key,
            IPadimValueProvider provider,
            CancellationToken cancellationToken = default)
        {
            EnsureOwned(parent);
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException("A declared optional member name is required.", nameof(browseName));
            }
            int padimNamespaceIndex = Context.NamespaceUris.GetIndex(Namespaces.PADIM);
            if (padimNamespaceIndex < 0 || browseName.NamespaceIndex != padimNamespaceIndex)
            {
                throw new ArgumentException(
                    "The optional member must use the PA-DIM namespace.", nameof(browseName));
            }
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("A stable provider key is required.", nameof(key));
            }
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            BaseVariableState declaration = FindOptionalVariableDeclaration(parent.TypeDefinitionId, browseName);
            PadimValueBinding? binding = null;
            await RunAsync(async ct =>
            {
                BaseVariableState variable;
                bool created;
                if (parent.FindChild(Context, browseName) is BaseInstanceState existing)
                {
                    variable = existing as BaseVariableState ??
                        throw new InvalidOperationException("The selected optional member is not a variable.");
                    created = false;
                    if (variable.TypeDefinitionId != declaration.TypeDefinitionId)
                    {
                        throw new InvalidOperationException(
                            "The existing optional member does not match its published declaration.");
                    }
                }
                else
                {
                    variable = PadimGroupBuilder.Instantiate(Context, declaration, parent, browseName)
                        as BaseVariableState ??
                        throw new InvalidOperationException(
                            "The selected optional member did not instantiate as a variable.");
                    created = true;
                    DisableUnboundWrites(variable);
                }

                try
                {
                    binding = BindValue(variable, key, provider);
                    await Manager.AddPredefinedNodeAsync(State, ct).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    if (binding != null)
                    {
                        lock (m_bindingGate)
                        {
                            m_bindings.RemoveAll(candidate => ReferenceEquals(candidate.Variable, variable));
                        }
                    }
                    if (!created)
                    {
                        throw;
                    }
                    parent.RemoveChild(variable);
                    try
                    {
                        await Manager.DeleteNodeAsync(Context, variable.NodeId, CancellationToken.None)
                            .ConfigureAwait(false);
                    }
                    catch (Exception cleanupFailure)
                    {
                        throw new AggregateException(
                            "Optional member registration and rollback failed.", failure, cleanupFailure);
                    }
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);
            return binding!;
        }

        /// <summary>
        /// Resolves an exact optional variable declaration from the parent's type and its supertypes.
        /// </summary>
        private BaseVariableState FindOptionalVariableDeclaration(NodeId typeId, QualifiedName browseName)
        {
            var visited = new HashSet<NodeId>();
            while (!typeId.IsNull && visited.Add(typeId))
            {
                if (Manager.FindPredefinedNode(typeId)?.FindChild(Context, browseName)
                    is BaseVariableState declaration)
                {
                    if (declaration.ModellingRuleId != Opc.Ua.Types.ObjectIds.ModellingRule_Optional)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadNotSupported,
                            "The selected member '{0}' is not an optional variable.",
                            browseName);
                    }
                    return declaration;
                }
                typeId = Context.TypeTable.FindSuperType(typeId);
            }
            throw ServiceResultException.Create(
                StatusCodes.BadNotSupported,
                "The parent type does not declare optional variable '{0}'.",
                browseName);
        }
    }
}
