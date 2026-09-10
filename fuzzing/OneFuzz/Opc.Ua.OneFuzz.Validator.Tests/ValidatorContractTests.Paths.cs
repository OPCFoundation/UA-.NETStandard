/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    public sealed partial class ValidatorContractTests
    {
        [Test]
        public async Task MissingCorpusPathFailsDespiteOtherPublishedSeedsAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            const string kMissing = "fuzzing/ContractFixture/Corpus/Missing";
            drop.Targets[0]!["corpus"] = ContractDrop.Strings([kMissing]);

            await AssertPreflightRejectedAsync(drop, "Required path is missing", kMissing).ConfigureAwait(false);
        }

        [Test]
        public async Task EveryRequiredCorpusBucketMustExistEvenWhenAnotherBucketHasSeedsAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            Directory.Delete(drop.PathFor(ContractDrop.SecondBucket), recursive: true);
            Assert.That(Directory.GetFiles(drop.PathFor(ContractDrop.FirstBucket)), Is.Not.Empty);

            await AssertPreflightRejectedAsync(drop, "Required path is missing", ContractDrop.SecondBucket)
                .ConfigureAwait(false);
        }

        [Test]
        public async Task MissingExactSeedCannotBeReplacedByANeighbouringFileOrAnotherBucketAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            string missing = ContractDrop.FirstBucket + "/missing.bin";
            drop.Targets[0]!["corpus"] = ContractDrop.Strings([missing, ContractDrop.SecondBucket]);

            await AssertPreflightRejectedAsync(drop, "Required path is missing", missing).ConfigureAwait(false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task EmptyRequiredCorpusFailsEvenWithOtherNonemptyBucketsAsync(bool emptySubdirectory)
        {
            using var drop = new ContractDrop(m_fixture);
            string bucket = drop.PathFor(ContractDrop.FirstBucket);
            Directory.Delete(bucket, recursive: true);
            Directory.CreateDirectory(emptySubdirectory ? Path.Combine(bucket, "empty child") : bucket);
            Assert.That(Directory.GetFiles(drop.PathFor(ContractDrop.SecondBucket)), Is.Not.Empty);

            await AssertPreflightRejectedAsync(
                drop, "Required corpus is empty", ContractDrop.InspectId, ContractDrop.FirstBucket)
                .ConfigureAwait(false);
        }

        [TestCase(ContractDrop.FirstDictionary)]
        [TestCase(ContractDrop.SecondDictionary)]
        public async Task MissingRequiredSourceDictionaryFailsBeforeReplayAsync(string dictionary)
        {
            using var drop = new ContractDrop(m_fixture);
            File.Delete(drop.PathFor(dictionary));

            await AssertPreflightRejectedAsync(drop, "Required path is missing", dictionary).ConfigureAwait(false);
        }

        [Test]
        public async Task EmptyRequiredDictionaryFailsBeforeReplayAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.WriteBytes(ContractDrop.FirstDictionary, []);

            await AssertPreflightRejectedAsync(drop, "Required dictionary is empty", ContractDrop.FirstDictionary)
                .ConfigureAwait(false);
        }

        [TestCase("../outside.bin")]
        [TestCase("./fuzzing/ContractFixture/Corpus/Inspect")]
        [TestCase("fuzzing/ContractFixture/Corpus/RequiredBucket/../Inspect")]
        [TestCase("/fuzzing/ContractFixture/Corpus/Inspect")]
        [TestCase("C:/outside.bin")]
        [TestCase("fuzzing//ContractFixture/Corpus/Inspect")]
        [TestCase("fuzzing/ContractFixture/Corpus/*")]
        public async Task CorpusPathsMustBePortableWithoutTraversalOrGlobsAsync(string path)
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[0]!["corpus"] = ContractDrop.Strings([path]);

            await AssertPreflightRejectedAsync(drop, "Expected a portable relative path", path).ConfigureAwait(false);
        }

        [TestCase("assembly", "opc.Ua.OneFuzz.TestTarget.dll")]
        [TestCase("corpus", "fuzzing/contractFixture/Corpus/Inspect")]
        [TestCase("dictionaries", "fuzzing/dictionaries/contract-first.dict")]
        public async Task EveryDeclaredPathUsesExactCaseEvenOnWindowsAsync(string field, string path)
        {
            using var drop = new ContractDrop(m_fixture);
            if (field == "assembly")
            {
                drop.Area[field] = path;
            }
            else
            {
                drop.Targets[0]![field] = ContractDrop.Strings([path]);
            }

            await AssertPreflightRejectedAsync(drop, "missing or has incorrect case", path).ConfigureAwait(false);
        }

        [Test]
        public async Task OverlappingCorpusDeclarationsReplayEachPathOnceButDoNotDeduplicateEqualBytesAsync()
        {
            using var drop = new ContractDrop(m_fixture);
            drop.Targets[0]!["corpus"] = ContractDrop.Strings(
            [
                ContractDrop.FirstBucket,
                ContractDrop.FirstBucket + "/Z-first seed.bin",
                ContractDrop.SecondBucket
            ]);
            drop.Save();

            ProcessResult result = await ValidateAsync(drop).ConfigureAwait(false);

            // Inspect has three distinct paths (including a zero-byte seed), while
            // Measure must keep both different paths containing the same byte array.
            AssertCompleted(drop, result, hasConfig: true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LinkedCorpusDirectoryIsRejectedBothDirectlyAndDuringRecursiveEnumerationAsync(bool nested)
        {
            using var drop = new ContractDrop(m_fixture);
            string relative = nested ? ContractDrop.FirstBucket + "/linked seeds" : "linked corpus";
            string link = drop.PathFor(relative);
            string destination = drop.PathFor(ContractDrop.SecondBucket);
            if (OperatingSystem.IsWindows())
            {
                // An NTFS directory junction does not require symlink privileges or
                // Developer Mode. It still exercises the reparse-point contract.
                ProcessResult creation = await ProcessRunner.RunAsync(
                    Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                    ["/d", "/c", "mklink", "/J", link, destination],
                    drop.Home,
                    TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                AssertSuccess(creation);
            }
            else
            {
                Directory.CreateSymbolicLink(link, destination);
            }

            try
            {
                Assert.That(File.GetAttributes(link) & FileAttributes.ReparsePoint,
                    Is.EqualTo(FileAttributes.ReparsePoint));
                if (!nested)
                {
                    drop.Targets[0]!["corpus"] = ContractDrop.Strings([relative]);
                }

                await AssertPreflightRejectedAsync(drop, "Links are not allowed in a relocatable drop")
                    .ConfigureAwait(false);
            }
            finally
            {
                Directory.Delete(link);
            }
        }
    }
}
