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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.FileSystem;

namespace Opc.Ua.AutoID.Server
{
    /// <summary>
    /// A named, read-only image file published below an OCR or optical reader's Images folder.
    /// </summary>
    public sealed record AutoIdImageSlot
    {
        /// <summary>
        /// Creates an image slot; a null initial value is unavailable until the provider reports content.
        /// </summary>
        public AutoIdImageSlot(
            QualifiedName name, string mimeType, ByteString content = default, int maximumBytes = 16 * 1024 * 1024)
        {
            if (name.IsNull || string.IsNullOrEmpty(name.Name))
            {
                throw new ArgumentException("An image name is required.", nameof(name));
            }
            if (string.IsNullOrWhiteSpace(mimeType))
            {
                throw new ArgumentException("An image MIME type is required.", nameof(mimeType));
            }
            if (maximumBytes <= 0 || maximumBytes > 16 * 1024 * 1024 || content.Length > maximumBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumBytes));
            }
            Name = name;
            MimeType = mimeType;
            Content = content.IsNull ? default : ByteString.From(content.Span);
            MaximumBytes = maximumBytes;
        }

        /// <summary>
        /// Gets the file's browse name, including its namespace.
        /// </summary>
        public QualifiedName Name { get; }

        /// <summary>
        /// Gets the format of the image bytes.
        /// </summary>
        public string MimeType { get; }

        /// <summary>
        /// Gets the initial image snapshot.
        /// </summary>
        public ByteString Content { get; }

        /// <summary>
        /// Gets the maximum size accepted for this slot.
        /// </summary>
        public int MaximumBytes { get; }
    }

    /// <summary>
    /// Optional provider sink capability for reporting images without changing the original sink contract.
    /// </summary>
    public interface IAutoIdImageSink
    {
        /// <summary>
        /// Replaces a configured image for subsequent FileType opens; existing handles retain their snapshots.
        /// </summary>
        ValueTask ReportImageAsync(
            QualifiedName name, ByteString content, CancellationToken cancellationToken = default);
    }

    public sealed partial class AutoIdReader : IAutoIdImageSink
    {
        /// <inheritdoc/>
        public async ValueTask ReportImageAsync(
            QualifiedName name, ByteString content, CancellationToken cancellationToken = default)
        {
            if (!m_operations.TryEnter())
            {
                throw new ObjectDisposedException(nameof(AutoIdReader));
            }
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_images.TryGetValue(name, out var image))
                {
                    throw ServiceResultException.Create(StatusCodes.BadNotFound, "The image slot is not configured.");
                }
                if (content.IsNull)
                {
                    throw new ArgumentException("Image content must not be null.", nameof(content));
                }
                if (content.Length > image.MaximumBytes)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadEncodingLimitsExceeded, "The image slot size limit was exceeded.");
                }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, m_stopping.Token);
                await image.Binding.UpdateContentAsync(ByteString.From(content.Span), linked.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <summary>
        /// Creates the declared FileType nodes before registration, using standard Organizes references.
        /// </summary>
        internal static void ConfigureImages(
            ISystemContext context, AutoIdDeviceState device, ArrayOf<AutoIdImageSlot> slots)
        {
            if (slots.Count == 0)
            {
                return;
            }
            FolderState images;
            if (device is OcrReaderDeviceState ocr)
            {
                ocr.AddImages(context);
                images = ocr.Images!;
            }
            else if (device is OpticalReaderDeviceState optical)
            {
                optical.AddImages(context);
                images = optical.Images!;
            }
            else
            {
                throw new InvalidOperationException("Only OCR and optical readers publish Images.");
            }
            for (int i = 0; i < slots.Count; i++)
            {
                AutoIdImageSlot slot = slots[i];
                FileState file = context.CreateInstanceOfFileType(images, slot.Name);
                file.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.Organizes;
                file.CreateOrReplaceMimeType(context, null).Value = slot.MimeType;
                file.CreateOrReplaceMaxByteStringLength(context, null);
                images.AddChild(file);
            }
        }

        /// <summary>
        /// Binds every configured file before the provider receives its sink.
        /// </summary>
        internal void BindImages(ArrayOf<AutoIdImageSlot> slots)
        {
            NodeState? images = Device switch
            {
                OcrReaderDeviceState ocr => ocr.Images,
                OpticalReaderDeviceState optical => optical.Images,
                _ => null
            };
            for (int i = 0; i < slots.Count; i++)
            {
                AutoIdImageSlot slot = slots[i];
                var file = (FileState)images!.FindChild(Runtime.SystemContext, slot.Name)!;
                m_images.Add(slot.Name, (Runtime.FileBinder.Bind(
                    file, Runtime.SystemContext,
                    slot.Content.IsNull ? default : ByteString.From(slot.Content.Span),
                    maximumBytes: slot.MaximumBytes), slot.MaximumBytes));
            }
        }

        /// <summary>
        /// The fixed image catalogue and reader-owned session bindings.
        /// </summary>
        private readonly Dictionary<QualifiedName, (IFileTypeBinding Binding, int MaximumBytes)> m_images = [];
    }
}
