# WAR Client Login Protocol — Reverse Engineering Design Document

> **Source**: Ghidra static analysis of the Warhammer Online: Age of Reckoning game client (32-bit x86 PE)
> **Namespace**: `MythLogin::LoginClient` (from embedded debug strings)
> **Date**: February 2026

---

## Table of Contents

1. [Protocol Overview](#1-protocol-overview)
2. [Wire Format](#2-wire-format)
3. [Message Type Catalogue](#3-message-type-catalogue)
4. [LoginClient State Machine](#4-loginclient-state-machine)
5. [Encryption Architecture](#5-encryption-architecture)
6. [Key Derivation Chain](#6-key-derivation-chain)
7. [Protobuf Message Definitions](#7-protobuf-message-definitions)
8. [Class Layouts](#8-class-layouts)
9. [Vtable Maps](#9-vtable-maps)
10. [Key Functions Reference](#10-key-functions-reference)
11. [Constant Strings & Data Addresses](#11-constant-strings--data-addresses)
12. [TCPNET Transport Layer](#12-tcpnet-transport-layer)
13. [Connection Lifecycle Walkthrough](#13-connection-lifecycle-walkthrough)
14. [Server Emulation Notes](#14-server-emulation-notes)

---

## 1. Protocol Overview

The login subsystem is built on:

| Layer | Technology |
|-------|-----------|
| Serialisation | Protocol Buffers (proto2), package `login` |
| Encryption | Curve25519 ECDH → AES-256-CBC + Jenkins lookup3 integrity hash |
| Transport | Custom `TCPNETConnection` / `TCPNETSet` over raw TCP (Winsock `send`/`recv`) |
| Framing | Variable-length integer (varint) header |

The client drives the protocol as a strict request/reply state machine. Only
**one** message is ever encrypted — the initial `VerifyProtocolReq` (type 1).
Every subsequent message in both directions is **plaintext** protobuf.

---

## 2. Wire Format

### 2.1 Message Framing

Each TCP message is framed as:

```
┌─────────────────────┬──────────────────┬─────────────────────┐
│  varint body_length  │  varint msg_type  │   body (N bytes)    │
└─────────────────────┴──────────────────┴─────────────────────┘
```

**Byte order confirmed from disassembly of `TCPNET::ReadMessage` at `0x0096761E`:**

1. **First** varint read → stored at `this+0x5C` → compared against max message size at `this+0x11C` → **body length**
2. **Second** varint read → stored in local variable → passed to `TCPNETMessage` constructor → **message type**
3. Remaining bytes → **body payload**

### 2.2 Varint Encoding

Standard protobuf-style varint (high-bit continuation). The reader at `0x0096761E`
reads one byte at a time via `recv()`:
- If `byte & 0x80` → more bytes follow, accumulate `(byte & 0x7F) << shift`
- If `byte & 0x80 == 0` → final byte

### 2.3 Example Captured Frame

```
Hex: 30 01 BA9F7D1A...  (50 bytes total)
      │  │  └─ encrypted body (48 bytes)
      │  └──── msg_type = 1 (VerifyProtocolReq)
      └─────── body_length = 0x30 = 48
```

Both header fields happen to be single-byte varints in this example.

---

## 3. Message Type Catalogue

Derived from the dispatch table at `DAT_00f76010` (16 entries) and the
`ReceivedMessage` function at `0x0095858B`.

| Type | Direction | Name | Handler/Sender | Notes |
|------|-----------|------|-----------------|-------|
| 1 | C → S | `VerifyProtocolReq` | `FUN_009583f0` (NewConnection) | **Encrypted** (AES-256-CBC) |
| 2 | S → C | `VerifyProtocolReply` | `FUN_0095734b` | Sets `this+0x22=1`, installs IVs |
| 3 | C → S | `AuthInitialTokenReq` | `FUN_00957168` (Authenticate) | Credential-based auth |
| 4 | S → C | `AuthInitialTokenReply` | `FUN_0095749e` | |
| 5 | C → S | `AuthSessionTokenReq` | `FUN_00957168` (Authenticate) | Session token auth |
| 6 | S → C | `AuthSessionTokenReply` | `FUN_0095758f` | |
| 7 | C → S | `GetCharSummaryListReq` | — | Empty body |
| 8 | S → C | `GetCharSummaryListReply` | `FUN_00957b11` | |
| 9 | C → S | `GetClusterListReq` | — | Empty body |
| 10 | S → C | `GetClusterListReply` | `FUN_0095763f` | |
| 11 | C → S | `GetAcctPropListReq` | — | Empty body |
| 12 | S → C | `GetAcctPropListReply` | `FUN_00957d2e` | |
| 13 | C → S | `MetricEventNotify` | — | Telemetry, fire-and-forget |

The factory table at `DAT_00f76010` holds 16 entries (indexed 0–15 by message type).
Each non-null entry points to a protobuf message factory vtable with a `Create()` at
slot `[2]`.

---

## 4. LoginClient State Machine

The state field lives at `this+0x20` (4 bytes). Transitions are driven by
`LoginClient::Process` (`FUN_009566d2`).

```
                           ┌─────────────┐
                           │  0: IDLE     │
                           └──────┬───────┘
                                  │ Connect()
                           ┌──────▼───────┐
                           │  1: CONNECT  │
                           └──────┬───────┘
                                  │ NewConnection() → sends VerifyProtocolReq (type 1)
                           ┌──────▼────────────┐
                           │  2: HANDSHAKE_SENT │
                           └──────┬────────────┘
                                  │ HandleVerifyProtocolReply (type 2)
                           ┌──────▼──────────────────┐
                           │  3: HANDSHAKE_COMPLETE   │
                           └──────┬──────────────────┘
                                  │ Authenticate() → sends type 3 or 5
                           ┌──────▼───────────────┐
                           │  4: AUTH_SENT         │
                           └──────┬───────────────┘
                                  │ HandleAuthReply (type 4 or 6)
                           ┌──────▼───────────────┐
                           │  5: AUTHENTICATED     │
                           └──────┬───────────────┘
                                  │ sends GetCharSummaryListReq, GetClusterListReq, etc.
                           ┌──────▼───────────────┐
                           │  6: DATA_REQUESTS     │
                           └──────┬───────────────┘
                                  │ All replies received
                           ┌──────▼───────────────┐
                           │  7: READY             │
                           └──────┬───────────────┘
                                  │
                           ┌──────▼───────────────┐
                           │  8: COMPLETE / ERROR  │
                           └──────────────────────┘
```

Timeout tracking at `this+0x50` is checked in the Process loop. If a pending
message type matches the timeout tracker, the tracker is cleared on reply receipt.

---

## 5. Encryption Architecture

### 5.1 When Encryption Applies

| Phase | client→server | server→client |
|-------|--------------|---------------|
| VerifyProtocolReq (type 1) | **AES-256-CBC encrypted** | — |
| VerifyProtocolReply (type 2) | — | Plaintext |
| All subsequent messages | Plaintext | Plaintext |

The encryption gate is the byte at `this+0x22`:
- `0x22 == 0` (initial) → `SerializeAndSendMessage` calls `encryptor->vtable[7]` → **AES encrypt**
- `0x22 == 1` (after VerifyProtocolReply) → encryption is **skipped**, raw protobuf is sent

Server→client messages are **never decrypted** by `LoginClient`. The `ReceivedMessage`
dispatcher passes the raw body directly to the protobuf deserializer.

### 5.2 AES-256-CBC Parameters

| Parameter | Value | Binary Evidence |
|-----------|-------|----------------|
| Algorithm | AES-256-CBC | ECDH produces 32-byte key → `SetKey` (`FUN_009cbf40`) accepts 0x20, computes Nk=8, Nr from lookup |
| Key Size | 256 bits (32 bytes) | Shared secret from X25519 is 32 bytes, passed directly to `SetKey` |
| Block Size | 128 bits (16 bytes) | Standard AES; `GetBlockSizeBits` returns `*this * 32` = 128 for Nk=4 blocks |
| Mode | CBC (mode=2) | Factory `FUN_009c9dc0` passes mode=2; `SetMode` (`FUN_009cbf20`) stores at `this+0x10` |
| IV | 16 zero bytes | `FUN_009d5870` (RijndaelEngine constructor) zeroes 8 DWORDs at offset +0x1F4 |
| Key Schedule | `FUN_009cbd40` | Standard AES key expansion |
| Block Cipher | `FUN_009cc020` → `FUN_009d0ab0` | CBC XOR + encrypt loop |

### 5.3 Padding Scheme

**Custom padding** (NOT PKCS#7) implemented in `FUN_009ca1d0` (RijndaelStream::Encrypt):

```
padded_size = ((data_size / block_size) + 1) * block_size
pad_count   = padded_size - data_size          // always >= 1
padding     = [random_byte × (pad_count - 1)] [pad_count as uint8]
```

- There is **always** at least one full block of padding added (the `+1` guarantees this)
- Padding bytes (except the last) are filled with `_rand()` (C runtime random)
- The **last byte** of the padded block is the padding count

**Example**: 42-byte input (38 protobuf + 4 hash) → padded to 48 bytes (6 pad bytes: 5 random + `0x06`)

### 5.4 Integrity Hash

Before encryption, a **Jenkins lookup3** hash is appended:

1. Compute `hashlittle2(protobuf_data, length)` → returns `(pc, pb)` pair
2. Byte-swap `pc` to **big-endian** (4 bytes)
3. Append these 4 bytes after the protobuf data
4. The combined `[protobuf_data | 4-byte hash]` is then padded and AES-encrypted

This hash is verified during decryption on the server side but is mainly a
data-integrity check (not a cryptographic MAC).

### 5.5 Encrypt Pipeline (in `SerializeAndSendMessage`, `FUN_00957f13`)

```
if this+0x22 == 0:
    protobuf_bytes = message.SerializeToArray()
    encrypted_buf  = encryptor->vtable[7](protobuf_bytes)
                   // vtable[7] = FUN_009ca9e0:
                   //   1. Jenkins lookup3 hash → 4 bytes big-endian
                   //   2. Append hash to protobuf data
                   //   3. Pass to RijndaelStream::Encrypt (AES-CBC + custom padding)
                   //   4. Return encrypted DataMemoryBuffer
    tcpnet->vtable[11](encrypted_buf, msg_type)
else:
    protobuf_bytes = message.SerializeToArray()
    tcpnet->vtable[11](protobuf_bytes, msg_type)    // plaintext
```

---

## 6. Key Derivation Chain

### 6.1 Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                        CLIENT SIDE                                │
│                                                                   │
│  static_key (32B)         ephemeral_private (32B)                 │
│  @ DAT_00b54a74           random, Curve25519-clamped              │
│       │                          │                                │
│       └────────┐  ┌──────────────┘                                │
│                ▼  ▼                                                │
│         X25519(ephemeral_private, static_key)                     │
│                │                                                  │
│                ▼                                                  │
│         shared_secret (32 bytes) = AES-256 key                    │
│                │                                                  │
│         ┌──────┴──────┐                                           │
│         ▼             ▼                                           │
│    encrypt_stream  decrypt_stream                                 │
│    (RijndaelStream) (RijndaelStream)                              │
│         │                                                         │
│         ▼                                                         │
│    ephemeral_public = X25519(ephemeral_private, basepoint)        │
│         │                                                         │
│         ▼                                                         │
│    Sent in VerifyProtocolReq.public_key (field 3)                 │
└──────────────────────────────────────────────────────────────────┘
```

### 6.2 Curve25519 Clamping

`FUN_009ca520` (GenerateKeypair) performs standard clamping:
```c
key[0]  &= 0xF8;               // clear bottom 3 bits
key[31]  = (key[31] & 0x3F) | 0x40;  // clear top bit, set second-to-top bit
```

### 6.3 Scalar Multiplication

`FUN_009d5cd0(output, scalar, point)` — confirmed argument order from assembly:

In `DerivePublicKey` (`FUN_009ca5f0`):
```asm
PUSH 0xa47ba0           ; basepoint (point)
MOV  ECX, EDI           ; private key buffer
CALL EDX                ; getData() on private key → scalar
PUSH EAX                ; scalar pointer
; ... output buffer setup ...
CALL FUN_009d5cd0       ; X25519(output, scalar=private_key, point=basepoint)
```

In `SharedSecret` (`FUN_009ca6b0`) with `param_1=remote_key, param_2=local_secret`:
```asm
; param_1 (EBX) = remote_key → getData() pushed first (= point)
; param_2 (EDI) = local_secret → getData() pushed second (= scalar)
; output pushed last
CALL FUN_009d5cd0       ; X25519(output, scalar=local_secret, point=remote_key)
```

**Basepoint address**: `DAT_00a47ba0` (standard Curve25519 basepoint `{9, 0, 0, ...}` expected)

### 6.4 Key Installation Sequence (in `GetPublicKey`, `FUN_009caef0`)

1. Generate 32 random bytes
2. Clamp as Curve25519 private key → store at `encryptor+0x14` (local_secret)
3. Call `FUN_009cad00` (auto-key-setup):
   - Checks: `this+0x14 != NULL && this+0x18 != NULL`
   - If both set: `shared_secret = X25519(this+0x18, this+0x14)` = `X25519(static_key, ephemeral_priv)`
   - Calls `encrypt_stream->SetKey(shared_secret)` and `decrypt_stream->SetKey(shared_secret)`
   - **Wipes** shared secret from memory
   - Sets `this+0x1C = 1` (keys_installed flag)
4. Derive public key: `ephemeral_public = X25519(ephemeral_priv, basepoint)`
5. Store public key at `encryptor+0x10`
6. Return copy of public key

### 6.5 Static Key

| Property | Value |
|----------|-------|
| Address | `DAT_00b54a74` |
| Size | 32 bytes |
| Section | `.data` (offset in range `0x00b4e000–0x00f8f67f`) |
| Cross-references | Single xref from `FUN_009565c9` (Initialize) at `0x00956657` |
| Role | Server's Curve25519 public key (or possibly private key — unclear without the bytes) |

The key is loaded in Initialize:
```asm
PUSH 0x0            ; flags
PUSH 0x20           ; size = 32 bytes
PUSH 0xb54a74       ; address of static key data
LEA  ECX, [ESP+0x1c]
CALL FUN_00992e10   ; DataMemoryBuffer wrapper constructor
```

Then passed via: `encryptor->vtable[3](buffer)` → copies 32 bytes to `encryptor+0x18` (remote_key slot).

---

## 7. Protobuf Message Definitions

### 7.1 VerifyProtocolReq (type 1) — C → S

Derived from `SerializeWithCachedSizes` at `0x0095bb7a`:

```protobuf
// package login;
message VerifyProtocolReq {
    optional uint32 crypto_type      = 1;  // constant 5 (Curve25519 + AES)
    optional uint32 protocol_version = 2;  // from server_config+0x02
    optional bytes  public_key       = 3;  // 32-byte Curve25519 ephemeral public key
}
```

**Plaintext binary layout**: `08 05 10 <varint:version> 1A 20 <32 bytes pubkey>`
Expected size: ~38 bytes (protobuf) + 4 bytes (Jenkins hash) + 6 bytes (padding) = **48 bytes** encrypted.

### 7.2 VerifyProtocolReply (type 2) — S → C

Derived from `HandleVerifyProtocolReply` (`FUN_0095734b`):

```protobuf
message VerifyProtocolReply {
    optional int32 result     = 1;  // 0 = success
    optional bytes server_key = 2;  // server's Curve25519 public key
    optional bytes server_iv  = 3;  // IV/nonce for key derivation
}
```

On receipt, the handler:
1. Checks `result == 0` (success)
2. Calls `encryptor->vtable[4](server_iv, server_key)` → installs IVs on both streams
3. Sets `this+0x22 = 1` → disables encryption for subsequent messages

### 7.3 Auth Messages (types 3–6)

```protobuf
message AuthInitialTokenReq {     // type 3
    // Credential-based authentication fields (username/password token)
}

message AuthInitialTokenReply {   // type 4
    // Auth result, session token
}

message AuthSessionTokenReq {     // type 5
    // Session token for re-authentication
}

message AuthSessionTokenReply {   // type 6
    // Auth result
}
```

Both type 3 and type 5 are sent by `FUN_00957168` (Authenticate). The choice depends
on whether a session token (`this+0x3C`) or credential token (`this+0x40`) is available.

### 7.4 Data Request Messages (types 7–12)

Types 7, 9, and 11 are **empty-body** requests (no protobuf fields). The server
replies with the corresponding even-numbered type containing the data.

```protobuf
message GetCharSummaryListReq  {}  // type 7
message GetCharSummaryListReply {  // type 8
    // Character list data
}

message GetClusterListReq  {}     // type 9
message GetClusterListReply {     // type 10
    // Server/cluster list
}

message GetAcctPropListReq  {}    // type 11
message GetAcctPropListReply {    // type 12
    // Account properties
}
```

### 7.5 MetricEventNotify (type 13) — C → S

Fire-and-forget telemetry message. No server reply expected.

---

## 8. Class Layouts

### 8.1 `MythLogin::LoginClient`

Size: at least 0x64 bytes. `this` pointer passed via ECX (thiscall).

```
Offset  Size  Type               Field
──────  ────  ────               ─────
+0x00   0x04  vtable*            LoginClient vtable pointer
+0x04   ????  ???                Internal sub-object (initialized at FUN_009c9bd0)
+0x08   0x04  Logger*            Logger pointer (from +0x0C in some decompilations)
+0x0C   0x04  Logger*            Logger instance
+0x1D   0x01  bool               initialized flag
+0x20   0x04  uint32             state (0–8, see state machine)
+0x22   0x01  uint8              encrypted flag (0=encrypt, 1=plaintext)
+0x24   0x04  uint32             detailed_state / sub-state
+0x28   0x04  ServerConfig*      server configuration object
+0x3C   0x04  Token*             session_token (for AuthSessionTokenReq)
+0x40   0x04  Token*             credential_token (for AuthInitialTokenReq)
+0x48   0x04  TCPNETSet*         network connection set
+0x4C   0x04  ITimer*            timer interface for timeouts
+0x50   0x04  uint32             pending_message_type (timeout tracker)
+0x60   0x04  BidirectionalEncryptor*  encryption engine
```

### 8.2 `Mythic::Encrypt::BidirectionalEncryptor`

Size: 0x20 (32) bytes. Constructed at `FUN_009cadb0`.

```
Offset  Size  Type               Field
──────  ────  ────               ─────
+0x00   0x04  vtable*            @ 0x00a47bf0 (8 entries)
+0x04   0x04  Curve25519*        Curve25519 algorithm instance
+0x08   0x04  RijndaelStream*    encrypt_stream (outbound AES)
+0x0C   0x04  RijndaelStream*    decrypt_stream (inbound AES)
+0x10   0x04  DataBuffer*        public_key (local ephemeral public key)
+0x14   0x04  DataBuffer*        local_secret (ephemeral Curve25519 private key)
+0x18   0x04  DataBuffer*        remote_key (static key from binary / server key)
+0x1C   0x01  bool               keys_installed (set after ECDH completes)
```

Constructor (`0x009cadb0`):
```asm
MOV dword ptr [EAX],     0xa47bf0  ; vtable
MOV dword ptr [EAX+0x04], ECX      ; curve25519 param
MOV dword ptr [EAX+0x08], EDX      ; encrypt_stream param
MOV dword ptr [EAX+0x0C], ECX_2    ; decrypt_stream param
XOR ECX, ECX
MOV dword ptr [EAX+0x10], ECX      ; public_key = NULL
MOV dword ptr [EAX+0x14], ECX      ; local_secret = NULL
MOV dword ptr [EAX+0x18], ECX      ; remote_key = NULL
MOV byte  ptr [EAX+0x1C], CL       ; keys_installed = false
```

### 8.3 `Mythic::Encrypt::RijndaelStream`

Wraps the RijndaelEngine. Vtable at `0x00a47b88`.

```
Offset  Size  Type               Field
──────  ────  ────               ─────
+0x00   0x04  vtable*            @ 0x00a47b88 (4 entries)
+0x04   ????  RijndaelEngine     Inline engine instance (see below)
```

### 8.4 `RijndaelEngine` (inline within RijndaelStream)

Constructed by `FUN_009d5870`. Total size approximately 0x214 (532) bytes.

```
Offset     Size    Type       Field
──────     ────    ────       ─────
+0x00      0x04    uint32     block_size_dwords (Nb, default 4 for AES)
+0x04      0x04    uint32     key_size_dwords (Nk: 4=AES128, 6=AES192, 8=AES256)
+0x08      0x04    uint32     num_rounds (Nr: 10/12/14)
+0x0C      0x04    ???        (reserved)
+0x10      0x04    uint32     mode (1=ECB, 2=CBC)
+0x14      ????    uint32[]   expanded key schedule (up to ~240 bytes)
+0x1F4     0x20    uint8[32]  CBC IV (8 DWORDs, zeroed on construction)
           ...
```

Key-size → rounds mapping (from lookup table at `DAT_00a47c18`):

| Nk (key dwords) | Key bits | Nr (rounds) |
|------------------|----------|-------------|
| 4 | 128 | 10 |
| 6 | 192 | 12 |
| 8 | 256 | 14 |

### 8.5 `Curve25519` Algorithm Object

Vtable at `0x00a47b08` (6 entries). Wraps the scalar multiplication primitives.

### 8.6 `DataMemoryBuffer`

Created by `FUN_00992e10` (from raw pointer + size) or `FUN_00992e60` (allocated).

```
Offset  Size  Type       Field
──────  ────  ────       ─────
+0x00   0x04  vtable*    Data buffer vtable
+0x04   0x04  ???        (metadata/refcount)
+0x08   N     uint8[]    raw data bytes
```

The vtable typically has:
- `[0]` → destructor
- `[1]` / `vtable+4` → `getData()` → returns pointer to `this+0x08`
- `[2]` / `vtable+8` → `getSize()` → returns `{size, 0}` as 64-bit

---

## 9. Vtable Maps

### 9.1 BidirectionalEncryptor Vtable (`0x00a47bf0`)

| Slot | Offset | Address | Name | Signature |
|------|--------|---------|------|-----------|
| [0] | +0x00 | `FUN_009cae00` | Destructor | `void __thiscall(int destroy_members)` |
| [1] | +0x04 | `FUN_009ca870` | SetPublicKey | `bool __thiscall(DataBuffer* key)` — validates via Curve25519, copies to `+0x10` |
| [2] | +0x08 | `FUN_009caef0` | GetPublicKey | `DataBuffer* __thiscall()` — generates keypair, does ECDH, returns pubkey |
| [3] | +0x0C | `FUN_009cafe0` | SetRemoteKey | `bool __thiscall(DataBuffer* key)` — stores at `+0x18`, triggers auto-key-setup |
| [4] | +0x10 | `FUN_009ca960` | SetStreamIVs | `bool __thiscall(DataBuffer* enc_iv, DataBuffer* dec_iv)` — installs IV on both streams |
| [5] | +0x14 | `FUN_009ca9a0` | SetKey | `bool __thiscall(DataBuffer* key)` — installs AES key on both streams, sets `+0x1C=1` |
| [6] | +0x18 | `FUN_009caae0` | EncryptMulti | `DataBuffer* __thiscall(DataBuffer** bufs, int count)` — hash + AES on multiple buffers |
| [7] | +0x1C | `FUN_009ca9e0` | EncryptSingle | `DataBuffer* __thiscall(DataBuffer* buf)` — hash + AES on single buffer |

### 9.2 RijndaelStream Vtable (`0x00a47b88`)

| Slot | Offset | Address | Name | Notes |
|------|--------|---------|------|-------|
| [0] | +0x00 | `FUN_009ca4d0` | Destructor | |
| [1] | +0x04 | `FUN_009ca1b0` | SetKey | Wrapper → `FUN_009cbf40` (validates size, expands schedule) |
| [2] | +0x08 | `FUN_009ca1c0` | SetIV | Wrapper → `FUN_009cbfc0` (copies up to 32 bytes into engine IV) |
| [3] | +0x0C | `FUN_009ca1d0` | Encrypt | AES-CBC encrypt with custom padding |

### 9.3 Curve25519 Vtable (`0x00a47b08`)

| Slot | Offset | Address | Name | Notes |
|------|--------|---------|------|-------|
| [0] | +0x00 | `FUN_009c9c10` | Destructor | |
| [1] | +0x04 | `FUN_009ca4f0` | ValidateKey (1) | Checks `size == 32` |
| [2] | +0x08 | `FUN_009ca4f0` | ValidateKey (2) | Same function |
| [3] | +0x0C | `FUN_009ca520` | GenerateKeypair | Random 32B → clamp as Curve25519 private |
| [4] | +0x10 | `FUN_009ca5f0` | DerivePublicKey | `X25519(private_key, basepoint_at_0xa47ba0)` |
| [5] | +0x14 | `FUN_009ca6b0` | SharedSecret | `X25519(param2_data, param1_data)` — note arg swap! |

### 9.4 TCPNETSet (partial, from usage in SerializeAndSendMessage)

| Slot | Offset | Name | Notes |
|------|--------|------|-------|
| [2] | +0x08 | GetConnection? | Called during init |
| [4] | +0x10 | Start/Init? | Called during init |
| [11] | +0x2C | SendMessage | `void(DataBuffer* body, uint16 msg_type)` |

---

## 10. Key Functions Reference

### 10.1 LoginClient Functions

| Address | Name | Description |
|---------|------|-------------|
| `0x009565c9` | `LoginClient::Initialize` | Creates encryptor, loads static key, initializes TCPNETSet |
| `0x009566d2` | `LoginClient::Process` | Main loop, drives state machine |
| `0x009583f0` | `LoginClient::NewConnection` | Sends VerifyProtocolReq (type 1). State 1→2 |
| `0x00957f13` | `LoginClient::SerializeAndSendMessage` | Serialize protobuf, optionally encrypt, send |
| `0x00957168` | `LoginClient::Authenticate` | Sends AuthInitialTokenReq (3) or AuthSessionTokenReq (5) |
| `0x0095858b` | `LoginClient::ReceivedMessage` | Dispatch incoming messages by type |
| `0x009580cb` | `LoginClient::DeserializeMessage` | Protobuf parse from raw bytes |
| `0x0095734b` | `HandleVerifyProtocolReply` | Processes type 2, installs IVs, sets plaintext mode |
| `0x0095749e` | `HandleAuthInitialTokenReply` | Processes type 4 |
| `0x0095758f` | `HandleAuthSessionTokenReply` | Processes type 6 |
| `0x00957b11` | `HandleGetCharSummaryReply` | Processes type 8 |
| `0x0095763f` | `HandleGetClusterListReply` | Processes type 10 |
| `0x00957d2e` | `HandleGetAcctPropListReply` | Processes type 12 |
| `0x0095959b` | (unknown) | Called at end of Initialize with a parameter from `EBP+0x08` |
| `0x0095ac84` | (SEH prolog stub) | Recovered from __SEH_prolog fix |

### 10.2 Encryption Functions

| Address | Name | Description |
|---------|------|-------------|
| `0x009c9dc0` | `EncryptorFactory` | Creates BidirectionalEncryptor. Args: `(1, 1, 0x80, 2)` → Curve25519 + AES-128-init + CBC |
| `0x009cadb0` | `BidirectionalEncryptor::ctor` | Constructor, sets vtable `0xa47bf0`, zeroes fields |
| `0x009cad00` | `AutoKeySetup` | If both local_secret and remote_key set → X25519 → install AES key |
| `0x009caef0` | `GetPublicKey` | Generates ephemeral keypair + ECDH + returns public key |
| `0x009cafe0` | `SetRemoteKey` | Stores server/static key at `+0x18` |
| `0x009ca9e0` | `EncryptSingle` | Jenkins hash (4B big-endian) + AES-CBC encrypt |
| `0x009caae0` | `EncryptMulti` | Same for multiple buffers |
| `0x009ca960` | `SetStreamIVs` | Installs IV on encrypt + decrypt streams |
| `0x009ca9a0` | `SetKey (direct)` | Installs AES key on both streams + sets installed flag |

### 10.3 AES / Rijndael Functions

| Address | Name | Description |
|---------|------|-------------|
| `0x009ca470` | `RijndaelStream::ctor` | Constructor, sets vtable `0xa47b88`, calls engine init |
| `0x009d5870` | `RijndaelEngine::ctor` | Zeroes fields, zeroes IV at +0x1F4, one-time init tables |
| `0x009ca1d0` | `RijndaelStream::Encrypt` | Concatenate + pad + AES-CBC encrypt |
| `0x009cbf40` | `SetKey (internal)` | Validate key size (16/24/32), compute Nr, expand schedule |
| `0x009cbfc0` | `SetIV` | Copy up to 32 bytes into engine IV field |
| `0x009cbd40` | `ExpandKeySchedule` | AES key schedule expansion |
| `0x009cbee0` | `SetKeySize` | Accepts 0x80/0xC0/0x100, stores key_dwords |
| `0x009cbf10` | `GetBlockSizeBits` | Returns `*this * 32` |
| `0x009cbf20` | `SetMode` | Stores mode: 1=ECB, 2=CBC |
| `0x009d0ab0` | `ProcessBlocks` | CBC encrypt loop (XOR + `FUN_009cc020`) |
| `0x009cc020` | `EncryptBlock` | Single AES block transform |
| `0x009cbcb0` | (one-time init 1) | AES S-box / table construction |
| `0x009cbcf0` | (one-time init 2) | AES table construction |
| `0x009cb770` | (one-time init 3) | AES table construction |

### 10.4 Curve25519 Functions

| Address | Name | Description |
|---------|------|-------------|
| `0x009ca520` | `GenerateKeypair` | Random 32B + clamp |
| `0x009ca5f0` | `DerivePublicKey` | `X25519(private, basepoint)` |
| `0x009ca6b0` | `SharedSecret` | `X25519(param1, param2)` (see arg order notes) |
| `0x009d5cd0` | `X25519` | Core scalar multiplication: `X25519(output, scalar, point)` |
| `0x009d9800` | (fe init) | Field element initialization for X25519 |
| `0x009d95c0` | (fe unpack) | Unpack bytes to field element |
| `0x009d6820` | (fe mul scalar) | Scalar multiply step |
| `0x009d58e0` | (fe square) | Field element squaring |
| `0x009d5d40` | (montgomery ladder) | Main Montgomery ladder |
| `0x009d6520` | (fe pack) | Pack field element to bytes |

### 10.5 TCPNET Functions

| Address | Name | Description |
|---------|------|-------------|
| `0x0096761e` | `TCPNET::ReadMessage` | Varint header reader, reads `[body_length][msg_type]` |
| `0x00966f11` | `TCPNET::ProcessOutbound` | Dequeues messages, calls SendData |
| `0x0096709e` | `TCPNET::SendData` | Winsock `send()` loop with chunk management |
| `0x009672bb` | `TCPNET::ProcessLoop` | Main network processing loop |

### 10.6 Utility Functions

| Address | Name | Description |
|---------|------|-------------|
| `0x00992e10` | `DataMemoryBuffer::ctor (wrap)` | Wraps existing pointer + size |
| `0x00992e60` | `DataMemoryBuffer::ctor (alloc)` | Allocates new buffer of given size |
| `0x00992f10` | `DataMemoryBuffer::dtor` | Destructor |
| `0x00407745` | `operator new` | Global heap allocator |
| `0x009d9820` | `__SEH_prolog` | SEH frame setup (was incorrectly marked no-return) |
| `0x009deaf0` | `ProtobufParse` | `message.ParseFromArray(data, size)` |

---

## 11. Constant Strings & Data Addresses

### 11.1 Debug/Log Strings

| Address | String | Used In |
|---------|--------|---------|
| `0x00ade40c` | (log category) | Initialize, throughout |
| `0x00ade430` | (init error string) | Initialize failure path |
| `0x00ade450` | (key setup error) | Initialize, SetRemoteKey failure |
| — | `"MythLogin::LoginClient::ReceivedMessage"` | ReceivedMessage dispatcher |
| — | `"%hs: Received message of type [%u] length [%u]"` | ReceivedMessage log |
| — | `"%hs: Failed to create message instance for type: %d"` | Unknown message type |
| — | `"%hs: Failed to parse message from array"` | DeserializeMessage |
| — | `"%hs: Failed to deserialize message type: %d"` | Deserialization error |
| — | `"%hs: Invalid remote message type: %d"` | Unhandled type in switch |
| — | `"%hs: Failed to handle reply message"` | Handler returned false |
| — | `"Failed to serialize and send VerifyProtocolReq message"` | NewConnection |

### 11.2 Key Data Addresses

| Address | Section | Size | Description |
|---------|---------|------|-------------|
| `DAT_00b54a74` | `.data` | 32 bytes | Static Curve25519 key (server public key) |
| `DAT_00a47ba0` | `.rdata` | 32 bytes | Curve25519 basepoint (`{9, 0, ..., 0}`) |
| `DAT_00a47c18` | `.rdata` | ~12 bytes | AES rounds lookup table (Nr by Nk index) |
| `DAT_00f76010` | `.data` | 64 bytes | Message factory table (16 × 4-byte pointers) |
| `DAT_00ba7cf0` | `.data` | 1 byte | AES one-time-init flag |
| `DAT_00b4e530` | `.data` | 4 bytes | Stack cookie / security cookie |

### 11.3 Vtable Addresses

| Address | Class |
|---------|-------|
| `0x00a47b08` | `Mythic::Encrypt::Curve25519` |
| `0x00a47b88` | `Mythic::Encrypt::RijndaelStream` |
| `0x00a47bf0` | `Mythic::Encrypt::BidirectionalEncryptor` |

---

## 12. TCPNET Transport Layer

### 12.1 TCPNETConnection Layout (partial)

```
Offset   Field
──────   ─────
+0x5C    current_body_length (set during header read)
+0x11C   max_message_size (compared against body_length)
```

### 12.2 Read Pipeline

`FUN_0096761e` (ReadMessage):
1. Read varint byte-by-byte via `recv(socket, &byte, 1, 0)`
2. First varint → `body_length` (stored at `this+0x5C`)
3. Validate: `body_length <= this+0x11C` (max message size)
4. Second varint → `msg_type`
5. Allocate receive buffer of `body_length` bytes
6. Read `body_length` bytes of body data
7. Construct `TCPNETMessage(msg_type, body_data, body_length)`
8. Enqueue for dispatch

### 12.3 Write Pipeline

`FUN_00966f11` (ProcessOutbound):
1. Dequeue `TCPNETMessage` from outbound queue
2. Call `FUN_0096709e` (SendData):
   - Loop calls `message->vtable[4]` (GetNextChunk) to get data pointer
   - Calls `send(socket, chunk, length, 0)`
   - Calls `message->vtable[5]` (ConsumeBytes) to advance
   - Repeats until `message->vtable[6]` (IsDone) returns true

---

## 13. Connection Lifecycle Walkthrough

### Phase 1: Initialization

```
LoginClient::Initialize(server_config_param)
  ├─ FUN_009c9bd0()                          // init internal sub-object
  ├─ encryptor = EncryptorFactory(1, 1, 0x80, 2)
  │     ├─ curve25519  = new Curve25519()
  │     ├─ enc_stream  = new RijndaelStream()   // AES-CBC mode 2
  │     ├─ dec_stream  = new RijndaelStream()   // AES-CBC mode 2
  │     └─ return new BidirectionalEncryptor(curve25519, enc_stream, dec_stream)
  ├─ this+0x60 = encryptor
  ├─ buf = DataMemoryBuffer(DAT_00b54a74, 0x20, 0)   // wrap 32-byte static key
  ├─ encryptor->vtable[3](buf)              // SetRemoteKey → stores at +0x18
  │     └─ AutoKeySetup: +0x14 is NULL → no ECDH yet
  ├─ tcpnet->vtable[4]()                    // Start network
  ├─ tcpnet->vtable[2]()                    // Get connection
  ├─ this+0x24 = server_config_param
  └─ this+0x1D = 1                          // initialized = true
```

### Phase 2: Handshake (VerifyProtocolReq)

```
LoginClient::NewConnection()               // state == 1
  ├─ state = 2
  ├─ pubkey = encryptor->vtable[2]()        // GetPublicKey
  │     ├─ Generate 32 random bytes
  │     ├─ Clamp as Curve25519 private key → store at +0x14
  │     ├─ AutoKeySetup: +0x14 AND +0x18 both set!
  │     │     ├─ shared_secret = X25519(+0x18, +0x14) = X25519(static_key, ephemeral_priv)
  │     │     ├─ enc_stream->SetKey(shared_secret)     // AES-256 key installed
  │     │     ├─ dec_stream->SetKey(shared_secret)
  │     │     ├─ wipe shared_secret
  │     │     └─ +0x1C = 1 (keys_installed)
  │     ├─ ephemeral_public = X25519(ephemeral_priv, basepoint)
  │     ├─ Store at +0x10
  │     └─ return copy of ephemeral_public
  ├─ Build VerifyProtocolReq:
  │     field 1 = 5 (crypto_type)
  │     field 2 = server_config+0x02 (protocol_version)
  │     field 3 = pubkey (32 bytes)
  └─ SerializeAndSendMessage(msg, type=1)
        ├─ protobuf_bytes = msg.SerializeToArray()     // ~38 bytes
        ├─ this+0x22 == 0 → ENCRYPT:
        │     encrypted = encryptor->vtable[7](protobuf_bytes)
        │       ├─ hash = jenkins_lookup3(protobuf_bytes)
        │       ├─ append 4-byte hash (big-endian)     // now ~42 bytes
        │       └─ AES-256-CBC encrypt with padding    // → 48 bytes
        └─ tcpnet->vtable[11](encrypted, msg_type=1)
              └─ Frame as: [varint 48][varint 1][48 encrypted bytes]
                           =  0x30     0x01     BA9F7D1A...
```

### Phase 3: Handshake Reply

```
TCPNET receives: [varint body_len][varint 2][body]
  → LoginClient::ReceivedMessage(TCPNETMessage)
    → type == 2 → HandleVerifyProtocolReply(parsed_msg)
        ├─ result = msg.field1  (expect 0)
        ├─ server_key = msg.field2
        ├─ server_iv = msg.field3
        ├─ encryptor->vtable[4](server_iv, server_key)   // SetStreamIVs
        └─ this+0x22 = 1                                  // PLAINTEXT MODE ON
```

### Phase 4: Authentication (Plaintext)

```
LoginClient::Authenticate()
  ├─ if session_token (+0x3C) exists:
  │     build AuthSessionTokenReq (type 5)
  ├─ else if credential_token (+0x40) exists:
  │     build AuthInitialTokenReq (type 3)
  └─ SerializeAndSendMessage(msg, type)
        ├─ this+0x22 == 1 → SKIP encryption
        └─ tcpnet->vtable[11](raw_protobuf, msg_type)
```

### Phase 5: Data Retrieval (Plaintext)

```
Send: GetCharSummaryListReq (type 7)  → Receive: type 8
Send: GetClusterListReq     (type 9)  → Receive: type 10
Send: GetAcctPropListReq    (type 11) → Receive: type 12
```

All plaintext protobuf, no encryption.

---

## 14. Server Emulation Notes

### 14.1 Minimal Server Requirements

A server emulator does NOT need to implement full Curve25519/AES to function:

1. **Accept** the `VerifyProtocolReq` — the encrypted body can be ignored if the server
   doesn't need to verify the client's identity cryptographically
2. **Reply** with `VerifyProtocolReply { result=0 }` — the `server_key` and `server_iv`
   fields can be arbitrary (or empty) since they're only installed as IVs that are
   never used (encryption is disabled by `this+0x22=1` immediately after)
3. **All subsequent messages** are plaintext protobuf — parse and respond normally

### 14.2 If Decryption of VerifyProtocolReq IS Required

To decrypt the client's VerifyProtocolReq:
1. Read the 32-byte static key from binary address `0x00b54a74`
2. If this key is the server's Curve25519 **private key**:
   - Compute: `shared = X25519(server_private, client_ephemeral_public)`
   - But the client's public key is INSIDE the encrypted message (chicken-and-egg)
3. Resolution: The server must know the **private key** corresponding to the static
   public key embedded in the client binary. With that private key, it cannot decrypt
   VerifyProtocolReq (since the client's ephemeral public is inside). This suggests
   the static key at `0xb54a74` might actually be a **pre-shared symmetric key** used
   differently than pure ECDH, OR the server simply validates the client has the
   correct static key by checking the AES decryption succeeds.
4. **Runtime approach**: Attach a debugger, break at `0x009CA76E`, read the 32-byte
   shared secret. This is session-specific.

### 14.3 Protobuf Compatibility

The client uses **proto2** syntax. The login messages are in the `login` package.
Field numbering and wire types must match exactly:

```protobuf
syntax = "proto2";
package login;

message VerifyProtocolReq {
    optional uint32 crypto_type      = 1;
    optional uint32 protocol_version = 2;
    optional bytes  public_key       = 3;
}

message VerifyProtocolReply {
    optional int32 result     = 1;
    optional bytes server_key = 2;
    optional bytes server_iv  = 3;
}
```

### 14.4 Important Gotchas

1. **Wire header order is `[body_length, msg_type]`** — NOT `[msg_type, body_length]`
2. **Message types are 1-indexed, odd = C→S, even = S→C**
3. **The factory table has 16 slots** — type values 0 and 14–15 are unused/null
4. **Encryption applies to ONE message only** — VerifyProtocolReq (type 1)
5. **Server→client messages are NEVER encrypted** at the LoginClient layer
6. **The SEH prolog at `0x009d9820`** was incorrectly marked as no-return in Ghidra,
   causing ~90% of LoginClient functions to appear as 2-instruction stubs. Must be
   fixed before meaningful analysis.

---

## Appendix A: Ghidra Analysis Notes

### A.1 SEH Prolog Fix

`FUN_009d9820` (`__SEH_prolog`) sets up Structured Exception Handling frames. Ghidra's
default analysis marked it as no-return, causing the decompiler to truncate every
calling function after the SEH setup. Fix:

1. Navigate to `FUN_009d9820`
2. Edit function properties → uncheck "No Return"
3. Clear all bytes of affected calling functions
4. Re-run "Disassemble" and "Create Function" on each affected address
5. Re-run decompiler analysis

~14 of 15 affected functions in the LoginClient module were successfully recovered.

### A.2 Factory Construction Call

```c
// FUN_009c9dc0(curve_type, aes_type, key_bits, mode)
// curve_type=1 → Curve25519
// aes_type=1   → RijndaelStream
// key_bits=0x80 → 128-bit default (overridden by ECDH output size)
// mode=2       → CBC
encryptor = FUN_009c9dc0(1, 1, 0x80, 2);
```

The `key_bits=0x80` sets an initial AES-128 configuration, but when the 32-byte
ECDH shared secret is installed via `SetKey`, it overrides to AES-256 (since
`FUN_009cbf40` accepts 16/24/32-byte keys and recomputes Nk/Nr accordingly).
