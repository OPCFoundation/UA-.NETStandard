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
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Loads and validates the closed release-verification schemas without external reference fetching.
    /// </summary>
    /// <param name="schemas">The registered schema names and compiled schema definitions.</param>
    internal sealed class VerificationSchemas(Dictionary<string, JsonSchema> schemas)
    {
        /// <summary>
        /// Loads the required contract schemas into an isolated, offline-only schema registry.
        /// </summary>
        public static async Task<VerificationSchemas> LoadAsync(
            string repositoryRoot, EvidenceFiles files, CancellationToken cancellationToken)
        {
            var registry = new SchemaRegistry
            {
                Fetch = (uri, _) => throw new InvalidDataException($"Unregistered offline schema: {uri.AbsolutePath}.")
            };
            var options = new BuildOptions { SchemaRegistry = registry };
            var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
            foreach (string name in Names)
            {
                using JsonDocument document = await files.ReadJsonAsync(
                    EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/release/" + name), cancellationToken)
                    .ConfigureAwait(false);
                var schema = JsonSchema.FromText(document.RootElement.GetRawText(), options);
                registry.Register(new Uri("https://opcfoundation.org/schemas/ua-netstandard/" + name), schema);
                schemas.Add(name, schema);
            }
            return new VerificationSchemas(schemas);
        }

        /// <summary>
        /// Rejects unrecognized schema names and documents that do not satisfy the named contract.
        /// </summary>
        public void Validate(string name, JsonElement document)
        {
            if (!schemas.TryGetValue(name, out JsonSchema? schema))
            {
                throw new InvalidDataException("Unknown verification schema name.");
            }
            if (!schema.Evaluate(document, new EvaluationOptions { RequireFormatValidation = true }).IsValid)
            {
                throw new InvalidDataException("The input does not satisfy its closed verification schema.");
            }
        }

        /// <summary>
        /// Gets the complete set of schema files required by the verification boundary.
        /// </summary>
        internal static readonly string[] Names =
        [
            "evidence.schema.json", "verification-bundle.schema.json",
            "verification-record.schema.json", "trusted-policy-snapshot.schema.json"
        ];
    }
}
