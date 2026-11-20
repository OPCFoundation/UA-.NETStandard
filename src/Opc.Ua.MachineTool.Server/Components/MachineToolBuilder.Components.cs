/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolBuilder<TState>
    {
        /// <summary>
        /// Selects the existing Machinery Components and DI identification bindings on the same machine owner.
        /// The callback must include every exposed physical machine component.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public MachineToolBuilder<TState> WithComponents(Action<IMachineComponentsBuilder> configure)
        {
            EnsureMutable();
            m_components = configure ?? throw new ArgumentNullException(nameof(configure));
            return this;
        }

        /// <summary>
        /// Configures components through the actual shared Machinery transaction, never a second manager.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ConfigureComponents(
            IMachineBuilder<TState> builder, TState state, MachineToolRuntime<TState> runtime)
        {
            if (m_components == null)
            {
                return;
            }
            ushort ns = (ushort)m_context.Context.NamespaceUris.GetIndex(Machinery.Namespaces.Machinery);
            BaseInstanceState? existing = state.FindChild(m_context.Context,
                new QualifiedName(Machinery.BrowseNames.Components, ns));
            if (existing is not null and not MachineComponentsState)
            {
                throw new ServiceResultException(StatusCodes.BadTypeDefinitionInvalid,
                    "The existing Components AddIn must retain its generated MachineComponentsType state.");
            }
            if (existing is MachineComponentsState components)
            {
                builder.WithComponents(components, Configure);
            }
            else
            {
                builder.WithComponents(Configure);
            }

            void Configure(IMachineComponentsBuilder selected)
            {
                m_components(selected);
                runtime.SetComponents(selected.State);
            }
        }

        /// <summary>The explicit shared component binding selection.</summary>
        private Action<IMachineComponentsBuilder>? m_components;
    }

    public sealed partial class MachineToolRuntime<TState>
    {
        /// <summary>
        /// Gets the actual shared Machinery Components AddIn, or null when not selected.
        /// </summary>
        public MachineComponentsState? Components { get; private set; }

        /// <summary>
        /// Remembers the shared binding without creating another owner or copying the generated state.
        /// </summary>
        internal void SetComponents(MachineComponentsState components)
        {
            Components = components;
        }
    }
}
