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

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Bindings
{
    public sealed partial class WotAffordanceForm
    {
        private WotBrowsePathTarget? BrowsePathTarget { get; init; }

        private string? BrowsePathError { get; init; }

        private bool HasBrowsePathCapture { get; init; }

        internal WotAffordanceForm WithBrowsePathCapture(WotDocument document)
        {
            Capture(document, JsonPointer, out WotBrowsePathTarget? target, out string? error);
            return new WotAffordanceForm(
                Kind, AffordanceName, Operations, Href, ContentType, Subprotocol, SecuritySchemes,
                JsonPointer, FormElement, AffordanceElement, TargetMapping)
            {
                PayloadSchema = PayloadSchema,
                BrowsePathTarget = target,
                BrowsePathError = error,
                HasBrowsePathCapture = true
            };
        }

        internal bool TryGetBrowsePath(
            WotBindingPlanContext context, out WotBrowsePathTarget? target, out string? error)
        {
            target = BrowsePathTarget;
            error = BrowsePathError;
            if (HasBrowsePathCapture)
            {
                return error is null;
            }
            if (FormElement.ValueKind != JsonValueKind.Object ||
                (!FormElement.TryGetProperty(WotBrowsePathTarget.PathTerm, out _) &&
                    (AffordanceElement.ValueKind != JsonValueKind.Object ||
                        !AffordanceElement.TryGetProperty(WotBrowsePathTarget.PathTerm, out _))))
            {
                return true;
            }
            string collection = Kind switch
            {
                WotAffordanceKind.Action => "actions",
                WotAffordanceKind.Event => "events",
                _ => "properties"
            };
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("@context");
                foreach (KeyValuePair<string, string> prefix in context.NamespacePrefixes)
                {
                    writer.WriteString(prefix.Key, prefix.Value);
                }
                writer.WriteEndObject();
                writer.WriteStartObject(collection);
                writer.WriteStartObject("target");
                if (AffordanceElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty property in AffordanceElement.EnumerateObject())
                    {
                        if (property.Name != "forms")
                        {
                            property.WriteTo(writer);
                        }
                    }
                }
                writer.WriteStartArray("forms");
                FormElement.WriteTo(writer);
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            using var document = WotDocument.Parse(stream.ToArray());
            Capture(document, "/" + collection + "/target/forms/0", out target, out error);
            return error is null;
        }

        private static void Capture(
            WotDocument document,
            string pointer,
            out WotBrowsePathTarget? target,
            out string? error)
        {
            try
            {
                target = WotBrowsePathTarget.FromForm(document, pointer);
                error = null;
            }
            catch (ServiceResultException exception)
            {
                target = null;
                error = exception.Message;
            }
        }
    }
}
