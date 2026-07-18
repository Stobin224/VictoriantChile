# Runtime Content Loader

PR 4 adds a fail-closed runtime loader for the existing Content Pack.

The loader lives in `VictoriantChile.Content`, separate from
`VictoriantChile.Simulation.Core`. The Core assembly remains pure simulation
logic; it does not read files, parse JSON, compute hashes, or reference
Newtonsoft.Json.

`VictoriantChile.Content` references the official Unity package
`com.unity.nuget.newtonsoft-json` pinned to `3.2.2`. The package is used only in
the Content assembly for strict runtime parsing.

## Source Boundary

`ContentPackLoader` receives an `IContentFileSource`. The default implementation,
`DirectoryContentFileSource`, reads from a directory that contains
`manifest.json`. A future Unity-facing layer can pass `Application.streamingAssetsPath`,
but this PR intentionally does not reference Unity APIs.

The current source is synchronous and targets PC/Editor filesystem loading for
small packs. Android APK, WebGL, remote packs, and UnityWebRequest loading are
outside this baseline.

Manifest paths are normalized relative paths:

- forward slashes only;
- no absolute paths;
- no `..`;
- no empty segments;
- `.json` files only;
- `manifest.json` is not listed in the manifest file map.

## Hashing

Declared file hashes use the same canonical algorithm as the Python tools:

1. read bytes;
2. normalize CRLF and CR line endings to LF;
3. compute SHA-256 over the normalized bytes;
4. format as `sha256:<64 lowercase hex>`.

The original bytes are then used for strict UTF-8 JSON parsing, avoiding a second
read of the same file.

## Validation

The loader validates and projects:

- `manifest.json` versions, languages, paths, required entries and hashes;
- `rules/target_config.json` into `TargetConfig` and `TargetConfigCatalog`;
- `core/regions.json`;
- `core/igs.json`;
- `core/movements.json`.
- `strings/es.json` into an immutable localization table for the default language;
- `rules/aggregation_config.json` into immutable aggregation models;
- `rules/legislative_config.json` into immutable legislative models;
- `templates/effects.json` into immutable ordered effect templates with ID lookup.

All declared manifest files are read and hash-checked. The current runtime pack
projects the core content, default-language localization, aggregation config,
legislative config, and effect templates, while still treating invalid content
as a fail-closed load error.

Schema compatibility is exact in this baseline:

- `content_schema_version` must be `1`;
- `min_game_schema_version` must be less than or equal to the current game schema
  version `1`;
- no migrations or best-effort compatibility are attempted.

If any error diagnostic is produced, `ContentLoadResult.Pack` is null and
`IsSuccess` is false. No partial pack is exposed as usable.

## Out Of Scope

This PR does not implement event loading, reform loading, effect execution,
aggregation formulas, legislative execution, GameState, mutation, persistence,
scheduling, normalization runtime, Android APK loading, web loading, or
UnityWebRequest.
