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
 *
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
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Converts OPC UA NodeSet2 documents to and from WoT Thing Models and
    /// Thing Descriptions. The default output uses the semantic/readable
    /// mapping of the OPC UA WoT Binding and adds the schema-complete,
    /// deterministic <c>uav:nodes</c> projection only when needed; the byte-exact
    /// <c>uav:nodeSet</c> envelope is an explicit or last-resort fallback.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// OPC UA WoT Binding vocabulary namespace.
        /// </summary>
        public const string VocabularyNamespace = WotVocabulary.VocabularyNamespace;

        private static bool HasErrors(List<WotDiagnostic> diagnostics)
        {
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                if (diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                {
                    return true;
                }
            }
            return false;
        }

        private static string? FirstDiagnosticMessage(
            List<WotDiagnostic> diagnostics)
        {
            return diagnostics.Count > 0 ? diagnostics[0].Message : null;
        }

        private static string? GetBaselineModellingRule(UANode node)
        {
            if (node.References is null)
            {
                return null;
            }
            foreach (Reference reference in node.References)
            {
                if (string.Equals(reference.ReferenceType, "HasModellingRule", StringComparison.Ordinal) &&
                    reference.IsForward &&
                    reference.Value is not null &&
                    WotVocabulary.TryGetModellingRuleName(reference.Value, out string rule))
                {
                    return rule;
                }
            }
            return null;
        }

        private static void ThrowIfErrors(IReadOnlyList<WotDiagnostic> diagnostics)
        {
            for (int ii = 0; ii < diagnostics.Count; ii++)
            {
                if (diagnostics[ii].Severity == WotDiagnosticSeverity.Error)
                {
                    throw new FormatException(diagnostics[ii].ToString());
                }
            }
        }

        /// <summary>
        /// Gets whether a NodeSet BrowseName names the given Node of the base
        /// OPC UA namespace.
        /// </summary>
        /// <remarks>
        /// A NodeSet writes a base-namespace BrowseName without a prefix, or
        /// with the explicit index <c>0</c>. Comparing the local name alone
        /// would accept a vendor's own <c>1:Severity</c> or
        /// <c>1:InputArguments</c>, which is a different QualifiedName standing
        /// for something this converter knows nothing about.
        /// </remarks>
        private static bool IsBaseNamespaceBrowseName(string? browseName, string name)
        {
            return string.Equals(browseName, name, StringComparison.Ordinal) ||
                string.Equals(browseName, "0:" + name, StringComparison.Ordinal);
        }

        private static bool TryGetString(JsonElement element, string name, out string? value)
        {
            if (element.TryGetProperty(name, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String)
            {
                value = property.GetString();
                return true;
            }
            value = null;
            return false;
        }

        private static byte[] ComputeSha256(byte[] data)
        {
#if NET6_0_OR_GREATER
            return SHA256.HashData(data);
#else
            using SHA256 sha256 = SHA256.Create();
            return sha256.ComputeHash(data);
#endif
        }



        private static bool TryParseDigest(string text, out byte[] digest)
        {
            string trimmed = text.Trim();
            if (trimmed.Length == 64)
            {
                try
                {
                    digest = CoreUtils.FromHexString(trimmed);
                    return digest.Length == 32;
                }
                catch (FormatException)
                {
                    // Not hex; try base64 below.
                }
            }
            try
            {
                byte[] decoded = System.Convert.FromBase64String(trimmed);
                if (decoded.Length == 32)
                {
                    digest = decoded;
                    return true;
                }
            }
            catch (FormatException)
            {
                // Not base64; fall through.
            }
            digest = [];
            return false;
        }



        private static bool FixedEquals(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            int difference = 0;
            for (int ii = 0; ii < left.Length; ii++)
            {
                difference |= left[ii] ^ right[ii];
            }
            return difference == 0;
        }
    }
}
