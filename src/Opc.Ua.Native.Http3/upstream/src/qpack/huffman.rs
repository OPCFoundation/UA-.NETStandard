//! QPACK ハフマン符号化 (RFC 7541 Appendix B)
//!
//! HPACK/QPACK で使用されるハフマン符号化/復号化を提供。

use crate::error::QpackError;

/// ハフマンシンボル (符号語と長さ)
#[derive(Clone, Copy)]
struct HuffmanSym {
    /// ビット長
    bits: u8,
    /// 符号語 (左詰め)
    code: u32,
}

/// ハフマン符号テーブル (RFC 7541 Appendix B)
static HUFFMAN_TABLE: [HuffmanSym; 257] = [
    HuffmanSym {
        bits: 13,
        code: 0xffc00000,
    }, // 0
    HuffmanSym {
        bits: 23,
        code: 0xffffb000,
    }, // 1
    HuffmanSym {
        bits: 28,
        code: 0xfffffe20,
    }, // 2
    HuffmanSym {
        bits: 28,
        code: 0xfffffe30,
    }, // 3
    HuffmanSym {
        bits: 28,
        code: 0xfffffe40,
    }, // 4
    HuffmanSym {
        bits: 28,
        code: 0xfffffe50,
    }, // 5
    HuffmanSym {
        bits: 28,
        code: 0xfffffe60,
    }, // 6
    HuffmanSym {
        bits: 28,
        code: 0xfffffe70,
    }, // 7
    HuffmanSym {
        bits: 28,
        code: 0xfffffe80,
    }, // 8
    HuffmanSym {
        bits: 24,
        code: 0xffffea00,
    }, // 9
    HuffmanSym {
        bits: 30,
        code: 0xfffffff0,
    }, // 10
    HuffmanSym {
        bits: 28,
        code: 0xfffffe90,
    }, // 11
    HuffmanSym {
        bits: 28,
        code: 0xfffffea0,
    }, // 12
    HuffmanSym {
        bits: 30,
        code: 0xfffffff4,
    }, // 13
    HuffmanSym {
        bits: 28,
        code: 0xfffffeb0,
    }, // 14
    HuffmanSym {
        bits: 28,
        code: 0xfffffec0,
    }, // 15
    HuffmanSym {
        bits: 28,
        code: 0xfffffed0,
    }, // 16
    HuffmanSym {
        bits: 28,
        code: 0xfffffee0,
    }, // 17
    HuffmanSym {
        bits: 28,
        code: 0xfffffef0,
    }, // 18
    HuffmanSym {
        bits: 28,
        code: 0xffffff00,
    }, // 19
    HuffmanSym {
        bits: 28,
        code: 0xffffff10,
    }, // 20
    HuffmanSym {
        bits: 28,
        code: 0xffffff20,
    }, // 21
    HuffmanSym {
        bits: 30,
        code: 0xfffffff8,
    }, // 22
    HuffmanSym {
        bits: 28,
        code: 0xffffff30,
    }, // 23
    HuffmanSym {
        bits: 28,
        code: 0xffffff40,
    }, // 24
    HuffmanSym {
        bits: 28,
        code: 0xffffff50,
    }, // 25
    HuffmanSym {
        bits: 28,
        code: 0xffffff60,
    }, // 26
    HuffmanSym {
        bits: 28,
        code: 0xffffff70,
    }, // 27
    HuffmanSym {
        bits: 28,
        code: 0xffffff80,
    }, // 28
    HuffmanSym {
        bits: 28,
        code: 0xffffff90,
    }, // 29
    HuffmanSym {
        bits: 28,
        code: 0xffffffa0,
    }, // 30
    HuffmanSym {
        bits: 28,
        code: 0xffffffb0,
    }, // 31
    HuffmanSym {
        bits: 6,
        code: 0x50000000,
    }, // 32 ' '
    HuffmanSym {
        bits: 10,
        code: 0xfe000000,
    }, // 33 '!'
    HuffmanSym {
        bits: 10,
        code: 0xfe400000,
    }, // 34 '"'
    HuffmanSym {
        bits: 12,
        code: 0xffa00000,
    }, // 35 '#'
    HuffmanSym {
        bits: 13,
        code: 0xffc80000,
    }, // 36 '$'
    HuffmanSym {
        bits: 6,
        code: 0x54000000,
    }, // 37 '%'
    HuffmanSym {
        bits: 8,
        code: 0xf8000000,
    }, // 38 '&'
    HuffmanSym {
        bits: 11,
        code: 0xff400000,
    }, // 39 '\''
    HuffmanSym {
        bits: 10,
        code: 0xfe800000,
    }, // 40 '('
    HuffmanSym {
        bits: 10,
        code: 0xfec00000,
    }, // 41 ')'
    HuffmanSym {
        bits: 8,
        code: 0xf9000000,
    }, // 42 '*'
    HuffmanSym {
        bits: 11,
        code: 0xff600000,
    }, // 43 '+'
    HuffmanSym {
        bits: 8,
        code: 0xfa000000,
    }, // 44 ','
    HuffmanSym {
        bits: 6,
        code: 0x58000000,
    }, // 45 '-'
    HuffmanSym {
        bits: 6,
        code: 0x5c000000,
    }, // 46 '.'
    HuffmanSym {
        bits: 6,
        code: 0x60000000,
    }, // 47 '/'
    HuffmanSym {
        bits: 5,
        code: 0x00000000,
    }, // 48 '0'
    HuffmanSym {
        bits: 5,
        code: 0x08000000,
    }, // 49 '1'
    HuffmanSym {
        bits: 5,
        code: 0x10000000,
    }, // 50 '2'
    HuffmanSym {
        bits: 6,
        code: 0x64000000,
    }, // 51 '3'
    HuffmanSym {
        bits: 6,
        code: 0x68000000,
    }, // 52 '4'
    HuffmanSym {
        bits: 6,
        code: 0x6c000000,
    }, // 53 '5'
    HuffmanSym {
        bits: 6,
        code: 0x70000000,
    }, // 54 '6'
    HuffmanSym {
        bits: 6,
        code: 0x74000000,
    }, // 55 '7'
    HuffmanSym {
        bits: 6,
        code: 0x78000000,
    }, // 56 '8'
    HuffmanSym {
        bits: 6,
        code: 0x7c000000,
    }, // 57 '9'
    HuffmanSym {
        bits: 7,
        code: 0xb8000000,
    }, // 58 ':'
    HuffmanSym {
        bits: 8,
        code: 0xfb000000,
    }, // 59 ';'
    HuffmanSym {
        bits: 15,
        code: 0xfff80000,
    }, // 60 '<'
    HuffmanSym {
        bits: 6,
        code: 0x80000000,
    }, // 61 '='
    HuffmanSym {
        bits: 12,
        code: 0xffb00000,
    }, // 62 '>'
    HuffmanSym {
        bits: 10,
        code: 0xff000000,
    }, // 63 '?'
    HuffmanSym {
        bits: 13,
        code: 0xffd00000,
    }, // 64 '@'
    HuffmanSym {
        bits: 6,
        code: 0x84000000,
    }, // 65 'A'
    HuffmanSym {
        bits: 7,
        code: 0xba000000,
    }, // 66 'B'
    HuffmanSym {
        bits: 7,
        code: 0xbc000000,
    }, // 67 'C'
    HuffmanSym {
        bits: 7,
        code: 0xbe000000,
    }, // 68 'D'
    HuffmanSym {
        bits: 7,
        code: 0xc0000000,
    }, // 69 'E'
    HuffmanSym {
        bits: 7,
        code: 0xc2000000,
    }, // 70 'F'
    HuffmanSym {
        bits: 7,
        code: 0xc4000000,
    }, // 71 'G'
    HuffmanSym {
        bits: 7,
        code: 0xc6000000,
    }, // 72 'H'
    HuffmanSym {
        bits: 7,
        code: 0xc8000000,
    }, // 73 'I'
    HuffmanSym {
        bits: 7,
        code: 0xca000000,
    }, // 74 'J'
    HuffmanSym {
        bits: 7,
        code: 0xcc000000,
    }, // 75 'K'
    HuffmanSym {
        bits: 7,
        code: 0xce000000,
    }, // 76 'L'
    HuffmanSym {
        bits: 7,
        code: 0xd0000000,
    }, // 77 'M'
    HuffmanSym {
        bits: 7,
        code: 0xd2000000,
    }, // 78 'N'
    HuffmanSym {
        bits: 7,
        code: 0xd4000000,
    }, // 79 'O'
    HuffmanSym {
        bits: 7,
        code: 0xd6000000,
    }, // 80 'P'
    HuffmanSym {
        bits: 7,
        code: 0xd8000000,
    }, // 81 'Q'
    HuffmanSym {
        bits: 7,
        code: 0xda000000,
    }, // 82 'R'
    HuffmanSym {
        bits: 7,
        code: 0xdc000000,
    }, // 83 'S'
    HuffmanSym {
        bits: 7,
        code: 0xde000000,
    }, // 84 'T'
    HuffmanSym {
        bits: 7,
        code: 0xe0000000,
    }, // 85 'U'
    HuffmanSym {
        bits: 7,
        code: 0xe2000000,
    }, // 86 'V'
    HuffmanSym {
        bits: 7,
        code: 0xe4000000,
    }, // 87 'W'
    HuffmanSym {
        bits: 8,
        code: 0xfc000000,
    }, // 88 'X'
    HuffmanSym {
        bits: 7,
        code: 0xe6000000,
    }, // 89 'Y'
    HuffmanSym {
        bits: 8,
        code: 0xfd000000,
    }, // 90 'Z'
    HuffmanSym {
        bits: 13,
        code: 0xffd80000,
    }, // 91 '['
    HuffmanSym {
        bits: 19,
        code: 0xfffe0000,
    }, // 92 '\\'
    HuffmanSym {
        bits: 13,
        code: 0xffe00000,
    }, // 93 ']'
    HuffmanSym {
        bits: 14,
        code: 0xfff00000,
    }, // 94 '^'
    HuffmanSym {
        bits: 6,
        code: 0x88000000,
    }, // 95 '_'
    HuffmanSym {
        bits: 15,
        code: 0xfffa0000,
    }, // 96 '`'
    HuffmanSym {
        bits: 5,
        code: 0x18000000,
    }, // 97 'a'
    HuffmanSym {
        bits: 6,
        code: 0x8c000000,
    }, // 98 'b'
    HuffmanSym {
        bits: 5,
        code: 0x20000000,
    }, // 99 'c'
    HuffmanSym {
        bits: 6,
        code: 0x90000000,
    }, // 100 'd'
    HuffmanSym {
        bits: 5,
        code: 0x28000000,
    }, // 101 'e'
    HuffmanSym {
        bits: 6,
        code: 0x94000000,
    }, // 102 'f'
    HuffmanSym {
        bits: 6,
        code: 0x98000000,
    }, // 103 'g'
    HuffmanSym {
        bits: 6,
        code: 0x9c000000,
    }, // 104 'h'
    HuffmanSym {
        bits: 5,
        code: 0x30000000,
    }, // 105 'i'
    HuffmanSym {
        bits: 7,
        code: 0xe8000000,
    }, // 106 'j'
    HuffmanSym {
        bits: 7,
        code: 0xea000000,
    }, // 107 'k'
    HuffmanSym {
        bits: 6,
        code: 0xa0000000,
    }, // 108 'l'
    HuffmanSym {
        bits: 6,
        code: 0xa4000000,
    }, // 109 'm'
    HuffmanSym {
        bits: 6,
        code: 0xa8000000,
    }, // 110 'n'
    HuffmanSym {
        bits: 5,
        code: 0x38000000,
    }, // 111 'o'
    HuffmanSym {
        bits: 6,
        code: 0xac000000,
    }, // 112 'p'
    HuffmanSym {
        bits: 7,
        code: 0xec000000,
    }, // 113 'q'
    HuffmanSym {
        bits: 6,
        code: 0xb0000000,
    }, // 114 'r'
    HuffmanSym {
        bits: 5,
        code: 0x40000000,
    }, // 115 's'
    HuffmanSym {
        bits: 5,
        code: 0x48000000,
    }, // 116 't'
    HuffmanSym {
        bits: 6,
        code: 0xb4000000,
    }, // 117 'u'
    HuffmanSym {
        bits: 7,
        code: 0xee000000,
    }, // 118 'v'
    HuffmanSym {
        bits: 7,
        code: 0xf0000000,
    }, // 119 'w'
    HuffmanSym {
        bits: 7,
        code: 0xf2000000,
    }, // 120 'x'
    HuffmanSym {
        bits: 7,
        code: 0xf4000000,
    }, // 121 'y'
    HuffmanSym {
        bits: 7,
        code: 0xf6000000,
    }, // 122 'z'
    HuffmanSym {
        bits: 15,
        code: 0xfffc0000,
    }, // 123 '{'
    HuffmanSym {
        bits: 11,
        code: 0xff800000,
    }, // 124 '|'
    HuffmanSym {
        bits: 14,
        code: 0xfff40000,
    }, // 125 '}'
    HuffmanSym {
        bits: 13,
        code: 0xffe80000,
    }, // 126 '~'
    HuffmanSym {
        bits: 28,
        code: 0xffffffc0,
    }, // 127
    HuffmanSym {
        bits: 20,
        code: 0xfffe6000,
    }, // 128
    HuffmanSym {
        bits: 22,
        code: 0xffff4800,
    }, // 129
    HuffmanSym {
        bits: 20,
        code: 0xfffe7000,
    }, // 130
    HuffmanSym {
        bits: 20,
        code: 0xfffe8000,
    }, // 131
    HuffmanSym {
        bits: 22,
        code: 0xffff4c00,
    }, // 132
    HuffmanSym {
        bits: 22,
        code: 0xffff5000,
    }, // 133
    HuffmanSym {
        bits: 22,
        code: 0xffff5400,
    }, // 134
    HuffmanSym {
        bits: 23,
        code: 0xffffb200,
    }, // 135
    HuffmanSym {
        bits: 22,
        code: 0xffff5800,
    }, // 136
    HuffmanSym {
        bits: 23,
        code: 0xffffb400,
    }, // 137
    HuffmanSym {
        bits: 23,
        code: 0xffffb600,
    }, // 138
    HuffmanSym {
        bits: 23,
        code: 0xffffb800,
    }, // 139
    HuffmanSym {
        bits: 23,
        code: 0xffffba00,
    }, // 140
    HuffmanSym {
        bits: 23,
        code: 0xffffbc00,
    }, // 141
    HuffmanSym {
        bits: 24,
        code: 0xffffeb00,
    }, // 142
    HuffmanSym {
        bits: 23,
        code: 0xffffbe00,
    }, // 143
    HuffmanSym {
        bits: 24,
        code: 0xffffec00,
    }, // 144
    HuffmanSym {
        bits: 24,
        code: 0xffffed00,
    }, // 145
    HuffmanSym {
        bits: 22,
        code: 0xffff5c00,
    }, // 146
    HuffmanSym {
        bits: 23,
        code: 0xffffc000,
    }, // 147
    HuffmanSym {
        bits: 24,
        code: 0xffffee00,
    }, // 148
    HuffmanSym {
        bits: 23,
        code: 0xffffc200,
    }, // 149
    HuffmanSym {
        bits: 23,
        code: 0xffffc400,
    }, // 150
    HuffmanSym {
        bits: 23,
        code: 0xffffc600,
    }, // 151
    HuffmanSym {
        bits: 23,
        code: 0xffffc800,
    }, // 152
    HuffmanSym {
        bits: 21,
        code: 0xfffee000,
    }, // 153
    HuffmanSym {
        bits: 22,
        code: 0xffff6000,
    }, // 154
    HuffmanSym {
        bits: 23,
        code: 0xffffca00,
    }, // 155
    HuffmanSym {
        bits: 22,
        code: 0xffff6400,
    }, // 156
    HuffmanSym {
        bits: 23,
        code: 0xffffcc00,
    }, // 157
    HuffmanSym {
        bits: 23,
        code: 0xffffce00,
    }, // 158
    HuffmanSym {
        bits: 24,
        code: 0xffffef00,
    }, // 159
    HuffmanSym {
        bits: 22,
        code: 0xffff6800,
    }, // 160
    HuffmanSym {
        bits: 21,
        code: 0xfffee800,
    }, // 161
    HuffmanSym {
        bits: 20,
        code: 0xfffe9000,
    }, // 162
    HuffmanSym {
        bits: 22,
        code: 0xffff6c00,
    }, // 163
    HuffmanSym {
        bits: 22,
        code: 0xffff7000,
    }, // 164
    HuffmanSym {
        bits: 23,
        code: 0xffffd000,
    }, // 165
    HuffmanSym {
        bits: 23,
        code: 0xffffd200,
    }, // 166
    HuffmanSym {
        bits: 21,
        code: 0xfffef000,
    }, // 167
    HuffmanSym {
        bits: 23,
        code: 0xffffd400,
    }, // 168
    HuffmanSym {
        bits: 22,
        code: 0xffff7400,
    }, // 169
    HuffmanSym {
        bits: 22,
        code: 0xffff7800,
    }, // 170
    HuffmanSym {
        bits: 24,
        code: 0xfffff000,
    }, // 171
    HuffmanSym {
        bits: 21,
        code: 0xfffef800,
    }, // 172
    HuffmanSym {
        bits: 22,
        code: 0xffff7c00,
    }, // 173
    HuffmanSym {
        bits: 23,
        code: 0xffffd600,
    }, // 174
    HuffmanSym {
        bits: 23,
        code: 0xffffd800,
    }, // 175
    HuffmanSym {
        bits: 21,
        code: 0xffff0000,
    }, // 176
    HuffmanSym {
        bits: 21,
        code: 0xffff0800,
    }, // 177
    HuffmanSym {
        bits: 22,
        code: 0xffff8000,
    }, // 178
    HuffmanSym {
        bits: 21,
        code: 0xffff1000,
    }, // 179
    HuffmanSym {
        bits: 23,
        code: 0xffffda00,
    }, // 180
    HuffmanSym {
        bits: 22,
        code: 0xffff8400,
    }, // 181
    HuffmanSym {
        bits: 23,
        code: 0xffffdc00,
    }, // 182
    HuffmanSym {
        bits: 23,
        code: 0xffffde00,
    }, // 183
    HuffmanSym {
        bits: 20,
        code: 0xfffea000,
    }, // 184
    HuffmanSym {
        bits: 22,
        code: 0xffff8800,
    }, // 185
    HuffmanSym {
        bits: 22,
        code: 0xffff8c00,
    }, // 186
    HuffmanSym {
        bits: 22,
        code: 0xffff9000,
    }, // 187
    HuffmanSym {
        bits: 23,
        code: 0xffffe000,
    }, // 188
    HuffmanSym {
        bits: 22,
        code: 0xffff9400,
    }, // 189
    HuffmanSym {
        bits: 22,
        code: 0xffff9800,
    }, // 190
    HuffmanSym {
        bits: 23,
        code: 0xffffe200,
    }, // 191
    HuffmanSym {
        bits: 26,
        code: 0xfffff800,
    }, // 192
    HuffmanSym {
        bits: 26,
        code: 0xfffff840,
    }, // 193
    HuffmanSym {
        bits: 20,
        code: 0xfffeb000,
    }, // 194
    HuffmanSym {
        bits: 19,
        code: 0xfffe2000,
    }, // 195
    HuffmanSym {
        bits: 22,
        code: 0xffff9c00,
    }, // 196
    HuffmanSym {
        bits: 23,
        code: 0xffffe400,
    }, // 197
    HuffmanSym {
        bits: 22,
        code: 0xffffa000,
    }, // 198
    HuffmanSym {
        bits: 25,
        code: 0xfffff600,
    }, // 199
    HuffmanSym {
        bits: 26,
        code: 0xfffff880,
    }, // 200
    HuffmanSym {
        bits: 26,
        code: 0xfffff8c0,
    }, // 201
    HuffmanSym {
        bits: 26,
        code: 0xfffff900,
    }, // 202
    HuffmanSym {
        bits: 27,
        code: 0xfffffbc0,
    }, // 203
    HuffmanSym {
        bits: 27,
        code: 0xfffffbe0,
    }, // 204
    HuffmanSym {
        bits: 26,
        code: 0xfffff940,
    }, // 205
    HuffmanSym {
        bits: 24,
        code: 0xfffff100,
    }, // 206
    HuffmanSym {
        bits: 25,
        code: 0xfffff680,
    }, // 207
    HuffmanSym {
        bits: 19,
        code: 0xfffe4000,
    }, // 208
    HuffmanSym {
        bits: 21,
        code: 0xffff1800,
    }, // 209
    HuffmanSym {
        bits: 26,
        code: 0xfffff980,
    }, // 210
    HuffmanSym {
        bits: 27,
        code: 0xfffffc00,
    }, // 211
    HuffmanSym {
        bits: 27,
        code: 0xfffffc20,
    }, // 212
    HuffmanSym {
        bits: 26,
        code: 0xfffff9c0,
    }, // 213
    HuffmanSym {
        bits: 27,
        code: 0xfffffc40,
    }, // 214
    HuffmanSym {
        bits: 24,
        code: 0xfffff200,
    }, // 215
    HuffmanSym {
        bits: 21,
        code: 0xffff2000,
    }, // 216
    HuffmanSym {
        bits: 21,
        code: 0xffff2800,
    }, // 217
    HuffmanSym {
        bits: 26,
        code: 0xfffffa00,
    }, // 218
    HuffmanSym {
        bits: 26,
        code: 0xfffffa40,
    }, // 219
    HuffmanSym {
        bits: 28,
        code: 0xffffffd0,
    }, // 220
    HuffmanSym {
        bits: 27,
        code: 0xfffffc60,
    }, // 221
    HuffmanSym {
        bits: 27,
        code: 0xfffffc80,
    }, // 222
    HuffmanSym {
        bits: 27,
        code: 0xfffffca0,
    }, // 223
    HuffmanSym {
        bits: 20,
        code: 0xfffec000,
    }, // 224
    HuffmanSym {
        bits: 24,
        code: 0xfffff300,
    }, // 225
    HuffmanSym {
        bits: 20,
        code: 0xfffed000,
    }, // 226
    HuffmanSym {
        bits: 21,
        code: 0xffff3000,
    }, // 227
    HuffmanSym {
        bits: 22,
        code: 0xffffa400,
    }, // 228
    HuffmanSym {
        bits: 21,
        code: 0xffff3800,
    }, // 229
    HuffmanSym {
        bits: 21,
        code: 0xffff4000,
    }, // 230
    HuffmanSym {
        bits: 23,
        code: 0xffffe600,
    }, // 231
    HuffmanSym {
        bits: 22,
        code: 0xffffa800,
    }, // 232
    HuffmanSym {
        bits: 22,
        code: 0xffffac00,
    }, // 233
    HuffmanSym {
        bits: 25,
        code: 0xfffff700,
    }, // 234
    HuffmanSym {
        bits: 25,
        code: 0xfffff780,
    }, // 235
    HuffmanSym {
        bits: 24,
        code: 0xfffff400,
    }, // 236
    HuffmanSym {
        bits: 24,
        code: 0xfffff500,
    }, // 237
    HuffmanSym {
        bits: 26,
        code: 0xfffffa80,
    }, // 238
    HuffmanSym {
        bits: 23,
        code: 0xffffe800,
    }, // 239
    HuffmanSym {
        bits: 26,
        code: 0xfffffac0,
    }, // 240
    HuffmanSym {
        bits: 27,
        code: 0xfffffcc0,
    }, // 241
    HuffmanSym {
        bits: 26,
        code: 0xfffffb00,
    }, // 242
    HuffmanSym {
        bits: 26,
        code: 0xfffffb40,
    }, // 243
    HuffmanSym {
        bits: 27,
        code: 0xfffffce0,
    }, // 244
    HuffmanSym {
        bits: 27,
        code: 0xfffffd00,
    }, // 245
    HuffmanSym {
        bits: 27,
        code: 0xfffffd20,
    }, // 246
    HuffmanSym {
        bits: 27,
        code: 0xfffffd40,
    }, // 247
    HuffmanSym {
        bits: 27,
        code: 0xfffffd60,
    }, // 248
    HuffmanSym {
        bits: 28,
        code: 0xffffffe0,
    }, // 249
    HuffmanSym {
        bits: 27,
        code: 0xfffffd80,
    }, // 250
    HuffmanSym {
        bits: 27,
        code: 0xfffffda0,
    }, // 251
    HuffmanSym {
        bits: 27,
        code: 0xfffffdc0,
    }, // 252
    HuffmanSym {
        bits: 27,
        code: 0xfffffde0,
    }, // 253
    HuffmanSym {
        bits: 27,
        code: 0xfffffe00,
    }, // 254
    HuffmanSym {
        bits: 26,
        code: 0xfffffb80,
    }, // 255
    HuffmanSym {
        bits: 30,
        code: 0xfffffffc,
    }, // 256 EOS
];

