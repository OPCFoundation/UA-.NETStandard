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
 *
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

using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task EnumeratedHostFormsMustBeAuthoredBeforeAnyProviderIsInvoked(bool selectAll)
        {
            JsonObject plan = HostPlan(bulk: false);
            if (selectAll)
            {
                plan["uav:projects"]![0]!["uav:selectAll"] = true;
            }
            JsonObject source = Source();
            string originalPlan = plan.ToJsonString();
            string originalSource = source.ToJsonString();
            int calls = 0;
            var provider = new ProjectionFormProvider((_, token) =>
            {
                token.ThrowIfCancellationRequested();
                calls++;
                return HostForms("https://host.test/runtime/provider-must-not-repair-enumeration");
            });

            WotConversionResult<WotDocument> result = await ResolveAsync(
                plan, source, new WotNodeSetConverterOptions { ProjectionFormProvider = provider })
                .ConfigureAwait(false);
            using WotDocument view = result.Value;

            Assert.Multiple(() =>
            {
                Assert.That(calls, Is.Zero, "An invalid enumeration must fail before any host-provider invocation.");
                Assert.That(result.Success, Is.False);
                Assert.That(view, Is.Null);
                Assert.That(result.Diagnostics.Any(diagnostic =>
                    diagnostic.Code == WotDiagnosticCode.ProjectionSourceUnresolved &&
                    diagnostic.Severity == WotDiagnosticSeverity.Error &&
                    diagnostic.Location?.Reference == "/properties/reading/forms"), Is.True);
                Assert.That(plan.ToJsonString(), Is.EqualTo(originalPlan));
                Assert.That(source.ToJsonString(), Is.EqualTo(originalSource));
            });
        }
    }
}
