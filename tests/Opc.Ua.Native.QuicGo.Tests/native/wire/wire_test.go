// Copyright (c) OPC Foundation. Licensed under the MIT License.
package wireproof

import (
	"bytes"
	"errors"
	"strings"
	"testing"

	"github.com/quic-go/quic-go/internal/protocol"
	"github.com/quic-go/quic-go/internal/qerr"
	"github.com/quic-go/quic-go/internal/wire"
	"github.com/quic-go/quic-go/quicvarint"
)

func parameters(t *testing.T, encoded []byte) map[uint64][]byte {
	t.Helper()
	result := map[uint64][]byte{}
	for len(encoded) > 0 {
		id, n, err := quicvarint.Parse(encoded)
		if err != nil {
			t.Fatal(err)
		}
		encoded = encoded[n:]
		size, n, err := quicvarint.Parse(encoded)
		if err != nil || size > uint64(len(encoded)-n) {
			t.Fatalf("invalid parameter %x: %v", id, err)
		}
		encoded = encoded[n:]
		if _, duplicate := result[id]; duplicate {
			t.Fatalf("duplicate parameter %x", id)
		}
		result[id] = append([]byte{}, encoded[:size]...)
		encoded = encoded[size:]
	}
	return result
}

func TestNativeReset09ParameterIs1dEmpty(t *testing.T) {
	for _, side := range []protocol.Perspective{protocol.PerspectiveClient, protocol.PerspectiveServer} {
		p := &wire.TransportParameters{EnableResetStreamAt: true, MaxDatagramFrameSize: 16383}
		encoded := p.Marshal(side)
		got := parameters(t, encoded)
		value, found := got[0x1d]
		if !found || len(value) != 0 {
			t.Fatalf("draft09 requires parameter 1d with empty value, got %x", encoded)
		}
		size, _, err := quicvarint.Parse(got[0x20])
		if err != nil || size != 16383 {
			t.Fatalf("RFC9221 max_datagram_frame_size mismatch: %x", encoded)
		}
		if _, found := got[0x17f7586d2cb570]; found {
			t.Fatal("MsQuic obsolete transport parameter emitted")
		}
		t.Logf("native marshal side=%s TP1d=%x TP20=%x", side, value, got[0x20])
	}
}

func TestNativeReset09ParameterRejectsNonempty(t *testing.T) {
	for _, value := range [][]byte{{0}, {1}, {0, 0}} {
		encoded := quicvarint.Append(nil, 0x1d)
		encoded = quicvarint.Append(encoded, uint64(len(value)))
		encoded = append(encoded, value...)
		encoded = append(encoded, 0x0f, 0x00)
		var p wire.TransportParameters
		err := p.Unmarshal(encoded, protocol.PerspectiveClient)
		var transport *qerr.TransportError
		if !errors.As(err, &transport) || transport.ErrorCode != qerr.TransportParameterError ||
			!strings.Contains(err.Error(), "expected empty") {
			t.Fatalf("nonempty reset parameter not rejected as TRANSPORT_PARAMETER_ERROR: %v", err)
		}
	}
}

func TestNativeReset09FrameGoldenAndParser(t *testing.T) {
	frame := &wire.ResetStreamFrame{StreamID: 4, ErrorCode: 42, FinalSize: 132, ReliableSize: 4}
	encoded, err := frame.Append(nil, protocol.Version1)
	expected := []byte{0x24, 0x04, 0x2a, 0x40, 0x84, 0x04}
	if err != nil || !bytes.Equal(encoded, expected) {
		t.Fatalf("reset09 frame: wanted %x, got %x: %v", expected, encoded, err)
	}
	parser := wire.NewFrameParser(true, true, false)
	kind, n, err := parser.ParseType(expected, protocol.Encryption1RTT)
	if err != nil || uint64(kind) != 0x24 || n != 1 {
		t.Fatalf("reset09 type not accepted: %x, %d, %v", kind, n, err)
	}
	decoded, n, err := parser.ParseLessCommonFrame(kind, expected[1:], protocol.Version1)
	if err != nil || n != len(expected)-1 || *decoded.(*wire.ResetStreamFrame) != *frame {
		t.Fatalf("reset09 exact fields not preserved: %+v %d %v", decoded, n, err)
	}
	t.Logf("exact native frame bytes=%x", encoded)
}

func TestNativeReset09RejectsInvalidSizeAndTruncation(t *testing.T) {
	parser := wire.NewFrameParser(true, true, false)
	for _, payload := range [][]byte{
		{4, 42, 3, 4},
		{},
		{4},
		{4, 42},
		{4, 42, 4},
	} {
		_, _, err := parser.ParseLessCommonFrame(wire.FrameTypeResetStreamAt, payload, protocol.Version1)
		var transport *qerr.TransportError
		if !errors.As(err, &transport) || transport.ErrorCode != qerr.FrameEncodingError {
			t.Fatalf("invalid reset payload %x not rejected as FRAME_ENCODING_ERROR: %v", payload, err)
		}
	}
}

func TestNativeReset09CannotAppearInHandshakeOrWithoutCapability(t *testing.T) {
	for _, level := range []protocol.EncryptionLevel{protocol.EncryptionInitial, protocol.EncryptionHandshake} {
		_, _, err := wire.NewFrameParser(true, true, false).ParseType([]byte{0x24}, level)
		if err == nil {
			t.Fatalf("reset09 accepted at encryption level %v", level)
		}
	}
	_, _, err := wire.NewFrameParser(true, false, false).ParseType([]byte{0x24}, protocol.Encryption1RTT)
	if err == nil {
		t.Fatal("reset09 accepted without local capability")
	}
	_, _, err = wire.NewFrameParser(true, true, false).ParseType([]byte{0x21}, protocol.Encryption1RTT)
	if err == nil {
		t.Fatal("MsQuic obsolete frame 0x21 accepted")
	}
}

func TestNativeDatagramWireCodepoints(t *testing.T) {
	for _, withLength := range []bool{false, true} {
		frame := &wire.DatagramFrame{DataLenPresent: withLength, Data: []byte{0xde, 0xad, 0xbe, 0xef}}
		encoded, err := frame.Append(nil, protocol.Version1)
		expected := []byte{0x30, 0xde, 0xad, 0xbe, 0xef}
		if withLength {
			expected = []byte{0x31, 0x04, 0xde, 0xad, 0xbe, 0xef}
		}
		if err != nil || !bytes.Equal(encoded, expected) {
			t.Fatalf("DATAGRAM encoding expected %x, got %x: %v", expected, encoded, err)
		}
	}
}

func TestLegacyOnlyResetParameterIsAnIntegrationLimitation(t *testing.T) {
	// Document the upstream compatibility behavior; do not mistake this boolean
	// for evidence that a remote peer supplied the required draft09 codepoint.
	encoded := quicvarint.Append(nil, 0x17f7586d2cb571)
	encoded = append(encoded, 0, 0x0f, 0)
	var p wire.TransportParameters
	if err := p.Unmarshal(encoded, protocol.PerspectiveClient); err != nil || !p.EnableResetStreamAt {
		t.Fatalf("upstream legacy behavior changed; reassess strict integration gate: %v", err)
	}
	t.Log("LIMITATION: public SupportsStreamResetPartialDelivery.Remote merges draft07 and draft09")
}
