## Task ID

-

## Summary

-

## Change Type

- [ ] docs
- [ ] checks
- [ ] content
- [ ] runtime
- [ ] UI

## Agent Run Summary

- Iterations performed:
- Files modified:
- Commands and results:
- Checks skipped and reason:

## Content Versioning Checklist

- [ ] I changed files under `Assets/StreamingAssets/content/**`.
- [ ] I verified manifest hashes with `python scripts/verify_manifest_hashes.py`.
- [ ] I recalculated hashes with `python scripts/recompute_manifest_hashes.py` only if content changed.
- [ ] I reviewed `docs/content_versioning.md` and updated version fields in `manifest.json` when applicable.
- [ ] If this PR changes schema semantics, I updated migration notes in `docs/content_versioning.md`.

## Validation Checklist

- [ ] `python scripts/run_checks.py`
- [ ] `python scripts/run_checks.py --base-ref <sha_base> --head-ref <sha_head>` when manifest bump enforcement applies

## Risks

-

## Follow-ups

-

## Merge Confirmation

- [ ] I confirm this PR must not be auto-merged.
