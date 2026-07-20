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
using Opc.Ua.OpenUsd;

namespace Opc.Ua.OpenUsd.Server
{
    /// <summary>
    /// Reusable server-side authoring helpers for the draft OPC UA — OpenUSD
    /// Bindings companion model. They materialise an <c>OpenUsdRepresentation</c>
    /// AddIn on any Object, register it in the well-known discovery registry, and
    /// author live/alarm/history/command bindings (§5.4) and component/composition
    /// bindings (§5.12–5.14) on it. The mechanics are domain-agnostic; a concrete
    /// server supplies the represented nodes, prim paths, and binding parameters.
    /// </summary>
    public static class OpenUsdRepresentationAuthoring
    {
        /// <summary>
        /// Attaches an <c>OpenUsdRepresentation</c> AddIn to <paramref name="owner"/>
        /// (HasComponent, browsable), pointing it at the given stage and prim path.
        /// The caller adds live/component bindings to the returned representation.
        /// </summary>
        /// <param name="context">The server system context.</param>
        /// <param name="owner">The Object the representation is attached to.</param>
        /// <param name="stage">NodeId of the target <c>OpenUsdStage</c>.</param>
        /// <param name="primPath">USD prim path the represented Object maps to.</param>
        /// <param name="ns">The OpenUSD companion namespace index.</param>
        public static OpenUsdRepresentationState CreateRepresentation(
            this ISystemContext context,
            NodeState owner,
            NodeId stage,
            string primPath,
            ushort ns)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (owner is null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            OpenUsdRepresentationState rep = context
                .CreateInstanceOfOpenUsdRepresentationType(
                    owner, new QualifiedName("OpenUsdRepresentation", ns));
            // The instance factory leaves ReferenceTypeId = Null; set HasComponent so
            // the AddIn is browsable from the represented object.
            rep.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            owner.AddChild(rep);
            rep.NodeId = context.NodeIdFactory.New(context, rep);
            rep.CreateOrReplaceStage(context, null!).Value = stage;
            rep.CreateOrReplacePrimPath(context, null!).Value = primPath;
            return rep;
        }

        /// <summary>
        /// Adds the Organizes reference pair between the discovery registry
        /// (<c>Server/OpenUSD/Representations</c>) and a representation, so a generic
        /// connector discovers it without any domain knowledge (spec §4.2).
        /// </summary>
        public static void RegisterInDiscovery(
            this OpenUsdRepresentationState representation,
            FolderState registry)
        {
            if (representation is null)
            {
                throw new ArgumentNullException(nameof(representation));
            }
            if (registry == null)
            {
                return;
            }
            registry.AddReference(ReferenceTypeIds.Organizes, false, representation.NodeId);
            representation.AddReference(ReferenceTypeIds.Organizes, true, registry.NodeId);
        }

