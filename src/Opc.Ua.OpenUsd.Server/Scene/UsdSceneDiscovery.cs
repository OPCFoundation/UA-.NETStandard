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
using Opc.Ua.OpenUsd.Scene;

namespace Opc.Ua.OpenUsd.Server.Scene
{
    /// <summary>
    /// Discovery and Part 1 interoperability helpers for materialized scenes
    /// (draft OPC UA — OpenUSD Scene Materialization §4.3, §10).
    /// </summary>
    /// <remarks>
    /// A Server should expose its materialized stages under one well-known folder so a client
    /// starts at a single entry point and browses <c>HasComponent</c> into the prim tree:
    /// Part 1's <c>Server/OpenUSD/Stages</c> when the Bindings model is also implemented, or
    /// a standalone <c>Server/OpenUSDScene/Stages</c> otherwise.
    /// </remarks>
    public static class UsdSceneDiscovery
    {
        /// <summary>
        /// The standalone discovery root BrowseName.
        /// </summary>
        public const string OpenUsdSceneRootName = "OpenUSDScene";

        /// <summary>
        /// The BrowseName of the stages folder under either discovery root.
        /// </summary>
        public const string StagesFolderName = "Stages";

        /// <summary>
        /// The BrowseName of Part 1's <c>TargetNodeId</c> live-binding member (Part 1 §; the
        /// NodeId form of a binding target). Named by string so Part 2 authors it without a
        /// compile-time dependency on the Part 1 model (§4.2 — the two models are independent).
        /// </summary>
        public const string TargetNodeIdName = "TargetNodeId";

        /// <summary>
        /// Finds — or creates — the folder materialized stages are organized under.
        /// </summary>
        /// <param name="context">The server system context.</param>
        /// <param name="server">The <c>Server</c> Object.</param>
        /// <param name="ns">The OpenUSD Scene companion namespace index.</param>
        /// <param name="part1StagesFolder">Part 1's <c>Server/OpenUSD/Stages</c> folder when
        /// the Bindings model is also implemented; pass <c>null</c> for a standalone Server.
        /// When supplied it is used as-is, so one connector discovers both the external-stage
        /// bindings and the in-server materialized stages (§10).</param>
        /// <returns>The stages folder.</returns>
        public static FolderState EnsureStagesFolder(
            this ISystemContext context,
            NodeState server,
            ushort ns,
            FolderState? part1StagesFolder = null)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (server is null)
            {
                throw new ArgumentNullException(nameof(server));
            }
            if (part1StagesFolder != null)
            {
                return part1StagesFolder;
            }

            FolderState root = EnsureFolder(context, server, OpenUsdSceneRootName, ns);
            return EnsureFolder(context, root, StagesFolderName, ns);
        }

        /// <summary>
        /// Resolves the materialized attribute Variable a Part 1 live binding targets (§10).
        /// </summary>
        /// <remarks>
        /// Part 1 ≥ 0.3.0 carries the target two ways and a Server should author both, so
        /// path-resolving and NodeId-resolving connectors agree: the optional
        /// <c>TargetNodeId</c> names the Variable directly, and the mandatory
        /// <c>TargetPrimPath</c>/<c>TargetPropertyName</c> pair resolves to the same Variable.
        /// This helper resolves the path form against a materialization result; a caller that
        /// already has a NodeId does not need it.
        /// </remarks>
        /// <param name="result">The materialization to resolve against.</param>
        /// <param name="targetPrimPath">The absolute prim path, for example
        /// <c>/Plant/Pumps/P101/Pump/Impeller</c>.</param>
        /// <param name="targetPropertyName">The attribute name, for example
        /// <c>xformOp:rotateZ</c>.</param>
        /// <param name="attribute">The resolved attribute Variable.</param>
        /// <returns><c>true</c> when the target resolved.</returns>
        public static bool TryResolveBindingTarget(
            this UsdMaterializationResult result,
            string targetPrimPath,
            string targetPropertyName,
            out UsdAttributeState? attribute)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            attribute = null;
            if (string.IsNullOrEmpty(targetPrimPath) || string.IsNullOrEmpty(targetPropertyName))
            {
                return false;
            }
            string key = targetPrimPath + "." + targetPropertyName;
            if (result.AttributesByPath.TryGetValue(key, out UsdAttributeState? found))
            {
                attribute = found;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The NodeId a Part 1 binding should author into its <c>TargetNodeId</c> member for
        /// the given materialized attribute (§10).
        /// </summary>
        /// <param name="result">The materialization to resolve against.</param>
        /// <param name="targetPrimPath">The absolute prim path.</param>
        /// <param name="targetPropertyName">The attribute name.</param>
        /// <returns>The NodeId, or a null NodeId when the target does not resolve.</returns>
        public static NodeId ResolveBindingTargetNodeId(
            this UsdMaterializationResult result,
            string targetPrimPath,
            string targetPropertyName)
        {
            return result.TryResolveBindingTarget(
                targetPrimPath, targetPropertyName, out UsdAttributeState? attribute)
                    && attribute != null
                ? attribute.NodeId
                : NodeId.Null;
        }

        /// <summary>
        /// Stage-aware target resolution: resolves the path form only when the binding names
        /// <em>this</em> stage, so a Server hosting several materialized stages never returns a
        /// same-path attribute from the wrong stage (§10).
        /// </summary>
        /// <param name="result">The materialization to resolve against.</param>
        /// <param name="targetStage">The NodeId of the <c>UsdStageType</c> the binding targets
        /// (Part 1's <c>TargetStage</c>). A null NodeId fails closed — the stage-aware form
        /// requires a stage; use the two-argument overload for a single-stage Server.</param>
        /// <param name="targetPrimPath">The absolute prim path.</param>
        /// <param name="targetPropertyName">The attribute name.</param>
        /// <param name="attribute">The resolved attribute Variable.</param>
        /// <returns><c>true</c> when the binding names this stage and the target resolves.</returns>
        public static bool TryResolveBindingTarget(
            this UsdMaterializationResult result,
            NodeId targetStage,
            string targetPrimPath,
            string targetPropertyName,
            out UsdAttributeState? attribute)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            attribute = null;
            // A binding that carries a TargetStage must match this stage exactly; otherwise the
            // target belongs to a different stage and this result must not answer for it.
            if (targetStage.IsNull || result.Stage.NodeId != targetStage)
            {
                return false;
            }
            return result.TryResolveBindingTarget(
                targetPrimPath, targetPropertyName, out attribute);
        }

