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
using System.Collections.Immutable;
using System.Linq;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// The immutable, browseable capability snapshot advertised by a protocol
    /// binder. It captures the version-pinned document the binder implements, the
    /// interaction operations it supports, the content types it can encode and
    /// whether it is executable (a planner-only binder validates and compiles but
    /// performs no transport I/O). The snapshot is projected onto the 1.1 registry
    /// <c>SupportedBindings</c> nodes and into refresh results.
    /// </summary>
    public sealed class WotBindingCapability
    {
        /// <summary>
        /// Initializes a new immutable capability snapshot.
        /// </summary>
        /// <param name="bindingUri">The protocol-binding vocabulary URI.</param>
        /// <param name="title">A human-readable binding title.</param>
        /// <param name="source">The version-pinned document the binder implements.</param>
        /// <param name="operations">The interaction operations the binding supports.</param>
        /// <param name="contentTypes">The content types the binding produces / consumes.</param>
        /// <param name="isExecutable">Whether a runtime executor is available.</param>
        public WotBindingCapability(
            string bindingUri,
            string title,
            WotBindingSource source,
            IEnumerable<WoTBindingCapabilityEnum> operations,
            IEnumerable<string> contentTypes,
            bool isExecutable)
        {
            BindingUri = bindingUri ?? throw new ArgumentNullException(nameof(bindingUri));
            Title = title ?? string.Empty;
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Operations = operations is null
                ? []
                : [.. operations.Distinct()];
            ContentTypes = contentTypes is null
                ? []
                : [.. contentTypes.Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.Ordinal)];
            IsExecutable = isExecutable;
        }

        /// <summary>
        /// Gets the protocol-binding vocabulary URI.
        /// </summary>
        public string BindingUri { get; }

        /// <summary>
        /// Gets the human-readable binding title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the version-pinned document the binder implements.
        /// </summary>
        public WotBindingSource Source { get; }

        /// <summary>
        /// Gets the interaction operations the binding supports.
        /// </summary>
        public ImmutableArray<WoTBindingCapabilityEnum> Operations { get; }

        /// <summary>
        /// Gets the content types the binding produces / consumes.
        /// </summary>
        public ImmutableArray<string> ContentTypes { get; }

        /// <summary>
        /// Gets whether a runtime executor is available. A planner-only binder
        /// (for example BACnet, PROFINET, LoRaWAN or CoAP in this build) validates
        /// and compiles binding plans but reports <c>false</c> so the runtime
        /// treats materialized affordances as non-executable.
        /// </summary>
        public bool IsExecutable { get; }

        /// <summary>
        /// Gets whether the binding declares the supplied operation.
        /// </summary>
        public bool Supports(WoTBindingCapabilityEnum operation)
        {
            return Operations.Contains(operation);
        }

        /// <summary>
        /// Projects this snapshot onto the generated
        /// <see cref="WoTBindingCapabilityDataType"/> for the registry nodes and
        /// refresh results. No credentials or secrets are ever included.
        /// </summary>
        /// <remarks>
        /// A Client reads <c>Capabilities</c> as what it can do through this
        /// binding, so a planner-only binder reports none. It validates and
        /// compiles plans, but nothing it names can be invoked, and advertising
        /// the operations anyway would promise a Client an interaction that
        /// fails only when it tries. The title still says which binding it is,
        /// so a planner-only binder remains visible rather than disappearing.
        /// </remarks>
        public WoTBindingCapabilityDataType ToDataType()
        {
            return ToDataType(executorPresent: true);
        }

        /// <summary>
        /// Projects this snapshot, taking into account whether a runtime
        /// executor is actually registered for the binder.
        /// </summary>
        /// <remarks>
        /// A binder may be executable in principle and still have no executor
        /// registered in a given host. The capability a Client reads has to
        /// describe the host it is talking to, not the binder's ambitions, so
        /// the two conditions are combined here.
        /// </remarks>
        /// <param name="executorPresent">
        /// Whether the registry holds an executor for this binder.
        /// </param>
        /// <returns>The projected capability.</returns>
        public WoTBindingCapabilityDataType ToDataType(bool executorPresent)
        {
            bool effective = IsExecutable && executorPresent;
            return new WoTBindingCapabilityDataType
            {
                BindingUri = BindingUri,
                Title = Title,
                ProfileVersion = Source.Version,
                DraftMaturity = effective
                    ? Source.MaturityText
                    : Source.MaturityText + PlannerOnlySuffix,
                Capabilities = effective ? Operations.ToArray() : [],
                ContentTypes = ContentTypes.ToArray()
            };
        }

        /// <summary>
        /// The token appended to <c>DraftMaturity</c> for a binder that has no
        /// runtime executor, so the empty capability list reads as a deliberate
        /// statement rather than as a binding that declares nothing.
        /// </summary>
        public const string PlannerOnlySuffix = "+PlannerOnly";
    }
}
