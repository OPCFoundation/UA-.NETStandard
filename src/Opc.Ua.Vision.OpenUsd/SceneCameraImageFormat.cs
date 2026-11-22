// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Vision.OpenUsd
{
    /// <summary>
    /// Encoded image format for a <see cref="SceneCameraCaptureResult"/>.
    /// PNG is the only format that ships with the current in-repo encoder;
    /// additional formats require additional encoders and are called out at
    /// the enum level so callers can pattern-match without hitting the wire.
    /// </summary>
    public enum SceneCameraImageFormat
    {
        /// <summary>
        /// RFC 2083 PNG, 8-bit RGBA. Encoded by the in-repo dependency-free
        /// encoder; always available.
        /// </summary>
        Png = 0
    }
}
