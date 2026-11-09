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

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Supplies actual host forms for bulk-selected and supporting
    /// projection-routed affordances (OPC 10101-2).
    /// </summary>
    public interface IWotProjectionFormProvider
    {
        /// <summary>
        /// Returns the forms the host actually serves, in their intended order.
        /// </summary>
        /// <remarks>
        /// Forms belong to the projection host, including their context,
        /// security and schema dependencies. Hrefs must resolve to absolute
        /// endpoints in its original origin; relative hrefs use its effective base.
        /// Return detached JSON objects that remain valid after this call;
        /// an empty result means unavailable and fails resolution.
        /// This call must not publish an endpoint or change source ownership.
        /// The provider must bound its own I/O and honor the cancellation token.
        /// Expected I/O, invalid-operation, timeout, JSON and format failures are
        /// reported as unsuccessful resolution results. Caller cancellation and
        /// unexpected programming exceptions propagate without a partial view.
        /// The resolver charges each request and returned payload to the shared
        /// resolution budget and owns a separate document for generated forms.
        /// </remarks>
        /// <param name="context">The WoT projection form context.</param>
        /// <param name="cancellationToken">The cancellation token supplied by the caller.</param>
        ValueTask<ArrayOf<JsonElement>> GetFormsAsync(
            WotProjectionFormContext context,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The selected source and projection owner for one host-form request.
    /// Documents are borrowed and remain valid only until the provider returns.
    /// </summary>
    public sealed class WotProjectionFormContext
    {
        /// <summary>
        /// Initializes a new <c>WotProjectionFormContext</c> instance.
        /// </summary>
        /// <param name="projectionDocument">The projection document used to initialize the instance.</param>
        /// <param name="projectionLocation">The projection location used to initialize the instance.</param>
        /// <param name="projectionBase">The projection base used to initialize the instance.</param>
        /// <param name="sourceDocument">The source document used to initialize the instance.</param>
        /// <param name="sourceLocation">The source location used to initialize the instance.</param>
        /// <param name="sourcePointer">The source pointer used to initialize the instance.</param>
        /// <param name="name">The name used to initialize <c>WotProjectionFormContext</c>.</param>
        /// <param name="kind">The kind used to initialize the instance.</param>
        /// <param name="resultKind">The result kind used to initialize the instance.</param>
        /// <param name="resolutionContext">The resolution context used to initialize the instance.</param>
        internal WotProjectionFormContext(
            WotDocument projectionDocument,
            string projectionLocation,
            string projectionBase,
            WotDocument sourceDocument,
            string sourceLocation,
            string sourcePointer,
            string name,
            WotAffordanceKind kind,
            WotDocumentKind resultKind,
            WotResolutionContext resolutionContext)
        {
            ProjectionDocument = projectionDocument;
            ProjectionLocation = projectionLocation;
            ProjectionBase = projectionBase;
            SourceDocument = sourceDocument;
            SourceLocation = sourceLocation;
            SourcePointer = sourcePointer;
            Name = name;
            Kind = kind;
            ResultKind = resultKind;
            ResolutionContext = resolutionContext;
        }

        /// <summary>
        /// Gets the immutable original projection document whose context and
        /// declarations govern the host forms, not a document containing the
        /// provider's newly generated forms.
        /// </summary>
        public WotDocument ProjectionDocument { get; }

        /// <summary>
        /// Gets the projection's original document location, including the
        /// retrieved location of a nested projection rather than its authored id.
        /// </summary>
        public string ProjectionLocation { get; }

        /// <summary>
        /// Gets the projection's effective endpoint base in its original space.
        /// </summary>
        public string ProjectionBase { get; }

        /// <summary>
        /// Gets the original selected source document, not the host document.
        /// </summary>
        public WotDocument SourceDocument { get; }

        /// <summary>
        /// Gets the source's document location, distinct from its endpoint base.
        /// </summary>
        public string SourceLocation { get; }

        /// <summary>
        /// Gets the selected affordance's canonical pointer in its source.
        /// </summary>
        public string SourcePointer { get; }

        /// <summary>
        /// Gets the affordance's final name in the projection.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the selected affordance kind.
        /// </summary>
        public WotAffordanceKind Kind { get; }

        /// <summary>
        /// Gets the projection's Thing Description or Thing Model result kind.
        /// </summary>
        public WotDocumentKind ResultKind { get; }

        /// <summary>
        /// Gets the caller's shared resolution context and budgets.
        /// </summary>
        public WotResolutionContext ResolutionContext { get; }
    }
}
