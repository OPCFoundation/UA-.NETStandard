# ========================================================================
# Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
#
# OPC Foundation MIT License 1.00
#
# Permission is hereby granted, free of charge, to any person
# obtaining a copy of this software and associated documentation
# files (the "Software"), to deal in the Software without
# restriction, including without limitation the rights to use,
# copy, modify, merge, publish, distribute, sublicense, and/or sell
# copies of the Software, and to permit persons to whom the
# Software is furnished to do so, subject to the following
# conditions:
#
# The above copyright notice and this permission notice shall be
# included in all copies or substantial portions of the Software.
# THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
# EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
# OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
# NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
# HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
# WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
# FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
# OTHER DEALINGS IN THE SOFTWARE.
#
# The complete license agreement can be found here:
# http://opcfoundation.org/License/MIT/1.00/
# ========================================================================

param()
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot))
. (Join-Path $PSScriptRoot 'FixtureWorkspace.ps1')
$fixture = Join-Path (Get-FixturePhysicalTempDirectory) "promotion-transport-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $fixture
try {
    $sources = @('EvidenceFiles.cs', 'PromotionContracts.cs', 'PromotionFileTransport.cs',
        'PromotionRequestValidator.cs', 'PromotionEligibility.cs', 'PromotionCoordinator.cs', 'PromotionCommands.cs')
    $includes = @($sources | ForEach-Object {
        $path = [Security.SecurityElement]::Escape((Join-Path $root "tools\Opc.Ua.ReleaseEvidence\$_"))
        "<Compile Include=`"$path`" Link=`"$_`" />"
    }) -join "`n"
    $commandLine = Join-Path $root 'tools\Opc.Ua.ReleaseEvidence\bin\Release\net10.0\System.CommandLine.dll'
    if (-not (Test-Path -LiteralPath $commandLine)) {
        throw 'Build the approved release evidence tool before running this fixture; no dependency is installed here.'
    }
    $commandLine = [Security.SecurityElement]::Escape($commandLine)
    $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>$includes</ItemGroup>
  <ItemGroup><Reference Include="System.CommandLine" HintPath="$commandLine" /></ItemGroup>
</Project>
"@
    [IO.File]::WriteAllText((Join-Path $fixture 'TransportFixture.csproj'), $project)
    $program = @'
// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.
using System.Security.Cryptography;
using System.Text;
using Opc.Ua.ReleaseEvidence;

var root = Path.Combine(AppContext.BaseDirectory, "isolated-files");
var candidate = Path.Combine(root, "candidate");
var store = Path.Combine(root, "store");
var journal = Path.Combine(root, "journal");
Directory.CreateDirectory(candidate);
Directory.CreateDirectory(store);
Directory.CreateDirectory(journal);
var files = new EvidenceFiles();
await File.WriteAllTextAsync(Path.Combine(candidate, "content"), "exact immutable candidate bytes");
await File.WriteAllTextAsync(Path.Combine(candidate, "proof"), "public fixture proof bytes, not a cryptographic claim");
var content = new PromotionFile("content", await files.DigestAsync(Path.Combine(candidate, "content"), default));
var proof = new PromotionFile("proof", await files.DigestAsync(Path.Combine(candidate, "proof"), default));
var member = new PromotionMember("Example", "2.0.0", "offline-fixture/official", content, [proof], "stable");
var transport = new PromotionFileTransport(store, files);
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) { throw new InvalidOperationException(message); }
    checks++;
}
async Task Reject<T>(Func<Task> action, string message) where T : Exception
{
    try { await action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException(message);
}
Check(!transport.IsOfficial, "The fixture must never represent official publication.");
await using (var lease = await transport.AcquireLeaseAsync("nuget", "offline-fixture/official", default))
{
    await Reject<IOException>(async () =>
    {
        await using var competing = await transport.AcquireLeaseAsync("nuget", "offline-fixture/official", default);
    }, "An active lease allowed concurrent mutation.");
    Check((await transport.ReadAsync(member, default)).ContentDigest == null, "Unexpected existing version.");
    await transport.CreateImmutableAsync(member, candidate, lease, default);
    Check((await transport.ReadAsync(member, default)).ContentDigest == content.Digest, "Readback changed bytes.");
    var destinationFile = Directory.GetFiles(store, "content", SearchOption.AllDirectories).Single();
    var before = File.GetLastWriteTimeUtc(destinationFile);
    await transport.CreateImmutableAsync(member, candidate, lease, default);
    Check(File.GetLastWriteTimeUtc(destinationFile) == before, "Matching immutable version was rewritten.");
    Check((await transport.ReadAsync(member, default)).EvidenceDigests.Length == 0, "Missing proof looked delivered.");
    await transport.RestoreEvidenceAsync(member, candidate, lease, default);
    Check((await transport.ReadAsync(member, default)).EvidenceDigests.SequenceEqual([proof.Digest]), "Proof readback failed.");
    await transport.CompareExchangeAliasAsync(member, null, lease, default);
    Check((await transport.ReadAsync(member, default)).AliasDigest == content.Digest, "Alias did not bind exact content.");
    await transport.CompareExchangeAliasAsync(member, null, lease, default);
    checks++;
    await File.WriteAllTextAsync(Path.Combine(candidate, "other"), "different candidate");
    var other = member with
    {
        Content = new PromotionFile("other", await files.DigestAsync(Path.Combine(candidate, "other"), default))
    };
    await Reject<PromotionRejectedException>(
        () => transport.CreateImmutableAsync(other, candidate, lease, default),
        "Same ID/version with different bytes overwrote an immutable version.");
    await Reject<PromotionRejectedException>(
        () => transport.CompareExchangeAliasAsync(other, null, lease, default),
        "A stale alias precondition was accepted.");
    var proofFile = Directory.GetFiles(store, proof.Digest[7..], SearchOption.AllDirectories).Single();
    File.Delete(proofFile);
    Check((await transport.ReadAsync(member, default)).EvidenceDigests.Length == 0, "Lost evidence remained complete.");
    await transport.RestoreEvidenceAsync(member, candidate, lease, default);
    Check((await transport.ReadAsync(member, default)).EvidenceDigests.SequenceEqual([proof.Digest]), "Evidence recovery failed.");
    await File.WriteAllTextAsync(proofFile, "conflicting stored proof");
    await Reject<PromotionRejectedException>(
        () => transport.RestoreEvidenceAsync(member, candidate, lease, default),
        "Conflicting immutable evidence was overwritten.");
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    await Reject<OperationCanceledException>(
        () => transport.CreateImmutableAsync(member with { Version = "2.0.1" }, candidate, lease, cancel.Token),
        "Cancelled operation changed the store.");
    Check((await transport.ReadAsync(member with { Version = "2.0.1" }, default)).ContentDigest == null,
        "Cancelled operation created an immutable version.");
}
// Simulate write-before-journal process interruption: the actual file persists, no receipt is needed.
await using (var recovery = await transport.AcquireLeaseAsync("nuget", "offline-fixture/official", default))
{
    Check((await transport.ReadAsync(member, default)).ContentDigest == content.Digest,
        "Recovery did not observe actual destination state.");
    await transport.CreateImmutableAsync(member, candidate, recovery, default);
    checks++;
}
var journalWriter = new PromotionFileJournal(journal, files);
var entry = new PromotionEvent(1, "fixed-event", content.Digest, content.Digest, proof.Digest, proof.Digest,
    proof.Digest, new string('a', 40), "42", 2, "nuget", member.Destination, member.Id,
    "content-readback", content.Digest, [proof.Digest], "fixture-local-fence", DateTimeOffset.UtcNow.ToString("O"));
await journalWriter.AppendAsync(entry, default);
var journalBytes = await File.ReadAllBytesAsync(Path.Combine(journal, "fixed-event.json"));
await Reject<IOException>(() => journalWriter.AppendAsync(entry, default), "Existing delivery event was overwritten.");
var afterJournalBytes = await File.ReadAllBytesAsync(Path.Combine(journal, "fixed-event.json"));
Check(journalBytes.SequenceEqual(afterJournalBytes),
    "Append-only journal bytes changed.");
var request = new PromotionRequest(1, "nuget", member.Destination, content.Digest, proof.Digest,
    proof.Digest, proof.Digest, new string('a', 40), "42", 2, [member]);
await PromotionRequestValidator.ValidateAsync(request, candidate, files, default);
checks++;
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(request with { Members = [member, member] }, candidate, files, default),
    "Duplicate version members were accepted.");
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(request with { Members = [] }, candidate, files, default),
    "An empty candidate group was accepted.");
