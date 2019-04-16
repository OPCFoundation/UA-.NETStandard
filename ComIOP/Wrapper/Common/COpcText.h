/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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

#ifndef _COpcText_H
#define _COpcText_H

#if _MSC_VER >= 1000
#pragma once
#endif // _MSC_VER >= 1000

#include "OpcDefs.h"
#include "COpcString.h"

#define OPC_EOF -1

//==============================================================================
// CLASS:   COpcText
// PURPOSE: Stores a text element extracted from a text buffer.

class OPCUTILS_API COpcText
{
    OPC_CLASS_NEW_DELETE();

public:

    //==========================================================================
    // Public Types

    enum Type
    {
        Literal,
        NonWhitespace,
        Whitespace,
        Delimited
    };

public:

    //==========================================================================
    // Operators

    // Constructor
    COpcText();  
    
    // Destructor
    ~COpcText() {}

    // Assignment
    COpcText& operator=(const COpcString& cStr) { m_cData = cStr; return *this; }

    // Cast
    operator COpcString&() { return m_cData; }
    operator const COpcString&() const { return m_cData; }

    //==========================================================================
    // Public Methods
    
    // Reset
    void Reset();

    // CopyData
    void CopyData(const WCHAR* pData, UINT uLength);

    //==========================================================================
    // Search Criteria

    // General
    // 
    // Type           - the type of token to extract.
    // MaxChars       - maximum number of characters to read before halting.
    // NoExtract      - do not extract the text from the buffer.
    // HaltChars      - a set of characters indicate the search failed. An eof is a halt unless the EofDelim is set.
    //
    // Literals       - A token that matches a string exactly.
    //
    // Text           - the string to match.("")
    // SkipLeading    - skips all chars until found or halt.(F)
    // SkipWhitespace - skips whitespace until found or halt.(T)
    // IgnoreCase     - ignores case when finding a match.(F)
    //
    // Whitespace     - A token consisting of whitespace only
    //
    // SkipLeading    - ignores leading non-whitespace chars.(F)
    // EofDelim       - treats an eof as a non-whitespace.(T)
    //
    // NonWhitespace  - A token consisting of non-whitespace chars
    //
    // SkipWhitespace - ignores leading whitespace.(T)
    // EofDelim       - treats an eof as a whitespace.(T)
    //
    // Delimited      - A token Delimited by a char or a string.
    // 
    // Delims         - a set of chars that are possible delimiters.("")
    // SkipWhitespace - ignores leading whitespace.(F)
    // IgnoreCase     - ignores case when looking for a delimiter.(F)
    // EofDelim       - treats an eof as a delimiter.(F)
    // NewLineDelim   - treats "\r\n" OR "\n" as a delimiter.(F)
    // LeaveDelim     - does not extract the delimiter char.(F)
    //
    // Enclosed       - A token enclosed by two chars.
    //
    // Bounds         - the start and end chars. Start:(") End:(")
    // SkipWhitespace - ignores leading whitespace.(F)
    // SkipLeading    - ignores all chars until start found or halt.(F)
    // AllowEscape    - ignores end char if preceded by a backslash '\'.(T)

    // Type
    COpcText::Type GetType() const { return m_eType; }
    void SetType(COpcText::Type eType);

    // HaltChars
    LPCWSTR GetHaltChars() const { return (COpcString&)m_cHaltChars; }
    void SetHaltChars(LPCWSTR szHaltChars) { m_cHaltChars = szHaltChars; }

    // MaxChars
    UINT GetMaxChars() const { return m_uMaxChars; }
    void SetMaxChars(UINT uMaxChars) { m_uMaxChars = uMaxChars; }

    // NoExtract 
    bool GetNoExtract() const { return m_bNoExtract; }
    void SetNoExtract(bool bNoExtract = true) { m_bNoExtract = bNoExtract; } 

    // Text   
    LPCWSTR GetText() const { return (COpcString&)m_cText; }
    void SetText(LPCWSTR szText) { m_cText = szText; }

    // SkipLeading
    bool GetSkipLeading() const { return m_bSkipLeading; }
    void SetSkipLeading(bool bSkipLeading = true) { m_bSkipLeading = bSkipLeading; } 

