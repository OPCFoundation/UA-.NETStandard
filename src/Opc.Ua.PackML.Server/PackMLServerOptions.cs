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

namespace Opc.Ua.PackML.Server
{
    /// <summary>
    /// Fluent standalone-host configuration.
    /// </summary>
    public sealed class PackMLServerOptions
    {
        /// <summary>
        /// Gets or sets the application-owned instance namespace.
        /// </summary>
        public string NamespaceUri { get; set; } = "urn:opcfoundation:packml:units";

        /// <summary>
        /// Gets or sets a startup callback for composition below chosen parents
        /// and for retaining unit handles used by the equipment application.
        /// </summary>
        public Func<PackMLNodeManager, CancellationToken, ValueTask>? ConfigureAsync { get; set; }

        /// <summary>
        /// Adds one unit directly below Objects using an application-owned provider.
        /// </summary>
        public PackMLServerOptions AddUnit(
            string name,
            IPackMLEquipmentProvider provider,
            Action<PackMLUnitOptions>? configure = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A PackML unit name is required.", nameof(name));
            }
            if (provider is null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            m_units.Add((name, provider, configure));
            return this;
        }

        /// <summary>
        /// Builds all configured units before the host accepts client requests.
        /// </summary>
        internal async ValueTask BuildAsync(PackMLNodeManager manager, CancellationToken cancellationToken)
        {
            foreach ((string name, IPackMLEquipmentProvider provider, Action<PackMLUnitOptions>? configure) in m_units)
            {
                await manager.Runtime.AddUnitAsync(
                    new QualifiedName(name, manager.InstanceNamespaceIndex), provider, configure,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            if (ConfigureAsync is { } configureAsync)
            {
                await configureAsync(manager, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Checks namespace ownership before registration.
        /// </summary>
        internal void Validate()
        {
            if (!Uri.TryCreate(NamespaceUri, UriKind.Absolute, out _) ||
                NamespaceUri == Namespaces.PackML || NamespaceUri == Ua.Namespaces.OpcUa)
            {
                throw new ArgumentException("PackML instances require a distinct absolute application namespace.");
            }
        }

        /// <summary>The startup units, borrowed providers and unit configuration callbacks.</summary>
        private readonly List<(string, IPackMLEquipmentProvider, Action<PackMLUnitOptions>?)> m_units = [];
    }
}
