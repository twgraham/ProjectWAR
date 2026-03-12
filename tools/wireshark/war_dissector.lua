-- =============================================================================
-- Wireshark Lua Dissector for the WAR Game Server Protocol
-- =============================================================================
--
-- Install:
--   Copy this file to your Wireshark plugins directory:
--     Windows: %APPDATA%\Wireshark\plugins\
--     Linux:   ~/.local/lib/wireshark/plugins/
--   Or load via: Edit → Preferences → Protocols → Lua → Script file
--
-- Usage:
--   The dissector auto-registers on TCP port 10300.
--   Change the port via: Decode As → TCP port → WAR
--
--   After loading, two protocol entries appear in the filter bar:
--     "war_c2s" — Client → Server packets
--     "war_s2c" — Server → Client packets
--
--   The dissector tracks the RC4 encryption handshake per TCP stream.
--   Once F_ENCRYPTKEY is seen with a 256-byte key, subsequent packets on
--   that stream are decrypted automatically using the MythicRC4 algorithm.
--
-- =============================================================================

-- ---------------------------------------------------------------------------
-- Opcode name table
-- ---------------------------------------------------------------------------
local opcode_names = {
    [0x00] = "F_RRQ",
    [0x01] = "F_UNK1",
    [0x02] = "F_QUEST",
    [0x03] = "F_UPDATE_SIEGE_LOOK_AT",
    [0x04] = "F_PLAYER_EXIT",
    [0x05] = "F_PLAYER_HEALTH",
    [0x06] = "F_CHAT",
    [0x07] = "F_TEXT",
    [0x09] = "F_OBJECT_STATE",
    [0x0A] = "F_OBJECT_DEATH",
    [0x0B] = "F_PING",
    [0x0C] = "F_PLAYER_QUIT",
    [0x0D] = "F_DUMP_STATICS",
    [0x0E] = "F_WAR_REPORT",
    [0x0F] = "F_CONNECT",
    [0x10] = "F_DISCONNECT",
    [0x11] = "F_HEARTBEAT",
    [0x13] = "F_REQUEST_CHAR_TEMPLATES",
    [0x14] = "F_HIT_PLAYER",
    [0x15] = "F_DEATHSPAM",
    [0x16] = "F_REQUEST_INIT_OBJECT",
    [0x17] = "F_OPEN_GAME",
    [0x18] = "F_PLAYER_INFO",
    [0x19] = "F_WORLD_ENTER",
    [0x1A] = "F_CAMPAIGN_STATUS",
    [0x1B] = "F_REQ_CAMPAIGN_STATUS",
    [0x1D] = "F_GUILD_DATA",
    [0x1E] = "F_MAX_VELOCITY",
    [0x1F] = "F_SWITCH_REGION",
    [0x20] = "F_PET_INFO",
    [0x21] = "F_PLAYER_CLEAR_DEATH",
    [0x22] = "F_COMMAND_CONTROLLED",
    [0x25] = "F_GUILD_COMMAND",
    [0x26] = "F_RENAME_CHARACTER",
    [0x27] = "F_REQUEST_TOK_REWARD",
    [0x28] = "F_SURVEY_BEGIN",
    [0x29] = "F_SHOW_DIALOG",
    [0x2A] = "F_PLAYERORG_APPROVAL",
    [0x2B] = "F_QUEST_INFO",
    [0x2C] = "F_RANDOM_NAME_LIST_INFO",
    [0x2F] = "F_INVITE_GROUP",
    [0x30] = "F_JOIN_GROUP",
    [0x31] = "F_PLAYER_DEATH",
    [0x35] = "F_DUMP_ARENAS_LARGE",
    [0x37] = "F_GROUP_COMMAND",
    [0x38] = "F_ZONEJUMP",
    [0x39] = "F_PLAYER_EXPERIENCE",
    [0x3A] = "F_XENON_VOICE",
    [0x40] = "F_REQUEST_WORLD_LARGE",
    [0x41] = "F_ACTION_COUNTER_INFO",
    [0x44] = "F_ACTION_COUNTER_UPDATE",
    [0x46] = "F_PLAYER_STATS",
    [0x47] = "F_MONSTER_STATS",
    [0x48] = "F_PLAY_EFFECT",
    [0x49] = "F_REMOVE_PLAYER",
    [0x4A] = "F_ZONEJUMP_FAILED",
    [0x4B] = "F_TRADE_STATUS",
    [0x4E] = "F_PLAYER_RENOWN",
    [0x4F] = "F_MOUNT_UPDATE",
    [0x50] = "F_PLAYER_LEVEL_UP",
    [0x51] = "F_ANIMATION",
    [0x52] = "F_PLAYER_WEALTH",
    [0x53] = "F_TROPHY_SETLOCATION",
    [0x54] = "F_REQUEST_CHAR",
    [0x55] = "F_REQUEST_CHAR_RESPONSE",
    [0x56] = "F_REQUEST_CHAR_ERROR",
    [0x57] = "F_CHARACTER_PREFS",
    [0x58] = "F_SEND_CHARACTER_RESPONSE",
    [0x59] = "F_SEND_CHARACTER_ERROR",
    [0x5A] = "F_PING_DATAGRAM",
    [0x5C] = "F_ENCRYPTKEY",
    [0x5D] = "F_PQLOOT_TRIGGER",
    [0x5E] = "F_SET_TARGET",
    [0x60] = "F_MYSTERY_BAG",
    [0x61] = "F_PLAY_SOUND",
    [0x62] = "F_PLAYER_STATE2",
    [0x63] = "F_QUERY_NAME",
    [0x64] = "F_QUERY_NAME_RESPONSE",
    [0x65] = "F_ADD_NAME",
    [0x68] = "F_DELETE_NAME",
    [0x6A] = "F_CHECK_NAME",
    [0x6B] = "F_CHECK_NAME_RESPONSE",
    [0x6F] = "F_LOCALIZED_STRING",
    [0x70] = "F_KILLING_SPREE",
    [0x71] = "F_CREATE_STATIC",
    [0x72] = "F_CREATE_MONSTER",
    [0x73] = "F_PLAYER_IMAGENUM",
    [0x75] = "F_TRANSFER_ITEM",
    [0x79] = "F_CRAFTING_STATUS",
    [0x7A] = "F_REQUEST_LASTNAME",
    [0x7C] = "F_INIT_PLAYER",
    [0x7D] = "F_REQUEST_INIT_PLAYER",
    [0x7E] = "F_SET_ABILITY_TIMER",
    [0x80] = "S_PID_ASSIGN",
    [0x81] = "S_PONG",
    [0x82] = "S_CONNECTED",
    [0x83] = "S_WORLD_SENT",
    [0x84] = "S_NOT_CONNECTED",
    [0x85] = "S_GAME_OPENED",
    [0x86] = "F_MAIL",
    [0x87] = "S_DATAGRAM_ESTABLISHED",
    [0x88] = "S_PLAYER_INITTED",
    [0x89] = "S_PLAYER_LOADED",
    [0x8A] = "F_RECEIVE_ENCRYPTKEY",
    [0x8C] = "F_MORALE_LIST",
    [0x8D] = "F_SURVEY_ADDQUESTION",
    [0x8E] = "F_SURVEY_END",
    [0x8F] = "F_SURVEY_RESULT",
    [0x90] = "F_EMOTE",
    [0x91] = "F_CREATE_CHARACTER",
    [0x92] = "F_DELETE_CHARACTER",
    [0x93] = "F_GFX_MOD",
    [0x94] = "F_INSTANCE_INFO",
    [0x95] = "F_BAG_INFO",
    [0x96] = "F_KEEP_STATUS",
    [0x97] = "F_PLAY_TIME_STATS",
    [0x98] = "F_CATAPULT",
    [0x99] = "F_GRAVITY_UPDATE",
    [0x9A] = "F_HELP_DATA",
    [0x9B] = "F_UPDATE_LASTNAME",
    [0x9E] = "F_GET_CULTIVATION_INFO",
    [0x9F] = "F_CRASH_PACKET",
    [0xA0] = "F_LOGINQUEUE",
    [0xA1] = "F_INTERRUPT",
    [0xA2] = "F_INSTANCE_SELECTED",
    [0xA3] = "F_ACTIVE_EFFECTS",
    [0xA5] = "F_SERVER_INFO",
    [0xA6] = "F_START_SIEGE_MULTIUSER",
    [0xA7] = "F_SIEGE_WEAPON_RESULTS",
    [0xA8] = "F_INTERACT_QUEUE",
    [0xA9] = "F_UPDATE_HOT_SPOT",
    [0xAA] = "F_GET_ITEM",
    [0xAB] = "F_DUEL",
    [0xAC] = "F_PLAYER_JUMP",
    [0xAD] = "F_INTRO_CINEMA",
    [0xAE] = "F_MAGUS_DISC_UPDATE",
    [0xAF] = "F_FIRE_SIEGE_WEAPON",
    [0xB0] = "F_GRAPHICAL_REVISION",
    [0xB2] = "F_AUCTION_POST_ITEM",
    [0xB3] = "F_CAST_PLAYER_EFFECT",
    [0xB4] = "F_AUCTION_SEARCH_QUERY",
    [0xB5] = "F_FLIGHT",
    [0xB6] = "F_SOCIAL_NETWORK",
    [0xB7] = "F_AUCTION_SEARCH_RESULT",
    [0xB8] = "F_PLAYER_ENTER_FULL",
    [0xB9] = "F_UPDATE_ITEM_COOLDOWN",
    [0xBB] = "F_AUCTION_BID_ITEM",
    [0xBC] = "F_ESTABLISH_DATAGRAM",
    [0xBD] = "F_PLAYER_INVENTORY",
    [0xBE] = "F_CHARACTER_INFO",
    [0xBF] = "F_INIT_STORE",
    [0xC0] = "F_STORE_BUY_BACK",
    [0xC1] = "F_OBJECTIVE_INFO",
    [0xC2] = "F_OBJECTIVE_UPDATE",
    [0xC3] = "F_SCENARIO_INFO",
    [0xC4] = "F_SCENARIO_POINT_UPDATE",
    [0xC5] = "F_OBJECTIVE_STATE",
    [0xC6] = "F_REALM_BONUS",
    [0xC7] = "F_OBJECTIVE_CONTROL",
    [0xC8] = "F_INTERFACE_COMMAND",
    [0xC9] = "F_SCENARIO_PLAYER_INFO",
    [0xCA] = "F_FLAG_OBJECT_STATE",
    [0xCB] = "F_FLAG_OBJECT_LOCATION",
    [0xCC] = "F_CITY_CAPTURE",
    [0xCD] = "F_ZONE_CAPTURE",
    [0xCE] = "F_SALVAGE_ITEM",
    [0xCF] = "F_AUCTION_BID_STATUS",
    [0xD0] = "F_PUNKBUSTER",
    [0xD1] = "F_ITEM_SET_DATA",
    [0xD2] = "F_INTERACT",
    [0xD5] = "F_DO_ABILITY",
    [0xD6] = "F_SET_TIME",
    [0xD7] = "F_INIT_EFFECTS",
    [0xD8] = "F_GROUP_STATUS",
    [0xD9] = "F_USE_ITEM",
    [0xDA] = "F_USE_ABILITY",
    [0xDB] = "F_INFLUENCE_DETAILS",
    [0xDC] = "F_SWITCH_ATTACK_MODE",
    [0xDD] = "F_BUG_REPORT",
    [0xDE] = "F_OBJECT_EFFECT_STATE",
    [0xE2] = "F_EXPERIENCE_TABLE",
    [0xE3] = "F_CREATE_PLAYER",
    [0xE4] = "F_UPDATE_STATE",
    [0xE5] = "F_UI_MOD",
    [0xE7] = "F_RVR_STATS",
    [0xE8] = "F_CLIENT_DATA",
    [0xE9] = "F_INTERACT_RESPONSE",
    [0xEA] = "F_QUEST_LIST",
    [0xEB] = "F_QUEST_UPDATE",
    [0xEC] = "F_REQUEST_QUEST",
    [0xED] = "F_QUEST_LIST_UPDATE",
    [0xEE] = "F_CAREER_CATEGORY",
    [0xEF] = "F_PLAYER_INIT_COMPLETE",
    [0xF1] = "F_CAREER_PACKAGE_UPDATE",
    [0xF2] = "F_BUY_CAREER_PACKAGE",
    [0xF3] = "F_CAREER_PACKAGE_INFO",
    [0xF4] = "F_PLAYER_RANK_UPDATE",
    [0xF5] = "F_DO_ABILITY_AT_POS",
    [0xF6] = "F_CHANNEL_LIST",
    [0xF7] = "F_TACTICS",
    [0xF8] = "F_TOK_ENTRY_UPDATE",
    [0xF9] = "F_TRADE_SKILL_UPDATE",
    [0xFA] = "F_RENDER_PRIMITIVE",
    [0xFB] = "F_INFLUENCE_UPDATE",
    [0xFC] = "F_INFLUENCE_INFO",
    [0xFD] = "F_KNOCKBACK",
    [0xFE] = "F_PLAY_VOICE_OVER",
    [0xFF] = "MAX_GAME_OPCODE",
}

