# Territory Feedback Contract

## Contract status

This revision freezes the canonical regional authority and ordering rules
defined by PR 15.1-C. It does not activate scheduler phases 9 or 10.

## Canonical regional authority

The following JSON block defines the binding regional authority for all
territorial computation in this project. It is the single source of truth
for region identity, count, order, weight, dynamic targets, and static
resources.

<!-- BEGIN CANONICAL REGION AUTHORITY -->
```json
{
  "canonical_region_order": {
    "authority": "content_pack_declaration_order",
    "source_path": "Assets/StreamingAssets/content/core/regions.json",
    "region_count": 16,
    "ordered_region_ids": [
      "arica_parinacota",
      "tarapaca",
      "antofagasta",
      "atacama",
      "coquimbo",
      "valparaiso",
      "metropolitana",
      "ohiggins",
      "maule",
      "nuble",
      "biobio",
      "araucania",
      "los_rios",
      "los_lagos",
      "aysen",
      "magallanes"
    ],
    "weight_ppm_each": 62500,
    "weight_ppm_sum_required": 1000000,
    "forbidden_order_sources": [
      "GameState.Regions",
      "RegionsById.Values",
      "dictionary_iteration",
      "lexicographic_sort"
    ]
  },
  "regional_dynamic_targets": [
    "support",
    "tension",
    "organization",
    "rival_presence"
  ],
  "static_regional_resources": {
    "admin_capS": 5000,
    "industry_capS": 5000,
    "extractive_capS": 5000,
    "social_capS": 5000,
    "populationS": 5000
  }
}
```
<!-- END CANONICAL REGION AUTHORITY -->

### Normative rules

1. **Order preservation**: The canonical region order (north to south)
   is defined by the declaration order in `regions.json`. This order
   must be preserved even though `GameState` stores regions in ordinal
   (alphabetical) order via `StateCollection.SnapshotSorted`.

2. **Dictionary non-authority**: `GameState.RegionsById` and similar
   dictionaries provide O(1) lookup by region ID but are not authority
   for iteration sequence. Iterating a dictionary without explicit
   ordering produces implementation-defined results and must not be
   used for territorial computation.

3. **Dynamic targets**: The four regional dynamic targets —
   `regions.{id}.support`, `regions.{id}.tension`,
   `regions.{id}.organization`, `regions.{id}.rival_presence` —
   represent mutable state that will be written by the territory
   feedback system. They are declared in `target_config.json` and
   registered in the visible target catalog via
   `CreateForMvp(..., orderedRegionIds, ...)`.

4. **Static resources**: The five static fields —
   `admin_capS`, `industry_capS`, `extractive_capS`, `social_capS`,
   `populationS` — are Content Pack data at value `5000` per region.
   They are not dynamic targets, not part of the visible causal
   surface, and have no territorial writer in PR 15.x. Their
   immutability guarantee is limited to this contract and the scope
   of PR 15.x; future contracts may revise this.

5. **Uniform weights**: Every region carries `weight_ppm = 62500`,
   producing a sum of `1000000`. Weights are uniform under this
   contract. Demographic, political, or economic weighting would
   require a new contract revision.

6. **Out of scope**: UI display names, polygon data, demographic data,
   and any non-simulation state are outside the scope of this
   contract.

7. **Scheduler phases**: Scheduler phases 9
   (`DriftNationalToRegions`) and 10 (`PullRegionsToInternals`)
   remain no-op. This contract does not activate or implement them.

## Contract boundaries with later PRs

- PR 15.1-D will freeze drift formulas (support, tension,
  organization, rival_presence), `alpha_ppm`, caps, rounding, and
  snapshot semantics.
- PR 15.1-E will freeze pull mechanics, weighted average regional,
  bindings, and latency.
- PR 15.1-F will freeze causal keys `REG_DRIFT` and ephemeral
  `REG_TO_INT` identities.
- PR 15.1-G will freeze atomicity and fail-closed contractual rules.
- PR 15.2 through 15.4 will implement the productive runtime plan.
