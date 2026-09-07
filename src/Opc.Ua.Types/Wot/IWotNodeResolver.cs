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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The NodeClass a WoT document expects a name to resolve to.
    /// </summary>
    public enum WotExpectedNodeClass
    {
        /// <summary>
        /// Any NodeClass is acceptable.
        /// </summary>
        Any,

        /// <summary>
        /// An ObjectType. A name that types an Object must resolve to one.
        /// </summary>
        ObjectType,

        /// <summary>
        /// A VariableType. A name that types a Variable must resolve to one.
        /// </summary>
        VariableType,

        /// <summary>
        /// A ReferenceType. A name a link <c>rel</c> or a <c>uav:refId</c>
        /// uses must resolve to one.
        /// </summary>
        /// <remarks>
        /// Telling this NodeClass apart from the others is what lets a
        /// relation naming an ObjectType be reported as naming the wrong kind
        /// of Node instead of merely being unresolvable, which is the
        /// difference between "this model does not define that" and "this
        /// model defines that, but it is not a ReferenceType".
        /// </remarks>
        ReferenceType
    }

    /// <summary>
    /// One node a <see cref="IWotNodeResolver"/> matched.
    /// </summary>
    /// <param name="NodeId">
    /// The node's identity, as a portable ExpandedNodeId string.
    /// </param>
    /// <param name="NodeClass">The node's NodeClass.</param>
    public readonly record struct WotResolvedNode(string NodeId, WotExpectedNodeClass NodeClass);

    /// <summary>
    /// Resolves a name or an identifier a WoT document uses to the OPC UA Node
    /// it names, following the WoT Binding Section 5.1.5 resolution order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 5.1.5 defines a <em>local context</em> with two parts, consulted
    /// in this order: the other WoT documents being converted together with
    /// this one, then a loaded AddressSpace as the fallback. A consumer uses
    /// the first part in which the name resolves, and reports the name as
    /// unresolved where it resolves in neither.
    /// </para>
    /// <para>
    /// An implementation of this interface covers one part. Compose them with
    /// <see cref="WotCompositeNodeResolver"/> to get the specified order; the
    /// composite is what a converter should be handed.
    /// </para>
    /// <para>
    /// A <em>compact model name</em> is a lookup hint and may match none, one
    /// or more than one node, so the result is a list. An
    /// <em>ExpandedNodeId</em> is definitive and matches exactly one node or
    /// none.
    /// </para>
    /// <para>
    /// This resolves nodes, not documents. A document IRI is resolved by
    /// <see cref="IWotThingResolver"/>, and Section 5.1.5 restricts it to the
    /// sibling documents only.
    /// </para>
    /// </remarks>
    public interface IWotNodeResolver
    {
        /// <summary>
        /// Gets whether this part of the local context holds the supplied
        /// namespace.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 5.2.1 tells a type binding apart from an
        /// ordinary <c>@type</c> annotation <em>by namespace</em>, not by
        /// whether the lookup happens to succeed: a name in a namespace the
        /// local context holds is a binding, and failing to resolve it is an
        /// error rather than a reason to treat it as an annotation. A name in a
        /// namespace neither part holds cannot have been meant as a type here,
        /// so it stays an annotation.
        /// </remarks>
        /// <param name="namespaceUri">The NamespaceUri to test.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns><c>true</c> when the namespace is held.</returns>
        ValueTask<bool> HoldsNamespaceAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves a NamespaceUri-qualified BrowseName.
        /// </summary>
        /// <param name="namespaceUri">
        /// The NamespaceUri the compact model name's prefix resolved to.
        /// </param>
        /// <param name="browseName">The unqualified BrowseName.</param>
        /// <param name="expected">
        /// The NodeClass the caller requires, or
        /// <see cref="WotExpectedNodeClass.Any"/>.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// Every match, which may be empty. More than one match makes the name
        /// ambiguous, which the caller resolves or reports.
        /// </returns>
        ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri,
            string browseName,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves a definitive ExpandedNodeId.
        /// </summary>
        /// <param name="expandedNodeId">
        /// The portable ExpandedNodeId, in the <c>nsu=…;…</c> form.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// The node, or <c>null</c> when this part of the local context does
        /// not hold it.
        /// </returns>
        ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// One ReferenceType a local context matched, and the direction the name
    /// that matched it expressed.
    /// </summary>
    /// <param name="NodeId">
    /// The ReferenceType's canonical identity, as a portable ExpandedNodeId
    /// string.
    /// </param>
    /// <param name="Name">
    /// The unqualified name that matched — the ReferenceType's BrowseName when
    /// <paramref name="IsForward"/> is <c>true</c>, its InverseName when it is
    /// <c>false</c>. It is carried back so a caller can report which of the two
    /// names a document used rather than only the identity behind it.
    /// </param>
    /// <param name="IsForward">
    /// <c>true</c> when the name matched the ReferenceType's BrowseName,
    /// <c>false</c> when it matched its InverseName. A reference named by an
    /// InverseName is the same reference read backwards, so it is emitted with
    /// its <c>IsForward</c> flag cleared. A symmetric ReferenceType has one
    /// name for both directions and therefore always reads forward.
    /// </param>
    public readonly record struct WotResolvedReferenceType(
        string NodeId,
        string Name,
        bool IsForward);

    /// <summary>
    /// An optional capability an <see cref="IWotNodeResolver"/> may also
    /// implement to resolve the ReferenceType a WoT Binding Section 5.3 link
    /// relation names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A link relation names a ReferenceType by its <em>model name</em>, and
    /// OPC 10000-3 gives every ReferenceType two names: the BrowseName reads
    /// the reference forward and the InverseName reads the same reference
    /// backwards. <see cref="IWotNodeResolver.ResolveByBrowseNameAsync"/>
    /// resolves BrowseNames only, so it cannot report the direction and cannot
    /// see an InverseName at all.
    /// </para>
    /// <para>
    /// This is deliberately a separate interface rather than a member of
    /// <see cref="IWotNodeResolver"/>: a local context that holds only
    /// documents describing no ReferenceType has none to offer, and the library
    /// targets frameworks without default interface implementations, so adding
    /// the member would break every existing implementation for no gain. The
    /// converter probes for the capability and falls back to the standard
    /// base-namespace names when a resolver does not offer it.
    /// </para>
    /// </remarks>
    public interface IWotReferenceTypeResolver
    {
        /// <summary>
        /// Resolves every ReferenceType this part of the local context holds
        /// whose BrowseName or InverseName is the supplied name.
        /// </summary>
        /// <remarks>
        /// The result is a list rather than a single value because a name is a
        /// lookup hint: one namespace may hold a ReferenceType whose BrowseName
        /// is the name and another whose InverseName is, and both are legitimate
        /// readings of the same spelling. Collapsing them here would pick one
        /// silently; returning both lets the caller settle the ambiguity with
        /// the definitive <c>uav:refId</c> of WoT Binding Section 6.2, or report
        /// it.
        /// </remarks>
        /// <param name="namespaceUri">
        /// The NamespaceUri the relation's prefix resolved to.
        /// </param>
        /// <param name="name">The unqualified BrowseName or InverseName.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// Every ReferenceType the name matched, which is empty when this part
        /// of the local context does not hold it.
        /// </returns>
        ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
            string namespaceUri,
            string name,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The resolver used when a caller supplies none: it holds nothing, so
    /// every name is unresolved.
    /// </summary>
    /// <remarks>
    /// A converter run without a local context can still convert a document
    /// that names no existing type. One that does name an existing type is
    /// reported as unresolved rather than silently mistyped, which is what
    /// Section 5.2.1 requires.
    /// </remarks>
    public sealed class NullWotNodeResolver : IWotNodeResolver
    {
        /// <summary>
        /// Gets the shared instance.
        /// </summary>
        public static NullWotNodeResolver Instance { get; } = new NullWotNodeResolver();

        private NullWotNodeResolver()
        {
        }

        /// <inheritdoc/>
        public ValueTask<bool> HoldsNamespaceAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<bool>(false);
        }

        /// <inheritdoc/>
        public ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri,
            string browseName,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<ArrayOf<WotResolvedNode>>(ArrayOf<WotResolvedNode>.Empty);
        }

        /// <inheritdoc/>
        public ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId,
            CancellationToken cancellationToken = default)
        {
            return new ValueTask<WotResolvedNode?>((WotResolvedNode?)null);
        }
    }

    /// <summary>
    /// Consults an ordered set of resolvers and uses the first that resolves,
    /// which is how WoT Binding Section 5.1.5 defines the local context.
    /// </summary>
    /// <remarks>
    /// Order matters and is the caller's to choose: Section 5.1.5 puts the
    /// sibling documents of the conversion first and a loaded AddressSpace
    /// second, so that a set of documents authored together resolves to itself
    /// and loading an unrelated companion model can never change what an
    /// existing document projects to.
    /// </remarks>
    public sealed class WotCompositeNodeResolver
        : IWotNodeResolver, IWotReferenceTypeResolver, IWotTypeDeclarationResolver
    {
        /// <summary>
        /// Initializes a composite over the supplied resolvers, in order.
        /// </summary>
        /// <param name="resolvers">The resolvers, most specific first.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="resolvers"/> is <c>null</c>.
        /// </exception>
        public WotCompositeNodeResolver(params IWotNodeResolver[] resolvers)
        {
            m_resolvers = resolvers ?? throw new ArgumentNullException(nameof(resolvers));
        }

        /// <inheritdoc/>
        public async ValueTask<bool> HoldsNamespaceAsync(
            string namespaceUri,
            CancellationToken cancellationToken = default)
        {
            foreach (IWotNodeResolver resolver in m_resolvers)
            {
                if (await resolver.HoldsNamespaceAsync(namespaceUri, cancellationToken)
                    .ConfigureAwait(false))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public async ValueTask<ArrayOf<WotResolvedNode>> ResolveByBrowseNameAsync(
            string namespaceUri,
            string browseName,
            WotExpectedNodeClass expected,
            CancellationToken cancellationToken = default)
        {
            foreach (IWotNodeResolver resolver in m_resolvers)
            {
                ArrayOf<WotResolvedNode> matches = await resolver
                    .ResolveByBrowseNameAsync(namespaceUri, browseName, expected, cancellationToken)
                    .ConfigureAwait(false);
                if (matches.Count > 0)
                {
                    return matches;
                }
            }

            return ArrayOf<WotResolvedNode>.Empty;
        }

        /// <inheritdoc/>
        public async ValueTask<WotResolvedNode?> ResolveByNodeIdAsync(
            string expandedNodeId,
            CancellationToken cancellationToken = default)
        {
            foreach (IWotNodeResolver resolver in m_resolvers)
            {
                WotResolvedNode? match = await resolver
                    .ResolveByNodeIdAsync(expandedNodeId, cancellationToken)
                    .ConfigureAwait(false);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A resolver that does not offer the capability contributes nothing
        /// rather than ending the walk, so one part of the local context
        /// holding no ReferenceType declarations never hides another that does.
        /// The first part that matches the name settles it, which is the same
        /// precedence <see cref="ResolveByBrowseNameAsync"/> and
        /// <see cref="ResolveByNodeIdAsync"/> follow: a set of documents
        /// authored together resolves to itself, and loading an unrelated
        /// companion model can never change what an existing document projects
        /// to.
        /// </remarks>
        public async ValueTask<ArrayOf<WotResolvedReferenceType>> ResolveReferenceTypesAsync(
            string namespaceUri,
            string name,
            CancellationToken cancellationToken = default)
        {
            foreach (IWotNodeResolver resolver in m_resolvers)
            {
                if (resolver is not IWotReferenceTypeResolver referenceTypes)
                {
                    continue;
                }
                ArrayOf<WotResolvedReferenceType> matches = await referenceTypes
                    .ResolveReferenceTypesAsync(namespaceUri, name, cancellationToken)
                    .ConfigureAwait(false);
                if (matches.Count > 0)
                {
                    return matches;
                }
            }

            return ArrayOf<WotResolvedReferenceType>.Empty;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The first part of the local context that holds the type answers for
        /// it, which is the same first-source precedence node resolution
        /// follows. Anything else would let a loaded AddressSpace contribute
        /// declarations to a type a sibling document already defines, and the
        /// merged answer would describe a type neither source states.
        /// </remarks>
        public async ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
            string typeNodeId,
            WotDeclarationScope scope,
            CancellationToken cancellationToken = default)
        {
            foreach (IWotNodeResolver resolver in m_resolvers)
            {
                if (resolver is not IWotTypeDeclarationResolver declarations)
                {
                    continue;
                }
                WotTypeDeclarationSet? set = await declarations
                    .ResolveDeclarationsAsync(typeNodeId, scope, cancellationToken)
                    .ConfigureAwait(false);
                if (set is not null)
                {
                    return set;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets whether any part of the local context can report instance
        /// declarations at all.
        /// </summary>
        /// <remarks>
        /// A rule that depends on declarations - <c>uav:additionalProperties</c>
        /// with the value <c>false</c>, for instance - must fail explicitly
        /// where nothing can evaluate it, rather than pass because no
        /// declaration contradicted it. Telling "no part offers the capability"
        /// apart from "every part offers it and none holds the type" is what
        /// makes that distinction possible.
        /// </remarks>
        public bool OffersDeclarations()
        {
            foreach (IWotNodeResolver resolver in m_resolvers)
            {
                if (resolver is IWotTypeDeclarationResolver)
                {
                    return true;
                }
            }
            return false;
        }

        private readonly IWotNodeResolver[] m_resolvers;
    }
}
