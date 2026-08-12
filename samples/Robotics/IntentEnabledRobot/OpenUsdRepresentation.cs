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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.OpenUsd;
using Opc.Ua.OpenUsd.Server;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;

namespace Robotics.IntentEnabledRobot
{
    /// <summary>
    /// Publishes the OpenUSD representation for the minimal Robot Intent sample.
    /// </summary>
    public sealed partial class IntentRobotCell
    {
        private async ValueTask MaterialiseOpenUsdFacilityAsync(CancellationToken cancellationToken)
        {
            try
            {
                ushort ns = (ushort)Manager.Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
                OpenUsdRootState root = SystemContext.CreateInstanceOfOpenUsdRootType(
                    null!, new QualifiedName("OpenUSD", ns));
                root.NodeId = new NodeId("OpenUSD", ns);

                FolderState stages = root.Stages ?? root.CreateOrReplaceStages(SystemContext, null!);
                _ = root.Representations ?? root.CreateOrReplaceRepresentations(SystemContext, null!);

                m_cellStage = SystemContext.CreateInstanceOfOpenUsdStageType(
                    stages, new QualifiedName("MinimalIntentRobotStage", ns));
                stages.AddChild(m_cellStage);
                m_cellStage.CreateOrReplaceRootLayerIdentifier(SystemContext, null!).Value = RootLayerIdentifier;

                List<ServedAsset> servedAssets = LoadServedAssets();
                byte[] rootLayerBytes = servedAssets.Find(a => a.Kind == OpenUsdAssetKindEnum.RootLayer)!.Bytes;
                byte[] digest;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    digest = sha.ComputeHash(rootLayerBytes);
                }
#pragma warning restore CA1850
                if (m_cellStage.RootLayerDigest != null)
                {
                    m_cellStage.RootLayerDigest.Value = (ByteString)digest;
                }
                if (m_cellStage.RootLayerDigestAlgorithm != null)
                {
                    m_cellStage.RootLayerDigestAlgorithm.Value = OpenUsdDigestAlgorithmEnum.Sha256;
                }

                root.AddReference(ReferenceTypeIds.HasComponent, true, Opc.Ua.ObjectIds.Server);
                UsdAssetDelivery.AttachStageAssets(SystemContext, m_cellStage, ns, servedAssets);
                SystemContext.AssignInstanceChildNodeIds(root);
                await Manager.AddPredefinedNodeAsync(root, cancellationToken).ConfigureAwait(false);

                m_openUsdRoot = root;
                await LinkOpenUsdRootToServerAsync(cancellationToken).ConfigureAwait(false);
                m_logger.MaterialisedOpenUsdFacility(root.NodeId, m_cellStage.NodeId);
            }
            catch (Exception ex)
            {
                m_cellStage = null;
                m_openUsdRoot = null;
                m_logger.OpenUsdFacilityFailed(ex);
            }
        }

