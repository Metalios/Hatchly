# Hatchly

Hatchly is a standalone Blazor WebAssembly planner for raising creatures in
ARK: Survival Ascended. It is a fresh implementation designed for static
hosting on GitHub Pages and does not include AngularJS, jQuery, or code from the
legacy Crumplecorn controller.

## Included

- Capacity-based baby feeding using a completely filled creature inventory
- Desired refill buffer with current/later/juvenile status
- Incubation, gestation, baby, juvenile, and adult lifecycle timing
- Multi-container Normal, Maeguana, Tek, and hand-feed simulation with
  stack-slot capacity, multiple food types, multiple creature groups, and
  spoilage
- Standard, Apocalypse, Small Tribes, and Conquest official rate profiles
- One browser-local unofficial hatch/mature/consume profile
- Versioned creature, food, diet, and override data
- ASA DevKit export and deterministic data validation tools

Imprints, notifications, accounts, active raises, saved plans, Gigantoraptor,
Procoptodon, and server INI importing are intentionally out of scope.

## Projects

- `src/Hatchly.App`: standalone Blazor WebAssembly UI
- `src/Hatchly.Core`: framework-independent domain and calculations
- `tests/Hatchly.Core.Tests`: xUnit calculation and synchronization tests
- `tools/Hatchly.Tools`: rate synchronization and data merge/validation CLI
- `devkit`: ASA DevKit creature exporter

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

The combined GitHub Pages workflow runs this command hourly at minute 17,
commits only changed normalized rate data, tests and audits the same working
tree, and deploys the validated Blazor output.

## Updating creatures

See [devkit/README.md](devkit/README.md). New creatures are data-driven: once a
valid generated record and any necessary manual override are present, no C# or
Razor changes are required.

## GitHub Pages and domain

GitHub Pages must use GitHub Actions as its source. The workflow reads the base
path from `actions/configure-pages`: repository previews use `/HatchlyApp/`,
while the configured `hatchlyapp.com` custom domain uses `/`. The workflow also
installs the transformed Blazor boot index and generates `404.html` for direct
client-side routes such as `/troughs`.

The repository intentionally does not ship a `CNAME` file. Configure and verify
the custom domain through the HatchlyApp repository's Pages settings after the
repository preview is accepted.

## Attribution

The initial migration used the legacy Hatchly/Crumplecorn data and behavior as
a reference. See `THIRD_PARTY_NOTICES.md`.
