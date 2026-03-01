## Summary
- Describe the content/runtime changes in this PR.

## Content Versioning Checklist (required for content changes)
- [ ] I changed files under `Assets/StreamingAssets/content/**`.
- [ ] I updated `Assets/StreamingAssets/content/manifest.json` hashes if content files changed.
- [ ] I reviewed `docs/content_versioning.md` and updated version fields in `manifest.json` when applicable.
- [ ] If this PR changes schema semantics, I updated migration notes in `docs/content_versioning.md`.

## Validation Checklist (required)
- [ ] `for f in $(rg --files Assets/StreamingAssets/content -g '*.json'); do jq empty "$f" || exit 1; done`
- [ ] `python3 scripts/validate_content.py`
- [ ] `python3 scripts/check_manifest_bump.py --base <sha_base> --head <sha_head>`
- [ ] `python3 scripts/smoke_simulation.py`

## Notes
- Add any known limitations or follow-ups.
