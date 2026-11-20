/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AutoID.Server.Builders
{
    /// <summary>
    /// Optional builder capability for the image files of OCR and optical readers.
    /// </summary>
    public interface IAutoIdImagesBuilder : IAutoIdReaderBuilder
    {
        /// <summary>
        /// Declares fixed, named image slots; the provider publishes content through IAutoIdImageSink.
        /// </summary>
        IAutoIdReaderBuilder WithImages(ArrayOf<AutoIdImageSlot> images);
    }
}
