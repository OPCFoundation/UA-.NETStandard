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

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Aggregate result of running
    /// <see cref="PubSubConfigurationValidator.Validate(PubSubConfigurationDataType)"/>.
    /// </summary>
    public sealed class PubSubConfigurationValidationResult
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="PubSubConfigurationValidationResult"/>.
        /// </summary>
        /// <param name="issues">All issues discovered.</param>
        public PubSubConfigurationValidationResult(
            IEnumerable<PubSubConfigurationIssue> issues)
        {
            if (issues is null)
            {
                throw new ArgumentNullException(nameof(issues));
            }
            Issues = issues.ToArrayOf();
        }

        /// <summary>
        /// Discovered issues. Never <see langword="null"/>.
        /// </summary>
        public ArrayOf<PubSubConfigurationIssue> Issues { get; }

        /// <summary>
        /// <see langword="true"/> when no error-severity issue was
        /// raised. Info and warning issues are tolerated.
        /// </summary>
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity == PubSubConfigurationIssueSeverity.Error)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        /// <summary>
        /// Throws a <see cref="PubSubConfigurationException"/> if any
        /// error-severity issue is present.
        /// </summary>
        /// <exception cref="PubSubConfigurationException">
        /// At least one error-severity issue was raised.
        /// </exception>
        public void ThrowIfInvalid()
        {
            if (!IsValid)
            {
                throw new PubSubConfigurationException([.. Issues]);
            }
        }
    }
}
