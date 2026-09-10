/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    public sealed partial class ValidatorContractTests
    {
        [TestCase(ContractDrop.AssemblyDll)]
        [TestCase("Opc.Ua.OneFuzz.TestTarget.deps.json")]
        [TestCase("Opc.Ua.OneFuzz.TestTarget.runtimeconfig.json")]
        [TestCase("Opc.Ua.OneFuzz.TestTarget.pdb")]
        [TestCase(ContractDrop.DependencyDll)]
        [TestCase("Opc.Ua.OneFuzz.TestDependency.pdb")]
        public async Task MissingPublishedClosureMemberFailsWithoutBuildDirectoryFallbackAsync(string file)
        {
            using var drop = new ContractDrop(m_fixture);
            File.Delete(drop.PathFor(file));

            await AssertPreflightRejectedAsync(drop, "Required path is missing", file).ConfigureAwait(false);
        }

        [TestCase("Opc.Ua.OneFuzz.TestTarget.pdb", "Opc.Ua.OneFuzz.TestDependency.pdb")]
        [TestCase("Opc.Ua.OneFuzz.TestDependency.pdb", "Opc.Ua.OneFuzz.TestTarget.pdb")]
        public async Task PortablePdbMustBelongToItsExactPublishedAssemblyAsync(string destination, string otherPdb)
        {
            using var drop = new ContractDrop(m_fixture);
            File.Copy(drop.PathFor(otherPdb), drop.PathFor(destination), overwrite: true);

            await AssertPreflightRejectedAsync(drop, "PDB does not belong to its published assembly", destination)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task InvalidPortablePdbCannotSatisfyClosureByFilenameAloneAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.WriteText("Opc.Ua.OneFuzz.TestTarget.pdb", "not a portable PDB");

            await AssertPreflightRejectedAsync(drop, "BadImageFormatException").ConfigureAwait(false);
        }

        [TestCase("tfm")]
        [TestCase("frameworkName")]
        [TestCase("frameworkVersion")]
        [TestCase("additionalProbingPaths")]
        public async Task RuntimeConfigurationMustBeManagedNet10AndRelocatableAsync(string change)
        {
            using var drop = new ContractDrop(m_fixture);
            const string kRuntimeConfig = "Opc.Ua.OneFuzz.TestTarget.runtimeconfig.json";
            JsonNode config = JsonNode.Parse(File.ReadAllText(drop.PathFor(kRuntimeConfig)))!;
            JsonNode options = config["runtimeOptions"]!;
            switch (change)
            {
                case "tfm":
                    options["tfm"] = "net9.0";
                    break;
                case "frameworkName":
                    options["framework"]!["name"] = "Microsoft.AspNetCore.App";
                    break;
                case "frameworkVersion":
                    options["framework"]!["version"] = "9.0.0";
                    break;
                case "additionalProbingPaths":
                    options["additionalProbingPaths"] = ContractDrop.Strings([m_fixture.TargetDirectory]);
                    break;
            }

            drop.WriteText(kRuntimeConfig, config.ToJsonString());

            await AssertPreflightRejectedAsync(drop, "Invalid or nonrelocatable runtime configuration")
                .ConfigureAwait(false);
        }

        [Test]
        public async Task SharpFuzzDependencyMetadataIsRejectedWithoutInstrumentingAnythingAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            const string kDependencies = "Opc.Ua.OneFuzz.TestTarget.deps.json";
            JsonNode config = JsonNode.Parse(File.ReadAllText(drop.PathFor(kDependencies)))!;
            config["libraries"]!["SharpFuzz.SyntheticContract/1.0.0"] = new JsonObject
            {
                ["type"] = "project",
                ["serviceable"] = false,
                ["sha512"] = string.Empty
            };
            drop.WriteText(kDependencies, config.ToJsonString());

            await AssertPreflightRejectedAsync(drop, "SharpFuzz is forbidden in service dependencies")
                .ConfigureAwait(false);
        }
    }
}
