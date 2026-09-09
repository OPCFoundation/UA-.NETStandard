// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Json.Schema;

namespace Opc.Ua.ReleaseEvidence
{
    internal sealed class VerificationSchemas(Dictionary<string, JsonSchema> schemas)
    {
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
                    EvidenceFiles.Confined(repositoryRoot, ".azurepipelines/" + name), cancellationToken)
                    .ConfigureAwait(false);
                var schema = JsonSchema.FromText(document.RootElement.GetRawText(), options);
                registry.Register(new Uri("https://opcfoundation.org/schemas/ua-netstandard/" + name), schema);
                schemas.Add(name, schema);
            }
            return new VerificationSchemas(schemas);
        }

        public void Validate(string name, JsonElement document)
        {
            if (!schemas[name].Evaluate(document, new EvaluationOptions { RequireFormatValidation = true }).IsValid)
            {
                throw new InvalidDataException("The input does not satisfy its closed verification schema.");
            }
        }

        internal static readonly string[] Names =
        [
            "release-evidence.schema.json", "verification-bundle.schema.json",
            "verification-record.schema.json", "trusted-policy-snapshot.schema.json"
        ];
    }
}
