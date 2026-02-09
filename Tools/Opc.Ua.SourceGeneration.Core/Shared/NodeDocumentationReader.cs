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

using CsvHelper;
using System.Globalization;
using CsvHelper.Configuration.Attributes;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Read node documentation files
    /// </summary>
    internal class NodeDocumentationReader
    {
        public NodeDocumentationReader(IFileSystem fileSystem)
        {
            m_fileSystem = fileSystem ?? LocalFileSystem.Instance;
        }

        /// <summary>
        /// Get the node documentation records from the specified files.
        /// </summary>
        /// <param name="filepaths"></param>
        /// <returns></returns>
        public IList<NodeDocumentationRow> Load(params string[] filepaths)
        {
            List<NodeDocumentationRow> records = [];
            foreach (string filepath in filepaths)
            {
                Append(filepath, records);
            }
            return records;
        }

        private void Append(string filepath, List<NodeDocumentationRow> results)
        {
            using TextReader istrm = m_fileSystem.CreateTextReader(filepath);
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                MissingFieldFound = (args) => { }
            };

            using var csv = new CsvReader(istrm, configuration);
            csv.Context.RegisterClassMap<NodeDocumentationMap>();
            foreach (NodeDocumentationRow ii in csv.GetRecords<NodeDocumentationRow>().ToList())
            {
                ii.Link = ii.Link.Trim();
                results.Add(ii);
            }
        }

        private readonly IFileSystem m_fileSystem;
    }

    internal sealed class NodeDocumentationRow
    {
        [Name("Id")]
        public uint Id { get; set; }

        [Name("Name")]
        public string Name { get; set; }

        [Name("Link")]
        public string Link { get; set; }

        [Name("ConformanceUnits")]
        public IReadOnlyList<string> ConformanceUnits { get; set; }
    }

    internal class ArrayConverter<T> : DefaultTypeConverter
    {
        public override object ConvertFromString(string text, IReaderRow row, MemberMapData memberMapData)
        {
            List<string> array = [];

            if (!string.IsNullOrEmpty(text))
            {
                foreach (string ii in text.Split(';'))
                {
                    string element = ii.Trim();

                    if (!string.IsNullOrEmpty(element))
                    {
                        array.Add(element);
                    }
                }
            }

            return array;
        }

        public override string ConvertToString(object value, IWriterRow row, MemberMapData memberMapData)
        {
            var builder = new StringBuilder();

            if (value is IList<string> list)
            {
                foreach (string ii in list)
                {
                    string element = ii?.Trim();

                    if (!string.IsNullOrEmpty(element))
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append(';');
                        }

                        builder.Append(element);
                    }
                }
            }

            return builder.ToString();
        }
    }

    internal sealed class NodeDocumentationMap : ClassMap<NodeDocumentationRow>
    {
        public NodeDocumentationMap()
        {
            Map(m => m.Id).Name("Id");
            Map(m => m.Name).Name("Name");
            Map(m => m.Link).Name("Link");
            Map(m => m.ConformanceUnits).Name("ConformanceUnits").TypeConverter<ArrayConverter<NodeDocumentationRow>>();
        }
    }
}
