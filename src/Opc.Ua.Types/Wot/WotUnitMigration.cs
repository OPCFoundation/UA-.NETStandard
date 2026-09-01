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
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The revision 1.0 compatibility rule of WoT Binding Section 6.4: a
    /// quantity kind written into a DataSchema <c>unit</c> is recognized,
    /// preserved, and - where the caller asks for it - moved to
    /// <c>qudt:hasQuantityKind</c>, which is where the fact belongs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A quantity kind says <em>what physical quantity is measured</em>; a unit
    /// says <em>what the measurement is counted in</em>. Neither is recoverable
    /// from the other - <c>AngularVelocity</c> is measured in <c>rpm</c> and in
    /// <c>rad/s</c> alike - so this never invents an engineering unit to put in
    /// a vacated <c>unit</c> member. Revision 1.0 drew no line here and
    /// documents exist that state a quantity kind as a unit, so a consumer
    /// preserves the authored fact rather than rejecting the document; moving
    /// it is an explicit, opt-in migration and not something a reader does
    /// behind the caller's back.
    /// </para>
    /// <para>
    /// The move is refused where the affordance already states a
    /// <em>different</em> <c>qudt:hasQuantityKind</c>: two quantity kinds are
    /// two facts, and choosing between them is the author's decision and not a
    /// converter's.
    /// </para>
    /// </remarks>
    public static class WotUnitMigration
    {
        /// <summary>
        /// The QUDT term a quantity kind belongs in (WoT Binding Section 6.4).
        /// This Binding deliberately mints no <c>uav</c> term for the concept:
        /// QUDT already defines it, and a second spelling would let the two
        /// disagree.
        /// </summary>
        public const string QuantityKindTerm = "qudt:hasQuantityKind";

        /// <summary>
        /// The compact-IRI prefix conventionally bound to the QUDT
        /// quantity-kind namespace.
        /// </summary>
        public const string QuantityKindPrefix = "qudt-quantitykind";

        /// <summary>
        /// The QUDT quantity-kind namespace.
        /// </summary>
        public const string QuantityKindNamespace = "http://qudt.org/vocab/quantitykind/";

        /// <summary>
        /// Determines whether a <c>unit</c> value names a QUDT quantity kind
        /// rather than an engineering unit (WoT Binding Section 6.4).
        /// </summary>
        /// <remarks>
        /// Both spellings the clause names are recognized: the absolute IRI in
        /// the QUDT quantity-kind namespace, and a compact IRI whose prefix the
        /// document binds to it. The conventional
        /// <c>qudt-quantitykind:</c> prefix is recognized even where the
        /// document binds nothing, because an unbound prefix is a document
        /// defect and not a reason to miss the finding.
        /// </remarks>
        /// <param name="unit">The authored <c>unit</c> value.</param>
        /// <param name="resolvePrefix">
        /// Resolves a compact-IRI prefix to the namespace the document's
        /// <c>@context</c> binds it to, or <c>null</c> where it binds none.
        /// May itself be <c>null</c>, in which case only the absolute IRI and
        /// the conventional prefix are recognized.
        /// </param>
        /// <returns><c>true</c> when the value names a quantity kind.</returns>
        public static bool IsQuantityKind(string? unit, Func<string, string?>? resolvePrefix = null)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return false;
            }
            if (IsQuantityKindNamespace(unit!))
            {
                return true;
            }
            int separator = unit!.IndexOf(':', 0);
            if (separator <= 0 ||
                separator + 1 >= unit.Length ||
                unit.Contains("//", StringComparison.Ordinal))
            {
                return false;
            }
            string prefix = unit.Substring(0, separator);
            if (string.Equals(prefix, QuantityKindPrefix, StringComparison.Ordinal))
            {
                return true;
            }
            string? bound = resolvePrefix?.Invoke(prefix);
            return bound is not null && IsQuantityKindNamespace(bound);
        }

        /// <summary>
        /// Moves every deprecated quantity kind out of a <c>unit</c> member and
        /// into <c>qudt:hasQuantityKind</c>, leaving the vacated <c>unit</c>
        /// absent rather than invented (WoT Binding Section 6.4).
        /// </summary>
        /// <param name="document">The document to migrate.</param>
        /// <returns>
        /// The result: the migrated UTF-8 bytes when anything moved, the
        /// pointers of the members that moved, and the pointers of the members
        /// that were left alone because the affordance already states a
        /// different quantity kind.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static WotUnitMigrationResult MoveQuantityKinds(WotDocument document)
        {
            if (document is null)
            {
                throw new ArgumentNullException(nameof(document));
            }
            var moved = new List<string>();
            var conflicts = new List<string>();
            JsonNode? root = JsonNode.Parse(
                document.RootElement.GetRawText(),
                nodeOptions: null,
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Disallow
                });
            if (root is null)
            {
                return new WotUnitMigrationResult(null, ArrayOf<string>.Empty, ArrayOf<string>.Empty);
            }
            Migrate(
                root,
                string.Empty,
                prefix => document.TryGetContextPrefix(prefix, out string ns) ? ns : null,
                moved,
                conflicts);
            byte[]? migrated = moved.Count == 0
                ? null
                : Encoding.UTF8.GetBytes(root.ToJsonString(
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return new WotUnitMigrationResult(
                migrated, moved.ToArray(), conflicts.ToArray());
        }

        private static void Migrate(
            JsonNode node,
            string pointer,
            Func<string, string?> resolvePrefix,
            List<string> moved,
            List<string> conflicts)
        {
            if (node is JsonArray array)
            {
                for (int ii = 0; ii < array.Count; ii++)
                {
                    if (array[ii] is { } item)
                    {
                        Migrate(
                            item,
                            pointer + "/" + ii.ToString(CultureInfo.InvariantCulture),
                            resolvePrefix,
                            moved,
                            conflicts);
                    }
                }
                return;
            }
            if (node is not JsonObject obj)
            {
                return;
            }
            MigrateUnit(obj, pointer, resolvePrefix, moved, conflicts);
            foreach (KeyValuePair<string, JsonNode?> member in obj)
            {
                if (member.Value is { } child)
                {
                    Migrate(
                        child,
                        pointer + "/" + EscapeToken(member.Key),
                        resolvePrefix,
                        moved,
                        conflicts);
                }
            }
        }

        private static void MigrateUnit(
            JsonObject obj,
            string pointer,
            Func<string, string?> resolvePrefix,
            List<string> moved,
            List<string> conflicts)
        {
            if (!obj.TryGetPropertyValue(UnitMember, out JsonNode? unitNode) ||
                unitNode is not JsonValue unitValue ||
                !unitValue.TryGetValue(out string? unit) ||
                !IsQuantityKind(unit, resolvePrefix))
            {
                return;
            }
            string unitPointer = pointer + "/" + UnitMember;
            if (obj.TryGetPropertyValue(QuantityKindTerm, out JsonNode? existing) &&
                existing is not null)
            {
                if (existing is not JsonValue existingValue ||
                    !existingValue.TryGetValue(out string? stated) ||
                    !string.Equals(stated, unit, StringComparison.Ordinal))
                {
                    // Two quantity kinds are two facts. Choosing between them is
                    // the author's decision, so the authored unit stays where it
                    // was written and the caller is told about it.
                    conflicts.Add(unitPointer);
                    return;
                }
            }
            obj.Remove(UnitMember);
            obj[QuantityKindTerm] = JsonValue.Create(unit);
            moved.Add(unitPointer);
        }

        private static bool IsQuantityKindNamespace(string iri)
        {
            return iri.StartsWith(QuantityKindNamespace, StringComparison.Ordinal) ||
                iri.StartsWith("https://qudt.org/vocab/quantitykind/", StringComparison.Ordinal);
        }

        private static string EscapeToken(string token)
        {
            return token
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);
        }

        private const string UnitMember = "unit";
    }

    /// <summary>
    /// The outcome of a <see cref="WotUnitMigration.MoveQuantityKinds"/> run.
    /// </summary>
    public sealed class WotUnitMigrationResult
    {
        /// <summary>
        /// Initializes a new migration result.
        /// </summary>
        /// <param name="document">
        /// The migrated UTF-8 document bytes, or <c>null</c> when nothing
        /// moved.
        /// </param>
        /// <param name="movedPointers">
        /// The RFC 6901 pointers of the <c>unit</c> members that moved.
        /// </param>
        /// <param name="conflictPointers">
        /// The RFC 6901 pointers of the <c>unit</c> members left in place
        /// because the affordance already states a different quantity kind.
        /// </param>
        public WotUnitMigrationResult(
            byte[]? document,
            ArrayOf<string> movedPointers,
            ArrayOf<string> conflictPointers)
        {
            Document = document;
            MovedPointers = movedPointers.IsNull ? ArrayOf<string>.Empty : movedPointers;
            ConflictPointers = conflictPointers.IsNull
                ? ArrayOf<string>.Empty
                : conflictPointers;
        }

        /// <summary>
        /// Gets whether anything moved.
        /// </summary>
        public bool Changed => Document is not null;

        /// <summary>
        /// Gets the migrated UTF-8 document bytes, or <c>null</c> when the
        /// document stated no quantity kind in a <c>unit</c> member.
        /// </summary>
        public byte[]? Document { get; }

        /// <summary>
        /// Gets the pointers of the <c>unit</c> members that moved to
        /// <c>qudt:hasQuantityKind</c>.
        /// </summary>
        public ArrayOf<string> MovedPointers { get; }

        /// <summary>
        /// Gets the pointers of the <c>unit</c> members that were left in place
        /// because the affordance already states a different quantity kind.
        /// </summary>
        public ArrayOf<string> ConflictPointers { get; }
    }
}