        /// <summary>
        /// Authors a live binding on a representation. The <c>&lt;Binding&gt;</c>
        /// placeholder is instantiated then retyped to the concrete intent subtype
        /// (§5.4): ValueChange (default), Alarm, History, or Command. Common members
        /// are set on the base; intent-specific members (alarm aspect, command
        /// target/trigger) are added on the retyped subtype instance.
        /// </summary>
        public static OpenUsdLiveBindingState AddLiveBinding(
            this OpenUsdRepresentationState representation,
            ISystemContext context,
            ushort ns,
            NodeId targetStage,
            string name,
            Guid bindingDefinitionId,
            NodeId? sourceNodeId,
            string targetPrimPath,
            string targetPropertyName,
            string targetUsdTypeName,
            OpenUsdRenderTargetKindEnum? kind,
            double scale,
            uint bindingTypeId = Opc.Ua.OpenUsd.ObjectTypes.OpenUsdValueChangeBindingType,
            OpenUsdSignalRoleEnum signalRole = OpenUsdSignalRoleEnum.Observable,
            string? sourceSemanticId = null,
            OpenUsdAlarmAspectEnum? alarmAspect = null,
            NodeId? commandTargetNodeId = null,
            string? commandTriggerPropertyName = null)
        {
            if (representation is null)
            {
                throw new ArgumentNullException(nameof(representation));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            // AddxBinding_ instantiates the <Binding> placeholder as a HasComponent
            // child (browsable) and creates its mandatory base members. The binding
            // intent is the concrete subtype (§5.4): retype the instance to the
            // requested OpenUsd{ValueChange,Alarm,History,Command}BindingType.
            OpenUsdLiveBindingState b = representation.AddxBinding_(context, new QualifiedName(name, ns));
            b.TypeDefinitionId = new NodeId(bindingTypeId, ns);

            // Mandatory base members already exist on the instance; set their values.
            b.CreateOrReplaceBindingDefinitionId(context, null!).Value = new Uuid(bindingDefinitionId);
            b.CreateOrReplaceEnabled(context, null!).Value = true;
            b.CreateOrReplaceTargetStage(context, null!).Value = targetStage;
            b.CreateOrReplaceTargetPrimPath(context, null!).Value = targetPrimPath;
            b.CreateOrReplaceTargetPropertyName(context, null!).Value = targetPropertyName;
            b.CreateOrReplaceTargetUsdTypeName(context, null!).Value = targetUsdTypeName;

            // Optional base members are not auto-created; supply a generated node so the
            // member carries a valid BrowseName/ReferenceType and is browsable.
            if (sourceNodeId != null)
            {
                b.CreateOrReplaceSourceNodeId(
                    context,
                    context.CreateOpenUsdLiveBindingType_SourceNodeId(b, forInstance: true))
                    .Value = (NodeId)sourceNodeId;
            }
            // SignalRole is always asserted; the semantic id is set when provided.
            b.CreateOrReplaceSignalRole(
                context,
                context.CreateOpenUsdLiveBindingType_SignalRole(b, forInstance: true))
                .Value = signalRole;
            if (!string.IsNullOrEmpty(sourceSemanticId))
            {
                b.CreateOrReplaceSourceSemanticId(
                    context,
                    context.CreateOpenUsdLiveBindingType_SourceSemanticId(b, forInstance: true))
                    .Value = sourceSemanticId;
            }
            // Intent-specific members live only on the concrete subtype (§5.4); add them
            // directly to the retyped instance via the subtype's member factory.
            if (alarmAspect != null)
            {
                var ap = context.CreateOpenUsdAlarmBindingType_AlarmAspect(b, forInstance: true);
                b.AddChild(ap);
                ap.Value = alarmAspect.Value;
            }
            if (commandTargetNodeId != null)
            {
                var ct = context.CreateOpenUsdCommandBindingType_CommandTargetNodeId(b, forInstance: true);
                b.AddChild(ct);
                ct.Value = (NodeId)commandTargetNodeId;
            }
            if (!string.IsNullOrEmpty(commandTriggerPropertyName))
            {
                var tp = context.CreateOpenUsdCommandBindingType_CommandTriggerPropertyName(b, forInstance: true);
                b.AddChild(tp);
                tp.Value = commandTriggerPropertyName;
            }
            if (kind != null)
            {
                b.CreateOrReplaceRenderTargetKind(
                    context,
                    context.CreateOpenUsdLiveBindingType_RenderTargetKind(b, forInstance: true))
                    .Value = kind.Value;
            }
            b.CreateOrReplaceScale(
                context,
                context.CreateOpenUsdLiveBindingType_Scale(b, forInstance: true))
                .Value = scale;
            b.CreateOrReplaceBadQualityAction(
                context,
                context.CreateOpenUsdLiveBindingType_BadQualityAction(b, forInstance: true))
                .Value = OpenUsdBadQualityActionEnum.Skip;
            return b;
        }

        /// <summary>
        /// Authors a component/composition binding on a representation. Instantiates
        /// the <c>&lt;Component&gt;</c> placeholder and sets its members (spec
        /// §5.12–5.14): cardinality, composition arc, target prim path, and the
        /// optional component representation / asset reference / dynamic tracking /
        /// cross-server location.
        /// </summary>
        public static OpenUsdComponentBindingState AddComponentBinding(
            this OpenUsdRepresentationState representation,
            ISystemContext context,
            ushort ns,
            string name,
            Guid bindingDefinitionId,
            OpenUsdCardinalityEnum cardinality,
            OpenUsdCompositionArcEnum arc,
            string targetPrimPath,
            NodeId? componentRepresentation = null,
            string? assetReference = null,
            bool dynamic = false,
            NodeId? changeEventSource = null,
            string? componentServerUri = null,
            string? componentEndpointUrl = null,
            NodeId? componentTypeDefinition = null)
        {
            if (representation is null)
            {
                throw new ArgumentNullException(nameof(representation));
            }
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            OpenUsdComponentBindingState b = representation.AddxComponent_(context, new QualifiedName(name, ns));
            b.CreateOrReplaceBindingDefinitionId(context, null!).Value = new Uuid(bindingDefinitionId);
            b.CreateOrReplaceEnabled(context, null!).Value = true;
            b.CreateOrReplaceCardinality(context, null!).Value = cardinality;
            b.CreateOrReplaceCompositionArc(context, null!).Value = arc;
            b.CreateOrReplaceTargetPrimPath(context, null!).Value = targetPrimPath;

            if (componentRepresentation != null)
            {
                b.CreateOrReplaceComponentRepresentation(context,
                    context.CreateOpenUsdComponentBindingType_ComponentRepresentation(b, forInstance: true))
                    .Value = (NodeId)componentRepresentation;
            }
            if (!string.IsNullOrEmpty(assetReference))
            {
                b.CreateOrReplaceComponentAssetReference(context,
                    context.CreateOpenUsdComponentBindingType_ComponentAssetReference(b, forInstance: true))
                    .Value = assetReference;
            }
            if (dynamic)
            {
                b.CreateOrReplaceDynamic(context,
                    context.CreateOpenUsdComponentBindingType_Dynamic(b, forInstance: true))
                    .Value = true;
            }
            if (changeEventSource != null)
            {
                b.CreateOrReplaceChangeEventSource(context,
                    context.CreateOpenUsdComponentBindingType_ChangeEventSource(b, forInstance: true))
                    .Value = (NodeId)changeEventSource;
            }
            if (!string.IsNullOrEmpty(componentServerUri))
            {
                b.CreateOrReplaceComponentServerUri(context,
                    context.CreateOpenUsdComponentBindingType_ComponentServerUri(b, forInstance: true))
                    .Value = componentServerUri;
            }
            if (!string.IsNullOrEmpty(componentEndpointUrl))
            {
                b.CreateOrReplaceComponentEndpointUrl(context,
                    context.CreateOpenUsdComponentBindingType_ComponentEndpointUrl(b, forInstance: true))
                    .Value = componentEndpointUrl;
            }
            if (componentTypeDefinition != null)
            {
                b.CreateOrReplaceComponentTypeDefinition(context,
                    context.CreateOpenUsdComponentBindingType_ComponentTypeDefinition(b, forInstance: true))
                    .Value = (NodeId)componentTypeDefinition;
            }
            return b;
        }
    }
}