        private async ValueTask MaterialiseRepresentationsAsync(CancellationToken cancellationToken)
        {
            if (m_cellStage == null || m_openUsdRoot == null)
            {
                return;
            }

            ushort usdNs = (ushort)Manager.Server.NamespaceUris.GetIndex(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
            List<OpenUsdRepresentationState> representations = [];

            OpenUsdRepresentationState controllerRep = AttachRepresentation(
                Controller.State, "/World/IntentCommand", usdNs);
            CreateBinding(controllerRep, usdNs, "GripperOpenSignal", GuidFor("gripper-open"),
                m_gripperOpenValue?.NodeId ?? NodeId.Null, "/World/IntentCommand", "inputs:gripperOpen", "bool",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);
            CreateBinding(controllerRep, usdNs, "BenchLightSignal", GuidFor("bench-light"),
                m_benchLightValue?.NodeId ?? NodeId.Null, "/World", "inputs:benchLight", "bool",
                OpenUsdRenderTargetKindEnum.Custom, 1.0);
            CreateBinding(controllerRep, usdNs, "HeldPartPosition", GuidFor("payload:held:position"),
                m_heldPartPositionValue?.NodeId ?? NodeId.Null, "/World/Payloads/HeldPart", "xformOp:translate",
                "double3", OpenUsdRenderTargetKindEnum.Translation, 1.0);
            CreateBinding(controllerRep, usdNs, "HeldPartVisibility", GuidFor("payload:held:visibility"),
                m_heldPartVisibleValue?.NodeId ?? NodeId.Null, "/World/Payloads/HeldPart", "visibility", "token",
                OpenUsdRenderTargetKindEnum.Visibility, 1.0);
            for (int ii = 0; ii < s_payloadSlotPrimPaths.Length && ii < m_payloadSlotFilledValues.Count; ii++)
            {
                CreateBinding(controllerRep, usdNs, $"PayloadSlot{ii + 1:00}Visibility",
                    GuidFor($"payload:slot:{ii + 1:00}:visibility"),
                    m_payloadSlotFilledValues[ii]?.NodeId ?? NodeId.Null, s_payloadSlotPrimPaths[ii],
                    "visibility", "token", OpenUsdRenderTargetKindEnum.Visibility, 1.0);
            }
            representations.Add(controllerRep);

            int axisIndex = 0;
            foreach (global::Opc.Ua.RobotIntent.AxisState axis in Axes)
            {
                OpenUsdRepresentationState axisRep = AttachRepresentation(axis, s_axisUsd[axisIndex].PrimPath, usdNs);
                CreateBinding(
                    axisRep, usdNs, $"{s_axisUsd[axisIndex].Name}Rotation", GuidFor(s_axisUsd[axisIndex].Name),
                    axis.Position?.NodeId ?? NodeId.Null, s_axisUsd[axisIndex].PrimPath, s_axisUsd[axisIndex].RotateOp,
                    "double", OpenUsdRenderTargetKindEnum.Rotation, 1.0);
                representations.Add(axisRep);
                axisIndex++;
            }

            foreach ((string Name, string PrimPath, double X, double Y, double Z, double Rz) in s_locations)
            {
                global::Opc.Ua.RobotIntent.LocationState? state = FindLocation(Name);
                if (state == null)
                {
                    continue;
                }
                OpenUsdRepresentationState locationRep = AttachRepresentation(state, PrimPath, usdNs);
                CreateBinding(locationRep, usdNs, $"{Name}LocationNode", GuidFor("location:" + Name),
                    state.NodeId, PrimPath, "inputs:robotIntentLocation", "token",
                    OpenUsdRenderTargetKindEnum.Custom, 1.0, sourceSemanticId: "RobotIntent.Location");
                representations.Add(locationRep);
            }

            foreach (OpenUsdRepresentationState representation in representations)
            {
                OrganiseRepresentation(representation);
                _ = await Manager.AddNodeAsync(
                    SystemContext,
                    representation.Parent!.NodeId,
                    representation,
                    cancellationToken).ConfigureAwait(false);
            }
            m_logger.MaterialisedRepresentations(representations.Count, LocationNodes.Count);
        }

        private global::Opc.Ua.RobotIntent.LocationState? FindLocation(string name)
        {
            foreach (global::Opc.Ua.RobotIntent.LocationState location in Locations)
            {
                if (string.Equals(location.LocationId?.Value, name, StringComparison.Ordinal))
                {
                    return location;
                }
            }
            return null;
        }

        private static List<ServedAsset> LoadServedAssets()
        {
            return
            [
                new("Bench.usda", OpenUsdAssetKindEnum.RootLayer, ReadEmbeddedAsset("Bench.usda")),
                new("arm.usda", OpenUsdAssetKindEnum.Reference, ReadEmbeddedAsset("arm.usda")),
                new("gripper.usda", OpenUsdAssetKindEnum.Reference, ReadEmbeddedAsset("gripper.usda"))
            ];
        }

        private static byte[] ReadEmbeddedAsset(string resourceName)
        {
            using Stream? stream = typeof(IntentRobotCell).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return [];
            }
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private async ValueTask LinkOpenUsdRootToServerAsync(CancellationToken cancellationToken)
        {
            if (m_openUsdRoot == null)
            {
                return;
            }
            IReference[] references =
            [
                new NodeStateReference(ReferenceTypeIds.HasComponent, false, m_openUsdRoot.NodeId)
            ];
            await Manager.Server.NodeManager.AddReferencesAsync(
                Opc.Ua.ObjectIds.Server,
                references,
                cancellationToken).ConfigureAwait(false);
        }

        private OpenUsdRepresentationState AttachRepresentation(NodeState owner, string primPath, ushort ns)
        {
            OpenUsdRepresentationState rep = SystemContext.CreateInstanceOfOpenUsdRepresentationType(
                owner, new QualifiedName("OpenUsdRepresentation", ns));
            rep.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            owner.AddChild(rep);
            AssignInstanceSubtree(rep, owner);
            rep.CreateOrReplaceStage(SystemContext, null!).Value = m_cellStage!.NodeId;
            rep.CreateOrReplacePrimPath(SystemContext, null!).Value = primPath;
            return rep;
        }

        private void OrganiseRepresentation(OpenUsdRepresentationState rep)
        {
            FolderState? registry = m_openUsdRoot?.Representations;
            if (registry == null)
            {
                return;
            }
            registry.AddReference(ReferenceTypeIds.Organizes, false, rep.NodeId);
            rep.AddReference(ReferenceTypeIds.Organizes, true, registry.NodeId);
        }

        private OpenUsdLiveBindingState CreateBinding(
            OpenUsdRepresentationState rep,
            ushort ns,
            string name,
            Guid bindingDefinitionId,
            NodeId sourceNodeId,
            string targetPrimPath,
            string targetPropertyName,
            string targetUsdTypeName,
            OpenUsdRenderTargetKindEnum? kind,
            double scale,
            string? sourceSemanticId = null)
        {
            OpenUsdLiveBindingState binding = rep.AddLiveBinding(
                SystemContext,
                ns,
                m_cellStage!.NodeId,
                name,
                bindingDefinitionId,
                sourceNodeId,
                targetPrimPath,
                targetPropertyName,
                targetUsdTypeName,
                kind,
                scale,
                sourceSemanticId: sourceSemanticId);
            AssignInstanceSubtree(binding, rep);
            return binding;
        }

        private void AssignInstanceSubtree(BaseInstanceState node, NodeState referenceRoot)
        {
            NodeId previousNodeId = SystemContext.AssignInstanceNodeId(node);
            SystemContext.AssignInstanceChildNodeIds(node, previousNodeId, referenceRoot);
        }

        private const string RootLayerIdentifier = "asset-repo/Bench.usd";
        private static readonly string[] s_payloadSlotPrimPaths =
        [
            "/World/Payloads/FixtureStack/Slot01",
            "/World/Payloads/FixtureStack/Slot02",
            "/World/Payloads/FixtureStack/Slot03",
            "/World/Payloads/FixtureStack/Slot04",
            "/World/Payloads/FixtureStack/Slot05",
            "/World/Payloads/FixtureStack/Slot06",
            "/World/Payloads/FixtureStack/Slot07",
            "/World/Payloads/FixtureStack/Slot08"
        ];

        private OpenUsdRootState? m_openUsdRoot;
        private OpenUsdStageState? m_cellStage;
    }

    internal static partial class OpenUsdRepresentationLog
    {
        [LoggerMessage(EventId = IntentEnabledRobotEventIds.OpenUsdRepresentation + 1,
            Level = LogLevel.Information,
            Message = "Materialised OpenUSD facility (root {RootId}, stage {StageId}).")]
        public static partial void MaterialisedOpenUsdFacility(this ILogger logger, NodeId rootId, NodeId stageId);

        [LoggerMessage(EventId = IntentEnabledRobotEventIds.OpenUsdRepresentation + 2,
            Level = LogLevel.Error,
            Message = "Failed to materialise the OpenUSD facility.")]
        public static partial void OpenUsdFacilityFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = IntentEnabledRobotEventIds.OpenUsdRepresentation + 3,
            Level = LogLevel.Information,
            Message = "Materialised {RepresentationCount} OpenUSD representations with " +
                "{LocationCount} target mappings.")]
        public static partial void MaterialisedRepresentations(
            this ILogger logger, int representationCount, int locationCount);
    }
}
