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
 *
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
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Controls whether a converter emits the opaque byte-exact
    /// <c>uav:nodeSet</c> preservation envelope.
    /// </summary>
    public enum WotNodeSetPreservationMode
    {
        /// <summary>
        /// Emit the envelope only if the structured native projection cannot
        /// reproduce the source NodeSet.
        /// </summary>
        WhenRequired,

        /// <summary>Always emit the byte-exact preservation envelope.</summary>
        Always,

        /// <summary>
        /// Never emit the envelope; report an error if native projection is not
        /// complete. This mode is intended for conformance and completeness tests.
        /// </summary>
        Never
    }

    /// <summary>
    /// How strictly a document is held to the WoT Binding revision this
    /// library implements.
    /// </summary>
    public enum WotConformanceMode
    {
        /// <summary>
        /// Process what is understood and preserve the rest. An unknown
        /// <c>uav:</c> term is carried unchanged as residue rather than
        /// reported, a revision this library does not implement is accepted,
        /// and a revision 1.0 opaque object whose top-level keys are not
        /// namespaced is preserved with a deprecation warning. This is the
        /// default and the behaviour WoT Binding Sections 4.1, 6.6, 9.4 and
        /// 10.2 require of a consumer.
        /// </summary>
        Permissive,

        /// <summary>
        /// Additionally report what the permissive mode tolerates: a
        /// <c>uav:</c> term this revision does not define, a declared
        /// vocabulary revision this library does not implement, a missing
        /// required conformance claim, and an opaque object that breaks the
        /// key or bound rules of Section 6.6. Intended for authoring and
        /// conformance testing, where a misspelled term should fail rather
        /// than travel silently.
        /// </summary>
        Strict
    }

    /// <summary>
    /// Resource limits and behavioural switches used while reading and
    /// writing WoT documents, preservation envelopes and NodeSet2 payloads.
    /// </summary>
    /// <remarks>
    /// All limits are enforced deliberately so that a malformed or hostile
    /// document cannot exhaust memory or stack. The defaults are generous
    /// enough for real companion specifications yet bounded.
    /// </remarks>
    public sealed class WotNodeSetConverterOptions
    {
        /// <summary>
        /// Gets or sets the preservation-envelope policy. The default uses
        /// readable mapping plus structured fallback and emits an opaque envelope
        /// only when required.
        /// </summary>
        public WotNodeSetPreservationMode PreservationMode { get; set; } =
            WotNodeSetPreservationMode.WhenRequired;

        /// <summary>
        /// Gets or sets whether session-local identifiers are tolerated instead
        /// of rejected.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 5.1.1 forbids the session-local
        /// <c>ns=&lt;index&gt;</c> form in NodeId-valued terms, and Section 5.1.3
        /// forbids a numeric namespace prefix in <c>uav:browseName</c> and
        /// <c>uav:browsePath</c>. Both were permitted by OPC 10101 v1.00, so a
        /// document authored against that release can still carry them. The
        /// default is <c>false</c>, which reports each occurrence as an error and
        /// matches the release 1.1 validator. Set it to <c>true</c> to downgrade
        /// those errors to warnings while migrating such a document; the value
        /// is then interpreted exactly as v1.00 defined it.
        /// </remarks>
        public bool AllowNonPortableIdentifiers { get; set; }

        /// <summary>
        /// Gets or sets how strictly a document is held to the WoT Binding
        /// revision this library implements. The default is
        /// <see cref="WotConformanceMode.Permissive"/>, which processes what it
        /// understands and preserves the rest.
        /// </summary>
        /// <remarks>
        /// Strict conformance is opt-in because Sections 4.1 and 6.6 forbid a
        /// consumer from rejecting a document merely because it declares an
        /// unimplemented revision or carries a term the consumer does not know:
        /// the value has to be preserved, not refused. A tool that authors or
        /// certifies documents wants the opposite, so it asks for it.
        /// </remarks>
        public WotConformanceMode ConformanceMode { get; set; } = WotConformanceMode.Permissive;

        /// <summary>
        /// Gets or sets whether the document is being validated as one
        /// <em>authored</em> against a published revision of this Binding,
        /// rather than read as one a consumer did not write
        /// (WoT Binding Section 4.1). The default is <c>false</c>, the consumer
        /// rule.
        /// </summary>
        /// <remarks>
        /// Section 4.1 states the two checks side by side and makes them
        /// deliberately different. An author names a revision this Binding
        /// publishes and only conformance units Section 11 defines, so an
        /// authoring validator reports anything else as an error. A consumer
        /// applies the syntactic rule alone: a well-formed revision it does not
        /// implement is <em>unsupported</em> and a claim it does not know is
        /// <em>unrecognized</em>, neither is a reason to reject the document,
        /// and both are preserved unchanged. A consumer that rejected such a
        /// document would be refusing to read one that is syntactically valid,
        /// whose known terms it understands and whose unknown terms it is
        /// already required to carry - which is the failure that makes a
        /// vocabulary unextendable.
        /// </remarks>
        public bool AuthoringValidation { get; set; }

        /// <summary>
        /// Gets or sets the WoT Binding Section 11 conformance units and
        /// profiles a document is required to claim through <c>uav:profile</c>.
        /// Empty by default, which requires no claim at all. Only
        /// <see cref="WotConformanceMode.Strict"/> enforces it, because a claim
        /// is a statement about the producer rather than a property of the
        /// document.
        /// </summary>
        public ArrayOf<string> RequiredConformance { get; set; }

        /// <summary>
        /// Gets or sets the maximum accepted WoT JSON document size in bytes.
        /// </summary>
        public int MaxJsonDocumentSize { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum accepted or decoded NodeSet2 XML size in bytes.
        /// </summary>
        public int MaxNodeSetSize { get; set; } = 64 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum JSON nesting depth.
        /// </summary>
        public int MaxJsonDepth { get; set; } = 128;

        /// <summary>
        /// Gets or sets the maximum XML nesting depth accepted when reading a
        /// decoded or synthesized NodeSet2 document.
        /// </summary>
        public int MaxXmlDepth { get; set; } = 256;

        /// <summary>
        /// Gets or sets the maximum number of UANode records projected into or
        /// reconstructed from a native <c>uav:nodes</c> projection.
        /// </summary>
        public int MaxNodeCount { get; set; } = 1_000_000;

        /// <summary>
        /// Gets or sets the maximum number of affordances (properties, actions
        /// and events combined) processed for a single Thing.
        /// </summary>
        public int MaxAffordanceCount { get; set; } = 100_000;

        /// <summary>
        /// Gets or sets the maximum external-document resolution depth used
        /// when following contexts, schemas and referenced TD/TM documents.
        /// </summary>
        public int MaxResolverDepth { get; set; } = 16;

        /// <summary>
        /// Gets or sets the maximum number of external documents (contexts,
        /// schemas and referenced TD/TM documents combined) resolved for a
        /// single top-level conversion.
        /// </summary>
        public int MaxResolverDocuments { get; set; } = 256;

        /// <summary>
        /// Gets or sets the maximum accepted size of a single externally
        /// resolved document.
        /// </summary>
        public int MaxResolverDocumentBytes { get; set; } = 16 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum cumulative size of all documents
        /// externally resolved for a single top-level conversion.
        /// </summary>
        public long MaxResolverTotalBytes { get; set; } = 128L * 1024 * 1024;

        /// <summary>
        /// Validates the option values and throws when a limit is not positive.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when any configured limit is not strictly positive.
        /// </exception>
        public void Validate()
        {
            if (PreservationMode is not (
                WotNodeSetPreservationMode.WhenRequired or
                WotNodeSetPreservationMode.Always or
                WotNodeSetPreservationMode.Never))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(PreservationMode),
                    PreservationMode,
                    "The preservation mode is not defined.");
            }
            if (ConformanceMode is not (
                WotConformanceMode.Permissive or WotConformanceMode.Strict))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ConformanceMode),
                    ConformanceMode,
                    "The conformance mode is not defined.");
            }
            foreach (string claim in RequiredConformance)
            {
                if (!WotBindingConformance.IsConformanceName(claim))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(RequiredConformance),
                        claim,
                        "The required claim is not a conformance unit or profile " +
                        "of WoT Binding Section 11.");
                }
            }
            EnsurePositive(MaxJsonDocumentSize, nameof(MaxJsonDocumentSize));
            EnsurePositive(MaxNodeSetSize, nameof(MaxNodeSetSize));
            EnsurePositive(MaxJsonDepth, nameof(MaxJsonDepth));
            EnsurePositive(MaxXmlDepth, nameof(MaxXmlDepth));
            EnsurePositive(MaxNodeCount, nameof(MaxNodeCount));
            EnsurePositive(MaxAffordanceCount, nameof(MaxAffordanceCount));
            EnsurePositive(MaxResolverDepth, nameof(MaxResolverDepth));
            EnsurePositive(MaxResolverDocuments, nameof(MaxResolverDocuments));
            EnsurePositive(MaxResolverDocumentBytes, nameof(MaxResolverDocumentBytes));
            if (MaxResolverTotalBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxResolverTotalBytes),
                    MaxResolverTotalBytes,
                    "The configured limit must be a positive value.");
            }
        }

        /// <summary>
        /// Projects the resolver limits configured on this instance onto a
        /// <see cref="WotResolverOptions"/> suitable for seeding a single
        /// <see cref="WotResolutionContext"/> per top-level conversion.
        /// </summary>
        /// <returns>The equivalent bounded resolution options.</returns>
        public WotResolverOptions ToResolverOptions()
        {
            return new WotResolverOptions
            {
                MaxDepth = MaxResolverDepth,
                MaxDocuments = MaxResolverDocuments,
                MaxDocumentBytes = MaxResolverDocumentBytes,
                MaxTotalBytes = MaxResolverTotalBytes
            };
        }

        /// <summary>
        /// Projects the limits a NodeSet2 comparison is bounded by onto a
        /// <see cref="NodeSetComparisonOptions"/>.
        /// </summary>
        /// <remarks>
        /// Comparing two NodeSets is not a WoT operation and does not depend
        /// on any conversion setting, so the comparison is given the one limit
        /// that applies to it rather than these options.
        /// </remarks>
        /// <returns>The equivalent bounded comparison options.</returns>
        public NodeSetComparisonOptions ToComparisonOptions()
        {
            return new NodeSetComparisonOptions
            {
                MaxXmlDepth = MaxXmlDepth
            };
        }

        private static void EnsurePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    name,
                    value,
                    "The configured limit must be a positive value.");
            }
        }
    }
}
