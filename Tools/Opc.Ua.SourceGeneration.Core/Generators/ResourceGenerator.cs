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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// <para>
    /// Generates embedded resources or string constants as code.
    /// </para>
    /// <para>
    /// The resource generator can be configured with a threshold value
    /// which specifies the max size of the resources that will be inlined
    /// as byte arrays or utf8 string literals which amounts to the same
    /// as the latter is lowered to a byte array. The compiler is however
    /// rather slow when it needs to pack large byte arrays into the .data
    /// section. So if length is known and the length is exceeding this
    /// value it will the value will be base64 encoded and included as a
    /// string constant, which then will be decoded at runtime into bytes.
    /// </para>
    /// <para>
    /// Add the $(Features);experimental-data-section-string-literals=x
    /// to let the compiler intern const strings as utf8 byte arrays. See
    /// https://github.com/dotnet/roslyn/blob/main/docs/features/string-literals-data-section.md
    /// for more information.
    /// </para>
    /// </summary>
    internal sealed class ResourceGenerator
    {
        /// <summary>
        /// Create code generator for embedded resources.
        /// </summary>
        public ResourceGenerator(
            IGeneratorContext context,
            int? base64Threshold = null,
            bool useByteArrayForBase64 = false)
        {
            m_context = context ?? throw new ArgumentNullException(nameof(context));
            m_base64Threshold = base64Threshold ??
                (m_context.Options.OptimizeForCompileSpeed ? 1024 : int.MaxValue);
            m_useByteArrayForBase64 = useByteArrayForBase64;
        }

        /// <summary>
        /// Embed resources as code into the compilation
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        public Resource Embed(
            string namespacePrefix,
            string name,
            bool internalAccess,
            params Resource[] resources)
        {
            m_internalAccess = internalAccess;
            if (resources.Length == 0)
            {
                throw new ArgumentException("At least one resource must be provided");
            }

            if (resources
                .Select(r => (r.ResourceGroup, r.ResourceName))
                .Distinct()
                .Count() != resources.Length)
            {
                throw new ArgumentException("Resource names must be unique");
            }

            IGrouping<string, Resource>[] groups = [.. resources
                .GroupBy(r => string.IsNullOrEmpty(r.ResourceGroup) ?
                    name : // Use the passed in file name name as default group
                    r.ResourceGroup)
                .OrderBy(g => g.Key)];

            string outputFile = Path.Combine(m_context.OutputFolder, CoreUtils.Format(
                "{0}.{1}.g.cs",
                namespacePrefix,
                name));

            using TextWriter writer = m_context.FileSystem.CreateTextWriter(outputFile);

            using var templateWriter = new TemplateWriter(writer);
            var template = new Template(templateWriter, ResourceTemplates.File);

            template.AddReplacement(Tokens.Namespace, namespacePrefix);
            template.AddReplacement(
                Tokens.ListOfResourceGroups,
                ResourceTemplates.Class,
                groups,
                WriteTemplate_ResourceGroup);

            template.Render();
            return outputFile.AsTextFileResource();
        }

        private bool WriteTemplate_ResourceGroup(IWriteContext context)
        {
            if (context.Target is not IGrouping<string, Resource> group)
            {
                return false;
            }

            context.Template.AddReplacement(Tokens.ClassName, group.Key);
            context.Template.AddReplacement(
                Tokens.AccessModifier,
                m_internalAccess ? "internal" : "public");

            context.Template.AddReplacement(
                Tokens.ListOfResourceDeclarations,
                [.. group],
                LoadTemplate_ResourceDeclaration,
                WriteTemplate_ResourceDeclaration);

            return context.Template.Render();
        }

        private TemplateString LoadTemplate_ResourceDeclaration(ILoadContext context)
        {
            if (context.Target is not Resource resource)
            {
                return null;
            }
            if (context.Target is StringResource str && str.AsUtf16)
            {
                return ResourceTemplates.Declaration_ConstString;
            }
            if (m_useByteArrayForBase64 &&
                resource.GetLength(m_context.FileSystem) > m_base64Threshold)
            {
                return ResourceTemplates.Declaration_ByteArray;
            }
            return ResourceTemplates.Declaration_ReadOnlySpan;
        }

        private bool WriteTemplate_ResourceDeclaration(IWriteContext context)
        {
            if (context.Target is not Resource resource)
            {
                return false;
            }
            context.Template.AddReplacement(Tokens.ResourceName, resource.ResourceName);

            context.Template.AddReplacement(
                Tokens.Resource,
                [context.Target],
                LoadTemplate_Resource,
                WriteTemplate_Resource);

            return context.Template.Render();
        }

        private bool WriteTemplate_Resource(IWriteContext context)
        {
            if (context.Target is StringResource str && str.AsUtf16)
            {
                switch (str)
                {
                    case TextResource textResource:
                        context.Template.AddReplacement(
                            Tokens.Resource,
                            textResource.Text);
                        return context.Template.Render();
                    case TextReaderResource textReaderResource:
                        context.Template.AddReplacement(
                            Tokens.Resource,
                            textReaderResource.Reader.ReadToEnd());
                        return context.Template.Render();
                    default:
                        // SHould not be here
                        return false;
                }
            }
            // Already written
            return true;
        }

        private TemplateString LoadTemplate_Resource(ILoadContext context)
        {
            if (context.Target is not Resource resource)
            {
                return null;
            }

            if (context.Target is StringResource str && str.AsUtf16)
            {
                return context.TemplateString;
            }

            bool writeAsBase64 =
                resource.GetLength(m_context.FileSystem) > m_base64Threshold;

            // Render the content here instead of rendering a template
            if (resource.IsText)
            {
                if (writeAsBase64 || !m_context.Options.UseUtf8StringLiterals)
                {
                    WriteTextResourceAsBase64(context, resource);
                }
                else
                {
                    WriteTextResource(context, resource);
                }
            }
            else if (writeAsBase64)
            {
                WriteBinaryResourceAsBase64(context, resource);
            }
            else
            {
                WriteBinaryResource(context, resource);
            }
            return null;
        }

        private void WriteBinaryResource(ILoadContext context, Resource resource)
        {
            Stream stream = GetResourceStream(resource, out bool leaveOpen);
            try
            {
                WriteAsByteArray(context, stream);
            }
            finally
            {
                if (!leaveOpen)
                {
                    stream.Dispose();
                }
            }

            Stream GetResourceStream(Resource resource, out bool leaveOpen)
            {
                leaveOpen = false;
                switch (resource)
                {
                    case BinaryFileResource fileResource:
                        return m_context.FileSystem.OpenRead(fileResource.FileName);
                    case StreamResource stream:
                        stream.Stream.Position = 0;
                        leaveOpen = true;
                        return stream.Stream;
                    case BinaryResource binary:
                        return new MemoryStream(binary.Data);
                    default:
                        throw new NotSupportedException(
                            $"Unable to get stream for resource {resource.GetType().Name}");
                }
            }
        }

        private void WriteBinaryResourceAsBase64(ILoadContext context, Resource resource)
        {
            Stream stream = GetResourceStream(resource, out bool leaveOpen);
            try
            {
                WriteAsBase64StringLiteral(context, AsBase64String(stream));
            }
            finally
            {
                if (!leaveOpen)
                {
                    stream.Dispose();
                }
            }
            Stream GetResourceStream(Resource resource, out bool leaveOpen)
            {
                leaveOpen = false;
                switch (resource)
                {
                    case BinaryFileResource fileResource:
                        return m_context.FileSystem.OpenRead(fileResource.FileName);
                    case StreamResource stream:
                        stream.Stream.Position = 0;
                        leaveOpen = true;
                        return stream.Stream;
                    case BinaryResource binary:
                        return new MemoryStream(binary.Data);
                    default:
                        throw new NotSupportedException(
                            $"Unable to get stream for resource {resource.GetType().Name}");
                }
            }
        }

        private void WriteTextResource(ILoadContext context, Resource resource)
        {
            TextReader reader = GetResourceTextReader(context, out bool disposeReader);
            try
            {
                WriteAsUtf8StringLiteral(context, reader);
            }
            finally
            {
                if (!disposeReader)
                {
                    reader.Dispose();
                }
            }

            TextReader GetResourceTextReader(ILoadContext context, out bool leaveOpen)
            {
                leaveOpen = false;
                switch (context.Target)
                {
                    case TextResource text:
                        return new StringReader(text.Text);
                    case TextReaderResource textReader:
                        leaveOpen = true;
                        return textReader.Reader;
                    case StreamResource stream:
                        stream.Stream.Position = 0;
                        return new StreamReader(
                            stream.Stream,
                            Encoding.UTF8,
                            true,
                            kReadBufferSize,
                            true);
                    case BinaryResource binary:
                        return new StreamReader(
                            new MemoryStream(binary.Data),
                            Encoding.UTF8,
                            true,
                            kReadBufferSize,
                            false);
                    case TextFileResource textFile:
                        return new StreamReader(
                            m_context.FileSystem.OpenRead(textFile.FileName),
                            Encoding.UTF8,
                            true,
                            kReadBufferSize,
                            false);
                    default:
                        throw new NotSupportedException(
                            $"Unable to get text reader for resource {resource.GetType().Name}");
                }
            }
        }

        private void WriteTextResourceAsBase64(ILoadContext context, Resource resource)
        {
            Stream istrm = GetResourceTextReader(resource, out bool leaveOpen);
            try
            {
                WriteAsBase64StringLiteral(
                    context,
                    AsBase64String(istrm));
            }
            finally
            {
                if (!leaveOpen)
                {
                    istrm.Dispose();
                }
            }

            Stream GetResourceTextReader(Resource resource, out bool leaveOpen)
            {
                switch (resource)
                {
                    case TextResource text:
                        leaveOpen = false;
                        return new MemoryStream(Encoding.UTF8.GetBytes(text.Text));
                    case TextFileResource textFile:
                        leaveOpen = false;
                        return m_context.FileSystem.OpenRead(textFile.FileName);
                    case StreamResource stream:
                        stream.Stream.Position = 0;
                        leaveOpen = true;
                        return stream.Stream;
                    case BinaryResource binary:
                        leaveOpen = false;
                        return new MemoryStream(binary.Data);
                    default:
                        throw new NotSupportedException(
                            $"Unable to get text reader for resource {resource.GetType().Name}");
                }
            }
        }

        private void WriteAsUtf8StringLiteral(
            ILoadContext context,
            TextReader reader,
            bool trimLines = false)
        {
            Debug.Assert(m_context.Options.UseUtf8StringLiterals);

            context.Out.Write("\"\"\"");
            bool firstLine = true;
            for (string line = reader.ReadLine();
                line != null;
                line = reader.ReadLine())
            {
                if (trimLines)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }
                }
                if (firstLine)
                {
                    // Cannot have multi line without writing anything
                    context.Out.WriteLine();
                    firstLine = false;
                }
                context.Out.WriteLine(line);
            }
            context.Out.Write("\"\"\"u8");
        }

        private static void WriteAsBase64StringLiteral(ILoadContext context, string base64)
        {
            context.Out.WriteLine("global::System.Convert.FromBase64String(");
            //
            // Do not format the string, roslyn code analyzers barf with stack
            // overflow when there are too many string "add" binary operations
            // in a single statement to visit.
            //
#if !BEAUTY_CONTEST
            context.Out.Write("\"");
            for (int ii = 0; ii < base64.Length; ii++)
            {
                // Escape backslashes
                if (base64[ii] == '\\')
                {
                    context.Out.Write("""\\""");
                    continue;
                }
                context.Out.Write(base64[ii]);
            }
            context.Out.WriteLine("\")");
#else
            for (int ii = 0; ii < base64.Length; ii += 80)
            {
                if (ii > 0)
                {
                    context.Out.Write(" +");
                }
                if (ii + 80 >= base64.Length)
                {
                    WriteChunk(context, base64[ii..]);
                }
                else
                {
                    WriteChunk(context, base64.Substring(ii, 80));
                }
            }
            context.Out.WriteLine(")");

            static void WriteChunk(Context context, string line)
            {
                context.Out.Write("   \"");

                for (int ii = 0; ii < line.Length; ii++)
                {
                    // Escape backslashes
                    if (line[ii] == '\\')
                    {
                        context.Out.Write("""\\""");
                        continue;
                    }
                    context.Out.WriteLine(line[ii]);
                }

                context.Out.WriteLine("\"");
            }
#endif
        }

        private static void WriteAsByteArray(ILoadContext context, Stream reader)
        {
            context.Out.WriteLine("new byte[]");
            context.Out.WriteLine("{");
            context.Out.Write("    ");
            bool first = true;
            int column = 0;

            int b = reader.ReadByte();
            while (b != -1)
            {
                // line break after x entries
                if (column++ >= 12)
                {
                    context.Out.WriteLine(",");
                    context.Out.Write("    ");
                    column = 1;
                }
                else if (!first)
                {
                    context.Out.Write(", ");
                }
                first = false;
                context.Out.Write("0x{0:X2}", (byte)b);
                b = reader.ReadByte();
            }
            context.Out.WriteLine("}");
        }

        private static string AsBase64String(Stream reader)
        {
            if (reader is MemoryStream ms)
            {
                return Convert.ToBase64String(ms.ToArray());
            }
            using var memoryStream = new MemoryStream();
            reader.CopyTo(memoryStream);
            return Convert.ToBase64String(memoryStream.ToArray());
        }

        private const int kReadBufferSize = 16 * 1024;
        private readonly IGeneratorContext m_context;
        private readonly int m_base64Threshold;
        private readonly bool m_useByteArrayForBase64;
        private bool m_internalAccess;
    }

    /// <summary>
    /// An embeddeable resource
    /// </summary>
    internal abstract record class Resource
    {
        /// <summary>
        /// How the resources should be grouped in the resource file
        /// </summary>
        public string ResourceGroup { get; }

        /// <summary>
        /// The name of the resource variable in the file
        /// </summary>
        public string ResourceName { get; }

        /// <summary>
        /// Whether the resource is text or binary
        /// </summary>
        public bool IsText { get; }

        /// <summary>
        /// Create resource
        /// </summary>
        public Resource(string resourceName, bool isText)
        {
            int index = resourceName
                .Trim('.')
                .IndexOf('.', StringComparison.Ordinal);
            if (index == -1)
            {
                ResourceGroup = string.Empty;
                ResourceName = resourceName;
            }
            else
            {
                ResourceGroup = resourceName[..index];
                ResourceName = resourceName[(index + 1)..];
            }
            IsText = isText;
        }

        /// <summary>
        /// Get length of the resource
        /// </summary>
        public abstract long GetLength(IFileSystem fileSystem);

        /// <summary>
        /// Make a resource name out of the input file name
        /// </summary>
        /// <param name="inputFile"></param>
        /// <param name="namespacePrefix"></param>
        /// <returns></returns>
        public static string GetNameForFile(string inputFile, string namespacePrefix)
        {
            inputFile = Path.GetFileName(inputFile);
            if (namespacePrefix != null &&
                inputFile.StartsWith(namespacePrefix, StringComparison.Ordinal))
            {
                inputFile = inputFile[namespacePrefix.Length..];
            }
            string[] parts = inputFile.Split('.');
            var buffer = new StringBuilder();
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }
                buffer = buffer
                    .Append(char.ToUpperInvariant(part[0]))
                    .Append(part, 1, part.Length - 1);
            }
            return buffer.ToString();
        }
    }

    internal abstract record class StringResource(
        string ResourceName,
        bool AsUtf16)
        : Resource(ResourceName, true);

    internal sealed record class StreamResource(
        string ResourceName,
        Stream Stream,
        bool IsText = false)
        : Resource(ResourceName, IsText)
    {
        public override long GetLength(IFileSystem fileSystem)
        {
            return Stream.Length;
        }
    }

    internal sealed record class BinaryResource(
        string ResourceName,
        byte[] Data,
        bool IsText = false)
        : Resource(ResourceName, IsText)
    {
        public override long GetLength(IFileSystem fileSystem)
        {
            return Data.Length;
        }
    }

    internal sealed record class TextReaderResource(
        string ResourceName,
        TextReader Reader,
        bool AsUtf16 = false)
        : StringResource(ResourceName, AsUtf16)
    {
        public override long GetLength(IFileSystem fileSystem)
        {
            return 0; // Unknown
        }
    }

    internal record class TextResource(
        string ResourceName,
        string Text,
        bool AsUtf16 = false)
        : StringResource(ResourceName, AsUtf16)
    {
        public override long GetLength(IFileSystem fileSystem)
        {
            return Encoding.UTF8.GetByteCount(Text);
        }
    }

    internal sealed record class TextFileResource(
        string ResourceName,
        string FileName)
        : Resource(ResourceName, true)
    {
        public override long GetLength(IFileSystem fileSystem)
        {
            return fileSystem.GetLength(FileName);
        }
    }

    internal sealed record class BinaryFileResource(
        string ResourceName,
        string FileName)
        : Resource(ResourceName, false)
    {
        public override long GetLength(IFileSystem fileSystem)
        {
            return fileSystem.GetLength(FileName);
        }
    }

    internal sealed record class StringConstant(string Text)
        : TextResource(GetName(Text), Text, true)
    {
        private static string GetName(string text)
        {
            var buffer = new StringBuilder();
            foreach (char ch in text)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    buffer.Append(ch);
                }
                else
                {
                    buffer.Append('_');
                }
            }
            return buffer.ToString();
        }
    }

    /// <summary>
    /// Extensions
    /// </summary>
    internal static class ResourceExtensions
    {
        public static TextFileResource AsTextFileResource(
            this string fileName,
            string namespacePrefix = null)
        {
            return new TextFileResource(
                Resource.GetNameForFile(fileName, namespacePrefix),
                fileName);
        }

        public static BinaryFileResource ToBinaryFileResource(
            this string fileName,
            string namespacePrefix = null)
        {
            return new BinaryFileResource(
                Resource.GetNameForFile(fileName, namespacePrefix),
                fileName);
        }
    }
}
