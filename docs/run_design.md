# Run Design — Frontier Protocol

_Working document. Updated as ideas are validated or discarded._

---

## Save / Planet relationship (confirmed)

**1 planet = 1 save file.** The world seed IS the save identifier.

- New game → new seed → new planet, fresh everything
- No mid-campaign planet regeneration (would wipe all progress)
- Cell geography is derived from `worldSeed + cellIndex` — consistent and predictable
- All discovered species, extracted resources, exploration bars are stored against the worldSeed

---

## Core loop (confirmed)

```
New Game → generate planet seed
    │
    ▼
Pick home base cell on globe
    │
    ▼
Deploy to cell run area  ◄────────────────────────────────┐
    │                                                      │
    ▼                                                      │
Scout, scan flora/fauna, mark POIs, fight, collect         │
    │                                                      │
    ▼                                                      │
Extract → update cell info bars                            │
    │                                                      │
    ├─ bars < 80% ──► cell still available, revisit ───────┘
    │
    └─ all bars ≥ 80% ──► adjacent cells unlock on globe
```

---

## Information system — the unlock mechanic

Each globe grid cell has **multiple information bars**. All must reach ~80% to unlock neighbours.

### Confirmed bar categories

| Bar          | Filled by                                        |
|--------------|--------------------------------------------------|
| **Flora**    | Scanning plant species in the run area           |
| **Fauna**    | Scanning / capturing animal species              |
| **Geography**| Entering chunks (terrain mapped)                 |
| **Resources**| Locating and tagging resource nodes              |
| **Intel**    | Revealing enemy camp locations, patrol patterns  |

Each bar tracks independently. A cell could be 100% geography but only 30% fauna — the player needs to return specifically for the missing data. This creates focused, purposeful revisits rather than "clear everything."

80% threshold per bar (not average): the player can afford to miss some rare entries.

---

## Species system — Flora and Fauna

### Rarity tiers per cell

Each cell has a **species pool** fixed by `cellSeed`. Pool size varies by biome (see table below),
but within any pool, entries follow three tiers:

| Tier        | Spawn chance per run | Bar contribution (normalised to pool) |
|-------------|----------------------|---------------------------------------|
| Common      | 85–100%              | Low — each contributes ~5–8%          |
| Rare        | 25–45%               | Medium — each contributes ~12–15%     |
| Ultra-Rare  | starts ~2%, pity++   | High — contributes ~18–22%            |

**Bar math example (Forest, 9/3/1 flora):** scanning all 9 commons ≈ 45%, all 3 rares ≈ 39%,
ultra-rare ≈ 20%. Getting to 80% requires commons + at least one rare. Ultra-rare is the
final push to 100%.

**Bar math example (Mountain, 3/1/1 flora):** commons ≈ 24%, rare ≈ 36%, ultra-rare ≈ 40%.
Here the ultra-rare is mandatory to unlock — which makes Mountain cells punishing but quick
if you find what you're looking for.

### What "spawned" means

The species *pool* for a cell is seed-locked. Which members of that pool *appear in a given run*
is probabilistic and varies each visit. So:
- Common species almost always appear
- Rares might not show up — player must revisit
- Ultra-rare might take many runs to encounter even once

Once a species is scanned/captured, it's recorded permanently for that cell. The bar never goes
backward — you keep credit for anything you've found across all visits.

### Ultra-rare fauna example: the Cloaker

- Miniature predator, cloaking ability
- Spawn: ~3–5% per run (biome-appropriate cells only)
- When spawned: doesn't just walk into the player — actively evades, de-cloaks briefly when
  attacking, must be cornered or baited
- Capture/scan requires specialist scout trait or specific equipment item
- Scientific value: major xenobiology points, unlocks research branch at base
- The fact that it's hard AND rare makes finally finding it feel earned

This pattern — rare spawn + hard to scan even when present — is the template for ultra-rares
across fauna. Flora ultra-rares are probably more about location (only grows in a specific
terrain type within the cell, hard to reach) than active evasion.

### Species pools vary by biome — not all cells are equal

Not every cell has the same 7/2/1 split. Dense biomes have larger pools (more runs needed),
sparse biomes have smaller pools (faster to complete, but each entry is rarer and more valuable).

