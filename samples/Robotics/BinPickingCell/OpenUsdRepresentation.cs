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

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Publishes the OpenUSD representation for the bin-picking cell.
    /// </summary>
    public sealed partial class BinPickingRobotCell
    {
        private async ValueTask MaterialiseOpenUsdFacilityAsync(CancellationToken cancellationToken)
        {
            try
            {
                ushort ns = Manager.Server.NamespaceUris.GetIndexOrAppend(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
                OpenUsdRootState root = SystemContext.CreateInstanceOfOpenUsdRootType(
                    null!, new QualifiedName("OpenUSD", ns));
                root.NodeId = new NodeId("OpenUSD", InstanceNamespaceIndex);

                FolderState stages = root.Stages ?? root.CreateOrReplaceStages(SystemContext, null!);
                _ = root.Representations ?? root.CreateOrReplaceRepresentations(SystemContext, null!);

                m_cellStage = SystemContext.CreateInstanceOfOpenUsdStageType(
                    stages, new QualifiedName("BinPickingCellStage", ns));
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
                _ = await Manager.AddNodeAsync(
                    SystemContext,
                    NodeId.Null,
                    root,
                    cancellationToken).ConfigureAwait(false);

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

            ushort usdNs = Manager.Server.NamespaceUris.GetIndexOrAppend(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
            List<OpenUsdRepresentationState> representations = [];

            OpenUsdRepresentationState controllerRep = AttachRepresentation(Controller.State, "/World", usdNs);
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

            foreach (OpenUsdRepresentationState representation in representations)
            {
                OrganiseRepresentation(representation);
                _ = await Manager.AddNodeAsync(
                    SystemContext,
                    representation.Parent!.NodeId,
                    representation,
                    cancellationToken).ConfigureAwait(false);
            }
            m_logger.MaterialisedRepresentations(representations.Count);
        }

        private static List<ServedAsset> LoadServedAssets()
        {
            return
            [
                new("stage.usda", OpenUsdAssetKindEnum.RootLayer, ReadEmbeddedAsset("Cell.usda")),
                new("arm.usda", OpenUsdAssetKindEnum.Reference, ReadEmbeddedAsset("arm.usda")),
                new("gripper.usda", OpenUsdAssetKindEnum.Reference, ReadEmbeddedAsset("gripper.usda"))
            ];
        }

        private static byte[] ReadEmbeddedAsset(string resourceName)
        {
            using Stream? stream = typeof(BinPickingRobotCell).Assembly.GetManifestResourceStream(resourceName);
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

        private static Guid GuidFor(string token)
        {
            byte[] hash;
#pragma warning disable CA1850 // Prefer static HashData (net48/netstandard2.0 compatibility)
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes("bin-picking-cell:" + token));
            }
#pragma warning restore CA1850
            byte[] guidBytes = new byte[16];
            Array.Copy(hash, guidBytes, guidBytes.Length);
            return new Guid(guidBytes);
        }

        private const string RootLayerIdentifier = "stage.usda";

        private static readonly (string Name, string PrimPath, string RotateOp)[] s_axisUsd =
        [
            ("J1", "/World/Robot/Arm/Base/J1", "xformOp:rotateZ"),
            ("J2", "/World/Robot/Arm/Base/J1/J2", "xformOp:rotateY"),
            ("J3", "/World/Robot/Arm/Base/J1/J2/J3", "xformOp:rotateY"),
            ("J4", "/World/Robot/Arm/Base/J1/J2/J3/J4", "xformOp:rotateY"),
            ("J5", "/World/Robot/Arm/Base/J1/J2/J3/J4/J5", "xformOp:rotateZ"),
            ("J6", "/World/Robot/Arm/Base/J1/J2/J3/J4/J5/J6", "xformOp:rotateY")
        ];

        private OpenUsdRootState? m_openUsdRoot;
        private OpenUsdStageState? m_cellStage;
    }

    internal static partial class OpenUsdRepresentationLog
    {
        [LoggerMessage(EventId = BinPickingCellEventIds.OpenUsdRepresentation + 1,
            Level = LogLevel.Information,
            Message = "Materialised OpenUSD facility (root {RootId}, stage {StageId}).")]
        public static partial void MaterialisedOpenUsdFacility(this ILogger logger, NodeId rootId, NodeId stageId);

        [LoggerMessage(EventId = BinPickingCellEventIds.OpenUsdRepresentation + 2,
            Level = LogLevel.Error,
            Message = "Failed to materialise the OpenUSD facility.")]
        public static partial void OpenUsdFacilityFailed(this ILogger logger, Exception exception);

        [LoggerMessage(EventId = BinPickingCellEventIds.OpenUsdRepresentation + 3,
            Level = LogLevel.Information,
            Message = "Materialised {RepresentationCount} OpenUSD representations.")]
        public static partial void MaterialisedRepresentations(this ILogger logger, int representationCount);
    }
}
