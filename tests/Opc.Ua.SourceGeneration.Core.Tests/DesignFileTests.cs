/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Api.Tests
{
    /// <summary>
    /// Unit tests for <see cref = "DesignFileExtensions"/> class.
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DesignFileExtensionsTests
    {
        /// <summary>
        /// Tests that Group throws ArgumentNullException when collection parameter is null.
        /// </summary>
        [Test]
        public void Group_NullCollection_ThrowsArgumentNullException()
        {
            // Arrange
            DesignFileCollection collection = null;
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => collection.Group().ToList());
        }

        /// <summary>
        /// Tests that Group throws NullReferenceException when DesignFiles property is null.
        /// </summary>
        [Test]
        public void Group_NullDesignFiles_ThrowsArgumentNullException()
        {
            // Arrange
            var collection = new DesignFileCollection
            {
                Targets = null,
                IdentifierFilePath = null,
                Options = null
            };
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => collection.Group().ToList());
        }

        /// <summary>
        /// Tests that Group returns empty enumerable when DesignFiles is empty.
        /// </summary>
        [Test]
        public void Group_EmptyDesignFiles_ReturnsEmptyEnumerable()
        {
            // Arrange
            var collection = new DesignFileCollection
            {
                Targets = [],
                IdentifierFilePath = null,
                Options = null
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Tests that Group returns single group with one file when DesignFiles contains single file.
        /// </summary>
        [Test]
        public void Group_SingleDesignFile_ReturnsSingleGroup()
        {
            // Arrange
            string designFile = Path.Combine("C:", "TestDir", "Design1.xml");
            var options = new DesignFileOptions
            {
                Version = "v105"
            };
            var collection = new DesignFileCollection
            {
                Targets =
                [
                    designFile
                ],
                IdentifierFilePath = "global.csv",
                Options = options
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Targets, Is.Not.Null);
            Assert.That(result[0].Targets, Has.Count.EqualTo(1));
            Assert.That(result[0].Targets[0], Is.EqualTo(designFile));
            Assert.That(result[0].IdentifierFilePath, Is.EqualTo("global.csv"));
            Assert.That(result[0].Options, Is.EqualTo(options));
        }

        /// <summary>
        /// Tests that Group returns one generation unit per design file when
        /// multiple targets share a directory.
        /// </summary>
        [Test]
        public void GroupMultipleFilesInSameDirectoryReturnsOneGroupPerTarget()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design1.xml"),
                Path.Combine(dir, "Design2.xml"),
                Path.Combine(dir, "Design3.xml")
            };
            var options = new DesignFileOptions
            {
                Version = "v104"
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "fallback.csv",
                Options = options
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result.SelectMany(group => group.Targets), Is.EquivalentTo(designFiles));
            foreach (DesignFileCollection group in result)
            {
                Assert.That(group.Targets, Has.Count.EqualTo(1));
                Assert.That(group.Dependencies, Has.Count.EqualTo(2));
                Assert.That(group.Dependencies, Does.Not.Contain(group.Targets[0]));
                Assert.That(group.IdentifierFilePath, Is.EqualTo("fallback.csv"));
                Assert.That(group.Options, Is.EqualTo(options));
            }
        }

        /// <summary>
        /// Tests that Group returns multiple groups when design files are in different directories.
        /// </summary>
        [Test]
        public void Group_FilesInDifferentDirectories_ReturnsMultipleGroups()
        {
            // Arrange
            string dir1 = Path.Combine("C:", "Dir1");
            string dir2 = Path.Combine("C:", "Dir2");
            string dir3 = Path.Combine("C:", "Dir3");
            var designFiles = new List<string>
            {
                Path.Combine(dir1, "Design1.xml"),
                Path.Combine(dir2, "Design2.xml"),
                Path.Combine(dir3, "Design3.xml")
            };
            var options = new DesignFileOptions
            {
                StartId = 1000
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "default.csv",
                Options = options
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(3));
            foreach (DesignFileCollection group in result)
            {
                Assert.That(group.Targets, Is.Not.Null);
                Assert.That(group.Targets, Has.Count.EqualTo(1));
                Assert.That(group.IdentifierFilePath, Is.EqualTo("default.csv"));
                Assert.That(group.Options, Is.EqualTo(options));
            }
        }

        /// <summary>
        /// Tests that Group uses collection's IdentifierFilePath when identifierFiles parameter is null.
        /// </summary>
        [Test]
        public void Group_NullIdentifierFilesParameter_UsesCollectionIdentifierFilePath()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design1.xml")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "collection_identifier.csv",
                Options = null
            };
            // Act
            var result = collection.Group(null).ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].IdentifierFilePath, Is.EqualTo("collection_identifier.csv"));
        }

        /// <summary>
        /// Tests that Group uses collection's IdentifierFilePath when identifierFiles parameter is empty.
        /// </summary>
        [Test]
        public void Group_EmptyIdentifierFilesList_UsesCollectionIdentifierFilePath()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design1.xml")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "fallback.csv",
                Options = null
            };
            // Act
            var result = collection.Group([]).ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].IdentifierFilePath, Is.EqualTo("fallback.csv"));
        }

        /// <summary>
        /// Tests that Group assigns an identifier file by matching the target
        /// base name, even when several models share a directory.
        /// </summary>
        [Test]
        public void GroupBasenameIdentifierFilesAssignsEachTarget()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design1.xml"),
                Path.Combine(dir, "Design2.xml")
            };
            var identifierFiles = new List<string>
            {
                Path.Combine(dir, "Design1.csv"),
                Path.Combine(dir, "Design2.CSV")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "fallback.csv",
                Options = null
            };
            // Act
            var result = collection.Group(identifierFiles).ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(
                result.Single(group => group.Targets[0].EndsWith("Design1.xml", StringComparison.Ordinal))
                    .IdentifierFilePath,
                Is.EqualTo(Path.Combine(dir, "Design1.csv")));
            Assert.That(
                result.Single(group => group.Targets[0].EndsWith("Design2.xml", StringComparison.Ordinal))
                    .IdentifierFilePath,
                Is.EqualTo(Path.Combine(dir, "Design2.CSV")));
        }

        /// <summary>
        /// Tests that the legacy single-model case continues to use the only
        /// adjacent identifier file even when its basename differs.
        /// </summary>
        [Test]
        public void GroupSingleTargetWithNonBasenameIdentifierUsesAdjacentFile()
        {
            string dir = Path.Combine("C:", "TestDir");
            string target = Path.Combine(dir, "Design.xml");
            string identifierFile = Path.Combine(dir, "LegacyIdentifiers.csv");
            var collection = new DesignFileCollection
            {
                Targets = [target],
                IdentifierFilePath = "fallback.csv"
            };

            DesignFileCollection result = collection
                .Group([identifierFile])
                .Single();

            Assert.That(result.IdentifierFilePath, Is.EqualTo(identifierFile));
        }

        /// <summary>
        /// Tests that each single-model directory preserves its legacy
        /// non-basename identifier file in a multi-directory collection.
        /// </summary>
        [Test]
        public void GroupMultipleSingleTargetDirectoriesUseAdjacentLegacyFiles()
        {
            string dir1 = Path.Combine("C:", "Dir1");
            string dir2 = Path.Combine("C:", "Dir2");
            string target1 = Path.Combine(dir1, "Model1.xml");
            string target2 = Path.Combine(dir2, "Model2.xml");
            string identifier1 = Path.Combine(dir1, "Identifiers.csv");
            string identifier2 = Path.Combine(dir2, "Identifiers.csv");
            var collection = new DesignFileCollection
            {
                Targets = [target1, target2],
                IdentifierFilePath = "fallback.csv"
            };

            List<DesignFileCollection> result = collection
                .Group([identifier1, identifier2])
                .ToList();

            Assert.That(
                result.Single(group => group.Targets[0] == target1).IdentifierFilePath,
                Is.EqualTo(identifier1));
            Assert.That(
                result.Single(group => group.Targets[0] == target2).IdentifierFilePath,
                Is.EqualTo(identifier2));
        }

        /// <summary>
        /// Tests that unrelated identifier files in the same directory are
        /// ignored in favour of the target's basename match.
        /// </summary>
        [Test]
        public void GroupMultipleIdentifierFilesInSameDirectoryUsesBasenameMatch()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design2.xml")
            };
            var identifierFiles = new List<string>
            {
                Path.Combine(dir, "identifiers1.csv"),
                Path.Combine(dir, "Design2.csv"),
                Path.Combine(dir, "identifiers3.csv")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "fallback.csv",
                Options = null
            };
            // Act
            var result = collection.Group(identifierFiles).ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].IdentifierFilePath, Is.EqualTo(Path.Combine(dir, "Design2.csv")));
        }

        /// <summary>
        /// Tests that Group uses collection's IdentifierFilePath when identifier files are in non-matching directories.
        /// </summary>
        [Test]
        public void Group_IdentifierFilesInNonMatchingDirectories_UsesCollectionIdentifierFilePath()
        {
            // Arrange
            string designDir = Path.Combine("C:", "DesignDir");
            string idDir = Path.Combine("C:", "IdentifierDir");
            var designFiles = new List<string>
            {
                Path.Combine(designDir, "Design1.xml")
            };
            var identifierFiles = new List<string>
            {
                Path.Combine(idDir, "identifiers.csv")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "fallback.csv",
                Options = null
            };
            // Act
            var result = collection.Group(identifierFiles).ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].IdentifierFilePath, Is.EqualTo("fallback.csv"));
        }

        /// <summary>
        /// Tests that Group correctly maps identifier files to their respective directories when multiple directories exist.
        /// </summary>
        [Test]
        public void Group_MultipleDirectoriesWithIdentifierFiles_CorrectlyMapsIdentifierFiles()
        {
            // Arrange
            string dir1 = Path.Combine("C:", "Dir1");
            string dir2 = Path.Combine("C:", "Dir2");
            string dir3 = Path.Combine("C:", "Dir3");
            var designFiles = new List<string>
            {
                Path.Combine(dir1, "Design1.xml"),
                Path.Combine(dir2, "Design2.xml"),
                Path.Combine(dir3, "Design3.xml")
            };
            var identifierFiles = new List<string>
            {
                Path.Combine(dir1, "Design1.csv"),
                Path.Combine(dir3, "Design3.csv")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "fallback.csv",
                Options = null
            };
            // Act
            var result = collection.Group(identifierFiles).ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(3));
            DesignFileCollection group1 = result.FirstOrDefault(g => g.Targets.Contains(Path.Combine(dir1, "Design1.xml")));
            Assert.That(group1, Is.Not.Null);
            Assert.That(group1.IdentifierFilePath, Is.EqualTo(Path.Combine(dir1, "Design1.csv")));
            DesignFileCollection group2 = result.FirstOrDefault(g => g.Targets.Contains(Path.Combine(dir2, "Design2.xml")));
            Assert.That(group2, Is.Not.Null);
            Assert.That(group2.IdentifierFilePath, Is.EqualTo("fallback.csv"));
            DesignFileCollection group3 = result.FirstOrDefault(g => g.Targets.Contains(Path.Combine(dir3, "Design3.xml")));
            Assert.That(group3, Is.Not.Null);
            Assert.That(group3.IdentifierFilePath, Is.EqualTo(Path.Combine(dir3, "Design3.csv")));
        }

        /// <summary>
        /// Tests that Group handles complex scenario with mixed directories, multiple files, and partial identifier file coverage.
        /// </summary>
        [Test]
        public void Group_ComplexScenario_CorrectlyGroupsAndAssignsIdentifierFiles()
        {
            // Arrange
            string dir1 = Path.Combine("C:", "Project", "Dir1");
            string dir2 = Path.Combine("C:", "Project", "Dir2");
            var designFiles = new List<string>
            {
                Path.Combine(dir1, "DesignA.xml"),
                Path.Combine(dir1, "DesignB.xml"),
                Path.Combine(dir2, "DesignC.xml"),
                Path.Combine(dir2, "DesignD.xml"),
                Path.Combine(dir2, "DesignE.xml")
            };
            var identifierFiles = new List<string>
            {
                Path.Combine(dir2, "DesignC.csv")
            };
            var options = new DesignFileOptions
            {
                Version = "v105",
                StartId = 5000,
                ModelVersion = "1.0.0"
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "global_fallback.csv",
                Options = options
            };
            // Act
            var result = collection.Group(identifierFiles).ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(5));
            DesignFileCollection group1 = result.FirstOrDefault(g => g.Targets.Contains(Path.Combine(dir1, "DesignA.xml")));
            Assert.That(group1, Is.Not.Null);
            Assert.That(group1.Targets, Has.Count.EqualTo(1));
            Assert.That(group1.IdentifierFilePath, Is.EqualTo("global_fallback.csv"));
            Assert.That(group1.Options, Is.EqualTo(options));
            DesignFileCollection group2 = result.FirstOrDefault(g => g.Targets.Contains(Path.Combine(dir2, "DesignC.xml")));
            Assert.That(group2, Is.Not.Null);
            Assert.That(group2.Targets, Has.Count.EqualTo(1));
            Assert.That(group2.IdentifierFilePath, Is.EqualTo(Path.Combine(dir2, "DesignC.csv")));
            Assert.That(group2.Options, Is.EqualTo(options));
        }

        /// <summary>
        /// Tests that Group handles null collection IdentifierFilePath correctly.
        /// </summary>
        [Test]
        public void Group_NullCollectionIdentifierFilePath_ReturnsNullIdentifierFilePath()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design1.xml")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = null,
                Options = null
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].IdentifierFilePath, Is.Null);
        }

        /// <summary>
        /// Tests that Group handles empty string collection IdentifierFilePath correctly.
        /// </summary>
        [Test]
        public void Group_EmptyStringCollectionIdentifierFilePath_ReturnsEmptyIdentifierFilePath()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design1.xml")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = string.Empty,
                Options = null
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].IdentifierFilePath, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that Group handles design files with relative paths correctly.
        /// </summary>
        [Test]
        public void GroupDesignFilesWithRelativePathsEmitsEachTarget()
        {
            // Arrange
            var designFiles = new List<string>
            {
                Path.Combine("SubDir1", "Design1.xml"),
                Path.Combine("SubDir1", "Design2.xml"),
                Path.Combine("SubDir2", "Design3.xml")
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "default.csv",
                Options = null
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(3));
        }

        /// <summary>
        /// Tests that Group preserves all DesignFileOptions properties correctly.
        /// </summary>
        [Test]
        public void Group_WithCompleteOptions_PreservesAllOptionProperties()
        {
            // Arrange
            string dir = Path.Combine("C:", "TestDir");
            var designFiles = new List<string>
            {
                Path.Combine(dir, "Design1.xml")
            };
            var options = new DesignFileOptions
            {
                Version = "v105",
                StartId = 10000,
                ModelVersion = "2.3.4",
                ModelPublicationDate = "2025-01-01",
                ReleaseCandidate = false
            };
            var collection = new DesignFileCollection
            {
                Targets = designFiles,
                IdentifierFilePath = "test.csv",
                Options = options
            };
            // Act
            var result = collection.Group().ToList();
            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Options, Is.EqualTo(options));
            Assert.That(result[0].Options.Version, Is.EqualTo("v105"));
            Assert.That(result[0].Options.StartId, Is.EqualTo(10000));
            Assert.That(result[0].Options.ModelVersion, Is.EqualTo("2.3.4"));
            Assert.That(result[0].Options.ModelPublicationDate, Is.EqualTo("2025-01-01"));
            Assert.That(result[0].Options.ReleaseCandidate, Is.False);
        }
    }
}
