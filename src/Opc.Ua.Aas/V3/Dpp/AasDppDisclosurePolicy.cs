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

namespace Opc.Ua.Aas.V3
{
    /// <summary>
    /// The Annex XIII regulatory classes used by the battery passport DPP mapping.
    /// </summary>
    public enum AasDppRegulatoryClass
    {
        /// <summary>
        /// Content available to the public.
        /// </summary>
        AvailableToPublic,

        /// <summary>
        /// Content available to persons with a legitimate interest and the Commission.
        /// </summary>
        LegitimateInterestAndCommission,

        /// <summary>
        /// Content available to notified bodies and market surveillance authorities.
        /// </summary>
        NotifiedBodiesAndMarketSurveillanceAuthorities
    }

    /// <summary>
    /// Matches an AAS entity to a DPP regulatory class.
    /// </summary>
    /// <param name="ModelType">The AAS metamodel class name to match.</param>
    /// <param name="IdShort">The AAS <c>idShort</c> to match, or an empty string to match any entity of the model type.</param>
    /// <param name="RegulatoryClass">The regulatory class to apply.</param>
    public sealed record AasDppDisclosureRule(
        string ModelType,
        string IdShort,
        AasDppRegulatoryClass RegulatoryClass);

    /// <summary>
    /// DPP disclosure policy for mapping Annex XIII classes to AAS disclosure tiers.
    /// </summary>
    /// <remarks>
    /// The policy preserves the regulatory class in <see cref="AasDisclosureDecision.DisclosureClass"/>
    /// while mapping the class to the two AAS tiers. The two controlled classes differ by the
    /// authorization a Consumer must present. Implementations must advertise that authorization through
    /// the AAS <c>Authorization</c> attribute and must not rely on
    /// <see cref="AASDisclosureTierDataType.Controlled"/> alone to separate them.
    ///
    /// A public tier on a submodel does not permit controlled elements to be included in an environment
    /// document. A filtered environment document is a session-specific projection, and callers must not
    /// infer that an omitted element is absent from the passport.
    /// </remarks>
    public sealed class AasDppDisclosurePolicy : IAasDisclosurePolicy
    {
        /// <summary>
        /// Initializes a policy that treats unlisted entities as public.
        /// </summary>
        public AasDppDisclosurePolicy()
            : this(ArrayOf<AasDppDisclosureRule>.Empty, AasDppRegulatoryClass.AvailableToPublic)
        {
        }

        /// <summary>
        /// Initializes a policy with a default regulatory class.
        /// </summary>
        /// <param name="defaultRegulatoryClass">The class to apply when no rule matches.</param>
        public AasDppDisclosurePolicy(AasDppRegulatoryClass defaultRegulatoryClass)
            : this(ArrayOf<AasDppDisclosureRule>.Empty, defaultRegulatoryClass)
        {
        }

        /// <summary>
        /// Initializes a policy with explicit rules and a default regulatory class.
        /// </summary>
        /// <param name="rules">The rules to evaluate in order.</param>
        /// <param name="defaultRegulatoryClass">The class to apply when no rule matches.</param>
        public AasDppDisclosurePolicy(
            ArrayOf<AasDppDisclosureRule> rules,
            AasDppRegulatoryClass defaultRegulatoryClass)
        {
            Rules = rules.IsNull ? ArrayOf<AasDppDisclosureRule>.Empty : rules;
            DefaultRegulatoryClass = defaultRegulatoryClass;
        }

        /// <summary>
        /// Gets the default regulatory class used when no rule matches.
        /// </summary>
        public AasDppRegulatoryClass DefaultRegulatoryClass { get; }

        /// <summary>
        /// Gets the rules evaluated before the default regulatory class is used.
        /// </summary>
        public ArrayOf<AasDppDisclosureRule> Rules { get; }

        /// <inheritdoc/>
        public AasDisclosureDecision GetDisclosure(AasReferable entity)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            AasDppRegulatoryClass regulatoryClass = DefaultRegulatoryClass;
            for (int ii = 0; ii < Rules.Count; ii++)
            {
                AasDppDisclosureRule rule = Rules[ii];
                if (Matches(rule, entity))
                {
                    regulatoryClass = rule.RegulatoryClass;
                    break;
                }
            }

            return Map(regulatoryClass);
        }

        /// <summary>
        /// Maps a DPP regulatory class to its disclosure tier and authorization text.
        /// </summary>
        /// <param name="regulatoryClass">The DPP regulatory class.</param>
        /// <returns>The disclosure decision preserving the regulatory class.</returns>
        public static AasDisclosureDecision Map(AasDppRegulatoryClass regulatoryClass)
        {
            return regulatoryClass switch
            {
                AasDppRegulatoryClass.AvailableToPublic => new AasDisclosureDecision(
                    AASDisclosureTierDataType.Public,
                    "available to the public",
                    string.Empty),
                AasDppRegulatoryClass.LegitimateInterestAndCommission => new AasDisclosureDecision(
                    AASDisclosureTierDataType.Controlled,
                    "available to persons with a legitimate interest and the Commission",
                    "Authorization for persons with a legitimate interest and the Commission."),
                AasDppRegulatoryClass.NotifiedBodiesAndMarketSurveillanceAuthorities => new AasDisclosureDecision(
                    AASDisclosureTierDataType.Controlled,
                    "available to notified bodies and market surveillance authorities",
                    "Authorization for notified bodies and market surveillance authorities."),
                _ => throw new ArgumentOutOfRangeException(nameof(regulatoryClass))
            };
        }

        private static bool Matches(AasDppDisclosureRule rule, AasReferable entity)
        {
            if (!string.Equals(rule.ModelType, entity.ModelType, StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrEmpty(rule.IdShort))
            {
                return true;
            }

            return entity.IdShort.TryGetValue(out string? idShort) &&
                string.Equals(rule.IdShort, idShort, StringComparison.Ordinal);
        }
    }
}
