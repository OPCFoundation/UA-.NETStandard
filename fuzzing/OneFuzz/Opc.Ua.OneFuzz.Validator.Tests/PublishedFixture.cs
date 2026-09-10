/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    internal sealed class PublishedFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(
            Path.GetTempPath(), "opcua-onefuzz-contract-" + Guid.NewGuid().ToString("N"));

        internal string TargetDirectory => Path.Combine(Root, "published target");

        internal string ValidatorDll => Path.Combine(
            Root, "published validator", "Opc.Ua.OneFuzz.Validator.dll");

        internal async Task PublishAsync()
        {
            Directory.CreateDirectory(Root);
            string projectDirectory = Path.GetFullPath(
                typeof(PublishedFixture).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .Single(static attribute => attribute.Key == "OneFuzzProjectDirectory").Value!);
            string fixtureProject = Path.Combine(
                projectDirectory, "Opc.Ua.OneFuzz.TestTarget", "Opc.Ua.OneFuzz.TestTarget.csproj");
            string dependencyDirectory = Path.Combine(Root, "published dependency");
            await PublishProjectAsync(
                fixtureProject, dependencyDirectory, "dependency",
                "-p:ContractDependencyOnly=true").ConfigureAwait(false);
            await PublishProjectAsync(
                fixtureProject, TargetDirectory, "target",
                "-p:ContractDependencyPath=" + Path.Combine(dependencyDirectory, ContractDrop.DependencyDll))
                .ConfigureAwait(false);
            await PublishProjectAsync(
                Path.Combine(projectDirectory, "Opc.Ua.OneFuzz.Validator", "Opc.Ua.OneFuzz.Validator.csproj"),
                Path.GetDirectoryName(ValidatorDll)!,
                "validator").ConfigureAwait(false);

            // Copy into per-test drops and make the original publish location unavailable.
            // The raw reference's HintPath and all build outputs must not rescue a relocated drop.
            Directory.Delete(dependencyDirectory, recursive: true);
            Directory.Delete(Path.Combine(Root, "sdk"), recursive: true);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private async Task PublishProjectAsync(
            string project,
            string destination,
            string artifactsName,
            params string[] properties)
        {
            var arguments = new List<string>
            {
                "publish", project,
                "-c", "Release",
                "-p:CustomTestTarget=net10.0",
                "--artifacts-path", Path.Combine(Root, "sdk", artifactsName),
                "--output", destination,
                "--nologo", "-v", "minimal"
            };
            arguments.AddRange(properties);
            ProcessResult result = await ProcessRunner.RunAsync(
                ProcessRunner.DotNetHost, arguments, Root, TimeSpan.FromMinutes(3)).ConfigureAwait(false);
            if (result.TimedOut || result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Isolated publish failed for {project}.\n{result.Diagnostics}");
            }
        }
    }
}
