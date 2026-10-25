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
using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Server
{
    /// <summary>
    /// Reports a removal failure that occurred after the registration was removed from the published view.
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "A committed removal exception requires its authoritative registration and result.")]
    public sealed class NodeManagerRemovalCommittedException : InvalidOperationException
    {
        /// <summary>
        /// Initializes an exception for a removal whose publication already became authoritative.
        /// </summary>
        /// <param name="registration">The registration removed from the published view.</param>
        /// <param name="result">The committed result, including its retirement task.</param>
        /// <param name="innerException">The failure raised during post-commit reconciliation.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="registration"/>, <paramref name="result"/>, or <paramref name="innerException"/> is
        /// <see langword="null"/>.
        /// </exception>
        public NodeManagerRemovalCommittedException(
            NodeManagerRegistration registration,
            NodeManagerBatchResult result,
            Exception innerException)
            : base("The NodeManager was removed, but post-commit reconciliation failed.", innerException)
        {
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>
        /// Gets the registration removed from the published view.
        /// </summary>
        public NodeManagerRegistration Registration { get; }

        /// <summary>
        /// Gets the committed result and retirement task.
        /// </summary>
        public NodeManagerBatchResult Result { get; }
    }
}
