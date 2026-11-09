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
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Publishes a bounded, authoritative common material catalogue with real generated material instances.
    /// </summary>
    /// <remarks>
    /// Select AddMaterial and RemoveMaterialById on the generated list before binding when remote
    /// mutation is needed. The provider owns persistence and in-use checks; this binding owns
    /// address-space reconciliation. Nodes live with their manager, while disposing the binding
    /// drains and disables only its operations.
    /// </remarks>
    public sealed class MaterialListBinding : IAsyncDisposable
    {
        /// <summary>
        /// Binds an initially empty generated catalogue without performing an implicit provider read.
        /// </summary>
        public MaterialListBinding(
            AsyncCustomNodeManager owner,
            MaterialListState node,
            IMaterialListProvider provider,
            IPlasticsRubberAccessPolicy accessPolicy)
        {
            m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Node = node ?? throw new ArgumentNullException(nameof(node));
            m_provider = provider ?? throw new ArgumentNullException(nameof(provider));
            if (node.NodeVersion == null || node.DensityUnit?.Value == null)
            {
                throw new ArgumentException("NodeVersion and a configured DensityUnit are required.", nameof(node));
            }
            var children = new List<BaseInstanceState>();
            node.GetChildren(owner.SystemContext, children);
            if (children.Exists(child => child is MaterialState))
            {
                throw new ArgumentException("Publish the initial catalogue through this binding.", nameof(node));
            }
            m_binding = new PlasticsRubberMethodBinding(node, owner.SystemContext, accessPolicy);
            if (node.AddMaterial != null)
            {
                node.AddMaterial.OnCallAsync = async (context, method, target, id, name, density, ct) =>
                    new AddMaterialMethodStateResult
                    {
                        ServiceResult = await m_binding.ExecuteAsync(
                            context, target, method.NodeId,
                            async (request, token) =>
                            {
                                ValidateIdentity(id);
                                if (name.IsNull || !IsDensity(density))
                                {
                                    throw Error(StatusCodes.BadOutOfRange,
                                        "A name and finite positive density are required.");
                                }
                                MaterialListSnapshot committed =
                                    await provider.AddAsync(request, id, name, density, token).ConfigureAwait(false);
                                await ApplyAsync(committed, token).ConfigureAwait(false);
                            },
                            ct).ConfigureAwait(false)
                    };
                m_binding.Track(node.AddMaterial);
            }
            if (node.RemoveMaterialById != null)
            {
                node.RemoveMaterialById.OnCallAsync = async (context, method, target, id, ct) =>
                    new RemoveMaterialByIdMethodStateResult
                    {
                        ServiceResult = await m_binding.ExecuteAsync(
                            context, target, method.NodeId,
                            async (request, token) =>
                            {
                                ValidateIdentity(id);
                                MaterialListSnapshot committed =
                                    await provider.RemoveAsync(request, id, token).ConfigureAwait(false);
                                await ApplyAsync(committed, token).ConfigureAwait(false);
                            },
                            ct).ConfigureAwait(false)
                    };
                m_binding.Track(node.RemoveMaterialById);
            }
        }

        /// <summary>
        /// Gets the actual generated list and its NodeVersion, density unit and material children.
        /// </summary>
        public MaterialListState Node { get; }

        /// <summary>
        /// Reads and publishes the complete catalogue using an explicitly supplied caller.
        /// </summary>
        public async ValueTask RefreshAsync(ISystemContext context, CancellationToken cancellationToken = default)
        {
            ServiceResult result = await m_binding.ExecuteAsync(
                context, Node.NodeId, Node.NodeId,
                async (request, ct) =>
                {
                    MaterialListSnapshot snapshot = await m_provider.ReadAsync(request, ct).ConfigureAwait(false);
                    await ApplyAsync(snapshot, ct).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
            ThrowIfBad(result);
        }

        /// <summary>
        /// Applies a committed server-side snapshot without invoking an external authority a second time.
        /// </summary>
        public async ValueTask PublishAsync(
            MaterialListSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            MaterialListSnapshot frozen = snapshot with { Materials = snapshot.Materials.ToArray().ToArrayOf() };
            ServiceResult result = await m_binding.RunAsync(
                ct => ApplyAsync(frozen, ct), cancellationToken).ConfigureAwait(false);
            ThrowIfBad(result);
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            return m_binding.DisposeAsync();
        }

        /// <summary>
        /// Validates the entire authority result before reconciling node topology or publishing values.
        /// </summary>
        private async ValueTask ApplyAsync(MaterialListSnapshot snapshot, CancellationToken ct)
        {
            snapshot.Validate();
            if (m_last != null && snapshot.Revision <= m_last.Revision)
            {
                if (snapshot.Revision == m_last.Revision &&
                    snapshot.SourceTimestamp == m_last.SourceTimestamp &&
                    snapshot.Materials == m_last.Materials)
                {
                    return;
                }
                throw Error(StatusCodes.BadInvalidState, "The material catalogue revision is stale or conflicting.");
            }
            var expected = new Dictionary<int, MaterialListEntry>(snapshot.Materials.Count);
            foreach (MaterialListEntry entry in snapshot.Materials)
            {
                expected.Add(entry.Number, entry);
                if (m_materials.TryGetValue(entry.Number, out MaterialState? existing) &&
                    !string.Equals(existing.Id!.Value, entry.Id, StringComparison.Ordinal))
                {
                    throw Error(StatusCodes.BadInvalidState, "A live material slot cannot change identity.");
                }
            }
            var added = new List<KeyValuePair<int, MaterialState>>();
            var removed = new List<KeyValuePair<int, MaterialState>>();
            foreach (KeyValuePair<int, MaterialState> entry in m_materials)
            {
                if (!expected.ContainsKey(entry.Key))
                {
                    removed.Add(entry);
                }
            }
            try
            {
                for (int index = 0; index < snapshot.Materials.Count; index++)
                {
                    MaterialListEntry entry = snapshot.Materials[index];
                    if (m_materials.ContainsKey(entry.Number))
                    {
                        continue;
                    }
                    MaterialState material = CreateMaterial(entry, snapshot.SourceTimestamp);
                    if (m_owner.FindPredefinedNode<NodeState>(material.NodeId) != null)
                    {
                        throw Error(StatusCodes.BadNodeIdExists, "A generated material identity is already owned.");
                    }
                    added.Add(new KeyValuePair<int, MaterialState>(entry.Number, material));
                    Node.AddChild(material);
                    await m_owner.AddPredefinedNodeAsync(material, ct).ConfigureAwait(false);
                }
                for (int index = 0; index < removed.Count; index++)
                {
                    MaterialState material = removed[index].Value;
                    await m_owner.DeleteNodeAsync(m_owner.SystemContext, material.NodeId, ct).ConfigureAwait(false);
                    Node.RemoveChild(material);
                }
            }
            catch (Exception failure)
            {
                try
                {
                    await RollBackTopologyAsync(added, removed).ConfigureAwait(false);
                }
                catch (Exception rollback)
                {
                    throw new AggregateException("Material publication and topology rollback failed.", failure,
                        rollback);
                }
                throw;
            }
            foreach (KeyValuePair<int, MaterialState> entry in added)
            {
                m_materials.Add(entry.Key, entry.Value);
            }
            foreach (KeyValuePair<int, MaterialState> entry in removed)
            {
                m_materials.Remove(entry.Key);
            }
            foreach (MaterialListEntry entry in snapshot.Materials)
            {
                UpdateMaterial(m_materials[entry.Number], entry, snapshot.SourceTimestamp);
            }
            Node.NodeVersion!.Value = snapshot.Revision.ToString(CultureInfo.InvariantCulture);
            Node.NodeVersion.StatusCode = StatusCodes.Good;
            Node.NodeVersion.Timestamp = snapshot.SourceTimestamp;
            Node.ClearChangeMasks(m_owner.SystemContext, true);
            m_last = snapshot with { Materials = snapshot.Materials.ToArray().ToArrayOf() };
            await ReportModelChangeAsync(added, removed, snapshot.SourceTimestamp, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a native material with its persistent standard placeholder name and declared density unit.
        /// </summary>
        private MaterialState CreateMaterial(MaterialListEntry entry, DateTimeUtc timestamp)
        {
            MaterialState material = m_owner.SystemContext.CreateInstanceOfMaterialType(
                Node,
                new QualifiedName("Material_" + entry.Number.ToString("D3", CultureInfo.InvariantCulture),
                    Node.NodeId.NamespaceIndex));
            material.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            EUInformation unit = Node.DensityUnit!.Value ??
                throw Error(StatusCodes.BadConfigurationError, "The density unit was removed.");
            material.Density!.EngineeringUnits!.Value = new EUInformation
            {
                NamespaceUri = unit.NamespaceUri,
                UnitId = unit.UnitId,
                DisplayName = unit.DisplayName,
                Description = unit.Description
            };
            UpdateMaterial(material, entry, timestamp);
            return material;
        }

        /// <summary>
        /// Publishes only authoritative material values and source times.
        /// </summary>
        private static void UpdateMaterial(MaterialState material, MaterialListEntry entry, DateTimeUtc timestamp)
        {
            material.Id!.Value = entry.Id;
            material.Name!.Value = entry.Name;
            material.Density!.Value = entry.Density;
            material.Id.StatusCode = StatusCodes.Good;
            material.Name.StatusCode = StatusCodes.Good;
            material.Density.StatusCode = StatusCodes.Good;
            material.Id.Timestamp = timestamp;
            material.Name.Timestamp = timestamp;
            material.Density.Timestamp = timestamp;
        }

        /// <summary>
        /// Restores topology after a failed asynchronous registration without changing committed source data.
        /// </summary>
        private async ValueTask RollBackTopologyAsync(
            List<KeyValuePair<int, MaterialState>> added,
            List<KeyValuePair<int, MaterialState>> removed)
        {
            List<Exception>? failures = null;
            foreach (KeyValuePair<int, MaterialState> entry in added)
            {
                MaterialState material = entry.Value;
                try
                {
                    Node.RemoveChild(material);
                    await m_owner.DeleteNodeAsync(
                        m_owner.SystemContext, material.NodeId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            foreach (KeyValuePair<int, MaterialState> entry in removed)
            {
                try
                {
                    if (m_owner.FindPredefinedNode<NodeState>(entry.Value.NodeId) == null)
                    {
                        Node.AddChild(entry.Value);
                        await m_owner.AddPredefinedNodeAsync(entry.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }
            if (failures != null)
            {
                throw new AggregateException("Material topology rollback failed.", failures);
            }
        }

        /// <summary>
        /// Reports the catalogue's structural additions/deletions with the standard model-change event.
        /// </summary>
        private async ValueTask ReportModelChangeAsync(
            List<KeyValuePair<int, MaterialState>> added,
            List<KeyValuePair<int, MaterialState>> removed,
            DateTimeUtc timestamp,
            CancellationToken ct)
        {
            if (added.Count == 0 && removed.Count == 0)
            {
                return;
            }
            var changes = new List<ModelChangeStructureDataType>(added.Count + removed.Count);
            foreach (KeyValuePair<int, MaterialState> entry in added)
            {
                changes.Add(Change(entry.Value, ModelChangeStructureVerbMask.NodeAdded));
            }
            foreach (KeyValuePair<int, MaterialState> entry in removed)
            {
                changes.Add(Change(entry.Value, ModelChangeStructureVerbMask.NodeDeleted));
            }
            GeneralModelChangeEventState notification =
                m_owner.SystemContext.CreateInstanceOfGeneralModelChangeEventType();
            notification.Initialize(m_owner.SystemContext, Node, EventSeverity.Low,
                new LocalizedText("Material catalogue changed."));
            notification.Changes!.Value = changes.ToArrayOf();
            notification.Time!.Value = timestamp;
            await Node.ReportEventAsync(m_owner.SystemContext, notification, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Identifies a changed native material node.
        /// </summary>
        private static ModelChangeStructureDataType Change(MaterialState node, ModelChangeStructureVerbMask verb)
        {
            return new ModelChangeStructureDataType
            {
                Affected = node.NodeId,
                AffectedType = node.TypeDefinitionId,
                Verb = (byte)verb
            };
        }

        /// <summary>
        /// Rejects malformed material identifiers before any provider request.
        /// </summary>
        private static void ValidateIdentity([NotNull] string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 1024)
            {
                throw Error(StatusCodes.BadInvalidArgument,
                    "A nonempty material identifier of at most 1024 characters is required.");
            }
        }

        /// <summary>
        /// Checks a physical density without clamping or replacing invalid input.
        /// </summary>
        private static bool IsDensity(double density)
        {
            return density > 0 && !double.IsNaN(density) && !double.IsInfinity(density);
        }

        /// <summary>
        /// Propagates a failed host publication or refresh.
        /// </summary>
        private static void ThrowIfBad(ServiceResult result)
        {
            if (ServiceResult.IsBad(result))
            {
                throw new ServiceResultException(result);
            }
        }

        /// <summary>
        /// Creates an explicit protocol failure.
        /// </summary>
        private static ServiceResultException Error(StatusCode code, string message)
        {
            return ServiceResultException.Create(code, message);
        }

        /// <summary>
        /// The existing address-space owner.
        /// </summary>
        private readonly AsyncCustomNodeManager m_owner;

        /// <summary>
        /// The explicitly injected catalogue authority.
        /// </summary>
        private readonly IMaterialListProvider m_provider;

        /// <summary>
        /// The shared authorization, serialization and lifetime guard.
        /// </summary>
        private readonly PlasticsRubberMethodBinding m_binding;

        /// <summary>
        /// Live generated materials indexed by stable slot.
        /// </summary>
        private readonly Dictionary<int, MaterialState> m_materials = [];

        /// <summary>
        /// The last completely published authority revision.
        /// </summary>
        private MaterialListSnapshot? m_last;
    }
}
