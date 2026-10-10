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

namespace Opc.Ua.Server
{
    /// <summary>
    /// Bounds the optional server-wide event identity guarantee.
    /// </summary>
    public sealed record EventIdentityAdmissionOptions
    {
        /// <summary>
        /// Gets the maximum simultaneously tracked EventIds, including native
        /// publications, generation reservations and queued or retained occurrences.
        /// Native evidence is retained until server disposal. Generation evidence
        /// is reclaimable after its reservations and notification references drain.
        /// Exhaustion never evicts a live occurrence.
        /// </summary>
        public int MaxEventIdentities { get; init; } = 65_536;
    }

    /// <summary>
    /// Trusted provider evidence identifying one occurrence, independently of its
    /// EventId. Matching bytes or fields from different authorities are not evidence.
    /// </summary>
    public interface IEventIdentitySource
    {
        /// <summary>
        /// Determines whether both providers demonstrably describe the same occurrence.
        /// Admission requires agreement in both directions. Implementations must not
        /// perform I/O or call back into event publication.
        /// </summary>
        bool IsSameOccurrence(IEventIdentitySource other);
    }
}
