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
    internal sealed partial class BinPickingRobotCell
    {
        private async ValueTask MaterialiseOpenUsdFacilityAsync(CancellationToken cancellationToken)
        {
            try
            {
                ushort ns = Manager.Server.NamespaceUris.GetIndexOrAppend(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
                OpenUsdRootState root = SystemContext.CreateInstanceOfOpenUsdRootType(
                    null!, new QualifiedName("OpenUSD", ns));
                root.NodeId = new NodeId("OpenUSD", InstanceNamespaceIndex);

                FolderState stages = root.Stages ?? root.CreateOrReplaceStages(SystemContext, null);
                _ = root.Representations ?? root.CreateOrReplaceRepresentations(SystemContext, null);

                m_cellStage = SystemContext.CreateInstanceOfOpenUsdStageType(
                    stages, new QualifiedName("BinPickingCellStage", ns));
                stages.AddChild(m_cellStage);
                m_cellStage.CreateOrReplaceRootLayerIdentifier(SystemContext, null).Value = RootLayerIdentifier;

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

        /// <summary>
        /// Publishes one world position variable per part, so the parts have somewhere to be
        /// read from and something for the OpenUSD live bindings to follow.
        /// </summary>
        /// <remarks>
        /// This is the cell's simulation ground truth, not a standard OPC UA concept: a part
        /// lying in a bin is not modelled by Robot Intent or by Vision, which describe the
        /// robot and what a sensor concluded rather than the scenery. It is published because
        /// the scene has to be drivable from the address space to be watchable, and because a
        /// client comparing what the detector claims against where the part actually is needs
        /// both halves.
        /// </remarks>
        private ValueTask MaterialisePartStateAsync(CancellationToken cancellationToken)
        {
            var folder = new FolderState(null)
            {
                NodeId = new NodeId("WorldState", InstanceNamespaceIndex),
                SymbolicName = "WorldState",
                BrowseName = new QualifiedName("WorldState", InstanceNamespaceIndex),
                DisplayName = new LocalizedText("WorldState"),
                Description = new LocalizedText(
                    "Simulation ground truth: where each part actually is, independent of what " +
                    "the vision pipeline reports."),
                TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType,
                EventNotifier = EventNotifiers.None
            };

            // Inverse reference on this node, forward reference on the Server object below:
            // two directions of the same edge, mirroring the OpenUSD root above.
            folder.AddReference(ReferenceTypeIds.HasComponent, true, Opc.Ua.ObjectIds.Server);

            foreach (BinPickingPart part in BinPickingPartsCatalog.Parts)
            {
                var position = new BaseDataVariableState(folder)
                {
                    NodeId = new NodeId("WorldState_" + part.ClassLabel, InstanceNamespaceIndex),
                    SymbolicName = part.ClassLabel,
                    BrowseName = new QualifiedName(part.ClassLabel, InstanceNamespaceIndex),
                    DisplayName = new LocalizedText(part.ClassLabel),
                    Description = new LocalizedText("Position of " + part.ClassLabel + " in the world frame, in metres."),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                    ReferenceTypeId = ReferenceTypeIds.HasComponent,

                    // A structured coordinate, not a double[3]: the OpenUSD companion
                    // specification defines a translation source as a structured 3D value
                    // and the connector's translation profile fails closed on anything
                    // else, so an array left every part unresolved and the viewport never
                    // moved a part however faithfully the server tracked it.
                    DataType = Opc.Ua.DataTypeIds.ThreeDCartesianCoordinates,
                    ValueRank = ValueRanks.Scalar,
                    AccessLevel = AccessLevels.CurrentRead,
                    UserAccessLevel = AccessLevels.CurrentRead,
                    Value = new Variant(new ExtensionObject(new ThreeDCartesianCoordinates
                    {
                        X = part.InitialWorldPosition[0],
                        Y = part.InitialWorldPosition[1],
                        Z = part.InitialWorldPosition[2]
                    }))
                };
                folder.AddChild(position);
                m_partPositionNodes[part.ClassLabel] = position;
            }

            // Give the folder and its children their NodeIds before anything hangs a
            // representation off them: AttachRepresentation derives the representation's
            // NodeId from its owner, so unassigned owners produce five identical ids.
            SystemContext.AssignInstanceChildNodeIds(folder);
            m_partStateFolder = folder;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Registers the part-state subtree once its representations are attached. Adding it
        /// earlier would register the position variables, and then register them again as the
        /// parents of representations grafted on afterwards.
        /// </summary>
        private async ValueTask AddPartStateAsync(CancellationToken cancellationToken)
        {
            if (m_partStateFolder == null)
            {
                return;
            }
            _ = await Manager.AddNodeAsync(
                SystemContext,
                NodeId.Null,
                m_partStateFolder,
                cancellationToken).ConfigureAwait(false);

            // The Server object belongs to the core node manager, so the forward reference is
            // added afterwards rather than by passing it as the parent: this manager cannot
            // resolve i=2253 while adding its own node.
            await Manager.Server.NodeManager.AddReferencesAsync(
                Opc.Ua.ObjectIds.Server,
                [new NodeStateReference(ReferenceTypeIds.HasComponent, false, m_partStateFolder.NodeId)],
                cancellationToken).ConfigureAwait(false);
        }

        private async ValueTask MaterialiseRepresentationsAsync(CancellationToken cancellationToken)
        {
            if (m_cellStage == null || m_openUsdRoot == null)
            {
                return;
            }

            ushort usdNs = Manager.Server.NamespaceUris.GetIndexOrAppend(Opc.Ua.OpenUsd.Namespaces.OpenUSD);
            List<OpenUsdRepresentationState> representations = [];
            List<OpenUsdRepresentationState> partRepresentations = [];

            OpenUsdRepresentationState controllerRep = AttachRepresentation(Controller.State, "/World", usdNs);
            representations.Add(controllerRep);

            CreateBinding(
                controllerRep,
                usdNs,
                "GripperLeftSlide",
                GuidFor("gripper:left"),
                m_gripperLeftSlideValue?.NodeId ?? NodeId.Null,
                "/World/Robot/Palletizer/Base/J1/J2/J3/Leveling/J4/Flange/Gripper/FingerLeftSlide",
                "xformOp:translate",
                "double3",
                OpenUsdRenderTargetKindEnum.Translation,
                1.0);
            CreateBinding(
                controllerRep,
                usdNs,
                "PalletizerLeveling",
                GuidFor("palletizer:leveling"),
                m_levelingValue?.NodeId ?? NodeId.Null,
                "/World/Robot/Palletizer/Base/J1/J2/J3/Leveling",
                "xformOp:rotateY",
                "double",
                OpenUsdRenderTargetKindEnum.Rotation,
                1.0);
            CreateBinding(
                controllerRep,
                usdNs,
                "GripperRightSlide",
                GuidFor("gripper:right"),
                m_gripperRightSlideValue?.NodeId ?? NodeId.Null,
                "/World/Robot/Palletizer/Base/J1/J2/J3/Leveling/J4/Flange/Gripper/FingerRightSlide",
                "xformOp:translate",
                "double3",
                OpenUsdRenderTargetKindEnum.Translation,
                1.0);

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

            foreach (BinPickingPart part in BinPickingPartsCatalog.Parts)
            {
                if (!m_partPositionNodes.TryGetValue(part.ClassLabel, out BaseDataVariableState? position))
                {
                    continue;
                }
                string primPath = PartPrimPath(part.ClassLabel);
                OpenUsdRepresentationState partRep = AttachRepresentation(position, primPath, usdNs);
                CreateBinding(
                    partRep, usdNs, $"{part.ClassLabel}Translation", GuidFor("part:" + part.ClassLabel),
                    position.NodeId, primPath, "xformOp:translate",
                    "double3", OpenUsdRenderTargetKindEnum.Translation, 1.0);
                representations.Add(partRep);
                partRepresentations.Add(partRep);
            }

            foreach (OpenUsdRepresentationState representation in representations)
            {
                OrganiseRepresentation(representation);
                if (partRepresentations.Contains(representation))
                {
                    // Rides into the address space inside the part-state folder below;
                    // registering it here as well would add its parent a second time.
                    continue;
                }
                _ = await Manager.AddNodeAsync(
                    SystemContext,
                    representation.Parent!.NodeId,
                    representation,
                    cancellationToken).ConfigureAwait(false);
            }
            await AddPartStateAsync(cancellationToken).ConfigureAwait(false);
            m_logger.MaterialisedRepresentations(representations.Count);
        }

        private static List<ServedAsset> LoadServedAssets()
        {
            return
            [
                new("stage.usda", OpenUsdAssetKindEnum.RootLayer, ReadEmbeddedAsset("Cell.usda")),
                new(
                    "palletizer-arm.usda",
                    OpenUsdAssetKindEnum.Reference,
                    ReadEmbeddedAsset("palletizer-arm.usda")),
                new(
                    "palletizer-gripper.usda",
                    OpenUsdAssetKindEnum.Reference,
                    ReadEmbeddedAsset("palletizer-gripper.usda")),
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
            rep.CreateOrReplaceStage(SystemContext, null).Value = m_cellStage!.NodeId;
            rep.CreateOrReplacePrimPath(SystemContext, null).Value = primPath;
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

        /// <summary>
        /// The prim a part's world position drives. Mirrors the Parts scope in Cell.usda.
        /// </summary>
        private static string PartPrimPath(string classLabel)
        {
            return "/World/Parts/" + classLabel;
        }

        private static readonly (string Name, string PrimPath, string RotateOp)[] s_axisUsd =
        [
            ("J1", "/World/Robot/Palletizer/Base/J1", "xformOp:rotateZ"),
            ("J2", "/World/Robot/Palletizer/Base/J1/J2", "xformOp:rotateY"),
            ("J3", "/World/Robot/Palletizer/Base/J1/J2/J3", "xformOp:rotateY"),
            ("J4", "/World/Robot/Palletizer/Base/J1/J2/J3/Leveling/J4", "xformOp:rotateX")
        ];

        private OpenUsdRootState? m_openUsdRoot;
        private OpenUsdStageState? m_cellStage;
        private FolderState? m_partStateFolder;
        private readonly Dictionary<string, BaseDataVariableState> m_partPositionNodes = [];
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