| Biome        | Flora pool | Fauna pool | Character                                                       |
|--------------|-----------|-----------|------------------------------------------------------------------|
| Forest       | 9 / 3 / 1 | 8 / 2 / 1 | Richest biodiversity; lots of revisits to hit 80%               |
| Jungle       | 10/ 3 / 2 | 9 / 3 / 1 | Most complex; two ultra-rare fauna; hardest to complete          |
| AlienWilds   | 8 / 2 / 1 | 7 / 2 / 1 | High scientific value per entry; alien species unlock research   |
| Grassland    | 6 / 2 / 1 | 6 / 2 / 1 | Balanced; moderate completion time                               |
| Savanna      | 5 / 2 / 1 | 6 / 2 / 1 | Slightly fauna-heavy; migration patterns drive revisits          |
| Coastal      | 5 / 1 / 1 | 5 / 2 / 1 | Aquatic/semi-aquatic fauna hard to observe; tidal zones          |
| Desert       | 4 / 1 / 1 | 4 / 1 / 1 | Sparse but each entry has high scientific value; quick to survey |
| Highland     | 4 / 2 / 1 | 4 / 1 / 1 | Mixed; mineral resources compensate for low fauna count          |
| Mountain     | 3 / 1 / 1 | 3 / 1 / 1 | Very sparse; extremely high resource node density instead        |
| Arctic       | 2 / 1 / 1 | 3 / 1 / 1 | Almost nothing lives here; what does is scientifically priceless |

Format: Common / Rare / Ultra-rare counts per category.

**Compensation rule:** Biomes with sparse species pools have denser or more valuable resource
nodes and more POIs (ruins, alien structures) — so the cell still justifies multiple visits,
just for different reasons (resources/intel rather than biodiversity).

Biome drives:
- Which species archetypes are eligible (no cactus in Arctic)
- Base spawn rates (Desert has low fauna density, high flora rarity value)
- Ultra-rare category (AlienWilds/Jungle ultra-rares are the highest scientific value in the game)

---

## POI marking system

Geography bar fills from entering chunks. But POIs add bonus progress AND have their own
value for base building / narrative.

Player-triggered actions that fill bars:
- **Enter a chunk** → Geography +chunk_weight
- **Scan a plant** → Flora +species_weight (via scanner tool or scout ability)
- **Observe/capture fauna** → Fauna +species_weight
- **Tag a resource node** → Resources +node_weight
- **Reveal enemy camp** → Intel +camp_weight (can also come from combat intel drops)

Scanner tool is likely a scout action (costs AP in exploration). Not passive — player
must actively target a specimen.

---

## Revisit motivation summary

| Goal                  | Why player returns                                         |
|-----------------------|------------------------------------------------------------|
| Missing rare species  | Didn't spawn last run — try again                         |
| Ultra-rare fauna      | Spawned but couldn't capture — need better gear/scout      |
| Locked resource node  | Node requires tool not yet built at base                   |
| Intel on elite camp   | Didn't have strength last run — return with better party   |
| 80% unlock threshold  | One bar still at 70%, need one more run to push through    |

This creates a cell lifecycle: early visits fill geography + commons fast, later visits
are targeted hunts for specific missing entries. The cell never feels "done" until 80% on all bars.

---

## Size: how many chunks per cell? (unchanged)

**9×9 generation area, ~25–35 accessible chunks.**
Barrier ring: outer 2 chunks impassable.
Tune after playtesting.

---

## Shape variation

Template library (seed-selected, biome-weighted). Same cell = same template every visit.

| Template         | Shape                                           | Good for biomes       |
|------------------|-------------------------------------------------|-----------------------|
| Valley           | Long corridor NW→SE, cliffs either side         | Mountain, Highland    |
| Archipelago      | 3–5 disconnected patches, water between         | Coastal, Ocean        |
| Peninsula        | Finger of land, water 3 sides                   | Coastal, Forest       |
| River delta      | Central river splitting into branches           | Forest, Grassland     |
| Crater           | Ring of accessible land, impassable center      | AlienWilds, Rocky     |
| Open field       | Large central zone, barriers at edges only      | Grassland, Savanna    |
| Mountain pass    | Narrow chokepoint between two larger zones      | Mountain, Arctic      |
| Ruins sprawl     | Irregular scattered tiles, dense barriers       | Desert, Ruins-heavy   |
| Coastal strip    | Long thin strip, water one side, cliff other    | Coastal               |
| Jungle canopy    | Clearings connected by forest corridors         | Forest, Jungle        |

---

## Natural barriers — no invisible walls

| Globe biome   | Primary barrier                              | Secondary              |
|---------------|----------------------------------------------|------------------------|
| Mountain      | Sheer cliff walls (Mountain/Snow, max elev)  | Avalanche debris       |
| Coastal       | Deep ocean (no beach transition)             | Sea cliffs             |
| Forest/Jungle | Impenetrable DenseForest wall (max height)   | Swamp / bog            |
| Desert        | Canyon walls (Rocky, max CliffDrop)          | Extreme heat flats     |
| Arctic        | Glacier wall (Snow, max elevation)           | Crevasse field         |
| Grassland     | River gorge (deep water + steep banks)       | Rocky highland edge    |
| AlienWilds    | Bioluminescent growth wall (AlienGrowth max) | Toxic pools            |
| Highland      | Boulder field (Rocky, CliffDrop everywhere)  | Snow wall              |

---

## Cell content — seed-locked vs dynamic

