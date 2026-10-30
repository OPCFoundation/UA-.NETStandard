// Copyright (c) OPC Foundation. Licensed under the MIT License.
//go:build quic_trace09

package wireproof

import (
	"testing"

	"github.com/quic-go/quic-go/internal/protocol"
	"github.com/quic-go/quic-go/internal/wire"
	"github.com/quic-go/quic-go/quicvarint"
)

func TestTelemetryDistinguishesExact09FromLegacyOnly(t *testing.T) {
	for _, tc := range []struct {
		name  string
		ids   []uint64
		exact bool
	}{
		{"absent", nil, false},
		{"legacy07-only", []uint64{0x17f7586d2cb571}, false},
		{"msquic-old-only", []uint64{0x17f7586d2cb570}, false},
		{"draft09-only", []uint64{0x1d}, true},
		{"draft09-then-legacy", []uint64{0x1d, 0x17f7586d2cb571}, true},
		{"legacy-then-draft09", []uint64{0x17f7586d2cb571, 0x1d}, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			var encoded []byte
			for _, id := range tc.ids {
				encoded = quicvarint.Append(encoded, id)
				encoded = append(encoded, 0)
			}
			encoded = append(encoded, 0x0f, 0)
			var parsed wire.TransportParameters
			if err := parsed.Unmarshal(encoded, protocol.PerspectiveClient); err != nil {
				t.Fatal(err)
			}
			if parsed.ResetStreamAt09 != tc.exact {
				t.Fatalf("exact09 observation=%v expected=%v for %x", parsed.ResetStreamAt09, tc.exact, encoded)
			}
		})
	}
}