        /// <summary>
        /// Stage-aware target resolution across every materialized stage a Server hosts: the
        /// binding's <c>TargetStage</c> selects which stage answers, then the path resolves
        /// within it (§10).
        /// </summary>
        /// <param name="results">Every materialized stage the Server hosts.</param>
        /// <param name="targetStage">The NodeId of the stage the binding targets.</param>
        /// <param name="targetPrimPath">The absolute prim path.</param>
        /// <param name="targetPropertyName">The attribute name.</param>
        /// <param name="attribute">The resolved attribute Variable.</param>
        /// <returns><c>true</c> when exactly the named stage resolves the target.</returns>
        public static bool TryResolveBindingTarget(
            this IEnumerable<UsdMaterializationResult> results,
            NodeId targetStage,
            string targetPrimPath,
            string targetPropertyName,
            out UsdAttributeState? attribute)
        {
            if (results is null)
            {
                throw new ArgumentNullException(nameof(results));
            }
            attribute = null;
            foreach (UsdMaterializationResult result in results)
            {
                if (result != null &&
                    result.TryResolveBindingTarget(
                        targetStage, targetPrimPath, targetPropertyName, out attribute))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The NodeId a Part 1 binding should author into its <c>TargetNodeId</c> member,
        /// disambiguated by the binding's <c>TargetStage</c> across every hosted stage (§10).
        /// </summary>
        /// <param name="results">Every materialized stage the Server hosts.</param>
        /// <param name="targetStage">The NodeId of the stage the binding targets.</param>
        /// <param name="targetPrimPath">The absolute prim path.</param>
        /// <param name="targetPropertyName">The attribute name.</param>
        /// <returns>The NodeId, or a null NodeId when nothing resolves in the named stage.</returns>
        public static NodeId ResolveBindingTargetNodeId(
            this IEnumerable<UsdMaterializationResult> results,
            NodeId targetStage,
            string targetPrimPath,
            string targetPropertyName)
        {
            return results.TryResolveBindingTarget(
                targetStage, targetPrimPath, targetPropertyName, out UsdAttributeState? attribute)
                    && attribute != null
                ? attribute.NodeId
                : NodeId.Null;
        }

        /// <summary>
        /// Authors Part 1's <c>TargetNodeId</c> onto a supplied binding node so a Server can
        /// publish <em>both</em> target forms in one call: the caller has already set the
        /// mandatory <c>TargetPrimPath</c>/<c>TargetPropertyName</c> pair, and this resolves the
        /// same attribute to a NodeId and writes it as the optional <c>TargetNodeId</c> so
        /// NodeId-resolving and path-resolving connectors agree (§10).
        /// </summary>
        /// <remarks>
        /// The binding is typed only as a <see cref="NodeState"/> and the member is addressed by
        /// BrowseName, so Part 2 authors the Part 1 member without a compile-time dependency on
        /// the Part 1 model (§4.2 — "Neither model requires the other"). It fails closed: when
        /// the path does not resolve to a materialized attribute, nothing is authored and it
        /// returns <c>false</c>, so a binding never gains a <c>TargetNodeId</c> naming a node
        /// that is not in the address space.
        /// </remarks>
        /// <param name="result">The materialization the target lives in.</param>
        /// <param name="context">The server system context.</param>
        /// <param name="binding">The Part 1 live-binding Object to author onto.</param>
        /// <param name="targetPrimPath">The absolute prim path.</param>
        /// <param name="targetPropertyName">The attribute name.</param>
        /// <param name="part1NamespaceIndex">The namespace index of the Part 1 Bindings
        /// companion, which owns the <c>TargetNodeId</c> BrowseName.</param>
        /// <returns><c>true</c> when the target resolved and <c>TargetNodeId</c> was authored.</returns>
        public static bool TryAuthorBindingTargetNodeId(
            this UsdMaterializationResult result,
            ISystemContext context,
            NodeState binding,
            string targetPrimPath,
            string targetPropertyName,
            ushort part1NamespaceIndex)
        {
            if (result is null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            NodeId target = result.ResolveBindingTargetNodeId(targetPrimPath, targetPropertyName);
            if (target.IsNull)
            {
                return false;
            }
            SetTargetNodeId(context, binding, part1NamespaceIndex, target);
            return true;
        }

        /// <summary>
        /// Stage-aware overload of
        /// <see cref="TryAuthorBindingTargetNodeId(UsdMaterializationResult, ISystemContext, NodeState, string, string, ushort)"/>:
        /// the binding's <c>TargetStage</c> selects which of the Server's stages the target is
        /// resolved in before the <c>TargetNodeId</c> is authored (§10).
        /// </summary>
        /// <param name="results">Every materialized stage the Server hosts.</param>
        /// <param name="context">The server system context.</param>
        /// <param name="binding">The Part 1 live-binding Object to author onto.</param>
        /// <param name="targetStage">The NodeId of the stage the binding targets.</param>
        /// <param name="targetPrimPath">The absolute prim path.</param>
        /// <param name="targetPropertyName">The attribute name.</param>
        /// <param name="part1NamespaceIndex">The namespace index of the Part 1 Bindings
        /// companion.</param>
        /// <returns><c>true</c> when the target resolved and <c>TargetNodeId</c> was authored.</returns>
        public static bool TryAuthorBindingTargetNodeId(
            this IEnumerable<UsdMaterializationResult> results,
            ISystemContext context,
            NodeState binding,
            NodeId targetStage,
            string targetPrimPath,
            string targetPropertyName,
            ushort part1NamespaceIndex)
        {
            if (results is null)
            {
                throw new ArgumentNullException(nameof(results));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (binding is null)
            {
                throw new ArgumentNullException(nameof(binding));
            }

            NodeId target = results.ResolveBindingTargetNodeId(
                targetStage, targetPrimPath, targetPropertyName);
            if (target.IsNull)
            {
                return false;
            }
            SetTargetNodeId(context, binding, part1NamespaceIndex, target);
            return true;
        }

        /// <summary>
        /// Writes <paramref name="target"/> into the binding's <c>TargetNodeId</c> member,
        /// reusing the member when it already exists (a generated Part 1 binding) or creating a
        /// PropertyType Variable of DataType <c>NodeId</c> when it does not.
        /// </summary>
        private static void SetTargetNodeId(
            ISystemContext context, NodeState binding, ushort part1NamespaceIndex, NodeId target)
        {
            var browseName = new QualifiedName(TargetNodeIdName, part1NamespaceIndex);
            if (binding.FindChild(context, browseName) is BaseVariableState existing)
            {
                existing.Value = Variant.From(target);
                return;
            }

            var property = new PropertyState(binding)
            {
                BrowseName = browseName,
                DisplayName = new LocalizedText(TargetNodeIdName),
                TypeDefinitionId = Opc.Ua.VariableTypeIds.PropertyType,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                DataType = Opc.Ua.DataTypeIds.NodeId,
                ValueRank = ValueRanks.Scalar,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = Variant.From(target)
            };
            binding.AddChild(property);
            property.NodeId = context.RequireNodeIdFactory().New(context, property);
        }

        private static FolderState EnsureFolder(
            ISystemContext context, NodeState parent, string name, ushort ns)
        {
            var browseName = new QualifiedName(name, ns);
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is FolderState existing && existing.BrowseName == browseName)
                {
                    return existing;
                }
            }

            var folder = new FolderState(parent)
            {
                BrowseName = browseName,
                DisplayName = new LocalizedText(name),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent,
                EventNotifier = EventNotifiers.None
            };
            parent.AddChild(folder);
            folder.NodeId = context.RequireNodeIdFactory().New(context, folder);
            return folder;
        }
    }
}