local function opcode_name(opcode)
    return opcode_names[opcode] or string.format("UNKNOWN_0x%02X", opcode)
end

-- Field extractor for the TCP stream index (used to key per-connection state)
local f_tcp_stream = Field.new("tcp.stream")

local function get_tcp_stream()
    local field_val = f_tcp_stream()
    if field_val then return field_val.value end
    return 0
end

-- ---------------------------------------------------------------------------
-- MythicRC4 implementation (non-standard)
--
-- The algorithm differs from standard RC4 in two ways:
--   1. Data is processed in two phases: second half first, then first half.
--   2. The state variable `y` is fed back from plaintext bytes (not ciphertext
--      on encrypt, and post-XOR on decrypt).
--
-- The C# MythicRc4.DecryptCore always starts with x=0, y=0 and copies the
-- base key fresh for every packet — there is NO state carried between packets.
-- This means each packet is independently decryptable with just the base key.
-- ---------------------------------------------------------------------------

-- Deep-copy a 256-entry table (used to snapshot RC4 state)
local function copy_key(t)
    local c = {}
    for i = 0, 255 do c[i] = t[i] end
    return c
end

--- Perform MythicRC4 decryption on a raw byte string.
--- Each call starts fresh with x=0, y=0 (matching C# DecryptCore behavior).
--- @param base_key table  256-entry table [0..255] of byte values
--- @param data     string ciphertext (raw bytes)
--- @return string decrypted raw bytes
local function mythic_rc4_decrypt(base_key, data)
    local wk = copy_key(base_key)
    local x = 0
    local y = 0
    local len = #data
    local midpoint = math.floor(len / 2)

    -- Convert to mutable byte array (1-based)
    local buf = {}
    for i = 1, len do
        buf[i] = string.byte(data, i)
    end

    -- Phase 1: process second half first (midpoint+1 .. len)  [1-based]
    -- C# equivalent: for (pos = midpoint; pos < length; ++pos)  [0-based]
    for pos = midpoint + 1, len do
        x = (x + 1) % 256
        y = (y + wk[x]) % 256

        -- swap
        wk[x], wk[y] = wk[y], wk[x]

        -- keystream byte
        local tmp = (wk[x] + wk[y]) % 256

        -- XOR with keystream (decrypt)
        buf[pos] = bit.bxor(buf[pos], wk[tmp])

        -- NON-STANDARD: update y with plaintext byte (after XOR)
        y = (y + buf[pos]) % 256
    end

    -- Phase 2: process first half (1 .. midpoint)
    -- C# equivalent: for (pos = 0; pos < midpoint; ++pos)  [0-based]
    for pos = 1, midpoint do
        x = (x + 1) % 256
        y = (y + wk[x]) % 256

        -- swap
        wk[x], wk[y] = wk[y], wk[x]

        -- keystream byte
        local tmp = (wk[x] + wk[y]) % 256

        -- XOR with keystream (decrypt)
        buf[pos] = bit.bxor(buf[pos], wk[tmp])

        -- NON-STANDARD: update y with plaintext byte (after XOR)
        y = (y + buf[pos]) % 256
    end

    -- Convert back to string
    local out = {}
    for i = 1, len do
        out[i] = string.char(buf[i])
    end

    return table.concat(out)
end

--- Convert a raw binary string to a hex string suitable for ByteArray.new().
local function raw_to_hex(s)
    local hex = {}
    for i = 1, #s do
        hex[i] = string.format("%02x", string.byte(s, i))
    end
    return table.concat(hex)
end

-- ---------------------------------------------------------------------------
-- Per-stream encryption state tracking
--
-- We key state on the TCP stream index. For each stream we store:
--   .base_key            - the 256-byte RC4 key (table[0..255])
--   .encrypted           - bool: whether encryption is active
--   .client_port         - the TCP port of the client (so we know direction)
--   .encrypt_after_frame - frame# after which encryption starts
--
-- Since MythicRC4 resets x=0, y=0 and copies the key fresh for every packet,
-- there is NO state carried between packets. Each packet is independently
-- decryptable — no frame-state snapshots needed.
--
-- NOTE: C2S encryption covers header+payload (after size prefix).
--       S2C encryption covers opcode+payload together (after size prefix).
--       When encrypted the wire format is [2: size][1+size bytes: encrypted(opcode+payload)].
--       When unencrypted it is              [2: size][1: opcode][size bytes: payload].
--       In both cases total_length = 2 + 1 + payload_size.
-- ---------------------------------------------------------------------------

local stream_state = {}

local function get_stream(stream_index)
    if not stream_state[stream_index] then
        stream_state[stream_index] = {
            base_key = nil,
            encrypted = false,
            client_port = nil,
            encrypt_after_frame = nil,
        }
    end
    return stream_state[stream_index]
end

-- ---------------------------------------------------------------------------
-- Protocol definitions
-- ---------------------------------------------------------------------------

-- Client → Server protocol
local proto_c2s = Proto("war_c2s", "WAR Game Protocol (Client → Server)")

local pf_c2s_size       = ProtoField.uint16("war_c2s.size",       "Packet Size",    base.DEC)
local pf_c2s_seq        = ProtoField.uint16("war_c2s.seq",        "Sequence ID",    base.DEC)
local pf_c2s_session    = ProtoField.uint16("war_c2s.session",    "Session ID",     base.HEX)
local pf_c2s_unk1       = ProtoField.uint16("war_c2s.unk1",       "Unknown1",       base.HEX)
local pf_c2s_unk2       = ProtoField.uint8 ("war_c2s.unk2",       "Unknown2",       base.HEX)
local pf_c2s_opcode     = ProtoField.uint8 ("war_c2s.opcode",     "Opcode",         base.HEX)
local pf_c2s_opname     = ProtoField.string("war_c2s.opcode_name","Opcode Name")
local pf_c2s_payload    = ProtoField.bytes ("war_c2s.payload",    "Payload")
local pf_c2s_encrypted  = ProtoField.bytes ("war_c2s.encrypted",  "Encrypted Data")
local pf_c2s_decrypted  = ProtoField.bytes ("war_c2s.decrypted",  "Decrypted Data")
local pf_c2s_enc_header = ProtoField.bytes ("war_c2s.enc_header", "Encrypted Header")
local pf_c2s_enc_payload= ProtoField.bytes ("war_c2s.enc_payload","Encrypted Payload")

-- EncryptKeyRequest fields
local pf_ek_cipher      = ProtoField.uint8 ("war_c2s.ek.cipher",      "Cipher",      base.DEC)
local pf_ek_app         = ProtoField.uint8 ("war_c2s.ek.application", "Application", base.DEC)
local pf_ek_major       = ProtoField.uint8 ("war_c2s.ek.major",       "Major",       base.DEC)
local pf_ek_minor       = ProtoField.uint8 ("war_c2s.ek.minor",       "Minor",       base.DEC)
local pf_ek_revision    = ProtoField.uint8 ("war_c2s.ek.revision",    "Revision",    base.DEC)
local pf_ek_unk         = ProtoField.uint8 ("war_c2s.ek.unk1",        "Unk1",        base.HEX)
local pf_ek_key         = ProtoField.bytes ("war_c2s.ek.key",         "RC4 Key")

-- Ping fields
local pf_ping_ts        = ProtoField.uint32("war_c2s.ping.timestamp", "Timestamp",   base.DEC)

proto_c2s.fields = {
    pf_c2s_size, pf_c2s_seq, pf_c2s_session, pf_c2s_unk1, pf_c2s_unk2,
    pf_c2s_opcode, pf_c2s_opname, pf_c2s_payload,
    pf_c2s_encrypted, pf_c2s_decrypted, pf_c2s_enc_header, pf_c2s_enc_payload,
    pf_ek_cipher, pf_ek_app, pf_ek_major, pf_ek_minor, pf_ek_revision, pf_ek_unk, pf_ek_key,
    pf_ping_ts,
}

-- Server → Client protocol
local proto_s2c = Proto("war_s2c", "WAR Game Protocol (Server → Client)")

local pf_s2c_size       = ProtoField.uint16("war_s2c.size",       "Payload Size",   base.DEC)
local pf_s2c_opcode     = ProtoField.uint8 ("war_s2c.opcode",     "Opcode",         base.HEX)
local pf_s2c_opname     = ProtoField.string("war_s2c.opcode_name","Opcode Name")
local pf_s2c_payload    = ProtoField.bytes ("war_s2c.payload",    "Payload")
local pf_s2c_encrypted  = ProtoField.bytes ("war_s2c.encrypted",  "Encrypted Payload")
local pf_s2c_decrypted  = ProtoField.bytes ("war_s2c.decrypted",  "Decrypted Payload")

-- EncryptKeyResponse fields
local pf_ekr_status     = ProtoField.uint8 ("war_s2c.ekr.status", "Status",         base.DEC)

-- Pong fields
local pf_pong_client_ts = ProtoField.uint32("war_s2c.pong.client_timestamp", "Client Timestamp", base.DEC)
local pf_pong_server_ts = ProtoField.uint64("war_s2c.pong.server_timestamp", "Server Timestamp", base.DEC)
local pf_pong_seq       = ProtoField.uint32("war_s2c.pong.sequence",         "Sequence",         base.DEC)
local pf_pong_unk1      = ProtoField.uint32("war_s2c.pong.unk1",             "Unk1",             base.HEX)

proto_s2c.fields = {
    pf_s2c_size, pf_s2c_opcode, pf_s2c_opname, pf_s2c_payload,
    pf_s2c_encrypted, pf_s2c_decrypted,
    pf_ekr_status,
    pf_pong_client_ts, pf_pong_server_ts, pf_pong_seq, pf_pong_unk1,
}

-- ---------------------------------------------------------------------------
-- Expert info
-- ---------------------------------------------------------------------------
local ef_c2s_unknown_opcode = ProtoExpert.new("war_c2s.expert.unknown_opcode",
    "Unknown opcode", expert.group.UNDECODED, expert.severity.WARN)
local ef_c2s_encrypted     = ProtoExpert.new("war_c2s.expert.encrypted",
    "Encrypted packet (no key available)", expert.group.DECRYPTION, expert.severity.NOTE)
local ef_c2s_key_installed = ProtoExpert.new("war_c2s.expert.key_installed",
    "RC4 encryption key installed", expert.group.SECURITY, expert.severity.CHAT)

local ef_s2c_unknown_opcode = ProtoExpert.new("war_s2c.expert.unknown_opcode",
    "Unknown opcode", expert.group.UNDECODED, expert.severity.WARN)
local ef_s2c_encrypted      = ProtoExpert.new("war_s2c.expert.encrypted",
    "Encrypted packet (no key available)", expert.group.DECRYPTION, expert.severity.NOTE)

proto_c2s.experts = { ef_c2s_unknown_opcode, ef_c2s_encrypted, ef_c2s_key_installed }
proto_s2c.experts = { ef_s2c_unknown_opcode, ef_s2c_encrypted }

-- ---------------------------------------------------------------------------
-- Helpers
-- ---------------------------------------------------------------------------

--- Convert a ByteArray or raw string to a 256-entry table keyed [0..255].
local function bytes_to_key_table(byte_str)
    local t = {}
    for i = 0, 255 do
        t[i] = string.byte(byte_str, i + 1)
    end
    return t
end

-- ---------------------------------------------------------------------------
-- Client → Server dissector
-- ---------------------------------------------------------------------------
function proto_c2s.dissector(tvbuf, pinfo, tree)
    local buf_len = tvbuf:len()
    if buf_len == 0 then return 0 end

    local bytes_consumed = 0

    while bytes_consumed < buf_len do
        -- Need at least 2 bytes for the size prefix
        if buf_len - bytes_consumed < 2 then
            -- Ask for more data
            pinfo.desegment_len = DESEGMENT_ONE_MORE_SEGMENT
            pinfo.desegment_offset = bytes_consumed
            return
        end

        local packet_size = tvbuf(bytes_consumed, 2):uint()
        local payload_length = packet_size + 2
        local total_length = 2 + 8 + payload_length  -- size_prefix + header + payload

        if buf_len - bytes_consumed < total_length then
            -- Incomplete packet, request reassembly
            pinfo.desegment_len = total_length - (buf_len - bytes_consumed)
            pinfo.desegment_offset = bytes_consumed
            return
        end

        -- We have a complete packet
        local pkt_tvb = tvbuf(bytes_consumed, total_length)
        local subtree = tree:add(proto_c2s, pkt_tvb, "WAR C2S Packet")

        subtree:add(pf_c2s_size, tvbuf(bytes_consumed, 2))

        local tcp_stream = get_tcp_stream()
        local state = get_stream(tcp_stream)

        -- Remember client port on first packet
        if state.client_port == nil then
            state.client_port = pinfo.src_port
        end

        local header_offset = bytes_consumed + 2
        local is_encrypted = state.encrypted and state.base_key ~= nil
            and state.encrypt_after_frame ~= nil
            and pinfo.number > state.encrypt_after_frame

        if is_encrypted then
            -- === Original (Encrypted) subtree ===
            local enc_tree = subtree:add(proto_c2s, tvbuf(header_offset, 8 + payload_length),
                "Original (Encrypted)")
            enc_tree:add(pf_c2s_encrypted,   tvbuf(header_offset, 8 + payload_length))
            enc_tree:add(pf_c2s_enc_header,  tvbuf(header_offset, 8))
            if payload_length > 0 then
                enc_tree:add(pf_c2s_enc_payload, tvbuf(header_offset + 8, payload_length))
            end

            -- Decrypt: grab raw bytes for header+payload
            local raw = tvbuf:raw(header_offset, 8 + payload_length)

            -- Each packet is independently decrypted (x=0, y=0, fresh key copy)
            local decrypted = mythic_rc4_decrypt(state.base_key, raw)

            -- Create a TVB from decrypted data (ByteArray.new expects hex string)
            local dec_tvb = ByteArray.new(raw_to_hex(decrypted)):tvb("Decrypted C2S")

            -- === Decrypted subtree ===
            local dec_tree = subtree:add(proto_c2s, tvbuf(header_offset, 8 + payload_length),
                "Decrypted")
            dec_tree:add(pf_c2s_decrypted, dec_tvb())

            -- Parse decrypted header
            dec_tree:add(pf_c2s_seq,     dec_tvb(0, 2))
            dec_tree:add(pf_c2s_session, dec_tvb(2, 2))
            dec_tree:add(pf_c2s_unk1,    dec_tvb(4, 2))
            dec_tree:add(pf_c2s_unk2,    dec_tvb(6, 1))

            local opcode = dec_tvb(7, 1):uint()
            dec_tree:add(pf_c2s_opcode, dec_tvb(7, 1))
            dec_tree:add(pf_c2s_opname, dec_tvb(7, 1), opcode_name(opcode))

            if payload_length > 0 then
                dec_tree:add(pf_c2s_payload, dec_tvb(8, payload_length))
            end

            pinfo.cols.protocol:set("WAR C2S")
            pinfo.cols.info:set(string.format("[Decrypted] %s (0x%02X)", opcode_name(opcode), opcode))

            if not opcode_names[opcode] then
                subtree:add_proto_expert_info(ef_c2s_unknown_opcode)
            end
        else
            -- Unencrypted packet — parse header directly
            subtree:add(pf_c2s_seq,     tvbuf(header_offset,     2))
            subtree:add(pf_c2s_session, tvbuf(header_offset + 2, 2))
            subtree:add(pf_c2s_unk1,    tvbuf(header_offset + 4, 2))
            subtree:add(pf_c2s_unk2,    tvbuf(header_offset + 6, 1))

            local opcode = tvbuf(header_offset + 7, 1):uint()
            subtree:add(pf_c2s_opcode,  tvbuf(header_offset + 7, 1))
            subtree:add(pf_c2s_opname,  tvbuf(header_offset + 7, 1), opcode_name(opcode))

            local payload_offset = header_offset + 8

            if payload_length > 0 then
                subtree:add(pf_c2s_payload, tvbuf(payload_offset, payload_length))
            end

            pinfo.cols.protocol:set("WAR C2S")
            pinfo.cols.info:set(string.format("%s (0x%02X)", opcode_name(opcode), opcode))

            -- Decode F_ENCRYPTKEY payload
            if opcode == 0x5C and payload_length >= 6 then
                local ek_tree = subtree:add(proto_c2s, tvbuf(payload_offset, payload_length), "EncryptKey Request")
                ek_tree:add(pf_ek_cipher,   tvbuf(payload_offset,     1))
                ek_tree:add(pf_ek_app,      tvbuf(payload_offset + 1, 1))
                ek_tree:add(pf_ek_major,    tvbuf(payload_offset + 2, 1))
                ek_tree:add(pf_ek_minor,    tvbuf(payload_offset + 3, 1))
                ek_tree:add(pf_ek_revision, tvbuf(payload_offset + 4, 1))
                ek_tree:add(pf_ek_unk,      tvbuf(payload_offset + 5, 1))

                local cipher = tvbuf(payload_offset, 1):uint()
                local key_len = payload_length - 6

                if cipher == 1 and key_len >= 256 then
                    ek_tree:add(pf_ek_key, tvbuf(payload_offset + 6, 256))

                    -- Install encryption key for this stream
                    local key_bytes = tvbuf:raw(payload_offset + 6, 256)
                    state.base_key = bytes_to_key_table(key_bytes)
                    state.encrypted = true
                    state.encrypt_after_frame = pinfo.number

                    subtree:add_proto_expert_info(ef_c2s_key_installed,
                        string.format("RC4 key installed (%d bytes)",
                            key_len))

                    pinfo.cols.info:append(" [RC4 KEY INSTALLED]")
                elseif cipher == 0 then
                    pinfo.cols.info:append(" [No Encryption]")
                elseif key_len > 0 then
                    ek_tree:add(pf_ek_key, tvbuf(payload_offset + 6, key_len))
                end
            end

            if not opcode_names[opcode] then
                subtree:add_proto_expert_info(ef_c2s_unknown_opcode)
            end
        end

        bytes_consumed = bytes_consumed + total_length
    end

    return bytes_consumed
end

-- ---------------------------------------------------------------------------
-- Server → Client dissector
-- ---------------------------------------------------------------------------
function proto_s2c.dissector(tvbuf, pinfo, tree)
    local buf_len = tvbuf:len()
    if buf_len == 0 then return 0 end

    local bytes_consumed = 0

    while bytes_consumed < buf_len do
        -- Always need at least 2 bytes for the size prefix
        if buf_len - bytes_consumed < 2 then
            pinfo.desegment_len = DESEGMENT_ONE_MORE_SEGMENT
            pinfo.desegment_offset = bytes_consumed
            return
        end

        local tcp_stream = get_tcp_stream()
        local state = get_stream(tcp_stream)
        local is_encrypted = state.encrypted and state.base_key ~= nil
            and state.encrypt_after_frame ~= nil
            and pinfo.number > state.encrypt_after_frame

        local payload_size = tvbuf(bytes_consumed, 2):uint()

        -- Wire layout (both encrypted and unencrypted):
        --   [2: size (payload bytes only, NOT counting opcode)]
        --   [1 + payload_size: opcode + payload]  (encrypted or plaintext)
        local total_length = 2 + 1 + payload_size

        if buf_len - bytes_consumed < total_length then
            pinfo.desegment_len = total_length - (buf_len - bytes_consumed)
            pinfo.desegment_offset = bytes_consumed
            return
        end

        local pkt_tvb = tvbuf(bytes_consumed, total_length)
        local subtree = tree:add(proto_s2c, pkt_tvb, "WAR S2C Packet")

        subtree:add(pf_s2c_size, tvbuf(bytes_consumed, 2))

        if is_encrypted then
            -- Encrypted blob = opcode (1 byte) + payload (payload_size bytes)
            local enc_offset = bytes_consumed + 2
            local enc_size = payload_size + 1

            -- === Original (Encrypted) subtree ===
            local enc_tree = subtree:add(proto_s2c, tvbuf(enc_offset, enc_size),
                "Original (Encrypted)")
            enc_tree:add(pf_s2c_encrypted, tvbuf(enc_offset, enc_size))

            -- Each packet is independently decrypted (x=0, y=0, fresh key copy)
            local raw = tvbuf:raw(enc_offset, enc_size)
            local decrypted = mythic_rc4_decrypt(state.base_key, raw)

            -- First decrypted byte is the opcode; the rest is the payload.
            local opcode = string.byte(decrypted, 1)
            local dec_payload_size = payload_size  -- size field does not count the opcode

            -- === Decrypted subtree ===
            local dec_tvb = ByteArray.new(raw_to_hex(decrypted)):tvb("Decrypted S2C")
            local dec_tree = subtree:add(proto_s2c, tvbuf(enc_offset, enc_size),
                "Decrypted")
            dec_tree:add(pf_s2c_decrypted, dec_tvb())
            dec_tree:add(pf_s2c_opcode, dec_tvb(0, 1))
            dec_tree:add(pf_s2c_opname, dec_tvb(0, 1), opcode_name(opcode))

            -- Decode known payload types (offset 1 = skip the opcode byte)
            dissect_s2c_payload(opcode, dec_tvb, 1, dec_payload_size, dec_tree, pinfo)

            pinfo.cols.protocol:set("WAR S2C")
            pinfo.cols.info:set(string.format("[Decrypted] %s (0x%02X)", opcode_name(opcode), opcode))

            if not opcode_names[opcode] then
                subtree:add_proto_expert_info(ef_s2c_unknown_opcode)
            end
        elseif is_encrypted then
            -- Encrypted but empty — nothing to decrypt
            subtree:add_proto_expert_info(ef_s2c_encrypted)
        else
            -- Unencrypted: opcode sits in plaintext between size and payload
            local opcode = tvbuf(bytes_consumed + 2, 1):uint()
            subtree:add(pf_s2c_opcode, tvbuf(bytes_consumed + 2, 1))
            subtree:add(pf_s2c_opname, tvbuf(bytes_consumed + 2, 1), opcode_name(opcode))

            local payload_offset = bytes_consumed + 3

            if payload_size > 0 then
                subtree:add(pf_s2c_payload, tvbuf(payload_offset, payload_size))
            end

            dissect_s2c_payload(opcode, tvbuf, payload_offset, payload_size, subtree, pinfo)

            pinfo.cols.protocol:set("WAR S2C")
            pinfo.cols.info:set(string.format("%s (0x%02X)", opcode_name(opcode), opcode))

            if not opcode_names[opcode] then
                subtree:add_proto_expert_info(ef_s2c_unknown_opcode)
            end
        end

        bytes_consumed = bytes_consumed + total_length
    end

    return bytes_consumed
end

--- Decode known S2C payload types
function dissect_s2c_payload(opcode, tvbuf, offset, length, subtree, pinfo)
    if opcode == 0x8A and length >= 1 then
        -- F_RECEIVE_ENCRYPTKEY
        local ekr_tree = subtree:add(proto_s2c, tvbuf(offset, length), "EncryptKey Response")
        ekr_tree:add(pf_ekr_status, tvbuf(offset, 1))
        local status = tvbuf(offset, 1):uint()
        if status == 1 then
            pinfo.cols.info:append(" [No Encryption Ack]")
        end
    elseif opcode == 0x81 and length >= 20 then
        -- S_PONG
        local pong_tree = subtree:add(proto_s2c, tvbuf(offset, length), "Pong Response")
        pong_tree:add(pf_pong_client_ts, tvbuf(offset,      4))
        pong_tree:add(pf_pong_server_ts, tvbuf(offset + 4,  8))
        pong_tree:add(pf_pong_seq,       tvbuf(offset + 12, 4))
        pong_tree:add(pf_pong_unk1,      tvbuf(offset + 16, 4))
    end
end

-- ---------------------------------------------------------------------------
-- Direction heuristic: use the TCP port to determine direction
-- ---------------------------------------------------------------------------
local server_port = 10300  -- default, overridden by Decode As

local function war_heuristic(tvbuf, pinfo, tree)
    -- Minimum packet: 2 (size) + 1 (opcode) = 3 bytes for s2c
    --                 2 (size) + 8 (header) + 2 (min payload) = 12 for c2s
    if tvbuf:len() < 3 then return false end

    if pinfo.dst_port == server_port then
        -- Client → Server
        return proto_c2s.dissector(tvbuf, pinfo, tree)
    elseif pinfo.src_port == server_port then
        -- Server → Client
        return proto_s2c.dissector(tvbuf, pinfo, tree)
    end

    return false
end

-- ---------------------------------------------------------------------------
-- Wrapper dissector that routes by direction
-- ---------------------------------------------------------------------------
local proto_war = Proto("war", "WAR Game Protocol")

function proto_war.dissector(tvbuf, pinfo, tree)
    if pinfo.dst_port == server_port or pinfo.src_port ~= server_port then
        -- Assume C2S if destination is server port, or as fallback
        if pinfo.dst_port == server_port then
            return proto_c2s.dissector(tvbuf, pinfo, tree)
        end
    end
    if pinfo.src_port == server_port then
        return proto_s2c.dissector(tvbuf, pinfo, tree)
    end

    -- Fallback: try both based on packet structure
    -- C2S has the 8-byte header pattern; S2C is simpler
    -- Default to C2S since that's what the client sends to the server
    return proto_c2s.dissector(tvbuf, pinfo, tree)
end

-- ---------------------------------------------------------------------------
-- Registration
-- ---------------------------------------------------------------------------

-- Register on the default server port
local tcp_table = DissectorTable.get("tcp.port")
tcp_table:add(server_port, proto_war)

-- Also register a preference so users can change the port
proto_war.prefs.server_port = Pref.uint("Server Port", server_port,
    "TCP port the WAR game server listens on")

function proto_war.prefs_changed()
    -- Remove old port, add new
    tcp_table:remove(server_port)
    server_port = proto_war.prefs.server_port
    tcp_table:add(server_port, proto_war)
end

-- Register for "Decode As" support
DissectorTable.get("tcp.port"):add_for_decode_as(proto_war)