var symbols = member with
{
    Kind = "nuget-symbols",
    Content = new PromotionFile("other", await files.DigestAsync(Path.Combine(candidate, "other"), default)),
    Alias = null
};
await PromotionRequestValidator.ValidateAsync(request with { Members = [member, symbols] }, candidate, files, default);
checks++;
await using (var lease = await transport.AcquireLeaseAsync("nuget", request.Destination, default))
{
    await transport.CreateImmutableAsync(symbols, candidate, lease, default);
    Check((await transport.ReadAsync(symbols, default)).ContentDigest == symbols.Content.Digest,
        "Symbols collide with their normal package ID/version.");
    Check((await transport.ReadAsync(member, default)).ContentDigest == content.Digest,
        "A symbol transfer overwrote the normal package.");
}
var ociMembers = new List<PromotionMember>();
for (int image = 1; image <= 9; image++)
{
    foreach (string scope in new[] { "index", "amd64", "arm64" })
    {
        string path = $"oci-{image}-{scope}";
        await File.WriteAllTextAsync(Path.Combine(candidate, path), $"immutable fixture image {image}, {scope}");
        ociMembers.Add(member with
        {
            Id = $"ghcr.io/isolated-fixture/image-{image}",
            Kind = scope == "index" ? "oci-index" : "oci-manifest",
            Platform = scope == "index" ? null : "linux/" + scope,
            Content = new PromotionFile(path, await files.DigestAsync(Path.Combine(candidate, path), default)),
            Alias = scope == "index" ? "latest" : null
        });
    }
}
var ociRequest = request with { Group = "containers", Members = ociMembers.ToArray() };
await PromotionRequestValidator.ValidateAsync(ociRequest, candidate, files, default);
Check(ociMembers.Count(m => m.Kind == "oci-index") == 9 &&
    ociMembers.Count(m => m.Kind == "oci-manifest") == 18,
    "The transport shape lost an index or runnable platform slot.");
