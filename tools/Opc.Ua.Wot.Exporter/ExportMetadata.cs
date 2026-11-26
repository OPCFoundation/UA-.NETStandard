// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Opc.Ua.Wot.Exporter
{
    /// <summary>
    /// Explicit publisher facts, keyed by portable native identity rather than output names.
    /// </summary>
    internal sealed partial class ExportMetadata
    {
        /// <summary>
        /// Parses and validates publisher metadata without reflection-based serialization.
        /// </summary>
        public static ExportMetadata Parse(ReadOnlyMemory<byte> json)
        {
            using var document = JsonDocument.Parse(json);
            var result = new ExportMetadata();
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("Publisher metadata must be an object keyed by portable NodeId.");
            }
            foreach (JsonProperty entry in document.RootElement.EnumerateObject())
            {
                string identity = ExportModelMapping.CanonicalIdentity(entry.Name);
                if (entry.Value.ValueKind != JsonValueKind.Object || result.m_entries.ContainsKey(identity))
                {
                    throw new FormatException($"Invalid or duplicate publisher metadata for '{entry.Name}'.");
                }
                var members = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty member in entry.Value.EnumerateObject())
                {
                    bool valid = member.Name switch
                    {
                        "name" => member.Value.ValueKind == JsonValueKind.String &&
                            NamePattern().IsMatch(member.Value.GetString()!),
                        "isStandalone" => member.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                        "eventContractType" => ValidContracts(member.Value),
                        "unitCodeSystem" => member.Value.ValueKind == JsonValueKind.String &&
                            Uri.TryCreate(member.Value.GetString(), UriKind.Absolute, out _),
                        "unitCode" => member.Value.ValueKind == JsonValueKind.String &&
                            !string.IsNullOrWhiteSpace(member.Value.GetString()),
                        _ => false
                    };
                    if (!valid || !members.Add(member.Name))
                    {
                        throw new FormatException($"Invalid publisher metadata '{entry.Name}/{member.Name}'.");
                    }
                    if (entry.Value.TryGetProperty("unitCodeSystem", out _) !=
                        entry.Value.TryGetProperty("unitCode", out _))
                    {
                        throw new FormatException($"Unit metadata for '{entry.Name}' requires a vocabulary and code.");
                    }
                }
                result.m_entries.Add(identity, JsonNode.Parse(entry.Value.GetRawText())!.AsObject());
            }
            return result;
        }

        /// <summary>
        /// Gets the metadata explicitly assigned to this identity.
        /// </summary>
        public JsonObject? Find(string identity)
        {
            m_entries.TryGetValue(identity, out JsonObject? entry);
            return entry;
        }

        /// <summary>
        /// Tests the reverse-DNS symbol grammar without altering the supplied spelling.
        /// </summary>
        public static bool IsName(string name)
        {
            return NamePattern().IsMatch(name);
        }

        /// <summary>
        /// Requires distinct exact contract type strings with positive version suffixes.
        /// </summary>
        private static bool ValidContracts(JsonElement contracts)
        {
            if (contracts.ValueKind != JsonValueKind.Array || contracts.GetArrayLength() == 0)
            {
                return false;
            }
            var distinct = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement contract in contracts.EnumerateArray())
            {
                if (contract.ValueKind != JsonValueKind.String ||
                    !ContractPattern().IsMatch(contract.GetString()!) ||
                    !distinct.Add(contract.GetString()!))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Matches complete reverse-DNS symbol names.
        /// </summary>
        [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*\z",
            RegexOptions.CultureInvariant)]
        private static partial Regex NamePattern();

        /// <summary>
        /// Matches versioned contract names without leading-zero versions.
        /// </summary>
        [GeneratedRegex(@"\A(?:[A-Za-z_][A-Za-z0-9_]*\.)+v[1-9][0-9]*\z", RegexOptions.CultureInvariant)]
        private static partial Regex ContractPattern();

        /// <summary>Holds only explicit publisher assertions.</summary>
        private readonly Dictionary<string, JsonObject> m_entries = new(StringComparer.Ordinal);
    }
}
