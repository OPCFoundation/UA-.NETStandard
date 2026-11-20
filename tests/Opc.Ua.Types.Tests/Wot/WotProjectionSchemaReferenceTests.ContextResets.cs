/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Text.Json.Nodes;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Contains tests for WoT projection schema reference.
    /// </summary>
    public sealed partial class WotProjectionSchemaReferenceTests
    {
        /// <summary>
        /// Verifies that null context reset does not restore implicit prefixes.
        /// </summary>
        /// <param name="scoped">The scoped flag for this test case.</param>
        [Test]
        public async Task NullContextResetDoesNotRestoreImplicitPrefixes(
            [Values("ua", "uav")] string prefix,
            [Values("implicit", "null-term", "reset", "restored")] string mode,
            [Values] bool scoped)
        {
            string namespaceUri = prefix == "ua"
                ? "http://opcfoundation.org/UA/"
                : "http://opcfoundation.org/UA/WoT-Binding/";
            JsonObject plan = Plan();
            JsonArray context = DefinitionTimeContexts();
            if (mode == "null-term")
            {
                context.Add(new JsonObject { [prefix] = null });
            }
            else if (mode is "reset" or "restored")
            {
                context.Add(null);
                if (mode == "restored")
                {
                    context.Add(new JsonObject { [prefix] = namespaceUri });
                }
            }
            plan["@context"] = scoped ? DefinitionTimeContexts(PropertiesScope(context)) : context;
            plan["properties"]!["reading"]!["uav:semanticId"] = prefix + ":Role";

            WotConversionResult<WotDocument> result = await ResolveAsync(plan, ContextReviewSource())
                .ConfigureAwait(false);
            using WotDocument? view = result.Value;

            if (mode is "null-term" or "reset")
            {
                AssertContextRejected(result);
                return;
            }
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            Assert.That(view.Required().Properties["reading"].GetProperty("uav:semanticId").GetString(),
                Is.EqualTo(prefix == "ua"
                    ? "http://opcfoundation.org/UA/Role"
                    : "http://opcfoundation.org/UA/WoT-Binding/Role"));
        }
    }
}
