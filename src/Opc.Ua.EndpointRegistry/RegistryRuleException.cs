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
using Opc.Ua.XRegistry;

namespace Opc.Ua.EndpointRegistry
{
#pragma warning disable CA1032 // The normative code/path/detail constructor is intentional for rule diagnostics.
    /// <summary>
    /// Exception raised by the Endpoint Registry semantic and structural validation rules.
    /// </summary>
    public sealed class RegistryRuleException : ServiceResultException
    {
        /// <summary>
        /// Creates a rule exception with the normative diagnostic code and path.
        /// </summary>
        public RegistryRuleException(string code, string path, string detail)
            : base(StatusCodes.BadInvalidArgument, code + " at " + path + ": " + detail)
        {
            Code = code ?? throw new ArgumentNullException(nameof(code));
            PathText = path ?? throw new ArgumentNullException(nameof(path));
            Path = PathFrom(PathText);
            Detail = detail ?? throw new ArgumentNullException(nameof(detail));
        }

        /// <summary>
        /// Gets the normative diagnostic code, for example <c>E_SCHEMA</c>.
        /// </summary>
        public new string Code { get; }

        /// <summary>
        /// Gets the diagnostic path split into xRegistry path segments.
        /// </summary>
        public ArrayOf<string> Path { get; }

        /// <summary>
        /// Gets the normative Python-style diagnostic path.
        /// </summary>
        public string PathText { get; }

        /// <summary>
        /// Gets the human-readable diagnostic detail.
        /// </summary>
        public string Detail { get; }

        /// <summary>
        /// Converts this exception to a registry diagnostic structure.
        /// </summary>
        public RegistryDiagnosticDataType ToDiagnostic()
        {
            return new RegistryDiagnosticDataType
            {
                StatusCode = StatusCodes.BadInvalidArgument,
                Code = Code,
                Path = [.. Path],
                Detail = Detail
            };
        }

        internal static RegistryRuleException Fail(string code, string path, string detail)
        {
            return new RegistryRuleException(code, path, detail);
        }

        private static ArrayOf<string> PathFrom(string path)
        {
            var result = new List<string>();
            if (path.Length == 0 || path == "/")
            {
                return [.. result];
            }
            string normalized = path[0] == '/' ? path[1..] : path;
            foreach (string part in normalized.Split('/'))
            {
                if (part.Length > 0)
                {
                    result.Add(part);
                }
            }
            return result;
        }
    }
}
