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
using System.CommandLine;
using System.IO;
using System.Text.Json;

namespace Opc.Ua.ReleaseEvidence
{
    /// <summary>
    /// Registers command-line access to offline NuGet delivery-content comparison.
    /// </summary>
    internal static class NugetDeliveryCommands
    {
        /// <summary>
        /// Adds the command that compares author and delivered NuGet archives without authenticating their signatures.
        /// </summary>
        public static void Register(RootCommand root)
        {
            var command = new Command(
                "verify-delivery",
                "Compare author and delivered NuGet content; independent signature authentication is still required.");
            var author = new Option<string>("--author") { Required = true };
            var delivered = new Option<string>("--delivered") { Required = true };
            var output = new Option<string>("--output") { Required = true };
            command.Options.Add(author);
            command.Options.Add(delivered);
            command.Options.Add(output);
            command.SetAction(async (parse, cancellationToken) =>
            {
                try
                {
                    return await new NugetDeliveryVerifier(new EvidenceFiles()).VerifyAsync(
                        parse.GetRequiredValue(author),
                        parse.GetRequiredValue(delivered),
                        parse.GetRequiredValue(output),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is
                    IOException or InvalidDataException or JsonException or ArgumentException or
                    InvalidOperationException or UnauthorizedAccessException or FormatException or
                    System.Xml.XmlException)
                {
                    await Console.Error.WriteLineAsync($"Invalid input: {exception.Message}").ConfigureAwait(false);
                    return 2;
                }
            });
            root.Subcommands.Add(command);
        }
    }
}