await using (var lease = await transport.AcquireLeaseAsync("containers", request.Destination, default))
{
    foreach (var image in ociMembers)
    {
        await transport.CreateImmutableAsync(image, candidate, lease, default);
        Check((await transport.ReadAsync(image, default)).ContentDigest == image.Content.Digest,
            "An OCI index/platform slot collided with another subject.");
        if (image.Alias != null)
        {
            await transport.CompareExchangeAliasAsync(image, null, lease, default);
        }
    }
}
var firstAlias = await transport.ReadAsync(ociMembers[0], default);
var secondAlias = await transport.ReadAsync(ociMembers[3], default);
Check(firstAlias.AliasDigest == ociMembers[0].Content.Digest &&
    secondAlias.AliasDigest == ociMembers[3].Content.Digest &&
    firstAlias.AliasDigest != secondAlias.AliasDigest,
    "Different image repositories shared one moving alias.");
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(
        ociRequest with { Members = [ociMembers[1], ociMembers[1]] }, candidate, files, default),
    "Duplicate runnable platform slots were accepted.");
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(
        ociRequest with { Members = [ociMembers[1] with { Platform = null }] }, candidate, files, default),
    "A runnable member omitted its platform identity.");
var pumpMembers = new[]
{
    ociMembers[0] with { Id = "ghcr.io/isolated-fixture/pump" },
    ociMembers[1] with { Id = "ghcr.io/isolated-fixture/pump" }
};
await PromotionRequestValidator.ValidateAsync(
    request with { Group = "pump", Members = pumpMembers }, candidate, files, default);
checks++;
await using (var lease = await transport.AcquireLeaseAsync("pump", request.Destination, default))
{
    foreach (var pump in pumpMembers)
    {
        await transport.CreateImmutableAsync(pump, candidate, lease, default);
        Check((await transport.ReadAsync(pump, default)).ContentDigest == pump.Content.Digest,
            "Independent Pump root/runnable identities collided.");
    }
}
var aliasPlan = ociMembers[0] with
{
    Id = "ghcr.io/isolated-fixture/multiple-tags",
    Alias = null,
    Aliases =
    [
        new PromotionAlias("2.0.0", true),
        new PromotionAlias("2.0", false),
        new PromotionAlias("release", false),
        new PromotionAlias("latest", false),
        new PromotionAlias("sha-aaaaaaa", true)
    ]
};
await PromotionRequestValidator.ValidateAsync(
    request with { Group = "containers", Members = [aliasPlan] }, candidate, files, default);
