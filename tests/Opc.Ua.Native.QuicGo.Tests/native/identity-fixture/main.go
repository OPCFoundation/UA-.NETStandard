// Copyright (c) OPC Foundation. Licensed under the MIT License.
// Test-only credentials travel exclusively through an anonymous pipe, never files.
package main

import (
	"crypto"
	"crypto/ecdsa"
	"crypto/ed25519"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/rsa"
	"crypto/sha256"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/json"
	"flag"
	"math/big"
	"net/url"
	"os"
	"time"
)

type identity struct {
	Cert []byte `json:"cert"`
	Key  []byte `json:"key"`
	SPKI []byte `json:"spki"`
	URI  string `json:"uri"`
}

func must[T any](value T, err error) T {
	if err != nil {
		panic(err)
	}
	return value
}

func main() {
	rsaMode := flag.Bool("rsa", false, "use RSA2048 identities")
	flag.Parse()
	mode, err := os.Stdout.Stat()
	if err != nil || mode.Mode()&os.ModeCharDevice != 0 || mode.Mode().IsRegular() {
		os.Exit(2)
	}
	newKey := func() crypto.Signer {
		if *rsaMode {
			return must(rsa.GenerateKey(rand.Reader, 2048))
		}
		return must(ecdsa.GenerateKey(elliptic.P256(), rand.Reader))
	}
	rootKey := newKey()
	rootTemplate := &x509.Certificate{
		SerialNumber: big.NewInt(1), Subject: pkix.Name{CommonName: "Native backend test authority"},
		NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(time.Hour),
		KeyUsage: x509.KeyUsageCertSign, IsCA: true, BasicConstraintsValid: true,
	}
	rootDER := must(x509.CreateCertificate(rand.Reader, rootTemplate, rootTemplate, rootKey.Public(), rootKey))
	root := must(x509.ParseCertificate(rootDER))
	names := []string{"client", "server", "second-client", "expired-client", "wrong-eku", "unsupported-key"}
	identities := make([]identity, len(names))
	for i, name := range names {
		uri := must(url.Parse("urn:opcfoundation:quic-backend-tests:" + name))
		key := newKey()
		if i == 5 {
			_, ed, err := ed25519.GenerateKey(rand.Reader)
			if err != nil {
				panic(err)
			}
			key = ed
		}
		eku := x509.ExtKeyUsageClientAuth
		if i == 1 || i == 4 {
			eku = x509.ExtKeyUsageServerAuth
		}
		template := &x509.Certificate{
			SerialNumber: big.NewInt(int64(i + 2)), Subject: pkix.Name{CommonName: name},
			NotBefore: root.NotBefore, NotAfter: root.NotAfter,
			KeyUsage: x509.KeyUsageDigitalSignature, ExtKeyUsage: []x509.ExtKeyUsage{eku},
			URIs: []*url.URL{uri}, DNSNames: []string{"localhost"},
		}
		if i == 3 {
			template.NotBefore, template.NotAfter = time.Now().Add(-2*time.Hour), time.Now().Add(-time.Hour)
		}
		der := must(x509.CreateCertificate(rand.Reader, template, root, key.Public(), rootKey))
		leaf := must(x509.ParseCertificate(der))
		pin := sha256.Sum256(leaf.RawSubjectPublicKeyInfo)
		identities[i] = identity{der, must(x509.MarshalPKCS8PrivateKey(key)), pin[:], uri.String()}
	}
	if json.NewEncoder(os.Stdout).Encode(struct {
		Root       []byte     `json:"root"`
		Identities []identity `json:"identities"`
	}{rootDER, identities}) != nil {
		os.Exit(3)
	}
	for _, identity := range identities {
		clear(identity.Key)
	}
}
