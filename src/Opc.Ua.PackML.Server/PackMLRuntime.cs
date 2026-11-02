/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
 * IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server;

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// Composes standalone PackML units into an existing address-space owner.
    /// The owner loads AddOpcUaPackML once and disposes this runtime on shutdown.
    /// </summary>
    public sealed class PackMLRuntime : IAsyncDisposable, IConformanceContributor
    {
        /// <summary>
        /// Creates a runtime borrowing an existing asynchronous owner.
        /// </summary>
        public PackMLRuntime(AsyncCustomNodeManager manager)
        {
            m_manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        /// <summary>
        /// Gets the committed units in creation order.
        /// </summary>
        public ArrayOf<PackMLUnit> Units
        {
            get
            {
                lock (m_unitsLock)
                {
                    return m_units.ToArray().ToArrayOf();
                }
            }
        }

        /// <summary>
        /// Gets only conformance units shared by every published PackML unit.
        /// An empty runtime contributes no PackML support.
        /// </summary>
        public ArrayOf<QualifiedName> ConformanceUnits
        {
            get
            {
                ArrayOf<PackMLUnit> units = Units;
                if (units.Count == 0)
                {
                    return [];
                }
                var common = new HashSet<QualifiedName>(units[0].ConformanceUnits.Span.ToArray());
                for (int index = 1; index < units.Count; index++)
                {
                    common.IntersectWith(units[index].ConformanceUnits.Span.ToArray());
                }
                return common.ToArrayOf();
            }
        }

        /// <summary>
        /// Gets no blanket server profile URI.
        /// </summary>
        public ArrayOf<string> ServerProfiles => [];

        /// <summary>
        /// Creates and registers a unit below a chosen parent, or directly in
        /// Objects when parent is null. No PackML-specific global folder is added.
        /// </summary>
        public ValueTask<PackMLUnit> AddUnitAsync(
            QualifiedName browseName,
            IPackMLEquipmentProvider provider,
            Action<PackMLUnitOptions>? configure = null,
            NodeState? parent = null,
            CancellationToken cancellationToken = default)
        {
            return m_lifetime.RunAsync(
                token => AddUnitCoreAsync(browseName, provider, configure, parent, token), cancellationToken);
        }

        /// <summary>
        /// Creates and registers one admitted composition operation.
        /// </summary>
        private async ValueTask<PackMLUnit> AddUnitCoreAsync(
            QualifiedName browseName,
            IPackMLEquipmentProvider provider,
            Action<PackMLUnitOptions>? configure,
            NodeState? parent,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new PackMLUnitOptions();
            configure?.Invoke(options);
            var children = new List<BaseInstanceState>();
            parent?.GetChildren(m_manager.SystemContext, children);
            lock (m_unitsLock)
            {
                if (children.Exists(child => child.BrowseName == browseName) ||
                    m_units.Exists(unit => unit.Node.Parent == parent && unit.Node.BrowseName == browseName))
                {
                    throw new ServiceResultException(StatusCodes.BadBrowseNameDuplicated);
                }
            }
            PackMLUnit unit = await PackMLUnit.CreateAsync(
                m_manager.SystemContext, parent, browseName, provider, options, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await m_manager.AddPredefinedNodeAsync(unit.ModeDataType, cancellationToken).ConfigureAwait(false);
                await m_manager.Server.NodeManager.AddReferencesAsync(
                    Ua.DataTypeIds.Enumeration,
                    [new NodeStateReference(Ua.ReferenceTypeIds.HasSubtype, false, unit.ModeDataType.NodeId)],
                    cancellationToken).ConfigureAwait(false);
                unit.Node.ReferenceTypeId = parent is null
                    ? Ua.ReferenceTypeIds.Organizes
                    : Ua.ReferenceTypeIds.HasComponent;
                if (parent is null)
                {
                    unit.Node.AddReference(Ua.ReferenceTypeIds.Organizes, true, Ua.ObjectIds.ObjectsFolder);
                }
                parent?.AddChild(unit.Node);
                if (parent is not null)
                {
                    parent.AddNotifier(m_manager.SystemContext, Ua.ReferenceTypeIds.HasEventSource, false, unit.Node);
                    unit.Node.AddNotifier(m_manager.SystemContext, Ua.ReferenceTypeIds.HasEventSource, true, parent);
                }
                await m_manager.AddPredefinedNodeAsync(unit.Node, cancellationToken).ConfigureAwait(false);
                if (parent is null)
                {
                    await m_manager.Server.NodeManager.AddReferencesAsync(
                        Ua.ObjectIds.ObjectsFolder,
                        [new NodeStateReference(Ua.ReferenceTypeIds.Organizes, false, unit.Node.NodeId)],
                        cancellationToken).ConfigureAwait(false);
                }
                if (m_manager is PackMLNodeManager standalone)
                {
                    await standalone.RegisterUnitNotifierAsync(unit.Node, cancellationToken).ConfigureAwait(false);
                }
                lock (m_unitsLock)
                {
                    m_units.Add(unit);
                }
                parent?.ClearChangeMasks(m_manager.SystemContext, false);
                return unit;
            }
            catch (Exception creationError)
            {
                parent?.RemoveChild(unit.Node);
                parent?.RemoveNotifier(m_manager.SystemContext, unit.Node, true);
                var errors = new List<Exception> { creationError };
                NodeId[] registeredNodes = [unit.Node.NodeId, unit.ModeDataType.NodeId];
                foreach (NodeId registeredNode in registeredNodes)
                {
                    try
                    {
                        await m_manager.DeleteNodeAsync(
                            m_manager.SystemContext, registeredNode, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception cleanupError)
                    {
                        errors.Add(cleanupError);
                    }
                }
                try
                {
                    await unit.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception cleanupError)
                {
                    errors.Add(cleanupError);
                }
                if (errors.Count > 1)
                {
                    throw new AggregateException("PackML composition failed and rollback could not finish.", errors);
                }
                throw;
            }
        }

        /// <summary>
        /// Stops creation and provider operations before the owning address space
        /// is deleted. Providers are borrowed, not disposed here.
        /// </summary>
        public ValueTask DisposeAsync()
        {
            return m_lifetime.StopAsync(DisposeUnitsAsync);
        }

        /// <summary>
        /// Starts cancellation on every committed unit before joining their shutdown.
        /// </summary>
        private async ValueTask DisposeUnitsAsync()
        {
            ArrayOf<PackMLUnit> units = Units;
            var shutdown = new Task[units.Count];
            for (int index = 0; index < units.Count; index++)
            {
                shutdown[index] = units[index].ShutdownAsync().AsTask();
            }
            Task all = Task.WhenAll(shutdown);
            try
            {
                await all.ConfigureAwait(false);
            }
            catch
            {
                if (all.Exception is { } errors)
                {
                    throw errors;
                }
                throw;
            }
        }

        /// <summary>The borrowed address-space owner.</summary>
        private readonly AsyncCustomNodeManager m_manager;

        /// <summary>Protects the committed-unit list.</summary>
        private readonly System.Threading.Lock m_unitsLock = new();

        /// <summary>The committed units in creation order.</summary>
        private readonly List<PackMLUnit> m_units = [];

        /// <summary>Serializes composition and drains it during shutdown.</summary>
        private readonly PackMLLifetime m_lifetime = new();
    }
}
