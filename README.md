# Hatchly

Hatchly is an independent raise planner for ARK: Survival Ascended. It helps
players plan creature maturation, baby feeding windows, food provisioning, and
trough coverage from a fast static site hosted on GitHub Pages.

The project began years ago as a fork of Crumplecorn's ARK breeding calculator.
The current Hatchly has since been completely remade as its own standalone
application with its own interface, calculation core, data pipeline, tooling,
and deployment workflow.

## Included

- Capacity-based baby feeding using a completely filled creature inventory
- Desired refill buffer with current/later/juvenile status
- Incubation, gestation, baby, juvenile, adolescent, and adult lifecycle timing
- Multi-container Normal, Maeguana, Tek, and hand-feed simulation with
  stack-slot capacity, multiple food types, multiple creature groups, and
  spoilage
- Official, Apocalypse, Small Tribes, and Conquest official rate profiles
- One browser-local unofficial hatch/mature/consume profile
- Versioned creature, food, diet, and override data

Imprints, notifications, accounts, active raises, saved plans, Gigantoraptor,
Procoptodon, and server INI importing are intentionally out of scope.

## Projects

- `src/Hatchly.App`: standalone Blazor WebAssembly UI
- `src/Hatchly.Core`: framework-independent domain and calculations
- `tests/Hatchly.Core.Tests`: xUnit calculation and synchronization tests
- `tools/Hatchly.Tools`: rate synchronization and data merge/validation CLI
- `devkit`: ASA DevKit creature exporter for maintainer-run data updates

The deployed GitHub Pages site is only the published Blazor WebAssembly output.
Repository tooling such as `devkit`, `tools`, `tests`, and `scripts` is not
published as site content.

## Local development

Requires the .NET 10 SDK.

```powershell
dotnet restore Hatchly.slnx
dotnet run --project tools/Hatchly.Tools -- merge-data --data-dir src/Hatchly.App/wwwroot/data --output src/Hatchly.App/wwwroot/data/catalog.json
dotnet test Hatchly.slnx
dotnet run --project src/Hatchly.App
```

The application only requests same-origin JSON at runtime. It never requests
Wildcard's CDN directly. The last valid official-rate document and the latest
unofficial values are cached in browser local storage.

## Updating official rates

```powershell
dotnet run --project tools/Hatchly.Tools -- sync-rates --output src/Hatchly.App/wwwroot/data/official-rates.json
```

The command fetches all four feeds before writing anything, validates both
required positive numeric values, and writes atomically only when profile
values change.

The `Sync official rates` workflow runs hourly at minute 17. It fetches the
official feeds, validates all required values, and commits only changed
normalized rate data to `official-rates.json` on `master`. It also updates
`data/official-rates.json` on the deployed `gh-pages` branch so the running
site can consume new official rates without rebuilding the Blazor application.

## Updating creatures

See [devkit/README.md](devkit/README.md). The exporter is maintainer tooling
that is run from inside the ASA DevKit to update generated JSON in the
repository. After the generated data is reviewed and pushed, the site consumes
the resulting catalog like any other static data file. No C# or Razor changes
are required for a valid new creature record.

## GitHub Pages and domain

GitHub Pages must use `Deploy from a branch` with `gh-pages` and `/` selected
as the source. The Pages workflow publishes the transformed Blazor output to
that branch with a root base path for `hatchlyapp.com`, writes `CNAME`, and
generates `404.html` for direct client-side routes such as `/troughs`.

Rate-only updates do not rebuild the site. The rate-sync workflow patches the
deployed `gh-pages` JSON file directly after validating all official feeds.

## Attribution

Hatchly no longer uses or depends on Crumplecorn. Historical attribution is
retained because the original Hatchly project began as a Crumplecorn fork and
legacy behavior was reviewed during the rewrite. See `THIRD_PARTY_NOTICES.md`.