Check(PromotionRequestValidator.GetAliases(aliasPlan).Length == 5, "Existing publication aliases were truncated.");
await using (var lease = await transport.AcquireLeaseAsync("containers", request.Destination, default))
{
    await transport.CreateImmutableAsync(aliasPlan, candidate, lease, default);
    await transport.RestoreEvidenceAsync(aliasPlan, candidate, lease, default);
    foreach (var alias in PromotionRequestValidator.GetAliases(aliasPlan))
    {
        var selected = aliasPlan with { Alias = alias.Name, ExpectedAliasDigest = alias.ExpectedDigest, Aliases = null };
        await transport.CompareExchangeAliasAsync(selected, alias.ExpectedDigest, lease, default);
        Check((await transport.ReadAsync(selected, default)).AliasDigest == aliasPlan.Content.Digest,
            "An explicit version/rolling/SHA alias was not delivered.");
    }
    var changed = aliasPlan with { Content = ociMembers[3].Content };
    await transport.CreateImmutableAsync(changed, candidate, lease, default);
    Check((await transport.ReadAsync(changed, default)).ContentDigest == changed.Content.Digest,
        "OCI digest storage incorrectly inferred a tag from semantic Version metadata.");
    var immutable = changed with { Alias = "2.0.0", ExpectedAliasDigest = null, Aliases = null };
    await Reject<PromotionRejectedException>(
        () => transport.CompareExchangeAliasAsync(immutable, null, lease, default),
        "A conflicting immutable version alias was overwritten.");
    var rolling = changed with
    {
        Alias = "latest", ExpectedAliasDigest = aliasPlan.Content.Digest, Aliases = null
    };
    await transport.CompareExchangeAliasAsync(rolling, aliasPlan.Content.Digest, lease, default);
    Check((await transport.ReadAsync(rolling, default)).AliasDigest == changed.Content.Digest,
        "A moving alias did not honor its exact precondition.");
    Check((await transport.ReadAsync(
        aliasPlan with { Alias = "release", Aliases = null }, default)).AliasDigest == aliasPlan.Content.Digest,
        "Changing latest implicitly changed a separate release alias.");
}
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(
        request with { Group = "containers", Members = [aliasPlan with { Alias = "latest" }] },
        candidate, files, default),
    "Mixed legacy and list alias representations were accepted.");
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(
        request with
        {
            Group = "containers",
            Members = [aliasPlan with { Aliases = [new PromotionAlias("2.0.0", true, content.Digest)] }]
        }, candidate, files, default),
    "An immutable alias was given overwrite authority.");
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(
        request with
        {
            Group = "containers",
            Members = [aliasPlan with { Aliases = [new PromotionAlias("latest", false), new PromotionAlias("latest", false)] }]
        }, candidate, files, default),
    "A duplicate alias was accepted.");
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(
        request with
        {
            Group = "containers",
            Members = [aliasPlan with
            {
                Aliases = Enumerable.Range(0, 33).Select(i => new PromotionAlias("tag-" + i, false)).ToArray()
            }]
        }, candidate, files, default),
    "Alias bounds were bypassed.");
await Reject<InvalidDataException>(
    () => PromotionRequestValidator.ValidateAsync(
        request with { Group = "containers", Members = [aliasPlan with { Aliases = [new PromotionAlias("../tag", false)] }] },
        candidate, files, default),
    "An invalid registry alias was accepted.");
await File.AppendAllTextAsync(Path.Combine(candidate, "content"), "changed");
await Reject<PromotionRejectedException>(
    () => PromotionRequestValidator.ValidateAsync(request, candidate, files, default),
    "Changed candidate bytes retained eligibility.");
Console.WriteLine($"PASS {checks} concrete offline transport checks; no cryptographic eligibility claims or publication.");
'@
    [IO.File]::WriteAllText((Join-Path $fixture 'Program.cs'), $program)
    & dotnet run --project (Join-Path $fixture 'TransportFixture.csproj') -c Release --no-restore `
        --verbosity quiet *> (Join-Path $fixture 'run.log')
    if ($LASTEXITCODE -ne 0) {
        $first = Get-Content -LiteralPath (Join-Path $fixture 'run.log') -Raw
        if ($first -notmatch 'NETSDK1004') { throw $first }
        & dotnet restore (Join-Path $fixture 'TransportFixture.csproj') --source $fixture --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Offline framework-only fixture restore failed.' }
        & dotnet run --project (Join-Path $fixture 'TransportFixture.csproj') -c Release --no-restore `
            --verbosity quiet *> (Join-Path $fixture 'run.log')
    }
    $output = Get-Content -LiteralPath (Join-Path $fixture 'run.log') -Raw
    if ($LASTEXITCODE -ne 0) { throw $output }
    Write-Host $output
}
finally {
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
