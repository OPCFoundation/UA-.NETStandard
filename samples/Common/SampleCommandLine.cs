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
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Opc.Ua.Samples
{
    /// <summary>
    /// Source-linked CLI conveniences for samples, not a stack configuration API.
    /// Applications own their commands, endpoint policies and host configuration.
    /// </summary>
    internal static class SampleCommandLine
    {
        /// <summary>
        /// Creates the default-false --auto-accept certificate-trust exception with app-selected aliases.
        /// This development-only switch does not select SecurityPolicy None.
        /// </summary>
        /// <param name="aliases">Additional command-line spellings chosen by the sample.</param>
        /// <returns>A Boolean option that requires explicit consent to accept untrusted peer certificates.</returns>
        internal static Option<bool> CreateAutoAcceptOption(params string[] aliases)
        {
            return new Option<bool>("--auto-accept", aliases)
            {
                Description =
                    "Accept untrusted peer certificates (development only; does not select SecurityPolicy None).",
                Arity = ArgumentArity.ZeroOrOne
            };
        }

        /// <summary>
        /// Separates explicitly forwarded generic-host configuration after <c>--</c>.
        /// Only host-based applications opt into this boundary.
        /// </summary>
        internal static (string[] Sample, string[] Host) SplitHostArguments(string[] arguments)
        {
            int separator = Array.IndexOf(arguments, "--");
            return separator < 0
                ? (arguments, [])
                : (arguments[..separator], arguments[(separator + 1)..]);
        }

        /// <summary>
        /// Projects app-declared host switches after forwarded and positional settings.
        /// Security flags are deliberately not projected into generic-host configuration.
        /// </summary>
        /// <param name="result">Successfully parsed sample arguments.</param>
        /// <param name="forwardedArguments">Host arguments supplied after the explicit -- boundary.</param>
        /// <param name="configurationArgument">The sample's positional key=value argument.</param>
        /// <param name="options">Declared host-setting options whose supplied values take precedence.</param>
        /// <returns>Host arguments ordered as forwarded settings, positional settings, then explicit options.</returns>
        internal static string[] GetHostArguments(
            ParseResult result,
            string[] forwardedArguments,
            Argument<string[]> configurationArgument,
            params Option<string>[] options)
        {
            var arguments = new List<string>(forwardedArguments);
            arguments.AddRange(result.GetValue(configurationArgument) ?? []);
            foreach (Option<string> option in options)
            {
                if (result.GetValue(option) is string value)
                {
                    arguments.Add($"{option.Name.TrimStart('-')}={value}");
                }
            }
            return [.. arguments];
        }

        /// <summary>
        /// Creates a bounded integer switch whose value can be forwarded to host configuration.
        /// No CLI default is supplied, preserving JSON and environment settings when omitted.
        /// </summary>
        internal static Option<string> CreateInt32ConfigurationOption(
            string name,
            string description,
            int minimum,
            int maximum)
        {
            var option = new Option<string>(name) { Description = description };
            option.Validators.Add(result =>
            {
                if (!int.TryParse(
                    result.GetValueOrDefault<string>(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value) ||
                    value < minimum ||
                    value > maximum)
                {
                    result.AddError($"{name} must be an integer between {minimum} and {maximum}.");
                }
            });
            return option;
        }

        /// <summary>
        /// Creates optional positional generic-host assignments without accepting
        /// unknown sample switches. Forward host switches using <see cref="SplitHostArguments"/>.
        /// </summary>
        internal static Argument<string[]> CreateConfigurationArgument()
        {
            var argument = new Argument<string[]>("configuration")
            {
                Description = "Generic-host key=value settings. Forward host switches after --.",
                Arity = ArgumentArity.ZeroOrMore,
                // In 2.0.11, GetValue during command cross-validation can bypass
                // argument validators. Keep these checks in conversion itself.
                CustomParser = result =>
                {
                    string[] values = new string[result.Tokens.Count];
                    for (int i = 0; i < values.Length; i++)
                    {
                        string value = result.Tokens[i].Value;
                        if (value.IndexOf('=', StringComparison.Ordinal) < 1 ||
                            value.StartsWith('-') ||
                            value.StartsWith('/'))
                        {
                            result.AddError(
                                "Expected a host setting in key=value form. Forward host switches after --.");
                        }
                        values[i] = value;
                    }
                    return values;
                }
            };
            return argument;
        }

        /// <summary>
        /// Rejects missing values that the generic-host command-line provider silently
        /// ignores, then validates its remaining syntax without creating a host.
        /// </summary>
        internal static string? GetHostArgumentError(string[] arguments)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].Contains('=', StringComparison.Ordinal))
                {
                    continue;
                }
                if (i + 1 == arguments.Length)
                {
                    return $"Host setting '{arguments[i]}' is missing a value. Use key=value or --key value.";
                }
                // The following token is a value, even when it resembles a flag.
                i++;
            }
            try
            {
                using var configuration = new ConfigurationManager();
                configuration.AddCommandLine(arguments);
            }
            catch (FormatException ex)
            {
                return $"Invalid host configuration: {ex.Message}";
            }
            return null;
        }

        /// <summary>
        /// Rejects malformed Boolean assignments and parse errors before invoking the application's action.
        /// Routes output to the supplied writers; help never runs the action.
        /// </summary>
        internal static async Task<int> InvokeAsync(
            RootCommand command,
            string[] arguments,
            TextWriter output,
            TextWriter error,
            CancellationToken cancellationToken = default)
        {
            // System.CommandLine treats a non-boolean value attached to a bool option
            // as a positional argument. Reject explicit assignments before that can
            // silently turn the flag on. Values of other options and tokens after --
            // are not sample flags.
            foreach (string argument in arguments)
            {
                if (argument == "--")
                {
                    break;
                }
                int separator = argument.IndexOfAny(['=', ':']);
                if (separator < 0)
                {
                    continue;
                }
                string name = argument[..separator];
                foreach (Option option in command.Options)
                {
                    if (option is Option<bool> &&
                        (option.Name == name || option.Aliases.Contains(name)) &&
                        !bool.TryParse(argument[(separator + 1)..], out _))
                    {
                        await error.WriteLineAsync(
                            $"Option '{name}' expects true or false, not '{argument[(separator + 1)..]}'.")
                            .ConfigureAwait(false);
                        await error.WriteLineAsync("Use --help to see valid options and arguments.")
                            .ConfigureAwait(false);
                        return 1;
                    }
                }
            }
            ParseResult result;
            try
            {
                result = command.Parse(arguments);
            }
            catch (InvalidOperationException ex)
            {
                // A command validator projecting an invalid typed value can throw
                // before Parse returns its errors. This is still a CLI failure.
                await error.WriteLineAsync(ex.Message).ConfigureAwait(false);
                await error.WriteLineAsync("Use --help to see valid options and arguments.").ConfigureAwait(false);
                return 1;
            }
            if (result.Errors.Count > 0)
            {
                foreach (ParseError parseError in result.Errors)
                {
                    await error.WriteLineAsync(parseError.Message).ConfigureAwait(false);
                }
                await error.WriteLineAsync("Use --help to see valid options and arguments.").ConfigureAwait(false);
                return 1;
            }
            return await result.InvokeAsync(
                new InvocationConfiguration { Output = output, Error = error },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Warns independently about accepting untrusted certificates and disabling message security.
        /// Call from the parsed action before starting a host or connecting, so help and parse errors do not warn.
        /// </summary>
        /// <param name="error">Writer receiving warnings for enabled security relaxations.</param>
        /// <param name="autoAccept">Whether untrusted peer certificates may be accepted.</param>
        /// <param name="securityNone">Whether messages will be sent without signing or encryption.</param>
        /// <param name="certificatePeer">Peer role named in the certificate warning, such as client or server.</param>
        /// <param name="securityNoneOption">Application-specific switch named in the unsecured-message warning.</param>
        internal static void WriteSecurityWarnings(
            TextWriter error,
            bool autoAccept,
            bool securityNone,
            string certificatePeer,
            string securityNoneOption)
        {
            if (autoAccept)
            {
                error.WriteLine(
                    $"WARNING: --auto-accept accepts untrusted {certificatePeer} certificates (development only).");
            }
            if (securityNone)
            {
                error.WriteLine(
                    $"WARNING: {securityNoneOption} enables SecurityPolicy None: " +
                    "OPC UA messages are not signed or encrypted.");
            }
        }
    }
}
