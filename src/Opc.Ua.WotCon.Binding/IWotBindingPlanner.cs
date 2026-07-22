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
using System.Collections.Immutable;
using System.Linq;

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// Read-only context handed to a planner while it validates and compiles a
    /// form. It exposes the document security definitions (secret-free), the codec
    /// registry, the document kind, the Thing base URI for relative <c>href</c>
    /// resolution and the applied safety bounds.
    /// </summary>
    public sealed class WotBindingPlanContext
    {
        /// <summary>Initializes a new plan context.</summary>
        public WotBindingPlanContext(
            ImmutableDictionary<string, WotSecurityDefinition>? securityDefinitions = null,
            IWotCodecRegistry? codecs = null,
            WoTDocumentKindEnum documentKind = WoTDocumentKindEnum.ThingDescription,
            string? baseUri = null,
            WotBindingBounds? bounds = null)
        {
            SecurityDefinitions = securityDefinitions ?? ImmutableDictionary<string, WotSecurityDefinition>.Empty;
            Codecs = codecs ?? WotPayloadCodecRegistry.Default;
            DocumentKind = documentKind;
            BaseUri = baseUri;
            Bounds = bounds ?? WotBindingBounds.Default;
        }

        /// <summary>Gets the secret-free security definitions declared by the document.</summary>
        public ImmutableDictionary<string, WotSecurityDefinition> SecurityDefinitions { get; }

        /// <summary>Gets the codec registry used to select payload codecs.</summary>
        public IWotCodecRegistry Codecs { get; }

        /// <summary>Gets the document kind being compiled.</summary>
        public WoTDocumentKindEnum DocumentKind { get; }

        /// <summary>Gets the Thing base URI used to resolve relative hrefs, if any.</summary>
        public string? BaseUri { get; }

        /// <summary>Gets the applied safety bounds.</summary>
        public WotBindingBounds Bounds { get; }
    }

    /// <summary>
    /// The immutable compiled plan for one supported (form, operation) pair. It
    /// carries the endpoint, addressing, operation and payload metadata plus the
    /// secret-free credential references the runtime resolves at activation time.
    /// A non-executable entry is a validated plan for which no runtime executor is
    /// available (for example a BACnet, PROFINET or LoRaWAN binding).
    /// </summary>
    public sealed class WotCompiledForm
    {
        /// <summary>Initializes a new immutable compiled form.</summary>
        public WotCompiledForm(
            WotBindingIdentity binding,
            WotAffordanceKind affordanceKind,
            string affordanceName,
            string jsonPointer,
            WoTBindingCapabilityEnum operation,
            string opToken,
            WotEndpointDescriptor endpoint,
            WotAddressingDescriptor addressing,
            WotOperationDescriptor operationInfo,
            WotPayloadDescriptor payload,
            ImmutableArray<WotCredentialReference> security,
            bool isExecutable)
        {
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            AffordanceKind = affordanceKind;
            AffordanceName = affordanceName ?? string.Empty;
            JsonPointer = jsonPointer ?? string.Empty;
            Operation = operation;
            OpToken = opToken ?? string.Empty;
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Addressing = addressing ?? throw new ArgumentNullException(nameof(addressing));
            OperationInfo = operationInfo ?? throw new ArgumentNullException(nameof(operationInfo));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            Security = security.IsDefault ? ImmutableArray<WotCredentialReference>.Empty : security;
            IsExecutable = isExecutable;
        }

        /// <summary>Gets the identity of the binder that compiled the form.</summary>
        public WotBindingIdentity Binding { get; }

        /// <summary>Gets the affordance kind.</summary>
        public WotAffordanceKind AffordanceKind { get; }

        /// <summary>Gets the affordance name.</summary>
        public string AffordanceName { get; }

        /// <summary>Gets the JSON Pointer of the originating form.</summary>
        public string JsonPointer { get; }

        /// <summary>Gets the resolved capability operation.</summary>
        public WoTBindingCapabilityEnum Operation { get; }

        /// <summary>Gets the originating WoT <c>op</c> token.</summary>
        public string OpToken { get; }

        /// <summary>Gets the compiled endpoint metadata.</summary>
        public WotEndpointDescriptor Endpoint { get; }

        /// <summary>Gets the compiled addressing metadata.</summary>
        public WotAddressingDescriptor Addressing { get; }

        /// <summary>Gets the compiled operation metadata.</summary>
        public WotOperationDescriptor OperationInfo { get; }

        /// <summary>Gets the compiled payload metadata.</summary>
        public WotPayloadDescriptor Payload { get; }

        /// <summary>Gets the secret-free credential references for the operation.</summary>
        public ImmutableArray<WotCredentialReference> Security { get; }

        /// <summary>Gets whether a runtime executor is available for the entry.</summary>
        public bool IsExecutable { get; }

        /// <summary>Returns a copy of this entry with the supplied executability.</summary>
        public WotCompiledForm WithExecutable(bool isExecutable)
        {
            if (isExecutable == IsExecutable)
            {
                return this;
            }
            return new WotCompiledForm(
                Binding, AffordanceKind, AffordanceName, JsonPointer, Operation, OpToken,
                Endpoint, Addressing, OperationInfo, Payload, Security, isExecutable);
        }
    }

    /// <summary>
    /// The result of compiling a single form with a binder: the compiled entries
    /// (one per supported operation), the structured diagnostics and whether the
    /// form was validated (supported) at all.
    /// </summary>
    public sealed class WotBindingCompilation
    {
        /// <summary>Initializes a new compilation result.</summary>
        public WotBindingCompilation(
            bool isSupported,
            ImmutableArray<WotCompiledForm> entries,
            ImmutableArray<WotBindingDiagnostic> diagnostics)
        {
            IsSupported = isSupported;
            Entries = entries.IsDefault ? ImmutableArray<WotCompiledForm>.Empty : entries;
            Diagnostics = diagnostics.IsDefault ? ImmutableArray<WotBindingDiagnostic>.Empty : diagnostics;
        }

        /// <summary>
        /// Gets whether the form was validated and compiled. A supported form has
        /// at least one compiled entry and no error diagnostics.
        /// </summary>
        public bool IsSupported { get; }

        /// <summary>Gets the compiled entries.</summary>
        public ImmutableArray<WotCompiledForm> Entries { get; }

        /// <summary>Gets the structured diagnostics.</summary>
        public ImmutableArray<WotBindingDiagnostic> Diagnostics { get; }

        /// <summary>Gets whether any error diagnostic was produced.</summary>
        public bool HasErrors => Diagnostics.Any(d => d.IsError);

        /// <summary>Creates an unsupported result (a binder declined or rejected the form).</summary>
        public static WotBindingCompilation Unsupported(params WotBindingDiagnostic[] diagnostics)
            => new WotBindingCompilation(false, ImmutableArray<WotCompiledForm>.Empty,
                diagnostics is null ? ImmutableArray<WotBindingDiagnostic>.Empty : diagnostics.ToImmutableArray());

        /// <summary>Creates a supported result.</summary>
        public static WotBindingCompilation Supported(
            ImmutableArray<WotCompiledForm> entries, ImmutableArray<WotBindingDiagnostic> diagnostics)
            => new WotBindingCompilation(true, entries, diagnostics);
    }

    /// <summary>
    /// Validates and compiles WoT interaction forms into immutable binding plans.
    /// A planner performs no transport I/O, so a planner-only binder can validate
    /// and compile forms for protocols the runtime cannot execute.
    /// </summary>
    public interface IWotBindingPlanner
    {
        /// <summary>Validates and compiles a single form into a binding plan.</summary>
        WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context);
    }
}