/// ハフマンエンコード後の長さを計算
pub fn encoded_len(data: &[u8]) -> usize {
    let bits: usize = data
        .iter()
        .map(|&b| HUFFMAN_TABLE[b as usize].bits as usize)
        .sum();
    bits.div_ceil(8)
}

/// ハフマンエンコード
///
/// 成功時はエンコードしたバイト数を返す
pub fn encode(buf: &mut [u8], data: &[u8]) -> Option<usize> {
    let required = encoded_len(data);
    if buf.len() < required {
        return None;
    }

    let mut acc: u64 = 0;
    let mut acc_bits: u32 = 0;
    let mut offset = 0;

    for &byte in data {
        let sym = &HUFFMAN_TABLE[byte as usize];
        // 符号を 64 ビットの上位に配置してからシフト
        let code_64 = (sym.code as u64) << 32;
        acc |= code_64 >> acc_bits;
        acc_bits += sym.bits as u32;

        while acc_bits >= 8 {
            buf[offset] = (acc >> 56) as u8;
            offset += 1;
            acc <<= 8;
            acc_bits -= 8;
        }
    }

    // 残りビットがある場合、1 でパディング (EOS プレフィックス)
    if acc_bits > 0 {
        // 残りビットを上位に、残りを 1 で埋める
        let padding_bits = 8 - acc_bits;
        let padding_mask = (1u64 << padding_bits) - 1;
        let byte = ((acc >> 56) as u8) | (padding_mask as u8);
        buf[offset] = byte;
        offset += 1;
    }

    Some(offset)
}

