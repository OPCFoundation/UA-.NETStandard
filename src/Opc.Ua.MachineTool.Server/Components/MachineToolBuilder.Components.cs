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
