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

namespace Opc.Ua.WotCon.Binding
{
    /// <summary>
    /// The standards maturity of the protocol-binding document a binder was
    /// pinned against. A binder must advertise the maturity of the exact
    /// document it implements so callers can distinguish a normative mapping
    /// from an editor's draft. The W3C Binding Templates registry is a pilot and
    /// is intentionally never reported as <see cref="RegistryCurrent"/>.
    /// </summary>
    public enum WotBindingMaturity
    {
        /// <summary>The maturity is not known.</summary>
        Unknown = 0,

        /// <summary>An unofficial draft (for example a GitHub working file).</summary>
        UnofficialDraft,

        /// <summary>A W3C Editor's Draft.</summary>
        EditorsDraft,

        /// <summary>A W3C First Public / Working Draft.</summary>
        WorkingDraft,

        /// <summary>A W3C Candidate Recommendation.</summary>
        CandidateRecommendation,

        /// <summary>A W3C Proposed Recommendation.</summary>
        ProposedRecommendation,

        /// <summary>A W3C Recommendation (a normative, published standard).</summary>
        Recommendation,

        /// <summary>A W3C Working Group Note.</summary>
        Note,

        /// <summary>A published OPC Foundation specification (for example OPC 10101).</summary>
        OpcSpecification,

        /// <summary>
        /// An entry that is Current in the W3C Binding Templates registry. This
        /// value is reserved: the registry is a pilot and currently empty, so no
        /// shipped binder claims it.
        /// </summary>
        RegistryCurrent
    }

    /// <summary>
    /// An immutable, version-pinned reference to the exact specification document
    /// a protocol binder implements. Every planner pins its source so operators
    /// can audit precisely which mapping is enforced.
    /// </summary>
    public sealed class WotBindingSource
    {
        /// <summary>Initializes a new immutable binding source.</summary>
        /// <param name="specificationUri">The canonical document URL.</param>
        /// <param name="version">The pinned version, date or tag of the document.</param>
        /// <param name="maturity">The standards maturity of the document.</param>
        /// <param name="commit">The pinned VCS commit or revision, if any.</param>
        /// <param name="retrieved">The ISO-8601 date the document was pinned, if any.</param>
        /// <param name="note">An optional caveat (for example "registry pilot is empty").</param>
        public WotBindingSource(
            string specificationUri,
            string version,
            WotBindingMaturity maturity,
            string? commit = null,
            string? retrieved = null,
            string? note = null)
        {
            SpecificationUri = specificationUri ?? throw new ArgumentNullException(nameof(specificationUri));
            Version = version ?? string.Empty;
            Maturity = maturity;
            Commit = commit;
            Retrieved = retrieved;
            Note = note;
        }

        /// <summary>Gets the canonical document URL that was pinned.</summary>
        public string SpecificationUri { get; }

        /// <summary>Gets the pinned version, date or tag of the document.</summary>
        public string Version { get; }

        /// <summary>Gets the standards maturity of the pinned document.</summary>
        public WotBindingMaturity Maturity { get; }

        /// <summary>Gets the pinned VCS commit or revision, if any.</summary>
        public string? Commit { get; }

        /// <summary>Gets the date the document was pinned, if any.</summary>
        public string? Retrieved { get; }

        /// <summary>Gets an optional caveat about the source, if any.</summary>
        public string? Note { get; }

        /// <summary>
        /// Gets the stable text token for <see cref="Maturity"/> reported in the
        /// browseable <c>DraftMaturity</c> capability field.
        /// </summary>
        public string MaturityText => MaturityToText(Maturity);

        /// <summary>Maps a <see cref="WotBindingMaturity"/> to its stable text token.</summary>
        public static string MaturityToText(WotBindingMaturity maturity)
        {
            return maturity switch
            {
                WotBindingMaturity.UnofficialDraft => "UnofficialDraft",
                WotBindingMaturity.EditorsDraft => "ED",
                WotBindingMaturity.WorkingDraft => "WD",
                WotBindingMaturity.CandidateRecommendation => "CR",
                WotBindingMaturity.ProposedRecommendation => "PR",
                WotBindingMaturity.Recommendation => "REC",
                WotBindingMaturity.Note => "NOTE",
                WotBindingMaturity.OpcSpecification => "OPC",
                WotBindingMaturity.RegistryCurrent => "RegistryCurrent",
                _ => "Unknown"
            };
        }
    }
}
