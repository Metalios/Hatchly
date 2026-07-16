# ASA DevKit export

`ExportHatchlyCreatures.py` is an Unreal Editor Python script. It scans creature
character, status-component, and egg assets, then writes deterministic records
sorted by stable creature ID.

Set `HATCHLY_EXPORT_PATH` to the repository's
`src/Hatchly.App/wwwroot/data/creatures.generated.json` before running it. The
script also writes `creature-export-report.json` containing new, changed,
missing, and suspicious records. It never reads or writes
`creature-overrides.json`.

Property names in ASA assets can vary between engine/DevKit releases. The script
uses a documented list of candidate property names and refuses to emit records
that lack required age, food-rate, birth, or adult-weight values. Suspicious
records remain visible in the report instead of being silently guessed.

After export, run:

```powershell
dotnet run --project tools/Hatchly.Tools -- validate-data --data-dir src/Hatchly.App/wwwroot/data
dotnet run --project tools/Hatchly.Tools -- merge-data --data-dir src/Hatchly.App/wwwroot/data --output src/Hatchly.App/wwwroot/data/catalog.json
dotnet test Hatchly.slnx
```

## Editor Utility Blueprint fallback

If an ASA DevKit release disables Python editor scripting, create an Editor
Utility Blueprint that performs the same operations:

1. Query Asset Registry paths under `/Game/PrimalEarth/Dinos` and `/Game/ASA`.
2. Match character assets with their status component and fertilized egg asset.
3. Read the candidate properties listed in `PROPERTY_NAMES` in the Python file.
4. Build the same camel-case JSON record contract.
5. Sort records by `id`, write UTF-8 JSON, and produce the same review report.

The Blueprint output must pass `Hatchly.Tools validate-data` before review.
