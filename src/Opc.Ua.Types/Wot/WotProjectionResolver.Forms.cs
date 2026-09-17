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

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Wot
{
    public sealed partial class WotProjectionResolver
    {
        private async ValueTask<bool> SupplyHostFormsAsync(
            WotProjection projection,
            Selection selection,
            WotResolutionContext context,
            List<WotDocument> openDocuments,
            List<WotDiagnostic> diagnostics,
            CancellationToken cancellationToken)
        {
            IWotProjectionFormProvider? provider = m_options.ProjectionFormProvider;
            if (provider is null)
            {
                return true;
            }
            string hostBase = EffectiveBase(selection.Document, selection.DocumentHref);
            foreach (ResolvedAffordance member in selection.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member.Source.Source.Routing != WotProjectionRouting.Projection ||
                    member.Enumerated ||
                    member.Value.ContainsKey("forms"))
                {
                    continue;
                }
                string pointer = "/" + MapName(member.Kind) + "/" + EscapePointer(member.Name);
                string location = selection.DocumentHref + "#" + pointer + "/forms";
                if (!context.TryEnter(WotResolutionKind.Thing, location, out WotDiagnostic? blocked))
                {
                    diagnostics.Add(blocked!);
                    return false;
                }
                try
                {
                    var request = new WotProjectionFormContext(
                        selection.Document, selection.DocumentHref, hostBase,
                        member.Source.Document, member.Source.DocumentHref, member.Pointer,
                        member.Name, member.Kind, projection.ResultKind, context);
                    ArrayOf<JsonElement> forms;
                    try
                    {
                        forms = await provider.GetFormsAsync(request, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return Fail("The host-form provider canceled its acquisition.", pointer);
                    }
                    catch (Exception exception) when (
                        exception is IOException or InvalidOperationException or TimeoutException or
                            JsonException or FormatException)
                    {
                        return Fail("The host-form provider failed: " + exception.Message, pointer);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (forms.IsNull || forms.Count == 0)
                    {
                        return Fail("The host-form provider supplied no actual forms.", pointer);
                    }
                    if (forms.Count > m_options.MaxNodeCount)
                    {
                        return Fail("The host-form response exceeds the configured node bound.", pointer);
                    }
                    try
                    {
                        long bytes = 2;
                        foreach (JsonElement form in forms)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!ValidateHostForm(form, hostBase))
                            {
                                return Fail(
                                    "A host form must be an object with an endpoint in the original host origin " +
                                    "and well-formed response declarations.", pointer);
                            }
                            bytes += Encoding.UTF8.GetByteCount(form.GetRawText()) + 1L;
                        }
                        if (bytes > int.MaxValue)
                        {
                            return Fail("The host-form response exceeds the supported byte bound.", pointer);
                        }
                        if (!context.TryAddBytes(location, (int)bytes, out WotDiagnostic? limit))
                        {
                            diagnostics.Add(limit!);
                            return false;
                        }
                        WotDocument owner = CreateHostFormDocument(selection, member, forms, openDocuments);
                        if (!ValidateContexts(owner, diagnostics) ||
                            !ValidateSecurityDefinitions(owner, context.Options.MaxDepth, diagnostics))
                        {
                            return false;
                        }
                        if (!WotDocument.TryEvaluatePointer(owner.RootElement, pointer + "/forms",
                            out JsonElement originals))
                        {
                            return Fail("The generated forms lost their owning document.", pointer);
                        }
                        var carried = new JsonArray();
                        foreach (JsonElement original in originals.EnumerateArray())
                        {
                            JsonObject form = CloneObject(original);
                            form["href"] = ResolveHref(hostBase, original.GetProperty("href").GetString()!);
                            List<string> security = EffectiveSecurity(form, owner);
                            CopySecurityClosure(
                                null, owner, selection.DocumentHref, security,
                                selection.SecurityDefinitions, selection.SecurityAdded);
                            if (security.Count != 0)
                            {
                                var names = new JsonArray();
                                foreach (string name in security)
                                {
                                    names.Add(JsonValue.Create(Qualify(null, name)));
                                }
                                form["security"] = names;
                            }
                            carried.Add(form);
                        }
                        member.Value["forms"] = carried;
                        member.GeneratedFormOwner = new ReferenceOwner(owner, selection.DocumentHref, null);
                    }
                    catch (Exception exception) when (
                        exception is JsonException or FormatException or InvalidOperationException or ArgumentException)
                    {
                        return Fail("The host-form response could not be carried: " + exception.Message, pointer);
                    }
                }
                finally
                {
                    context.Leave(location);
                }
            }
            return true;

            bool Fail(string message, string pointer)
            {
                AddError(diagnostics, WotDiagnosticCode.ProjectionSourceUnresolved, message, pointer + "/forms");
                return false;
            }
        }

        private WotDocument CreateHostFormDocument(
            Selection selection,
            ResolvedAffordance member,
            ArrayOf<JsonElement> forms,
            List<WotDocument> openDocuments)
        {
            // Generated forms need a real owner; the immutable plan never contained their JSON elements.
            JsonObject root = CloneObject(selection.Document.RootElement);
            PreserveLiteralValues(root, selection.Document, selection.Document.RootElement);
            string mapName = MapName(member.Kind);
            if (root[mapName] is not JsonObject map)
            {
                map = [];
                root[mapName] = map;
            }
            if (map[member.Name] is not JsonObject affordance)
            {
                affordance = [];
                map[member.Name] = affordance;
            }
            var ownedForms = new JsonArray();
            foreach (JsonElement form in forms)
            {
                ownedForms.Add(CloneObject(form));
            }
            affordance["forms"] = ownedForms;
            WotDocument document = WotDocument.Parse(Serialize(root), m_options);
            openDocuments.Add(document);
            return document;
        }

        private static bool ValidateHostForm(JsonElement form, string hostBase)
        {
            if (form.ValueKind != JsonValueKind.Object ||
                !form.TryGetProperty("href", out JsonElement href) ||
                href.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(href.GetString()) ||
                !Uri.TryCreate(hostBase, UriKind.Absolute, out Uri? host) ||
                string.IsNullOrEmpty(host.Authority) ||
                !Uri.TryCreate(ResolveHref(hostBase, href.GetString()!), UriKind.Absolute, out Uri? endpoint) ||
                !string.Equals(host.Scheme, endpoint.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(host.IdnHost, endpoint.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                host.Port != endpoint.Port ||
                !string.IsNullOrEmpty(endpoint.UserInfo))
            {
                return false;
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in form.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    return false;
                }
            }
            if (form.TryGetProperty("additionalResponses", out JsonElement responses))
            {
                if (responses.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }
                foreach (JsonElement response in responses.EnumerateArray())
                {
                    if (response.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
