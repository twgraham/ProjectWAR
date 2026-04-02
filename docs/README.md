# ProjectWAR — Documentation

ProjectWAR is a private-server reimplementation of Warhammer Online: Age of Reckoning.
The codebase contains two server executables:

- **WorldServer** — the original working server (~120 K lines, C#). Handles gameplay today.
- **WorldServerV2** — an incremental rewrite from scratch, running alongside the old server. Clean architecture, DI, full test coverage. The long-term replacement.

---

## Folder Layout

```
docs/
├── README.md              ← you are here
├── architecture/          ← WorldServerV2 redesign documentation
│   ├── Overview.md                   start here — rationale, current state, roadmap
│   ├── Glossary.md                   WAR / server-internal term definitions
│   ├── System_01_GameData.md         static game data loading & IGameDataStore
│   ├── System_02_EntityModel.md      entity/component hierarchy
│   ├── System_03_WorldTopology.md    regions, cells, visibility, 20Hz tick loop
│   ├── Player_Login_Flow.md          login protocol, init pipeline, packet sequence
│   ├── System_04_Combat.md           stats, damage pipeline, buffs, abilities
│   ├── System_13_Spawning.md         NPC/GO lifecycle, factory, respawn, packet DTOs
│   └── WorldServerV2_Architecture.md archived monolith (redirects to files above)
└── protocol/              ← client reverse-engineering & packet format references
    ├── WAR_Login_Protocol_Design.md  login protocol — full RE from Ghidra analysis
    └── F_GET_ITEM_Format.md          F_GET_ITEM (0xAA) packet field mapping
```

---

## Where to Start

| Goal | Document |
|------|---------|
| Understand the project and why V2 exists | [architecture/Overview.md](./architecture/Overview.md) |
| Look up a term (Region, OID, Tick, Career, …) | [architecture/Glossary.md](./architecture/Glossary.md) |
| Resume work on a specific system | See the system doc in `architecture/` |
| Understand the login wire protocol | [protocol/WAR_Login_Protocol_Design.md](./protocol/WAR_Login_Protocol_Design.md) |
| Look up item packet field offsets | [protocol/F_GET_ITEM_Format.md](./protocol/F_GET_ITEM_Format.md) |

---

## WorldServerV2 System Status

| System | Document | Status |
|--------|---------|--------|
| **System 1**: Game Data Pipeline | [System_01_GameData.md](./architecture/System_01_GameData.md) | ✅ Complete |
| **System 2**: Entity Model | [System_02_EntityModel.md](./architecture/System_02_EntityModel.md) | ✅ Complete |
| **System 3**: World Topology & Tick | [System_03_WorldTopology.md](./architecture/System_03_WorldTopology.md) | ✅ Complete |
| **Player Login Flow** | [Player_Login_Flow.md](./architecture/Player_Login_Flow.md) | ✅ Phase 3 minimum viable set done |
| **System 4**: Combat & Ability Engine | [System_04_Combat.md](./architecture/System_04_Combat.md) | 📐 Design complete, not implemented |
| **System 5**: AI / Brain | — | 🔲 Not started |
| **System 6**: Character Persistence | — | 🔲 Not started |
| **System 7**: Group & Warband | — | 🔲 Not started |
| **System 8**: Guild | — | 🔲 Not started |
| **System 9**: RvR / Campaign | — | 🔲 Not started |
| **System 10**: Quests | — | 🔲 Not started |
| **System 11**: Scenarios | — | 🔲 Not started |
| **System 12**: Economy | — | 🔲 Not started |
| **System 13**: NPC & Static Object Spawning | [System_13_Spawning.md](./architecture/System_13_Spawning.md) | 📐 Design complete, not implemented |
