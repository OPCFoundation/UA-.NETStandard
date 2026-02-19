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
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Model options
    /// </summary>
    public sealed record class DesignFileOptions
    {
        /// <summary>
        /// -version [v104, v105]
        /// </summary>
        public string Version { get; init; }

        /// <summary>
        /// -id [start id]
        /// </summary>
        public uint StartId { get; init; }

        /// <summary>
        /// -mv [model version]
        /// </summary>
        public string ModelVersion { get; init; }

        /// <summary>
        /// -pd [publication date]
        /// </summary>
        public string ModelPublicationDate { get; init; }

        /// <summary>
        /// -rc
        /// </summary>
        public bool ReleaseCandidate { get; init; } = true;
    }

    /// <summary>
    /// Collection of design files and options
    /// </summary>
    public sealed record class DesignFileCollection
    {
        /// <summary>
        /// Design file location
        /// </summary>
        public IReadOnlyList<string> DesignFiles { get; init; }

        /// <summary>
        /// Optional identifier file if not same name and side
        /// by side with design files
        /// </summary>
        public string IdentifierFilePath { get; init; }

        /// <summary>
        /// Design file options
        /// </summary>
        public DesignFileOptions Options { get; init; }
    }

    /// <summary>
    /// Validate model design
    /// </summary>
    internal static class DesignFileExtensions
    {
        /// <summary>
        /// Get design file groups for processing. A group is a set of design files
        /// in the same common folder with an optional csv file included.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="collection"/>
        /// is <c>null</c>.</exception>
        public static IEnumerable<DesignFileCollection> Group(
            this DesignFileCollection collection,
            List<string> identifierFiles = null)
        {
            if (collection is null)
            {
                throw new ArgumentNullException(nameof(collection));
            }
            var idFiles = new Dictionary<string, List<string>>();
            if (identifierFiles != null)
            {
                foreach (string idFile in identifierFiles)
                {
                    string dir = Path.GetDirectoryName(idFile);
                    if (!idFiles.TryGetValue(dir, out List<string> value))
                    {
                        value = [];
                        idFiles[dir] = value;
                    }
                    value.Add(idFile);
                }
            }
            return collection.DesignFiles
                .GroupBy(Path.GetDirectoryName)
                .Select(g => new DesignFileCollection
                {
                    DesignFiles = [.. g],
                    IdentifierFilePath = idFiles.TryGetValue(g.Key, out List<string> files) ?
                        files.FirstOrDefault() : collection.IdentifierFilePath,
                    Options = collection.Options
                });
        }

        /// <summary>
        /// Validates the model design files
        /// </summary>
        public static IModelDesign OpenModelDesign(
            this IFileSystem fileSystem,
            DesignFileCollection designFiles,
            IReadOnlyList<string> exclusions,
            ITelemetryContext telemetry,
            bool useAllowSubtypes = true)
        {
            DesignFileOptions options = designFiles.Options ?? new DesignFileOptions();
            var validator = new ModelDesignValidator(
                fileSystem,
                options.StartId,
                exclusions,
                telemetry,
                SpecificationVersion.V105)
            {
                UseAllowSubtypes = useAllowSubtypes,
                ReleaseCandidate = options.ReleaseCandidate,
                ModelVersion = options.ModelVersion,
                ModelPublicationDate = options.ModelPublicationDate
            };

            string identifierFilePath = designFiles.IdentifierFilePath;
            validator.Validate(designFiles.DesignFiles, identifierFilePath);
            return validator;
        }
    }
}
