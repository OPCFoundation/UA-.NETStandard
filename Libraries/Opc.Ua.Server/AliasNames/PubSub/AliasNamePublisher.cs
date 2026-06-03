/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.Server.AliasNames.PubSub
{
    /// <summary>
    /// Listens to an <see cref="IAliasNameStoreRegistry"/> and builds a
    /// Part 17 Annex D <see cref="AliasUpdateDataType"/> for every
    /// category change. The constructed message is exposed via the
    /// <see cref="AliasUpdateProduced"/> event — callers wire it to the
    /// transport of their choice (e.g. <c>Opc.Ua.PubSub.UaPubSubApplication</c>
    /// configured with the DataSet from
    /// <see cref="AliasUpdateDataSetFactory"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each emitted message carries one entry in
    /// <see cref="AliasUpdateDataType.Categories"/> — the changed
    /// category's <c>PortableNodeId</c> + new <c>LastChange</c>. The
    /// publisher does NOT batch multiple categories per message; that
    /// behaviour is provided by the publishing layer (e.g. a
    /// DataSetWriter with a publishing interval).
    /// </para>
    /// <para>
    /// Per Part 17 §D rationale, the message only carries
    /// <c>LastChange</c> — subscribers must call
    /// <c>FindAlias</c>/<c>FindAliasVerbose</c> on the originating
    /// publisher to retrieve the actual alias contents.
    /// </para>
    /// </remarks>
    public sealed class AliasNamePublisher : IDisposable
    {
        /// <summary>
        /// Initializes a new publisher bound to the supplied registry.
        /// </summary>
        /// <param name="registry">The server-wide registry whose
        /// changes drive the publisher.</param>
        /// <param name="portableResolver">Translates local category
        /// <see cref="NodeId"/>s into transport-friendly
        /// <see cref="PortableNodeId"/>s.</param>
        /// <param name="applicationUri">The local server's
        /// application URI; stamped into every produced message.</param>
        /// <param name="options">Optional tunables.</param>
        public AliasNamePublisher(
            IAliasNameStoreRegistry registry,
            IPortableNodeIdResolver portableResolver,
            string applicationUri,
            AliasNamePublisherOptions? options = null)
        {
            m_registry = registry ?? throw new ArgumentNullException(nameof(registry));
            m_portableResolver = portableResolver
                ?? throw new ArgumentNullException(nameof(portableResolver));
            if (string.IsNullOrEmpty(applicationUri))
            {
                throw new ArgumentException(
                    "applicationUri must not be null or empty.",
                    nameof(applicationUri));
            }
            ApplicationUri = applicationUri;
            Options = options ?? new AliasNamePublisherOptions();
            if (string.IsNullOrEmpty(Options.ApplicationUri))
            {
                Options.ApplicationUri = applicationUri;
            }

            m_registry.Changed += OnRegistryChanged;
        }

        /// <summary>
        /// The application URI stamped into every produced message.
        /// </summary>
        public string ApplicationUri { get; }

        /// <summary>The configured tunables.</summary>
        public AliasNamePublisherOptions Options { get; }

        /// <summary>
        /// Raised every time the publisher builds a new
        /// <see cref="AliasUpdateDataType"/> message in response to a
        /// store change.
        /// </summary>
        public event EventHandler<AliasUpdateProducedEventArgs>? AliasUpdateProduced;

        /// <inheritdoc/>
        public void Dispose()
        {
            m_registry.Changed -= OnRegistryChanged;
        }

        private void OnRegistryChanged(object? sender, AliasStoreChangedEventArgs e)
        {
            if (Options.IncludedCategories != null &&
                !Options.IncludedCategories.Contains(e.CategoryId))
            {
                return;
            }

            PortableNodeId? portable = m_portableResolver.ToPortable(e.CategoryId);
            if (portable == null)
            {
                return;
            }

            var update = new AliasUpdateDataType
            {
                ApplicationUri = Options.ApplicationUri,
                Categories = new[]
                {
                    new AliasCategoryUpdateDataType
                    {
                        Category = portable,
                        LastChange = e.LastChange
                    }
                }.ToArrayOf()
            };

            AliasUpdateProduced?.Invoke(
                this, new AliasUpdateProducedEventArgs(update));
        }

        private readonly IAliasNameStoreRegistry m_registry;
        private readonly IPortableNodeIdResolver m_portableResolver;
    }
}
