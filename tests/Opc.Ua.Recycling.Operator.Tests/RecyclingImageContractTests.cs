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

#if NET10_0
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Operator.Tests
{
    /// <summary>
    /// Static image wiring contracts, separate from actual container publication and smoke tests.
    /// </summary>
    [TestFixture]
    public sealed class RecyclingImageContractTests
    {
        /// <summary>
        /// Both samples participate in the existing image matrix with executable project paths.
        /// </summary>
        [TestCase("recyclingserver", "RecyclingServer")]
        [TestCase("recyclingclient", "RecyclingOperatorConsole")]
        public async Task ImageMatrixRegistersRecyclingExecutablesAsync(string image, string project)
        {
            string workflow = await ReadRepositoryFileAsync(".github", "workflows", "docker-image.yml")
                .ConfigureAwait(false);
            int start = workflow.IndexOf("IMAGES: >-", StringComparison.Ordinal) + "IMAGES: >-".Length;
            int end = workflow.IndexOf("\njobs:", start, StringComparison.Ordinal);
            using JsonDocument matrix = JsonDocument.Parse(workflow[start..end]);
            JsonElement[] entries = matrix.RootElement.EnumerateArray()
                .Where(entry => entry.GetProperty("image").GetString() == image)
                .ToArray();

            Assert.That(entries, Has.Length.EqualTo(1), "An image must be published exactly once.");
            Assert.That(
                entries[0].GetProperty("project").GetString(),
                Is.EqualTo($"samples/Recycling/{project}/{project}.csproj"));
            Assert.That(
                entries[0].GetProperty("dockerfile").GetString(),
                Is.EqualTo($"./samples/Recycling/{project}/Dockerfile"));
        }

        /// <summary>
        /// Source builds use a compatible pinned SDK and publish portable IL on the native build platform.
        /// </summary>
        [TestCase("RecyclingServer")]
        [TestCase("RecyclingOperatorConsole")]
        public async Task SourceBuildPublishesPortableNet10IlAsync(string project)
        {
            string dockerfile = await ReadRepositoryFileAsync("samples", "Recycling", project, "Dockerfile")
                .ConfigureAwait(false);
            using JsonDocument global = JsonDocument.Parse(
                await ReadRepositoryFileAsync("global.json").ConfigureAwait(false));
            string sdk = global.RootElement.GetProperty("sdk").GetProperty("version").GetString()
                ?? throw new InvalidOperationException("The repository SDK version is required.");

            Assert.That(
                global.RootElement.GetProperty("sdk").GetProperty("rollForward").GetString(),
                Is.EqualTo("latestMajor"),
                "The image version check uses the repository's minimum-SDK roll-forward policy.");
            Assert.That(global.RootElement.GetProperty("sdk").GetProperty("allowPrerelease").GetBoolean(), Is.False);
            Assert.That(UsesCompatibleSdkImage(dockerfile, Version.Parse(sdk)), Is.True,
                "The source stage must pin a stable Azure Linux SDK at or above the repository minimum.");
            Assert.That(dockerfile, Does.Contain($"samples/Recycling/{project}/{project}.csproj"));
            Assert.That(dockerfile, Does.Contain("-f net10.0"));
            Assert.That(dockerfile, Does.Contain("-p:CustomTestTarget=net10.0"));
            Assert.That(dockerfile, Does.Contain("-p:PublishAot=false"));
            Assert.That(dockerfile, Does.Contain("-p:UseAppHost=false"));
            Assert.That(dockerfile, Does.Contain("-p:SelfContained=false"));
            Assert.That(dockerfile, Does.Not.Contain("--runtime"));
            Assert.That(dockerfile, Does.Not.Contain("-p:RuntimeIdentifier="));
            Assert.That(dockerfile, Does.Not.Contain("$TARGETPLATFORM"));
        }

        /// <summary>
        /// The minimum SDK and newer stable SDKs satisfy the source-image version contract.
        /// </summary>
        [TestCase("10.0.400")]
        [TestCase("10.0.401")]
        [TestCase("10.0.402")]
        [TestCase("11.0.100")]
        public void SdkImageAcceptsMinimumAndNewerStableVersions(string sdk)
        {
            string dockerfile =
                $"FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:{sdk}-azurelinux3.0 AS build";

            Assert.That(UsesCompatibleSdkImage(dockerfile, new Version(10, 0, 400)), Is.True);
        }

        /// <summary>
        /// Older, prerelease, floating and incomplete SDK versions do not satisfy the image contract.
        /// </summary>
        [TestCase("10.0.399")]
        [TestCase("9.0.999")]
        [TestCase("10.0.401-preview.1")]
        [TestCase("latest")]
        [TestCase("10.0")]
        [TestCase("10..400")]
        public void SdkImageRejectsUnsupportedVersions(string sdk)
        {
            string dockerfile =
                $"FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:{sdk}-azurelinux3.0 AS build";

            Assert.That(UsesCompatibleSdkImage(dockerfile, new Version(10, 0, 400)), Is.False);
        }

        /// <summary>
        /// Version compatibility does not relax the native-platform, distribution or source-stage requirements.
        /// </summary>
        [TestCase("FROM mcr.microsoft.com/dotnet/sdk:10.0.400-azurelinux3.0 AS build")]
        [TestCase("FROM --platform=$TARGETPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.400-azurelinux3.0 AS build")]
        [TestCase("FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.400-alpine AS build")]
        [TestCase("FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.400-azurelinux3.0 AS unused")]
        public void SdkImageRejectsIncorrectBuildStage(string dockerfile)
        {
            Assert.That(UsesCompatibleSdkImage(dockerfile, new Version(10, 0, 400)), Is.False);
        }

        /// <summary>
        /// The source stage is recognized among other stages with either checkout line ending.
        /// </summary>
        [TestCase("\n")]
        [TestCase("\r\n")]
        public void SdkImageAcceptsMultistageDockerfile(string newline)
        {
            string dockerfile = string.Join(newline,
                "ARG PUBLISH_SOURCE=build",
                "FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.401-azurelinux3.0 AS build",
                "RUN dotnet publish",
                "FROM scratch AS prebuilt",
                "FROM mcr.microsoft.com/dotnet/runtime:10.0-azurelinux3.0 AS runtime");

            Assert.That(UsesCompatibleSdkImage(dockerfile, new Version(10, 0, 400)), Is.True);
        }

        /// <summary>
        /// Both runtime images consume the selected source or CI publish stage without compiling again.
        /// </summary>
        [TestCase("RecyclingServer", "runtime", "62570")]
        [TestCase("RecyclingOperatorConsole", "aspnet", "8080")]
        public async Task PrebuiltRouteCopiesCompletePayloadIntoNonRootRuntimeAsync(
            string project,
            string runtime,
            string port)
        {
            string dockerfile = await ReadRepositoryFileAsync("samples", "Recycling", project, "Dockerfile")
                .ConfigureAwait(false);

            Assert.That(dockerfile, Does.Contain("ARG PUBLISH_SOURCE=build"));
            Assert.That(dockerfile, Does.Contain("FROM scratch AS prebuilt"));
            Assert.That(dockerfile, Does.Contain("ARG PUBLISH_DIR=artifacts/docker/publish"));
            Assert.That(dockerfile, Does.Contain("COPY ${PUBLISH_DIR}/ /app/publish/"));
            Assert.That(dockerfile, Does.Contain("FROM ${PUBLISH_SOURCE} AS publish"));
            Assert.That(dockerfile, Does.Contain($"FROM mcr.microsoft.com/dotnet/{runtime}:10.0"));
            Assert.That(dockerfile, Does.Contain("COPY --from=publish --chown=app:app /app/publish/ ./"));
            Assert.That(dockerfile, Does.Contain("USER $APP_UID"));
            Assert.That(dockerfile, Does.Contain($"EXPOSE {port}"));
            Assert.That(dockerfile, Does.Contain($"ENTRYPOINT [\"dotnet\", \"{project}.dll\"]"));
        }

        /// <summary>
        /// The CI publish folder remains available in the Docker context for both platform legs.
        /// </summary>
        [Test]
        public async Task ExistingCiConsumesPrebuiltPublishDirectoryForBothPlatformsAsync()
        {
            string workflow = await ReadRepositoryFileAsync(".github", "workflows", "docker-image.yml")
                .ConfigureAwait(false);
            string dockerignore = await ReadRepositoryFileAsync(".dockerignore").ConfigureAwait(false);

            Assert.That(workflow, Does.Contain("PUBLISH_SOURCE=prebuilt"));
            Assert.That(workflow, Does.Contain("PUBLISH_DIR=artifacts/docker/publish/${{ matrix.image }}"));
            Assert.That(workflow, Does.Contain("linux/amd64,linux/arm64/v8"));
            Assert.That(workflow, Does.Contain("'-p:PublishAot=false'"));
            Assert.That(workflow, Does.Contain("'-p:UseAppHost=false'"));
            Assert.That(dockerignore, Does.Contain("!artifacts/docker/publish/**"));
        }

        /// <summary>
        /// Web SDK publishing includes the console's local assets without an npm or CDN build.
        /// </summary>
        [Test]
        public async Task OperatorWebSdkRetainsPublishedStaticAssetsAsync()
        {
            string projectPath = Path.Combine(
                FindRepositoryRoot(), "samples", "Recycling", "RecyclingOperatorConsole");
            var project = XDocument.Load(Path.Combine(projectPath, "RecyclingOperatorConsole.csproj"));
            string html = await File.ReadAllTextAsync(Path.Combine(projectPath, "wwwroot", "index.html"))
                .ConfigureAwait(false);

            Assert.That(project.Root?.Attribute("Sdk")?.Value, Is.EqualTo("Microsoft.NET.Sdk.Web"));
            Assert.That(
                project.Descendants("EnableDefaultContentItems").Any(element => element.Value == "false"),
                Is.False,
                "Default Web SDK content must survive dotnet publish.");
            Assert.That(
                project.Descendants("Content")
                    .Any(element => element.Attribute("Remove")?.Value.Contains(
                        "wwwroot", StringComparison.OrdinalIgnoreCase) == true),
                Is.False);
            Assert.That(
                Directory.GetFiles(Path.Combine(projectPath, "wwwroot"), "*.js", SearchOption.AllDirectories),
                Is.Not.Empty);
            Assert.That(
                Directory.GetFiles(Path.Combine(projectPath, "wwwroot"), "*.css", SearchOption.AllDirectories),
                Is.Not.Empty);
            Assert.That(html, Does.Contain("type=\"module\""));
            Assert.That(html, Does.Not.Contain("src=\"https://"));
            Assert.That(html, Does.Not.Contain("src=\"http://"));
            Assert.That(html, Does.Not.Contain("href=\"https://"));
            Assert.That(html, Does.Not.Contain("href=\"http://"));
        }

        /// <summary>
        /// Runtime layers copy only publication output, with no embedded credentials or state mounts.
        /// </summary>
        [TestCase("RecyclingServer")]
        [TestCase("RecyclingOperatorConsole")]
        public async Task RuntimeRecipeDoesNotEmbedSecretsOrJournalsAsync(string project)
        {
            string dockerfile = await ReadRepositoryFileAsync("samples", "Recycling", project, "Dockerfile")
                .ConfigureAwait(false);
            int runtimeStart = dockerfile.LastIndexOf("\nFROM mcr.microsoft.com/dotnet/", StringComparison.Ordinal);
            string runtime = dockerfile[runtimeStart..];
            string[] copies = runtime.Split('\n')
                .Where(line => line.StartsWith("COPY ", StringComparison.Ordinal))
                .Select(line => line.TrimEnd())
                .ToArray();

            Assert.That(copies, Has.Length.EqualTo(1));
            Assert.That(copies[0], Is.EqualTo("COPY --from=publish --chown=app:app /app/publish/ ./"));
            Assert.That(runtime, Does.Not.Contain("ENV "));
            Assert.That(runtime, Does.Not.Contain(".pfx"));
            Assert.That(runtime, Does.Not.Contain(".pem"));
            Assert.That(runtime, Does.Not.Contain("identities.json"));
            Assert.That(runtime, Does.Not.Contain("/data/state"));
            Assert.That(dockerfile, Does.Not.Contain("ARG PASSWORD"));
            Assert.That(dockerfile, Does.Not.Contain("ARG TOKEN"));
        }

        /// <summary>
        /// The opt-in browser service publishes loopback only and mounts separate external client stores.
        /// </summary>
        [Test]
        public async Task ComposeConsoleIsOptionalLoopbackAndUsesIsolatedExternalSecretsAsync()
        {
            string compose = await ReadRepositoryFileAsync(
                "samples", "Recycling", "RecyclingServer", "compose.yaml").ConfigureAwait(false);
            int start = compose.IndexOf("  operator-console:", StringComparison.Ordinal);
            int end = compose.IndexOf("\n  coordinator:", start, StringComparison.Ordinal);
            string console = compose[start..end];

            Assert.That(console, Does.Contain("profiles: [operator-console]"));
            Assert.That(console, Does.Contain("ports: [\"127.0.0.1:8080:8080\"]"));
            Assert.That(console, Does.Contain("RecyclingConsole__ContainerBind: \"true\""));
            Assert.That(console, Does.Contain("RecyclingConsole__AllowedHost: \"localhost:8080\""));
            Assert.That(console, Does.Contain("RecyclingConsole__Role: \"Observer\""));
            Assert.That(console, Does.Contain("opc.tcp://recycling-coordinator:62570/Recycling"));
            Assert.That(console, Does.Contain("RecyclingConsole__ProcessEndpoints__Bagging:"));
            Assert.That(console, Does.Contain("/operator-console/pki:/data/pki"));
            Assert.That(console, Does.Contain("/operator-console/secrets:/run/console-secrets:ro"));
            Assert.That(console, Does.Contain("RecyclingConsole__UserCertificatePasswordFile:"));
            Assert.That(console, Does.Contain("RecyclingConsole__UserCertificatePath: \"/run/console-secrets/users\""));
            Assert.That(console, Does.Contain("RecyclingConsole__UserCertificateSubject:"));
            Assert.That(console, Does.Not.Contain("RecyclingConsole__UserCertificatePassword:"));
            Assert.That(console, Does.Not.Contain("identities.json"));
            Assert.That(console, Does.Not.Contain("/coordinator:/data"));
            Assert.That(console, Does.Not.Contain("/data/state"));
        }

        /// <summary>
        /// Checks the pinned SDK image used by the native source-build stage.
        /// </summary>
        private static bool UsesCompatibleSdkImage(string dockerfile, Version minimumSdk)
        {
            const string prefix = "FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:";
            const string suffix = "-azurelinux3.0 AS build";
            string[] stages = dockerfile.Split('\n')
                .Select(static line => line.TrimEnd('\r'))
                .Where(static line =>
                    line.StartsWith("FROM ", StringComparison.Ordinal) &&
                    line.EndsWith(" AS build", StringComparison.Ordinal))
                .ToArray();

            if (stages.Length != 1 ||
                !stages[0].StartsWith(prefix, StringComparison.Ordinal) ||
                !stages[0].EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            string version = stages[0][prefix.Length..^suffix.Length];
            return version.Count(static c => c == '.') == 2 &&
                version.All(static c => char.IsAsciiDigit(c) || c == '.') &&
                Version.TryParse(version, out Version? sdk) &&
                sdk >= minimumSdk;
        }

        /// <summary>
        /// Reads a repository fixture without depending on the test runner's working directory.
        /// </summary>
        private static Task<string> ReadRepositoryFileAsync(params string[] segments)
        {
            return File.ReadAllTextAsync(Path.Combine([FindRepositoryRoot(), .. segments]));
        }

        /// <summary>
        /// Finds the checkout that contains these image recipes.
        /// </summary>
        private static string FindRepositoryRoot()
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
                directory != null;
                directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "UA.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Cannot find the UA.slnx repository root.");
        }
    }
}
#endif
