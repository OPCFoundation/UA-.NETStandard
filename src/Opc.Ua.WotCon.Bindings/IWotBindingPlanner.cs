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
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    /// <summary>
    /// Read-only context handed to a planner while it validates and compiles a
    /// form. It exposes the document security definitions (secret-free), the codec
    /// registry, the document kind, the Thing base URI for relative <c>href</c>
    /// resolution and the applied safety bounds.
    /// </summary>
    public sealed class WotBindingPlanContext
    {
        /// <summary>
        /// Initializes a new plan context.
        /// </summary>
        public WotBindingPlanContext(
            ImmutableDictionary<string, WotSecurityDefinition>? securityDefinitions = null,
            IWotCodecRegistry? codecs = null,
            WoTDocumentKindEnum documentKind = WoTDocumentKindEnum.ThingDescription,
            string? baseUri = null,
            WotBindingBounds? bounds = null,
            ImmutableDictionary<string, string>? namespacePrefixes = null)
        {
            SecurityDefinitions = securityDefinitions ?? ImmutableDictionary<string, WotSecurityDefinition>.Empty;
            Codecs = codecs ?? WotPayloadCodecRegistry.Default;
            DocumentKind = documentKind;
            BaseUri = baseUri;
            Bounds = bounds ?? WotBindingBounds.Default;
            NamespacePrefixes = namespacePrefixes ?? ImmutableDictionary<string, string>.Empty;
        }

        /// <summary>
        /// Gets the secret-free security definitions declared by the document.
        /// </summary>
        public ImmutableDictionary<string, WotSecurityDefinition> SecurityDefinitions { get; }

        /// <summary>
        /// Gets the codec registry used to select payload codecs.
        /// </summary>
        public IWotCodecRegistry Codecs { get; }

        /// <summary>
        /// Gets the document kind being compiled.
        /// </summary>
        public WoTDocumentKindEnum DocumentKind { get; }

        /// <summary>
        /// Gets the Thing base URI used to resolve relative hrefs, if any.
        /// </summary>
        public string? BaseUri { get; }

        /// <summary>
        /// Gets the applied safety bounds.
        /// </summary>
        public WotBindingBounds Bounds { get; }

        /// <summary>
        /// Gets the namespace prefixes the document's <c>@context</c> binds,
        /// keyed by prefix. A compact model name (WoT Binding Section 5.1.2)
        /// such as <c>pump:Temperature</c> resolves through it, which is what
        /// lets a planner rewrite a select-clause path element into the
        /// portable <c>nsu=</c> form a channel can resolve without the
        /// document.
        /// </summary>
        public ImmutableDictionary<string, string> NamespacePrefixes { get; }
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
        /// <summary>
        /// Initializes a new immutable compiled form.
        /// </summary>
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
            bool isExecutable,
            WotTargetMappingDescriptor? targetMapping = null)
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
            Security = security.IsDefault ? [] : security;
            IsExecutable = isExecutable;
            TargetMapping = targetMapping ?? WotTargetMappingDescriptor.Empty;
        }

        /// <summary>
        /// Initializes a new immutable compiled form carrying the OPC UA event
        /// field selection of WoT Binding Section 6.1 and the <c>auto</c>
        /// endpoint security floor of Section 5.7.1.
        /// </summary>
        /// <param name="binding">The identity of the binder that compiled the form.</param>
        /// <param name="affordanceKind">The affordance kind.</param>
        /// <param name="affordanceName">The affordance name.</param>
        /// <param name="jsonPointer">The JSON Pointer of the originating form.</param>
        /// <param name="operation">The resolved capability operation.</param>
        /// <param name="opToken">The originating WoT <c>op</c> token.</param>
        /// <param name="endpoint">The compiled endpoint metadata.</param>
        /// <param name="addressing">The compiled addressing metadata.</param>
        /// <param name="operationInfo">The compiled operation metadata.</param>
        /// <param name="payload">The compiled payload metadata.</param>
        /// <param name="security">The secret-free credential references.</param>
        /// <param name="isExecutable">Whether a runtime executor is available.</param>
        /// <param name="targetMapping">The OPC 10101 §6.5.4 target mapping, if any.</param>
        /// <param name="eventSelection">
        /// The compiled event field selection, present on an event affordance
        /// only.
        /// </param>
        /// <param name="securityFloor">
        /// The security floor an <c>auto</c> scheme puts on endpoint selection,
        /// if any.
        /// </param>
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
            bool isExecutable,
            WotTargetMappingDescriptor? targetMapping,
            WotEventSelection? eventSelection,
            WotSecurityFloor? securityFloor)
            : this(
                binding, affordanceKind, affordanceName, jsonPointer, operation, opToken,
                endpoint, addressing, operationInfo, payload, security, isExecutable, targetMapping)
        {
            EventSelection = eventSelection;
            SecurityFloor = securityFloor;
        }

        /// <summary>
        /// Gets the identity of the binder that compiled the form.
        /// </summary>
        public WotBindingIdentity Binding { get; }

        /// <summary>
        /// Gets the affordance kind.
        /// </summary>
        public WotAffordanceKind AffordanceKind { get; }

        /// <summary>
        /// Gets the affordance name.
        /// </summary>
        public string AffordanceName { get; }

        /// <summary>
        /// Gets the JSON Pointer of the originating form.
        /// </summary>
        public string JsonPointer { get; }

        /// <summary>
        /// Gets the resolved capability operation.
        /// </summary>
        public WoTBindingCapabilityEnum Operation { get; }

        /// <summary>
        /// Gets the originating WoT <c>op</c> token.
        /// </summary>
        public string OpToken { get; }

        /// <summary>
        /// Gets the compiled endpoint metadata.
        /// </summary>
        public WotEndpointDescriptor Endpoint { get; }

        /// <summary>
        /// Gets the compiled addressing metadata.
        /// </summary>
        public WotAddressingDescriptor Addressing { get; }

        /// <summary>
        /// Gets the compiled operation metadata.
        /// </summary>
        public WotOperationDescriptor OperationInfo { get; }

        /// <summary>
        /// Gets the compiled payload metadata.
        /// </summary>
        public WotPayloadDescriptor Payload { get; }

        /// <summary>
        /// Gets the secret-free credential references for the operation.
        /// </summary>
        public ImmutableArray<WotCredentialReference> Security { get; }

        /// <summary>
        /// Gets whether a runtime executor is available for the entry.
        /// </summary>
        public bool IsExecutable { get; }

        /// <summary>
        /// Gets the protocol-neutral OPC 10101 §6.5.4 target-mapping descriptor
        /// carried from the originating property affordance. It is empty unless
        /// the affordance authors <c>uav:mapToNodeId</c>, <c>uav:mapToType</c> or
        /// <c>uav:mapByFieldPath</c>.
        /// </summary>
        public WotTargetMappingDescriptor TargetMapping { get; }

        /// <summary>
        /// Gets the compiled OPC UA event field selection of WoT Binding
        /// Section 6.1, or <c>null</c> for a form that is not an event
        /// subscription. It is always the <em>effective</em> selection: the
        /// documented default when the affordance states none, the complete
        /// standardized list when it states one.
        /// </summary>
        public WotEventSelection? EventSelection { get; }

        /// <summary>
        /// Gets the security floor an <c>auto</c> security scheme puts on
        /// endpoint selection (<c>uav:minimumSecurity</c>, WoT Binding
        /// Section 5.7.1), or <c>null</c> when the document constrains nothing.
        /// </summary>
        public WotSecurityFloor? SecurityFloor { get; }

        /// <summary>
        /// Returns a copy of this entry with the supplied executability.
        /// </summary>
        public WotCompiledForm WithExecutable(bool isExecutable)
        {
            if (isExecutable == IsExecutable)
            {
                return this;
            }
            return new WotCompiledForm(
                Binding, AffordanceKind, AffordanceName, JsonPointer, Operation, OpToken,
                Endpoint, Addressing, OperationInfo, Payload, Security, isExecutable, TargetMapping,
                EventSelection, SecurityFloor);
        }

        /// <summary>
        /// Returns a copy of this entry with the supplied target-mapping descriptor.
        /// </summary>
        /// <param name="targetMapping">The target-mapping descriptor to attach.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="targetMapping"/> is <c>null</c>.
        /// </exception>
        public WotCompiledForm WithTargetMapping(WotTargetMappingDescriptor targetMapping)
        {
            if (targetMapping is null)
            {
                throw new ArgumentNullException(nameof(targetMapping));
            }
            if (ReferenceEquals(targetMapping, TargetMapping))
            {
                return this;
            }
            return new WotCompiledForm(
                Binding, AffordanceKind, AffordanceName, JsonPointer, Operation, OpToken,
                Endpoint, Addressing, OperationInfo, Payload, Security, IsExecutable, targetMapping,
                EventSelection, SecurityFloor);
        }
    }

    /// <summary>
    /// The result of compiling a single form with a binder: the compiled entries
    /// (one per supported operation), the structured diagnostics and whether the
    /// form was validated (supported) at all.
    /// </summary>
    public sealed class WotBindingCompilation
    {
        /// <summary>
        /// Initializes a new compilation result.
        /// </summary>
        public WotBindingCompilation(
            bool isSupported,
            ImmutableArray<WotCompiledForm> entries,
            ImmutableArray<WotBindingDiagnostic> diagnostics)
        {
            IsSupported = isSupported;
            Entries = entries.IsDefault ? [] : entries;
            Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        }

        /// <summary>
        /// Gets whether the form was validated and compiled. A supported form has
        /// at least one compiled entry and no error diagnostics.
        /// </summary>
        public bool IsSupported { get; }

        /// <summary>
        /// Gets the compiled entries.
        /// </summary>
        public ImmutableArray<WotCompiledForm> Entries { get; }

        /// <summary>
        /// Gets the structured diagnostics.
        /// </summary>
        public ImmutableArray<WotBindingDiagnostic> Diagnostics { get; }

        /// <summary>
        /// Gets whether any error diagnostic was produced.
        /// </summary>
        public bool HasErrors => Diagnostics.Any(d => d.IsError);

        /// <summary>
        /// Creates an unsupported result (a binder declined or rejected the form).
        /// </summary>
        public static WotBindingCompilation Unsupported(params WotBindingDiagnostic[] diagnostics)
        {
            return new WotBindingCompilation(false, [],
                        diagnostics is null ? [] : [.. diagnostics]);
        }

        /// <summary>
        /// Creates a supported result.
        /// </summary>
        public static WotBindingCompilation Supported(
            ImmutableArray<WotCompiledForm> entries, ImmutableArray<WotBindingDiagnostic> diagnostics)
        {
            return new WotBindingCompilation(true, entries, diagnostics);
        }
    }

    /// <summary>
    /// Validates and compiles WoT interaction forms into immutable binding plans.
    /// A planner performs no transport I/O, so a planner-only binder can validate
    /// and compile forms for protocols the runtime cannot execute.
    /// </summary>
    public interface IWotBindingPlanner
    {
        /// <summary>
        /// Validates and compiles a single form into a binding plan.
        /// </summary>
        WotBindingCompilation Compile(WotAffordanceForm form, WotBindingPlanContext context);
    }
}