/// ハフマンデコード
pub fn decode(data: &[u8]) -> Result<Vec<u8>, QpackError> {
    let mut result = Vec::new();
    let mut acc: u64 = 0;
    let mut acc_bits: u32 = 0;

    for &byte in data {
        acc = (acc << 8) | (byte as u64);
        acc_bits += 8;

        // 十分なビットがある間、デコードを続ける
        'decode: while acc_bits >= 5 {
            // 現在のビットを左詰めで 32 ビットに配置
            let current = if acc_bits >= 32 {
                (acc >> (acc_bits - 32)) as u32
            } else {
                (acc << (32 - acc_bits)) as u32
            };

            // 符号テーブルを検索
            for (sym_idx, sym) in HUFFMAN_TABLE.iter().enumerate() {
                if sym.bits as u32 <= acc_bits {
                    // 符号のマスクを作成 (左詰め)
                    let mask = if sym.bits >= 32 {
                        0xffffffff_u32
                    } else {
                        !((1u32 << (32 - sym.bits)) - 1)
                    };

                    // 符号が一致するかチェック
                    if (current & mask) == sym.code {
                        if sym_idx == 256 {
                            // EOS シンボルはデコードエラーとして扱う (RFC 7541 Section 5.2)
                            return Err(QpackError::InvalidHuffman);
                        }
                        result.push(sym_idx as u8);
                        acc_bits -= sym.bits as u32;
                        // 使用したビットをクリア
                        if acc_bits > 0 {
                            acc &= (1u64 << acc_bits) - 1;
                        } else {
                            acc = 0;
                        }
                        continue 'decode;
                    }
                }
            }

            // マッチしない = まだ符号が完成していない
            break;
        }
    }

    // 残りビットが EOS パディング (すべて 1) か検証
    if acc_bits > 0 && acc_bits <= 7 {
        let mask = (1u64 << acc_bits) - 1;
        if (acc & mask) == mask {
            return Ok(result);
        }
        // パディングが不正
        return Err(QpackError::InvalidHuffman);
    }

    if acc_bits > 7 {
        // 7 ビット以上残っている = 不完全なデータ
        return Err(QpackError::InvalidHuffman);
    }

    Ok(result)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encode_decode_simple() {
        let input = b"www.example.com";
        let encoded_length = encoded_len(input);
        let mut buf = vec![0u8; encoded_length];

        let len = encode(&mut buf, input).expect("test must succeed");
        assert_eq!(len, encoded_length);

        let decoded = decode(&buf).expect("test must succeed");
        assert_eq!(decoded, input);
    }

    #[test]
    fn test_encode_decode_method() {
        let input = b"GET";
        let mut buf = vec![0u8; encoded_len(input)];

        encode(&mut buf, input).expect("test must succeed");
        let decoded = decode(&buf).expect("test must succeed");
        assert_eq!(decoded, input);
    }

    #[test]
    fn test_encode_decode_path() {
        let input = b"/index.html";
        let mut buf = vec![0u8; encoded_len(input)];

        encode(&mut buf, input).expect("test must succeed");
        let decoded = decode(&buf).expect("test must succeed");
        assert_eq!(decoded, input);
    }

    #[test]
    fn test_encoded_len() {
        // "www.example.com" should encode to 12 bytes
        assert_eq!(encoded_len(b"www.example.com"), 12);
    }

    #[test]
    fn test_encode_buffer_too_short() {
        let input = b"test";
        let mut buf = [0u8; 1];
        assert!(encode(&mut buf, input).is_none());
    }
}
