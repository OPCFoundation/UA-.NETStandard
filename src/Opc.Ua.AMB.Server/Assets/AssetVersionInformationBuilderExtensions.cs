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

namespace Opc.Ua.AMB.Server.Assets
{
    /// <summary>
    /// Adds optional vendor patch identifiers to version information.
    /// </summary>
    public static class AssetVersionInformationBuilderExtensions
    {
        /// <summary>
        /// Gives the asset its version information and, when there is no
        /// <c>SoftwareUpdate</c> AddIn, the vendor-supplied
        /// <c>PatchIdentifiers</c> list (OPC 10000-110 §10.2).
        /// </summary>
        /// <remarks>
        /// Patch identifiers are not inferred or synthesized. The application
        /// supplies the vendor's list, including an empty list when that is
        /// the vendor data. The DI Property is created only when the asset
        /// has a <c>SoftwareRevision</c>.
        /// </remarks>
        /// <param name="builder">The asset builder.</param>
        /// <param name="hardwareRevision">The hardware revision, or null to leave it.</param>
        /// <param name="softwareRevision">The software revision, or null to leave it.</param>
        /// <param name="revisionCounter">
        /// The revision counter, or null to keep an existing one; a created one
        /// starts at 0.
        /// </param>
        /// <param name="patchIdentifiers">
        /// The vendor-supplied patch identifiers to publish as the read-only
        /// DI <c>2:PatchIdentifiers</c> array when the asset has a software
        /// revision and no <c>SoftwareUpdate</c> AddIn.
        /// </param>
        /// <returns>The same builder, for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
        /// <exception cref="NotSupportedException">
        /// The builder does not implement <see cref="IAssetVersionInformationBuilder"/>.
        /// </exception>
        public static IAssetBuilder WithVersionInformation(
            this IAssetBuilder builder,
            string? hardwareRevision,
            string? softwareRevision,
            int? revisionCounter,
            ArrayOf<string> patchIdentifiers)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
            if (builder is not IAssetVersionInformationBuilder versionInformationBuilder)
            {
                throw new NotSupportedException(
                    "This asset builder does not support vendor-supplied PatchIdentifiers.");
            }
            return versionInformationBuilder.WithVersionInformation(
                hardwareRevision,
                softwareRevision,
                revisionCounter,
                patchIdentifiers);
        }
    }
}
