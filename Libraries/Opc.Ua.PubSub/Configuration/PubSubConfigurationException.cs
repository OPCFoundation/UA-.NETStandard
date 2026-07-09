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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;

namespace Opc.Ua.PubSub.Configuration
{
    /// <summary>
    /// Raised when a <see cref="PubSubConfigurationDataType"/> fails
    /// validation or when constructing a
    /// <see cref="PubSubConfigurationSnapshot"/> detects an index
    /// collision (e.g. duplicate connection name).
    /// </summary>
    [SuppressMessage(
        "Design",
        "CA1032:Implement standard exception constructors",
        Justification = "Configuration exceptions always carry the issue list; a default or message-only constructor would discard required diagnostic context.")]
    [SuppressMessage(
        "Usage",
        "RCS1194:Implement exception constructors",
        Justification = "Configuration exceptions always carry the issue list; a default or message-only constructor would discard required diagnostic context.")]
    public sealed class PubSubConfigurationException : Exception
    {
        /// <summary>
        /// Initializes a new
        /// <see cref="PubSubConfigurationException"/>.
        /// </summary>
        /// <param name="issues">
        /// Issues that motivated the exception. Only error-severity
        /// issues are reflected in the message; non-error issues are
        /// retained for diagnostics.
        /// </param>
        public PubSubConfigurationException(IEnumerable<PubSubConfigurationIssue> issues)
            : base(BuildMessage(issues))
        {
            if (issues is null)
            {
                throw new ArgumentNullException(nameof(issues));
            }
            Issues = issues.ToArrayOf();
        }

        /// <summary>
        /// All issues captured at the time the exception was raised.
        /// </summary>
        public ArrayOf<PubSubConfigurationIssue> Issues { get; }

        private static string BuildMessage(IEnumerable<PubSubConfigurationIssue> issues)
        {
            if (issues is null)
            {
                return "PubSub configuration is invalid.";
            }
            PubSubConfigurationIssue[] errors = [.. issues
                .Where(static i => i.Severity == PubSubConfigurationIssueSeverity.Error)
                .Take(MaxErrorsInMessage + 1)];
            if (errors.Length == 0)
            {
                return "PubSub configuration is invalid.";
            }
            var builder = new StringBuilder("PubSub configuration is invalid:");
            for (int i = 0; i < errors.Length && i < MaxErrorsInMessage; i++)
            {
                PubSubConfigurationIssue issue = errors[i];
                builder
                    .Append(' ')
                    .Append('[')
                    .Append(issue.Code)
                    .Append("] ")
                    .Append(issue.Path)
                    .Append(": ")
                    .Append(issue.Message);
                if (i < errors.Length - 1 && i < MaxErrorsInMessage - 1)
                {
                    builder.Append(';');
                }
            }
            if (errors.Length > MaxErrorsInMessage)
            {
                builder.Append(" (+ further errors omitted).");
            }
            return builder.ToString();
        }

        private const int MaxErrorsInMessage = 3;
    }
}
