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

namespace Opc.Ua.AMB.Server.Health
{
    /// <summary>
    /// The values of <c>PotentialRootCauses</c> OPC 10000-110 §9.4.2
    /// distinguishes.
    /// </summary>
    public static class AssetRootCauses
    {
        /// <summary>
        /// The text of the entry that says no root cause has been identified.
        /// </summary>
        public const string UnknownText = "unknown";

        /// <summary>
        /// Gets the list that says no potential root cause has been
        /// identified: one entry without a <c>RootCauseId</c>.
        /// </summary>
        public static ArrayOf<RootCauseDataType> Unknown =>
        [
            new RootCauseDataType { RootCauseId = NodeId.Null, RootCause = new LocalizedText(UnknownText) }
        ];

        /// <summary>
        /// Gets the list that says the alarm itself is the root cause: an
        /// empty one.
        /// </summary>
        public static ArrayOf<RootCauseDataType> Self => ArrayOf<RootCauseDataType>.Empty;

        /// <summary>
        /// Creates one potential root cause.
        /// </summary>
        /// <param name="rootCauseId">
        /// The node that is the root cause, for example another node of the
        /// address space or a ConditionId; <see cref="NodeId.Null"/> when
        /// there is none.
        /// </param>
        /// <param name="rootCause">The description of the root cause.</param>
        /// <returns>The root cause.</returns>
        /// <exception cref="ArgumentException">
        /// Neither a node nor a description is given.
        /// </exception>
        public static RootCauseDataType Of(NodeId rootCauseId, LocalizedText rootCause)
        {
            if (rootCauseId.IsNull && rootCause.IsNullOrEmpty)
            {
                throw new ArgumentException("A root cause needs a node or a description.", nameof(rootCause));
            }
            return new RootCauseDataType { RootCauseId = rootCauseId, RootCause = rootCause };
        }

        /// <summary>
        /// Applies the rules of §9.4.2 to what an application passed: no list
        /// means the root cause is unknown; an empty list means the alarm
        /// itself is the root cause.
        /// </summary>
        /// <exception cref="ArgumentException">An entry is <c>null</c>.</exception>
        internal static ArrayOf<RootCauseDataType> Normalize(ArrayOf<RootCauseDataType> rootCauses)
        {
            if (rootCauses.IsNull)
            {
                return Unknown;
            }
            foreach (RootCauseDataType? rootCause in rootCauses)
            {
                if (rootCause == null)
                {
                    throw new ArgumentException("A potential root cause must not be null.", nameof(rootCauses));
                }
            }
            return rootCauses;
        }
    }
}
