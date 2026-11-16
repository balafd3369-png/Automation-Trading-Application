# Phase 9A.2 — SQLite security remediation & release freeze

| Field | Value |
|-------|--------|
| Date | 2026-11-16 |
| Advisory | [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) (CVE-2025-6965) |
| **Final status** | **SECURITY VERIFIED — RELEASE READY** |

No production database was modified. Live trading remains disabled. No infrastructure was provisioned.

---

## Dependency tree (resolved)

| Package | Version | Role |
|---------|---------|------|
| `Microsoft.Data.Sqlite` | 10.0.0 | ORM/driver |
| `Microsoft.Data.Sqlite.Core` | 10.0.0 | Transitive |
| `SQLitePCLRaw.bundle_e_sqlite3` | **3.0.5** | Batteries / provider wiring |
| `SQLitePCLRaw.core` | 3.0.5 | Transitive |
| `SQLitePCLRaw.provider.e_sqlite3` | 3.0.5 | Transitive |
| `SQLitePCLRaw.config.e_sqlite3` | 3.0.5 | Transitive (3.x bundle) |
| `SQLitePCLRaw.lib.e_sqlite3` | **3.50.3** | Native SQLite (patched; was **2.1.11** / SQLite 3.49.1) |
| `SQLite` (metapackage) | 3.53.4 | Transitive metadata |

**Removed:** all **2.1.11** references from `TradeWithBeast.csproj` and published `TradeWithBeast.deps.json`.

---

## Remediation approach

1. **2.x line is last-affected** for GHSA-2m69-gcr7-jv3q (≤ 2.1.11). Patched native payloads ship on the **3.x** `SQLitePCLRaw.lib.e_sqlite3` line (version = embedded SQLite, e.g. **3.50.3**).
2. Upgraded `SQLitePCLRaw.bundle_e_sqlite3` to **3.0.5** for net10.0 compatibility.
3. **Explicit pin** `SQLitePCLRaw.lib.e_sqlite3` **3.50.3** so NuGet audit and runtime load the patched native library (not a vulnerable 2.x lib).

No `NuGetAuditSuppress` or warning suppression was added.

---

## Verification evidence

| Check | Result |
|-------|--------|
| `dotnet list package --vulnerable --include-transitive` (TradeWithBeast) | **No vulnerable packages** |
| `dotnet list package --vulnerable --include-transitive` (TradeWithBeast.Tests) | **No vulnerable packages** |
| `dotnet build -c Release` | **0 warnings, 0 errors** |
| Backend tests | **275 / 275 passed** |
| SQLite-focused tests (`ProductionSafetyPhase7`, `PersistentStorage`, `TradeHistory`, native version) | **PASS** |
| `SqliteNativeRuntimeVersionTests` — `SELECT sqlite_version()` ≥ **3.50.2** | **PASS** |
| `deploy-release.ps1` | **PASS** (fresh artifacts + checksums) |
| `preflight.sh` | Run after manifest rebuild |

---

## Release artifacts (immutable snapshot)

| Item | Value |
|------|--------|
| Manifest | `deploy/digitalocean/artifacts/build-manifest.json` |
| API checksum (`TradeWithBeast.dll`) | `eb84771d3dc1127f7d32994a439c63ecdcc2c71b51d75347ed5a9d78b561c11e` |
| Web checksum (`web/index.html`) | `9c0493eca9564f024c865eee60f146051061d014fe27816e4f247265af7e0792` |
| TFM | `net10.0` |
| `liveTradingEnabled` | `false` |

Manifest includes `sqlitePackages` for traceability after rebuild.

---

## Git release freeze

| Item | State |
|------|--------|
| Prior base commit | `9e6ed948962249c6065380db8dcb8fc69002e206` |
| Release tag | `v1.0.0-restricted-prod-2026.11.16` (annotated, local only — not pushed) |
| Release commit | `ac8f60011653c75ecf708089c1529d39859e7b69` |
| Excluded from git | `deploy/digitalocean/artifacts/`, `backend/publish/` (see `.gitignore`) |
| Checksums in repo | `docs/RELEASE-CHECKSUMS.json` |

---

## Reports updated (9A.2)

- `docs/PHASE9A.2-SQLITE-SECURITY-RELEASE-REPORT.md` (this file)
- `docs/PHASE9A-INFRASTRUCTURE-READINESS-REPORT.md` — SQLite row cleared
- `docs/PHASE9-PRODUCTION-DEPLOYMENT-REPORT.md` — risk table
- `docs/FINAL-DEPLOYMENT-READINESS.md` — dependency section