    // SkipWhitespace
    bool GetSkipWhitespace() const { return m_bSkipWhitespace; }
    void SetSkipWhitespace(bool bSkipWhitespace = true) { m_bSkipWhitespace = bSkipWhitespace; } 

    // IgnoreCase
    bool GetIgnoreCase() const { return m_bIgnoreCase; }
    void SetIgnoreCase(bool bIgnoreCase = true) { m_bIgnoreCase = bIgnoreCase; } 

    // EofDelim
    bool GetEofDelim() const { return m_bEofDelim; }
    void SetEofDelim(bool bEofDelim = true) { m_bEofDelim = bEofDelim; } 

    // NewLineDelim
    bool GetNewLineDelim() const { return m_bNewLineDelim; }
    void SetNewLineDelim(bool bNewLineDelim = true) { m_bNewLineDelim = bNewLineDelim; } 

    // Delims        
    LPCWSTR GetDelims() const { return (COpcString&)m_cDelims; }
    void SetDelims(LPCWSTR szDelims) { m_cDelims = szDelims; }

    // LeaveDelim
    bool GetLeaveDelim() const { return m_bLeaveDelim; }
    void SetLeaveDelim(bool bLeaveDelim = true) { m_bLeaveDelim = bLeaveDelim; } 

    // Bounds    
    void GetBounds(WCHAR& zStart, WCHAR& zEnd) const { zStart = m_zStart; zEnd = m_zEnd; }
    void SetBounds(WCHAR zStart, WCHAR zEnd = 0) { m_zStart = zStart; m_zEnd = (zEnd==0)?zStart:zEnd; }

    // AllowEscape
    bool GetAllowEscape() const { return m_bAllowEscape; }
    void SetAllowEscape(bool bAllowEscape = true) { m_bAllowEscape = bAllowEscape; }

    //==========================================================================
    // Search Results
    
    // Start     - position in buffer of start of token.
    // End       - position in buffer of end of token.
    // DelimChar - the delim char found. 0 = not found.
    // Eof       - indicates that an eof was encountered.
    // HaltChar  - the char that caused a halt. 0 = no halt.
    // HaltPos   - the position in buffer of halt character.
    // NewLine   - indicates that an "\r\n" OR "\n" was encountered.

    // Start
    UINT GetStart() const { return m_uStart; }
    void SetStart(UINT uStart) { m_uStart = uStart; }

    // End
    UINT GetEnd() const { return m_uEnd; }
    void SetEnd(UINT uEnd) { m_uEnd = uEnd; }

    // HaltChar
    WCHAR GetHaltChar() const { return m_zHaltChar; }
    void SetHaltChar(WCHAR zHaltChar) { m_zHaltChar = zHaltChar; }

    // HaltPos
    UINT GetHaltPos() const { return m_uHaltPos; }
    void SetHaltPos(UINT uHaltPos) { m_uHaltPos = uHaltPos; }

    // DelimChar
    WCHAR GetDelimChar() const { return m_zDelimChar; }
    void SetDelimChar(WCHAR zDelimChar) { m_zDelimChar = zDelimChar; }

    // Eof
    bool GetEof() const { return m_bEof; }
    void SetEof(bool bEof = true) { m_bEof = bEof; } 

    // NewLine
    bool GetNewLine() const { return m_bNewLine; }
    void SetNewLine(bool bNewLine = true) { m_bNewLine = bNewLine; } 

private:

    //==========================================================================
    // Private Members

    // Data
    COpcString      m_cData;

    // Search Criteria
    COpcText::Type  m_eType;
    COpcString      m_cHaltChars;
    UINT            m_uMaxChars;
    bool            m_bNoExtract;
    COpcString      m_cText;
    bool            m_bSkipLeading;
    bool            m_bSkipWhitespace;
    bool            m_bIgnoreCase;
    bool            m_bEofDelim;
    bool            m_bNewLineDelim;
    COpcString      m_cDelims;
    bool            m_bLeaveDelim;
    WCHAR           m_zStart;
    WCHAR           m_zEnd;
    bool            m_bAllowEscape;

    // Search Results
    UINT            m_uStart;
    UINT            m_uEnd;
    WCHAR           m_zHaltChar;
    UINT            m_uHaltPos;
    WCHAR           m_zDelimChar;
    bool            m_bEof;
    bool            m_bNewLine;
};

#endif //ndef _COpcText_H
