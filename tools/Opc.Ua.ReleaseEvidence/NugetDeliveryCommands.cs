// Copyright (c) OPC Foundation, Inc. All rights reserved.
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using System;
using System.CommandLine;
using System.IO;
using System.Text.Json;

namespace Opc.Ua.ReleaseEvidence
{
    internal static class NugetDeliveryCommands
    {
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
