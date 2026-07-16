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

#nullable enable

using System;

namespace Opc.Ua.PubSub.Tests
{
    /// <summary>
    /// Links a test method, fixture, or assembly to the OPC UA specification
    /// clause it validates. The attribute is purely declarative; it is read
    /// by the spec-coverage reporter to emit a clause-to-test traceability
    /// matrix, but has no effect on test discovery or execution.
    /// </summary>
    /// <remarks>
    /// Use one attribute per logical clause. A single test may carry multiple
    /// attributes when it exercises overlapping clauses. The <see cref="Part"/>
    /// defaults to 14 (PubSub) because that is the primary specification the
    /// PubSub test assemblies cover; pass a different value for cross-spec tests.
    /// </remarks>
    [AttributeUsage(
        AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly,
        AllowMultiple = true,
        Inherited = false)]
    public sealed class TestSpecAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestSpecAttribute"/>
        /// class for the given specification clause.
        /// </summary>
        /// <param name="clause">
        /// Clause reference within the part, in dotted notation as printed
        /// in the spec. Must be non-empty.
        /// </param>
        public TestSpecAttribute(string clause)
        {
            if (clause is null)
            {
                throw new ArgumentNullException(nameof(clause));
            }

            if (clause.Length == 0)
            {
                throw new ArgumentException("Value cannot be empty.", nameof(clause));
            }

            Clause = clause;
        }

        /// <summary>
        /// OPC UA specification part number. Defaults to 14 (PubSub).
        /// </summary>
        public int Part { get; init; } = 14;

        /// <summary>
        /// Specification version string used to disambiguate when a clause
        /// reference has changed across versions. Optional.
        /// </summary>
        public string? Version { get; init; }

        /// <summary>
        /// Clause reference within the part (dotted notation as in the spec).
        /// </summary>
        public string Clause { get; }

        /// <summary>
        /// Optional one-line summary of what this test validates.
        /// </summary>
        public string? Summary { get; init; }
    }
}
