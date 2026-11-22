// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.SourceGeneration
{
    internal interface ITemplateWriter
    {
        /// <summary>
        /// Writes the text to the stream.
        /// </summary>
        void Write(char text);

        /// <summary>
        /// Writes the text to the stream.
        /// </summary>
        void Write(string text);

        /// <summary>
        /// Formats and writes the text to the stream.
        /// </summary>
        void Write(string format, object arg1);

        /// <summary>
        /// Formats and writes the text to the stream.
        /// </summary>
        void Write(string format, object arg1, object arg2);

        /// <summary>
        /// Formats and writes the text to the stream.
        /// </summary>
        void Write(string format, object arg1, object arg2, object arg3);

        /// <summary>
        /// Writes a new line
        /// </summary>
        void WriteLine();

        /// <summary>
        /// Writes the text to the stream followed by a new line.
        /// </summary>
        void WriteLine(string text);

        /// <summary>
        /// Formats and writes the text to the stream followed
        /// by a new line.
        /// </summary>
        void WriteLine(string text, params object?[] args);
    }
}
