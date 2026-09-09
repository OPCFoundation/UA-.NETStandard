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
using System.Linq;
using CycloneDX;
using CycloneDX.Models;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Converts reconciled package inventories into CycloneDX bills of materials.
    /// </summary>
    internal static class CycloneDxInventory
    {
        /// <summary>
        /// Serializes and validates a CycloneDX 1.6 bill of materials covering payloads and dependency relationships.
        /// </summary>
        public static string Serialize(PackageInventory inventory)
        {
            ArtifactRecord artifact = inventory.Artifact;
            string rootRef = "artifact:" + artifact.Digest;
            var bom = new Bom
            {
                SpecVersion = SpecificationVersion.v1_6,
                Version = 1,
                Metadata = new Metadata
                {
                    Component = new Component
                    {
                        Type = Component.Classification.Library,
                        BomRef = rootRef,
                        Name = artifact.Id,
                        Version = artifact.Version,
                        Purl = Purl(artifact.Id, artifact.Version),
                        Hashes = [CreateHash(artifact.Digest)],
                        Licenses = Licenses(inventory.Licenses),
                        Properties =
                        [
                            Property("artifact-kind", artifact.Kind),
                            Property("configuration", artifact.Configuration)
                        ]
                    }
                },
                Components = [],
                Dependencies = [],
                Properties = [Property("producer", "CycloneDX.Core/12.1.1"), Property("reconciliation", "offline")]
            };
            foreach (ResolvedComponent resolved in inventory.ResolvedGraphs)
            {
                bool shipped = inventory.Payloads.Any(p => p.Owner == resolved.Id &&
                    p.Version == resolved.Version &&
                    p.Targets.Contains(resolved.Target, StringComparer.Ordinal));
                bom.Components.Add(new Component
                {
                    Type = Component.Classification.Library,
                    BomRef = ResolvedRef(resolved),
                    Name = resolved.Id,
                    Version = resolved.Version,
                    Purl = resolved.Type == "package" ? Purl(resolved.Id, resolved.Version) : null,
                    Licenses = Licenses(resolved.Licenses),
                    Properties =
                    [
                        Property("ownership", shipped ? "shipped" : "build-only"),
                        Property("target", resolved.Target),
                        Property("project", resolved.Project ?? "unknown")
                    ]
                });
                bom.Dependencies.Add(new Dependency
                {
                    Ref = ResolvedRef(resolved),
                    Dependencies = [.. resolved.Dependencies.Keys.Order(StringComparer.Ordinal)
                        .Select(id => inventory.ResolvedGraphs.SingleOrDefault(c =>
                            c.Project == resolved.Project &&
                            c.Target == resolved.Target &&
                            string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
                        .Where(c => c != null).Select(c => new Dependency { Ref = ResolvedRef(c!) })]
                });
            }
            foreach (InventoryPayload payload in inventory.Payloads.Where(p => p.Classification != "metadata"))
            {
                bom.Components.Add(new Component
                {
                    Type = Component.Classification.File,
                    BomRef = "payload:" + payload.Path,
                    Name = payload.Path,
                    Hashes = [CreateHash(payload.Digest)],
                    Properties =
                    [
                        Property("ownership", payload.Owner == null ? "unknown" : "shipped"),
                        Property("classification", payload.Classification),
                        Property("owner", payload.Owner ?? "unknown"),
                        Property("owner-version", payload.Version ?? "unknown"),
                        Property("targets", string.Join(';', payload.Targets)),
                        Property("roslyn", string.Join(';', payload.Roslyn))
                    ]
                });
            }
            foreach (ConsumerDependency dependency in inventory.ConsumerDependencies)
            {
                bom.Components.Add(new Component
                {
                    Type = Component.Classification.Library,
                    BomRef = "consumer:" + dependency.Target + ":" + dependency.Id,
                    Name = dependency.Id,
                    Properties =
                    [
                        Property("ownership", "declared-consumer"),
                        Property("declared-version-range", dependency.Range),
                        Property("target", dependency.Target)
                    ]
                });
            }
            foreach (string prerequisite in inventory.ExternalPrerequisites)
            {
                bom.Components.Add(new Component
                {
                    Type = Component.Classification.Framework,
                    BomRef = "prerequisite:" + prerequisite,
                    Name = prerequisite,
                    Properties = [Property("ownership", "external-prerequisite")]
                });
            }
            bom.Dependencies.Add(new Dependency
            {
                Ref = rootRef,
                Dependencies = [.. bom.Components.Where(c =>
                    c.Properties.Any(p => p.Name == "opcua:ownership" &&
                        p.Value is "shipped" or "declared-consumer" or "external-prerequisite"))
                    .Select(c => new Dependency { Ref = c.BomRef })]
            });
            string json = CycloneDX.Json.Serializer.Serialize(bom);
            ValidationResult validation = CycloneDX.Json.Validator.Validate(json, SpecificationVersion.v1_6);
            return validation.Valid
                ? json
                : throw new InvalidDataException(
                    "The SDK-produced CycloneDX 1.6 inventory failed SDK schema validation.");
        }

        private static List<LicenseChoice> Licenses(LicenseRecord[] licenses)
        {
            return [.. licenses.Where(l => l.Kind != "unknown").Select(l => l.Kind == "expression"
                ? new LicenseChoice { Expression = l.Value }
                : new LicenseChoice
                {
                    License = new License
                    {
                        Name = l.Kind == "file" ? "License file: " + l.Value : "Declared license URL",
                        Url = l.Kind == "url" ? l.Value : null,
                        Properties = l.Digest == null ? null : [Property("license-file-digest", l.Digest)]
                    }
                })];
        }

        private static Property Property(string name, string value)
        {
            return new Property { Name = "opcua:" + name, Value = value };
        }

        private static Hash CreateHash(string digest)
        {
            return new Hash { Alg = Hash.HashAlgorithm.SHA_256, Content = digest[7..] };
        }

        private static string ResolvedRef(ResolvedComponent component)
        {
            return string.Join(':', "resolved", component.Project, component.Target, component.Id, component.Version);
        }

        private static string Purl(string id, string version)
        {
            return "pkg:nuget/" +
                Uri.EscapeDataString(id) +
                "@" +
                Uri.EscapeDataString(Versions.Parse(version).ToNormalizedString());
        }
    }
}
