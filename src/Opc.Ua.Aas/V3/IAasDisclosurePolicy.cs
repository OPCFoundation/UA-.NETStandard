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
    /// Decides the information disclosure tier for an AAS entity.
    /// </summary>
    /// <remarks>
    /// Implementations are server-agnostic so they can be registered in dependency injection by a
    /// server package or constructed directly by callers that do not use DI. A tier classifies the
    /// information itself. A finer regulatory or business class must be carried in the returned
    /// <see cref="AasDisclosureDecision.DisclosureClass"/> and advertised through the AAS
    /// <c>Authorization</c> attribute; callers must not use the tier alone as the authorization
    /// boundary.
    /// </remarks>
    public interface IAasDisclosurePolicy
    {
        /// <summary>
        /// Gets the disclosure decision for an AAS entity.
        /// </summary>
        /// <param name="entity">The AAS entity whose content is being disclosed.</param>
        /// <returns>The disclosure tier and the class that led to it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="entity"/> is <c>null</c>.</exception>
        AasDisclosureDecision GetDisclosure(AasReferable entity);
    }

    /// <summary>
    /// The disclosure tier and the finer class that produced it.
    /// </summary>
    /// <param name="Tier">Whether the content is readable without authentication.</param>
    /// <param name="DisclosureClass">The regulatory or policy class that produced the tier.</param>
    /// <param name="Authorization">The authorization description to advertise for controlled content.</param>
    /// <remarks>
    /// For DPP battery passport data, the two controlled regulatory classes both map to
    /// <see cref="AASDisclosureTierDataType.Controlled"/>. They remain distinguishable through
    /// <paramref name="DisclosureClass"/> and <paramref name="Authorization"/> and must be advertised
    /// through the AAS <c>Authorization</c> attribute rather than inferred from the tier alone.
    /// </remarks>
    public sealed record AasDisclosureDecision(
        AASDisclosureTierDataType Tier,
        string DisclosureClass,
        string Authorization);
}
