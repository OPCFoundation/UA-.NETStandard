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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The NodeClass an instance declaration of a type declares.
    /// </summary>
    public enum WotDeclarationKind
    {
        /// <summary>
        /// The declaration's NodeClass is not one this capability models.
        /// </summary>
        Unknown,

        /// <summary>
        /// An Object the type declares, reached by a hierarchical reference.
        /// </summary>
        Object,

        /// <summary>
        /// A Variable the type declares, reached by <c>HasComponent</c> or
        /// <c>HasProperty</c>.
        /// </summary>
        Variable,

        /// <summary>
        /// A Method the type declares, reached by <c>HasComponent</c>.
        /// </summary>
        Method,

        /// <summary>
        /// An EventType the type declares it can raise, reached by
        /// <c>GeneratesEvent</c>. It is not an instance declaration of
        /// OPC 10000-3, but a WoT event affordance maps onto it, so the two
        /// have to be told apart by name in the same table.
        /// </summary>
        Event
    }

    /// <summary>
    /// The ModellingRule an instance declaration carries.
    /// </summary>
    public enum WotModellingRule
    {
        /// <summary>
        /// The declaration carries no ModellingRule.
        /// </summary>
        None,

        /// <summary>
        /// <c>Mandatory</c>: every instance of the type has the declaration.
        /// </summary>
        Mandatory,

        /// <summary>
        /// <c>Optional</c>: an instance may have the declaration.
        /// </summary>
        Optional,

        /// <summary>
        /// <c>MandatoryPlaceholder</c>: at least one instance of the pattern
        /// the declaration stands for is present, under a name the instance
        /// chooses.
        /// </summary>
        MandatoryPlaceholder,

        /// <summary>
        /// <c>OptionalPlaceholder</c>: any number of instances of the pattern,
        /// including none.
        /// </summary>
        OptionalPlaceholder,

        /// <summary>
        /// <c>ExposesItsArray</c>.
        /// </summary>
        ExposesItsArray
    }

    /// <summary>
    /// Which declarations of a type a caller asked for.
    /// </summary>
    /// <remarks>
    /// This is what <c>uav:includeInherited</c> selects (WoT Binding
    /// Section 5.2.1): <c>false</c> means the declarations the type itself
    /// states, <c>true</c> means those plus every declaration it inherits,
    /// with a subtype's declaration hiding a supertype's declaration of the
    /// same qualified BrowseName.
    /// </remarks>
    public enum WotDeclarationScope
    {
        /// <summary>
        /// Only the declarations the type itself states.
        /// </summary>
        Direct,

        /// <summary>
        /// The bounded effective closure: the type's own declarations plus the
        /// ones it inherits from its supertypes.
        /// </summary>
        Effective
    }

    /// <summary>
    /// One instance declaration of a resolved ObjectType or VariableType.
    /// </summary>
    /// <remarks>
    /// The record carries everything a caller needs to populate a matching
    /// member of a Thing Description without having to browse the type a second
    /// time: what the declaration is, how it is reached, what it is typed as,
    /// and which type declared it.
    /// </remarks>
    public sealed record WotTypeDeclaration
    {
        /// <summary>
        /// Gets the NamespaceUri of the declaration's BrowseName.
        /// </summary>
        public required string NamespaceUri { get; init; }

        /// <summary>
        /// Gets the unqualified BrowseName the declaration is known by. A
        /// member matches a declaration only when this and
        /// <see cref="NamespaceUri"/> are both exactly equal.
        /// </summary>
        public required string BrowseName { get; init; }

        /// <summary>
        /// Gets the NodeClass the declaration declares.
        /// </summary>
        public required WotDeclarationKind Kind { get; init; }

        /// <summary>
        /// Gets the type that states the declaration, as a portable
        /// ExpandedNodeId string. For an inherited declaration this is the
        /// supertype it came from rather than the type that was resolved.
        /// </summary>
        public required string DeclaringTypeNodeId { get; init; }

        /// <summary>
        /// Gets the declaration's own identity, as a portable ExpandedNodeId
        /// string, or an empty string when the source does not name one.
        /// </summary>
        public string NodeId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the name of the ReferenceType the declaring type reaches the
        /// declaration through - <c>HasComponent</c>, <c>HasProperty</c>,
        /// <c>GeneratesEvent</c> or a companion model's own subtype of them.
        /// </summary>
        public string ReferenceTypeName { get; init; } = "HasComponent";

        /// <summary>
        /// Gets the declaration's HasTypeDefinition target, as a portable
        /// ExpandedNodeId string, or an empty string for a Method and for a
        /// source that does not state one.
        /// </summary>
        public string TypeDefinitionNodeId { get; init; } = string.Empty;

        /// <summary>
        /// Gets the Method declaration a Method instance has to point its
        /// <c>MethodDeclarationId</c> at, as a portable ExpandedNodeId string.
        /// It is empty for every other <see cref="Kind"/>.
        /// </summary>
        public string MethodDeclarationNodeId { get; init; } = string.Empty;

        /// <summary>
        /// Gets a Variable declaration's DataType, as a portable
        /// ExpandedNodeId string, or an empty string when the declaration is
        /// not a Variable or the source does not state one.
        /// </summary>
        public string DataType { get; init; } = string.Empty;

        /// <summary>
        /// Gets a Variable declaration's ValueRank. It is
        /// <see cref="ValueRanks.Scalar"/> when the source states none.
        /// </summary>
        public int ValueRank { get; init; } = ValueRanks.Scalar;

        /// <summary>
        /// Gets a Variable declaration's ArrayDimensions, which is empty for a
        /// scalar and for a source that states none.
        /// </summary>
        public ArrayOf<uint> ArrayDimensions { get; init; }

        /// <summary>
        /// Gets the ModellingRule the declaration carries.
        /// </summary>
        public WotModellingRule ModellingRule { get; init; } = WotModellingRule.None;

        /// <summary>
        /// Gets whether the declaration was inherited from a supertype rather
        /// than stated by the resolved type itself.
        /// </summary>
        public bool IsInherited { get; init; }

        /// <summary>
        /// Gets whether an instance of the declaring type has to carry the
        /// declaration, which is what makes a same-named member of a Thing
        /// Description populate it rather than add a sibling beside it.
        /// </summary>
        public bool IsMandatory =>
            ModellingRule is WotModellingRule.Mandatory
                or WotModellingRule.MandatoryPlaceholder;
    }

    /// <summary>
    /// The instance declarations one part of the local context reported for one
    /// resolved type.
    /// </summary>
    public sealed record WotTypeDeclarationSet
    {
        /// <summary>
        /// Gets the type the declarations belong to, as a portable
        /// ExpandedNodeId string.
        /// </summary>
        public required string TypeNodeId { get; init; }

        /// <summary>
        /// Gets the declarations, ordered by NamespaceUri then BrowseName then
        /// kind so that the same type always reports the same sequence.
        /// </summary>
        public ArrayOf<WotTypeDeclaration> Declarations { get; init; }

        /// <summary>
        /// Gets the supertypes the effective closure walked, nearest first, as
        /// portable ExpandedNodeId strings. It is empty for a
        /// <see cref="WotDeclarationScope.Direct"/> request.
        /// </summary>
        public ArrayOf<string> Supertypes { get; init; }

        /// <summary>
        /// Gets whether the reported set is the whole set. It is <c>false</c>
        /// when a bound stopped the supertype walk or a supertype could not be
        /// read, in which case <see cref="Detail"/> says why and a caller must
        /// not conclude that an undeclared member is undeclared.
        /// </summary>
        public bool IsComplete { get; init; } = true;

        /// <summary>
        /// Gets why the set is incomplete, or <c>null</c> when it is complete.
        /// </summary>
        public string? Detail { get; init; }
    }

    /// <summary>
    /// An optional capability an <see cref="IWotNodeResolver"/> may also
    /// implement to report the effective instance declarations of a type it
    /// resolved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WoT Binding Section 5.2.1 lets a document bind its projected Node to a
    /// type that already exists, and then makes a member whose BrowseName is
    /// one the type already declares <em>populate that declaration</em> rather
    /// than add a second Node beside it. Deciding that needs more than the
    /// identity <see cref="IWotNodeResolver"/> returns: it needs the
    /// declaration's ReferenceType, type definition, DataType and ModellingRule,
    /// because the populated Node has to be the one the type declared and not a
    /// differently-typed Node that merely shares its name.
    /// </para>
    /// <para>
    /// It is a separate interface for the same reason
    /// <see cref="IWotReferenceTypeResolver"/> is: a local context that holds
    /// only documents describing no type has nothing to report, the library
    /// targets frameworks without default interface implementations, and
    /// <see cref="IWotNodeResolver"/> is public API that existing
    /// implementations must keep satisfying unchanged. A caller probes for the
    /// capability; where no part of the local context offers it, a rule that
    /// depends on declarations is reported as unevaluable rather than silently
    /// skipped.
    /// </para>
    /// <para>
    /// Every implementation bounds and cycle-checks its supertype walk: a
    /// hierarchy that loops, or one deeper than
    /// <see cref="WotTypeDeclarations.MaxSupertypeDepth"/>, yields a set whose
    /// <see cref="WotTypeDeclarationSet.IsComplete"/> is <c>false</c> instead of
    /// running forever or reporting a partial closure as if it were whole.
    /// </para>
    /// </remarks>
    public interface IWotTypeDeclarationResolver
    {
        /// <summary>
        /// Reports the instance declarations of a resolved type.
        /// </summary>
        /// <param name="typeNodeId">
        /// The type's identity, as a portable ExpandedNodeId string. It is the
        /// value <see cref="WotResolvedNode.NodeId"/> carried.
        /// </param>
        /// <param name="scope">
        /// Whether only the type's own declarations are wanted, or the bounded
        /// effective closure over its supertypes as well.
        /// </param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>
        /// The declarations, or <c>null</c> when this part of the local context
        /// does not hold the type at all. A type it holds that declares nothing
        /// reports an empty set rather than <c>null</c>, because "declares
        /// nothing" and "is not here" are different answers.
        /// </returns>
        ValueTask<WotTypeDeclarationSet?> ResolveDeclarationsAsync(
            string typeNodeId,
            WotDeclarationScope scope,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Shared helpers and bounds for the
    /// <see cref="IWotTypeDeclarationResolver"/> capability.
    /// </summary>
    public static class WotTypeDeclarations
    {
        /// <summary>
        /// The maximum number of supertypes an effective closure walks before
        /// it reports itself incomplete.
        /// </summary>
        /// <remarks>
        /// OPC 10000-5 type hierarchies are shallow; a hundred levels is far
        /// beyond any real model and still small enough that a hostile or
        /// mistaken document cannot turn one conversion into unbounded work.
        /// The bound is public so a test can prove the truncation rather than
        /// assume it.
        /// </remarks>
        public const int MaxSupertypeDepth = 100;

        /// <summary>
        /// Orders declarations so that one type always reports one sequence.
        /// </summary>
        /// <param name="left">The first declaration.</param>
        /// <param name="right">The second declaration.</param>
        /// <returns>The ordinal comparison result.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="left"/> or <paramref name="right"/> is <c>null</c>.
        /// </exception>
        public static int Compare(WotTypeDeclaration left, WotTypeDeclaration right)
        {
            if (left is null)
            {
                throw new ArgumentNullException(nameof(left));
            }
            if (right is null)
            {
                throw new ArgumentNullException(nameof(right));
            }
            int order = string.CompareOrdinal(left.NamespaceUri, right.NamespaceUri);
            if (order != 0)
            {
                return order;
            }
            order = string.CompareOrdinal(left.BrowseName, right.BrowseName);
            return order != 0 ? order : ((int)left.Kind).CompareTo((int)right.Kind);
        }

        /// <summary>
        /// Maps a NodeSet ModellingRule name onto the enumeration.
        /// </summary>
        /// <param name="name">The ModellingRule's BrowseName, or <c>null</c>.</param>
        /// <returns>The rule, or <see cref="WotModellingRule.None"/>.</returns>
        public static WotModellingRule ToModellingRule(string? name)
        {
            return name switch
            {
                "Mandatory" => WotModellingRule.Mandatory,
                "Optional" => WotModellingRule.Optional,
                "MandatoryPlaceholder" => WotModellingRule.MandatoryPlaceholder,
                "OptionalPlaceholder" => WotModellingRule.OptionalPlaceholder,
                "ExposesItsArray" => WotModellingRule.ExposesItsArray,
                _ => WotModellingRule.None
            };
        }

        /// <summary>
        /// Maps a ModellingRule NodeId onto the enumeration.
        /// </summary>
        /// <param name="nodeId">The ModellingRule's identity.</param>
        /// <returns>The rule, or <see cref="WotModellingRule.None"/>.</returns>
        public static WotModellingRule FromModellingRuleId(string? nodeId)
        {
            return nodeId switch
            {
                WotVocabulary.ModellingRuleMandatory => WotModellingRule.Mandatory,
                WotVocabulary.ModellingRuleOptional => WotModellingRule.Optional,
                WotVocabulary.ModellingRuleMandatoryPlaceholder =>
                    WotModellingRule.MandatoryPlaceholder,
                WotVocabulary.ModellingRuleOptionalPlaceholder =>
                    WotModellingRule.OptionalPlaceholder,
                _ => WotModellingRule.None
            };
        }
    }
}
