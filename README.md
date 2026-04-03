<h1 align="center">ProjectWAR</h1>

<p align="center">
	<img src="https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" />
	<img src="https://img.shields.io/badge/Rewrite-In%20Progress-2ea44f" alt="Rewrite Status" />
	<img src="https://img.shields.io/badge/Docs-Available-0366d6" alt="Docs" />
	<a href="https://github.com/twgraham/ProjectWAR/actions/workflows/ci.yml">
		<img src="https://github.com/twgraham/ProjectWAR/actions/workflows/ci.yml/badge.svg" alt="Build" />
	</a>
	<a href="https://coveralls.io/github/twgraham/ProjectWAR?branch=master">
		<img src="https://coveralls.io/repos/github/twgraham/ProjectWAR/badge.svg?branch=master" alt="Coverage Status" />
	</a>
</p>

<!-- Replace OWNER/REPO and ci.yml with your actual GitHub owner, repository, and workflow file name. -->

ProjectWAR is a Warhammer Online server.

My fork of the original ProjectWAR, is focused on a comprehensive rewrite of the server codebase to modernize the architecture, improve maintainability, and enhance performance while preserving the core gameplay experience.

This repository started from a widely used open-source base and is now being rewritten toward a cleaner, testable, modern .NET architecture while keeping the project playable and maintainable during migration.

## Project Status

- `WorldServer` is the current legacy server path.
- `WorldServerV2` is the in-progress rewrite and long-term target.
- Shared infrastructure and core libraries are being actively modernized under `src/Core.*`.

## Repository Layout

- `src/` – server applications and core libraries.
- `test/` – unit/integration test projects.
- `docs/architecture/` – rewrite architecture, system design, and roadmap.
- `docs/protocol/` – protocol reverse-engineering and packet references.
- `Database/` – schema and migration scripts.

## Quick Start

Requirements:

- .NET SDK 10+
- A database backend compatible with the project configuration

Build:

```bash
dotnet restore
dotnet build ProjectWAR.slnx
```

Run tests:

```bash
dotnet test ProjectWAR.slnx --no-build
```

For environment setup and deeper implementation notes, start in `docs/README.md`.

## Documentation

- Documentation index: `docs/README.md`
- Architecture overview: `docs/architecture/Overview.md`
- Protocol notes: `docs/protocol/WAR_Login_Protocol_Design.md`

## Disclaimer

ProjectWAR is an independent fan-run software project. It is not affiliated with, endorsed by, or sponsored by Games Workshop, Mythic Entertainment, or Electronic Arts. All related trademarks and IP belong to their respective owners.
