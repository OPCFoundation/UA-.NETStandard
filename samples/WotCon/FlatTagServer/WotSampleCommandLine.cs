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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Opc.Ua.Samples
{
    /// <summary>
    /// Keeps the three WoT executables' host arguments and independent security choices consistent.
    /// </summary>
    internal static class WotSampleCommandLine
    {
        /// <summary>
        /// Parses WoT sample options and explicitly forwarded host settings before invoking the host action.
        /// Passes the three independent, default-false security opt-ins separately from host configuration.
        /// </summary>
        /// <param name="args">Sample options, key=value settings, and host arguments forwarded after --.</param>
        /// <param name="description">Root command description displayed in help.</param>
        /// <param name="configurationKeys">
        /// Host keys exposed as sample options; port, timeout, and document-size keys receive bounded validators.
        /// </param>
        /// <param name="management">Whether to expose the separate --allow-anonymous-management switch.</param>
        /// <param name="action">
        /// Callback receiving the host builder, certificate auto-accept flag, SecurityPolicy None flag,
        /// anonymous-management flag, and cancellation token, in that order.
        /// The callback applies the selected policies and emits their security warnings.
        /// </param>
        /// <param name="cancellationToken">Cancellation token forwarded to command invocation and the callback.</param>
        /// <returns>Zero on success, or a nonzero command failure code after reporting the error.</returns>
        internal static Task<int> InvokeAsync(
            string[] args,
            string description,
            string[] configurationKeys,
            bool management,
            Func<HostApplicationBuilder, bool, bool, bool, CancellationToken, Task> action,
            CancellationToken cancellationToken = default)
        {
            (string[] sampleArguments, string[] forwarded) = SampleCommandLine.SplitHostArguments(args);
            Option<bool> autoAccept = SampleCommandLine.CreateAutoAcceptOption();
            var securityNone = new Option<bool>("--security-none")
            {
                Description = "Enable/select SecurityPolicy None (isolated demo only; does not accept certificates)."
            };
            var anonymousManagement = new Option<bool>("--allow-anonymous-management")
            {
                Description = "Permit anonymous registry mutation (isolated demo only)."
            };
            Argument<string[]> configuration = SampleCommandLine.CreateConfigurationArgument();
            var command = new RootCommand(description) { autoAccept, securityNone, configuration };
            if (management)
            {
                command.Add(anonymousManagement);
            }
            var options = new Option<string>[configurationKeys.Length];
            for (int i = 0; i < configurationKeys.Length; i++)
            {
                string key = configurationKeys[i];
                options[i] = key switch
                {
                    "port" => SampleCommandLine.CreateInt32ConfigurationOption("--port", "TCP port.", 1, 65535),
                    "timeoutSeconds" => SampleCommandLine.CreateInt32ConfigurationOption(
                        "--timeoutSeconds", "Workflow timeout in seconds.", 1, 3600),
                    "maximumDocumentBytes" => SampleCommandLine.CreateInt32ConfigurationOption(
                        "--maximumDocumentBytes", "Document size limit.", 1, int.MaxValue),
                    _ => new Option<string>("--" + key) { Description = $"Override host setting {key}." }
                };
                command.Add(options[i]);
            }
            command.Validators.Add(result =>
            {
                string? error = SampleCommandLine.GetHostArgumentError(forwarded);
                if (error is not null)
                {
                    result.AddError(error);
                }
            });
            command.SetAction(async (result, ct) =>
            {
                try
                {
                    HostApplicationBuilder builder = Host.CreateApplicationBuilder(
                        SampleCommandLine.GetHostArguments(result, forwarded, configuration, options));
                    await action(
                        builder,
                        result.GetValue(autoAccept),
                        result.GetValue(securityNone),
                        management && result.GetValue(anonymousManagement),
                        ct).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            });
            return SampleCommandLine.InvokeAsync(
                command, sampleArguments, Console.Out, Console.Error, cancellationToken);
        }
    }
}
