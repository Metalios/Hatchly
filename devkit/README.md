# ASA DevKit export

Hatchly's exporter has two layers:

- `ExportHatchlyData.py` is the Unreal adapter. It uses the Asset Registry and
  reflected editor properties to discover official live creatures, reproduction
  paths, accepted foods, and source metadata.
- `hatchly_export_core.py` is Unreal-independent. It applies policy, validates
  records, detects removals, and writes deterministic reviewable JSON.

`ExportHatchlyCreatures.py` remains as a compatibility entry point and runs the
new adapter.

These scripts are maintainer tooling. They are run from inside the ASA DevKit to
update generated JSON in the repository and are not published with the Hatchly
site.

## 1. Probe an ASA DevKit release

Set these environment variables in the editor process, then run the script from
the Unreal Python console:

```text
HATCHLY_REPO_ROOT=I:/Repos/metalios/Hatchly
HATCHLY_EXPORT_MODE=probe
py "I:/Repos/metalios/Hatchly/devkit/ExportHatchlyData.py"
```

Probe mode does not modify generated creature or food data. It writes
`property-probe.json`, records the candidate property selected for each required
field, and fails if the current DevKit cannot resolve a required binding.
Property binding changes require explicit review before export.

## 2. Export generated data

Change `HATCHLY_EXPORT_MODE` to `export` and run the same script. Successful
exports atomically write:

- `creatures.generated.json`
- `foods.generated.json`
- `devkit-export-report.json`

Generated application files contain no timestamps or source paths and remain
byte-identical when the underlying DevKit data has not changed. Version, source
assets, property matches, changes, missing records, and ambiguities are kept in
the report. A previously exported creature cannot disappear unless its stable ID
is explicitly excluded in `export-policy.json`.

The exporter never writes `export-policy.json`, `creature-overrides.json`, or
`food-overrides.json`. Exceptional native Blueprint behavior belongs in those
reviewed manual files.

## 3. Validate and review

Run the repository wrapper after every export:

```powershell
./scripts/Validate-DevKitExport.ps1
```

It runs Python fixtures, catalog validation and merge, all .NET tests, checks the
semantic export report, and checks the resulting diff for whitespace errors.

## Editor Utility Blueprint fallback

If an ASA DevKit release disables Python editor scripting, an Editor Utility
Blueprint may supply the Unreal-adapter records. It must use the same property
candidates and policy, write the identical JSON contracts, and pass the same
validation wrapper. The Python extraction core remains the authority for
filtering, deterministic output, disappearance checks, and reporting.