**Seed-locked (identical every visit):**
- Terrain shape, cliffs, water features
- Resource node positions (Crystal veins, Ruins materials)
- Species pool composition (which 10 flora, which 8 fauna exist in this cell)
- POI positions (enemy camp locations, shrines, artifact sites)
- Run area shape (template + noise mask)

**Dynamic per visit (varies run-to-run):**
- Which species from the pool actually spawn this run (probability-based)
- Enemy composition at each camp
- Harvestable plant yield (regrows)
- Ambient creature density

---

## Save data per cell

```
CellRecord {
    cellIndex:              Vector2I
    isUnlocked:             bool
    runCount:               int

    // Geography bar
    exploredChunks:         HashSet<Vector2I>

    // Flora bar
    scannedFlora:           HashSet<int>       // species IDs
    floraBarProgress:       float              // computed from scannedFlora weights

    // Fauna bar
    scannedFauna:           HashSet<int>
    faunaBarProgress:       float

    // Resource bar
    taggedResources:        HashSet<int>       // resource node IDs
    extractedResources:     HashSet<int>       // taken, won't respawn

    // Intel bar
    revealedCamps:          HashSet<int>       // camp IDs
}
```

Bar progress is recomputed from the sets on load — no floating point drift across saves.

---

## Resolved decisions

### Scanning mechanic (confirmed)
**Baldur's Gate 3 style — direct world interaction, no AP cost.**
The player walks up to a specimen and interacts (E / click). No scanner tool budget, no action economy.
Scout traits (Science, Biology) can add passive detection radius or bonus interaction options,
but the base scan is always available. This keeps exploration flowing and rewards curiosity.

### Ultra-rare spawn probability (confirmed)
**Pity timer system — escalating % per failed run, no hard guarantee.**

Base spawn chance ~1–3% per run. Each run where the ultra-rare does NOT appear, the chance
increases by a fixed step (e.g. +1.5% per miss). Resets to base once it spawns.

Additional rule borrowed from Dispatch/XCOM: if the adjusted spawn chance exceeds ~75–80%,
treat it as an automatic spawn — so very long streaks of bad luck self-correct without
feeling like a hard guarantee. Player never knows the hidden counter is running; the
ultra-rare just eventually "decides to show up."

| Run | Base | Accumulated | Effective |
|-----|------|-------------|-----------|
| 1   | 2%   | +0%         | 2%        |
| 5   | 2%   | +6%         | 8%        |
| 10  | 2%   | +13.5%      | 15.5%     |
| 20  | 2%   | +28.5%      | 30.5%     |
| ~50 | 2%   | +73.5%      | auto-spawn |

The pity counter is per-cell per-species, stored in `CellRecord`.

### Adjacency (confirmed)
**4-cardinal only (N/S/E/W).** Simpler progression path, easier to telegraph to the player.
Diagonal adjacency deferred — can revisit if playtest shows the map feels too linear.

### Home base cell + tutorial flow (confirmed)

**Game start: pick 1 of 3 offered landing sites.**

The planet seed generates 3 viable SafeZone candidate cells (pre-filtered: land biome,
not surrounded by impassable terrain, reasonable resource density). Player sees a brief
survey readout for each — biome type, rough resource tier, distance from equator — and
picks one. No wrong answer, just preference.

The home base cell doubles as the **tutorial run**:
1. Deploy to cell → guided scan of a common flora specimen (teaches interaction)
2. Scout an obvious resource node → teaches tagging / resource bar
3. Scripted event (downed equipment pod or distress signal) → teaches POI marking
4. Small, easy combat encounter at the end — scripted enemy count, player can't be
   overwhelmed (tutorial difficulty floor)
5. Extract → bars shown, home base established, adjacent cells visible on globe

Home base cell species pool: **commons only + 1 rare, no ultra-rare.**
Narrative reason: the survey team already did a preliminary scan before your unit landed.
The obvious life is catalogued; the rare one is your first real find.

### Fauna interaction — capture (confirmed)

**All fauna interaction = capture, not observation.**

Fits the corporate framing — the sponsors want live specimens and genetic samples,
not field sketches. The scouts are contractors doing uncomfortable work for profit.
That moral tension (you're ripping creatures off their home planet for a corporation)
is intentional and can feed into later narrative events and scout clash dialogue.

| Tier       | Capture method                                                      |
|------------|---------------------------------------------------------------------|
| Common     | Walk up → interact (E). Standard containment protocol.              |
| Rare       | Must approach undetected (crouch / slow move). Alert = fauna flees, no credit. |
| Ultra-rare | Undetected approach + requires Biologist trait OR containment kit item. |

Flora remains observe-only (you're taking a sample, not a living thing — lower moral weight,
simpler interaction). The distinction also gives fauna encounters more tension.

---

_Last updated: 2026-03-12 (late evening) — all open questions resolved_
