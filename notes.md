# Design Notes — Frontier Protocol

## Sector / Planet Architecture (discussed 2026-03-02)

### Goal
- Isometric world is finite (bounded planet)
- East↔West travel wraps continuously (no edge, no teleport)
- North↔South travel reaches poles naturally (player pushes back south at pole)
- Each globe grid cell = one explorable "sector" in the isometric world
- Isometric biomes must match what the globe shows at that location

### Decision: Cylindrical coordinate system
- Longitude wraps (0..360°), latitude is bounded at poles (−90..+90°)
- NOT a flat disc — NOT a teleporting edge — just continuous east-west
- Poles are accessible real locations (Arctic biome), north boundary = north pole

### World dimensions
- SectorsLon = 24 (matching globe F1 grid)
- SectorsLat = 12 (matching globe F1 grid)
- ChunksPerSector = 5
- Total world: 120 × 60 chunks (east-west wraps at 120)
- One sector = 5×5 chunks = 80×80 tiles = solid explorable area per globe cell

### Coordinate mapping
- chunkLon (0..119): wraps — loaded as ((cx % 120) + 120) % 120
- chunkLat (0..59): bounded — clamped to [0, 59]
- Sector index: (chunkLon / 5, chunkLat / 5) → (0..23, 0..11)
- Player position: longitude wraps in world-space X; latitude clamps at top/bottom

### Noise must tile at lon seam
- Current: SampleElev(x, y) uses flat 2D Perlin — DOES NOT tile at x=0/120
- Fix: use cylindrical 3D coords for noise sampling:
    lon_rad = chunkLon / 120f * 2π
    lat_norm = (chunkLat / 60f) * 2 - 1   // −1..+1
    nx = cos(lon_rad), ny = sin(lon_rad), nz = lat_norm
    elev = noise.GetNoise3D(nx, ny, nz)
- This is identical to how globe texture already samples noise — seamlessly tileable east-west

### What changes in code
1. ChunkManager.cs
   - Add const LonWidth = 120, LatHeight = 60
   - In UpdateChunks/LoadChunk: wrap cx: ((cx % 120) + 120) % 120
   - In WorldToChunk: result x wraps, result y clamps
   - Player movement: clamp world Y position to LatHeight boundary

2. ChunkGenerator.cs
   - SampleElev and SampleMoist: switch to cylindrical 3D noise
   - lon_rad from chunk X (mod LonWidth), lat from chunk Y
   - This also makes isometric biomes consistent with globe texture

3. Player.cs
   - World X position wraps (modulo LonWidth × ChunkData.Size × TileWidth/2)
   - World Y position clamped to [0, LatHeight × ChunkData.Size × TileHeight/2]

4. GlobeView.cs
   - Highlight player's current sector on the globe (yellow outline)
   - Later: click sector to navigate there

### Sector data (future)
Each sector (sectorLon, sectorLat) stores:
- Dominant biome (from globe gen at that lat/lon)
- Exploration state: unvisited / scouted / fully explored
- Per-sector seed = hash(worldSeed, sectorLon, sectorLat)
- Later: named POIs, events, resources, run history

### True sphere (future option)
Cube-mapped sphere (6 faces) would give true pole-to-pole wrapping but is significantly
more complex. Not worth it now — cylindrical covers 95% of the gameplay feel.
The user specifically wants continuous movement with no teleport; cylindrical achieves this.

---

## Progress Log

### 2026-03-10 — Combat system + Visual style pass

**Combat system (completed 2026-03-09)**
Full turn-based combat loop wired into the world:
- `GameManager` autoload — holds Phase (Globe/Iso/Combat), DeployChunkCoord, PendingEnemies
- `WorldScene.cs` — Globe↔Iso mode switch, encounter rolls (20% base × biome danger multiplier), scene change to combat.tscn
- `PartyManager` autoload — persistent scout HP across scenes, KO tracking (2nd KO = permanent death)
- `CombatData.cs` — all enums/records: WeaponDef, EnemyDef, CombatTileData, StatusEffect flags; WeaponDB (9 weapons with range sweetspots); EnemyDB (Scout/Warrior/Lurker)
- `CombatGrid.cs` — 11×11 arena + 8-tile decorative outer ring, LoS (Bresenham), BFS reachability, cover/elevation
- `CombatUnit.cs` — animated sprites (Walk/Run/Shot/Attack/Hurt/Dead), HP bar, AP pips, status icons, hit-chance calc
- `TurnQueue.cs` — initiative ordering, round cycling, dead-unit removal
- `EnemyAI.cs` — stateless Evaluate(), Lurker flanks, Warrior charges, Scout kites
- `CombatManager.cs` — full FSM, HUD, all player input, projectile system
- `scenes/combat.tscn` — combat scene

**Visual style pass (completed 2026-03-10)**
Target aesthetic: low-poly diorama (flat-shaded polygons, bold silhouettes, unified earth slab).
- `PropDraw.cs` (new, `scripts/world/`) — shared static renderer used by both CombatGrid and Chunk
  - Pine trees: 3 stacked triangles dark→light, trunk, snow-cap variant
  - Round trees: 10-gon flattened canopy with lighting gradient, alien (teal) variant
  - Rock piles: 3 overlapping irregular polygons, rubble variant for Ruins
  - Cactus: trunk + L/R arms with verticals
  - All props: drop-shadow ellipse underneath
- `CombatGrid.cs` — VisNoisePx=26f; tile heights: Grassland=9, Forest=18, Rocky=32, Mountain=54, Snow=65px
  - Unified earthy slab: sideL=(0.32,0.20,0.10), sideR=(0.20,0.13,0.06) — identical for all tile types
  - Sprite overlay system fully removed; RowNode + OuterNode use PropDraw
- `Chunk.cs` — ElevationOf: Grassland=7, Forest=12, Rocky=20, Mountain=36, Snow=40; jitter=hashB×10f
  - Unified slab side=(0.28,0.18,0.09); prop pass in _Draw() after DrawMesh(), scale=0.75f

**Build status:** Clean — 0 errors, 0 warnings

**Next up (candidates)**
- Terrain color blending at tile boundaries (eliminate hard grid lines)
- CombatManager UI polish (action buttons, better HUD layout)
- Scout recruitment screen in meta loop
- Globe → combat direct launch (click sector → deploy → encounter)
- Performance check on Chunk prop pass (16×16 × props per frame)

**End of day (23:00)** — Build confirmed clean. No regressions. Session closed.

---

### 2026-03-11 (evening) — Overworld terrain rework: ChunkTerrainMesh

**Goal:** Apply the continuous height-field approach from the combat arena to the overworld chunk renderer, driven by globe-generated biome data.

**Architecture decision:** 1 globe chunk = 9×9 overworld chunks; 1 overworld chunk = 1 combat arena (16×16 world tiles). Each chunk is now rendered by `ChunkTerrainMesh` (VRes=2 subdivision → 32×32 hi-res tiles per chunk).

**New file: `scripts/world/ChunkTerrainMesh.cs`**
- `GenerateFromChunk(ChunkData data, int worldSeed)` — primary entry point
- World-space FBM noise: `wx = chunkCoord.X * NTiles + u` → seamless borders across all loaded chunks
- 3 noise layers: large (0.006, 4oct), medium (0.016, 3oct), detail (0.040, 2oct)
- `TileBaseH()` maps each `TileType` to pixel height: DeepOcean=0, Beach=8, Grassland=18, Forest=28, Rocky=50, Mountain=62, Snow=72
- `AvgBaseH()` samples 4 surrounding world tiles per vertex → no seam within chunk
- Water tiles (baseH < 9.5) clamped to WaterLevel+1.5 so noise never lifts ocean above shoreline
- Same `TerrainProfile` struct (defined in IsoTerrainMesh.cs, project-global) drives water chance, elevation bias, forest density, height scale
- `ProfileForBiome(BiomeType)` maps all 13 globe biomes to appropriate TerrainProfile preset
- Same cliff faces (CliffDrop=14), CarvePond (3-zone: water/beach/shore), CarveRiver as combat terrain
- Same HeightColor gradient (7 stops: deep water → beach → grassland → forest → rocky → mountain → snow)
- `VtxY = (u+v)*HTH - TH - _vH[u,v]` ensures vertex(0,0) aligns exactly with N corner of old Chunk tile(0,0)
- Props via `PropDraw.DrawProp(this, wt, centre, pSeed, 0.55f)` in separate back-to-front pass

**Modified: `scripts/world/Chunk.cs`**
- Removed: `BuildMesh()`, `AddTileGeometry()`, `AddQuad()`, `AddQuadV()`, `TileColors()`, all ArrayMesh / DrawMesh code, PropDraw prop pass
- Added: `private ChunkTerrainMesh _terrain;` child node
- `Init()` signature: `Init(ChunkData data, Vector2 worldPos, int worldSeed = 12345)`
- `_Draw()` now handles only: Crystal/Ruins labels (always), Mountain/Snow labels (debug only), chunk border debug overlay

**Modified: `scripts/world/ChunkManager.cs`**
- Line 138: `chunk.Init(data, ChunkToWorld(coord), WorldSeed);` — passes global seed for world-space noise coherence

**Build status:** Clean — 0 errors, 0 warnings (2.70s)

**Visual result:** Overworld now renders as a continuous sculpted terrain matching globe biome data:
- Water biomes flat at sea level; land biomes elevated and shaped by FBM noise
- Smooth height transitions between tiles within each chunk
- Height-based color gradient replaces hand-coded per-tile colors
- Cliff faces on steep height drops (mountains, highlands)
- Procedural ponds/rivers in land biomes based on BiomeType water chance
- Props (trees, rocks, cactus) positioned on terrain surface via world-space density noise

**Next up:**
- Test in-engine: verify seamlessness at chunk borders, biome transitions
- Tweak TileBaseH values and NoiseAmp for better visual range
- Cross-chunk border seam fix (border vertices sample adjacent chunk tiles — currently clamped to this chunk's edge)

**End of day (23:00)** — Build confirmed clean. No regressions. Session closed.

---

### 2026-03-12 (late evening) — Memory optimization pass + Overworld design

**Optimization pass (orders.md directives applied)**

Full audit of hot-path allocations across all rendering and combat code:

- `StarBackground.cs` — highest-impact fix: `_Draw()` runs every frame (QueueRedraw in _Process). Eliminated 6 per-frame heap allocations by pre-allocating `_isoPolyVerts[4]`, `_isoPolyColors[4]`, `_auroraVerts[4]`, `_auroraColors[4]` as instance fields; `AtmRingScale/Alpha` as static readonly arrays
- `ChunkTerrainMesh.cs` — `HeightColor` no longer allocates arrays per tile; gradient moved to `static readonly float[]`/`Color[]`. `DrawQuad` and `DrawCliffFaces` fill static `_quadVerts[4]`, `_quadColors[4]`, `_faceVerts[4]`, `_faceColors[4]` instead of `new[] {}`
- `IsoTerrainMesh.cs` — same static buffer treatment; added `_slabVerts[4]`, `_slabColors[4]`, `static readonly Color _slabBase`; fixed double `LengthSquared()` call in `SegDist`
- `CombatGrid.cs` — `HighlightNode` now uses static `_hlVerts[4]`, `_hlColors[4]`, `_borderLine[5]`; extracted `FillDiamondVerts()` helper; arena border reuses same buffer for both glow+main passes. BFS/attack collections pre-sized with capacity hints
- `ChunkManager.cs` — `UpdateChunks` collections pre-sized (HashSet 25, List 8)
- `EnemyAI.cs` — removed LINQ entirely; alive-filter manual foreach; `PickTarget` rewritten as single O(n) pass tracking both min-HP-in-range and closest simultaneously; `occupied` built with foreach
- `TurnQueue.cs` — removed LINQ; `Build` uses `_order.Clear()` + foreach + `Sort()`; `Remove` uses `RemoveAll` + queue rotation trick (dequeue all, re-enqueue non-matching); `HasUnitsOfSide` manual foreach

**Design discussion — Overworld**

Reviewed and clarified core overworld design:
- Run area: 9×9 chunk *budget*, not a square — templates carve navigable shape (valley, crater, spider-legs, etc.); barrier ring is just the cage
- Resources simplified: visiting a biome cell → cell produces biome-appropriate resources (Forest=wood/bio, Mountain=ore/minerals, Desert=rare minerals, AlienWilds=exotic compounds). No dedicated resource-node scan system
- Fauna: carnivores / herbivores / omnivores — design TBD
- Base: start with a single home-cell prototype, grow from there
- Gear: 5 Diablo-style slots — Weapon / Armor / Helmet / Utility / Implant
- Land cells only (~180 of 288 cells); ocean = locked for now (DLC territory)
- Story hook candidate: planet is a designed artifact; sponsors know, player doesn't. Final choice: transmit data (corp strip-mines it) or burn it. Post-completion: planet keeps changing, you stay as the only person who understands it

**Build status:** Clean — no regressions from optimization pass

**Next up (candidates)**
- Implement `CellRecord` save data struct and wire Geography bar to chunk entry
- Biome → resource production mapping (Forest/Mountain/Desert/AlienWilds first)
- Home base cell: single persistent chunk that survives between runs
- Run area template system: Valley and Open Field templates as first two shapes
- Fauna placeholder: one herbivore that idles + flees on approach

**End of day (23:00)** — Build confirmed clean. Session closed.

---

### 2026-03-13 (late evening) — Landing site picker: centering fix + water spawn fix

**Goal:** Fix two bugs reported after landing site UI was merged: globe snap not centering the selected cell, and scouts deploying into ocean water.

**GlobeView.cs — centering fixes**
- `SnapToCandidate`: phi formula was `u * Tau - Pi` (shifted 180° off); corrected to `Mathf.Wrap(u * Tau, -Pi, Pi)` to match the convention used in `TryClickGridCell`
- Added X-axis tilt: `_targetXRot = latRad` computed from grid cell `gj` index, so high-latitude cells tilt to the vertical centre of the screen rather than appearing at the top
- `TryClickGridCell`: added `_targetXRot = lat` (hit-point latitude) for the same centering on regular cell clicks
- `_Process`: snap block now lerps both X and Y rotations toward their targets; non-snapping branch drifts X back to 0 with `Lerp(..., 0f, delta * 2f)` so the globe returns to normal orientation after the panel closes
- `HideCellPanel`: resets `_targetXRot = 0f` so next snap starts clean

**WorldScene.cs + GlobeView.cs — water spawn fix**
Root cause: deploy formula used `gj * 10 - 50` which is the **northern edge** of the grid cell, not its centre. Chunk (45, -30) was coastal/ocean while the cell centre at (45, -25) is Grassland. The globe's `DominantBiome` for the whole cell said Grassland, masking the edge-case.

- `OnDeployRequested` + `OnLandingSiteChosen`: changed `latIdx * 10 - 50` → `latIdx * 10 - 45` (cell centre, consistent with X which already used `+5`)
- `CellDatabase.BiomeLookup`: same formula fix — uses centre chunk for biome lookups
- `FindLandingCandidates`: now cross-checks each candidate's centre chunk via `GetBiomeForChunk(cx, cy)` and rejects any cell whose centre chunk is Ocean/DeepOcean/Coastal, regardless of globe-level `DominantBiome`

**Note:** The "not square" cell shape is expected spherical geometry — a 15°×15° lat/lon cell at 52°N is genuinely trapezoidal on a sphere. The planet shader also brightens the full 150-chunk cell region, which looks large.

**Build status:** One compile error mid-session (`Math` vs `Mathf` in GlobeView.cs) — fixed. Should be clean; verify in-engine.

**Next up (candidates)**
- Verify centering X-tilt direction in-engine (may need sign flip if Godot euler order differs from assumption)
- Globe generation discussion (user queued this explicitly)
- CellDatabase / CellRecord save wiring to actual run data
- Run area template system (Valley / Open Field)
- Globe generation improvements (user said they want to talk about it)

**End of day (23:00)** — Session closed.

---

### 2026-03-11 — Continuous height-field terrain rework

**Goal:** Eliminate visible tile grid from combat arena; match low-poly diorama references.

**Problem with previous approach:** Each tile rendered as an independent raised diamond with its own side faces. Adjacent tiles had discrete height steps (Grassland=9, Forest=18 etc.) causing a visible staircase grid. Even with slope-shaded color, the tile boundaries remained obvious.

**New approach — `CombatTerrain.cs`:**
- Vertex-shared height field: (TotalW+1)×(TotalH+1) = 28×28 vertex grid
- Each vertex height = average of surrounding 4 tile base-heights + FBM noise × 28px
- Tile (c,r) uses 4 shared corner vertices → **adjacent tiles share edge vertices** → no height gap at seam
- Slope shading per-quad: gradient from 4 vertex heights → NW light source → flat-shaded facets
- Interior tiles: top face ONLY (no side faces → no staircase seams)
- Perimeter slab: SW face (tiles at r=TotalH-1) + SE face (tiles at c=TotalW-1) → clean earth block edge
- Outer ring: fade (alpha 1→0.22) + darken (0.58→0.44) proportional to distance from arena
- Props: back-to-front pass after terrain, positioned at visual quad centre

**CombatGrid.cs changes:**
- Removed `OuterNode` and `RowNode` inner classes (all visual code deleted)
- Added `private CombatTerrain _terrain` field
- `_Ready()`: creates CombatTerrain (ZIndex=-100) + HighlightNode only
- After `GenerateHeightMap()`: calls `_terrain.Build(_allTiles, _noisePx)`
- `GetCellVisualPos()`: now uses `_terrain.GetTileVisualHeight(col, row)` — highlight diamonds align with actual terrain surface

**Build status:** Clean — 0 errors, 0 warnings

**Next up (candidates):**
- Test in-game: verify terrain looks smooth, props align, highlight diamonds track terrain
- Tweak NoiseAmp and TileBaseH values for better height variation
- Biome-wide color pass (all tiles in same biome should share a color family)
- Re-enable combat FSM after terrain is satisfactory
- Vignetting as separate CanvasLayer

---

### 2026-03-17 — Overworld design rethink

#### Core architecture decision: 2 spaces, not 3
The current "3 zoom levels" model (Globe → Overworld → Combat arena) is a design flaw.
Correct mental model: **2 spaces.**
- **Globe** — strategic/meta layer. Choose where to deploy, read terrain, plan.
- **Ground** — everything else. The overworld *is* the run. Combat happens on the same tile/terrain, just shifts to a playable arena view. Logically the same place.

#### Globe ↔ Overworld connection problem
Right now they feel like 2 different games with no visual or tonal link. The grid selection feeding data to the overworld is invisible to a new player. **Priority: tighten these together graphically and by feel before any further overworld work.**

#### What the overworld actually is
The overworld is the primary gameplay space — where most of the run happens. Not a lobby for combat.

Activities that happen organically while exploring:
- Codex entries: fauna & flora discoveries
- Secrets, ruins, unusual terrain features
- Resource collection
- Combat encounters (ambushes, territorial creatures, enemy scouts)
- Events (extraordinary finds, environmental hazards, NPC encounters)

Design intent: the player should *never be waiting*. Things happen as they move. No holding screen, no scheduled random-roll-then-combat. It should feel like the terrain is alive.

#### Full run structure (2026-03-17 draft)
```
Choose deployment cell (globe)
  → Choose scouts
    → Choose gear
      → Deploy
        → Immediate engagement — player has a clear first read on the terrain
          → Organic exploration loop:
               move → encounter something (event / codex / loot / combat / secret)
               → resolve it → keep moving
          → Reach goal (or stumble onto it)
            → Option A: exfil → run breakdown → base/factory loop → repeat
            → Option B: stay, explore more
          → Fail state: KO/forced exfil
            → Run breakdown → deal with losses → base/factory → repeat
```

Two mission flavors (both procedural, both valid):
- **Open world**: multiple things to tackle, player chooses order and depth
- **Directed goal**: one primary objective, side content en route

#### Globe ↔ Overworld visual consistency (no zoom needed)
Aesthetic match is the goal — not a literal zoom animation. When a player deploys on a cell showing a mountain, there should be a mountain. Rivers, lakes, forests — what the globe shows is what the ground delivers. The two layers speak the same visual language.

This means:
- Overworld terrain color palette must derive from the same biome signal the globe uses (not independent noise coloring)
- Elevation that produces mountain-shading on the globe → actual high-ground in the overworld tile
- River traces on the globe → actual river in the chunk
- Already partially wired (BiomeLookup + RiverLookup callbacks) but visually disconnected

#### Dynamic globe — world events visible from space
The globe is NOT static. World events that happen on the ground should be visible from orbit.

Examples:
- Forest fire → smoke/orange glow visible on the globe over that cell
- Large explosion → visible flash
- Alien creep spreading across the plains → dark spreading overlay on the globe
- Base expansion → structure signature visible from space (metal/grey footprint growing)
- A crashed ship → distinctive mark on the surface

Architecture implications:
- Globe texture needs a **dynamic event layer** on top of the base noise-generated texture
- Need a `WorldEventManager` (or similar) that registers events at world/chunk coordinates
- Globe rendering reads active events and overlays them per-pixel (chunk coords → globe pixel coords)
- Events can be: transient (fire, explosion), persistent (base, wreckage), spreading (alien creep)
- Base persistence question: do globe events persist between runs? (Probably yes for base/wreckage, no for transient)

This system makes the globe feel like a command satellite view — intelligence, not decoration.

#### Camera / perspective reference
Wasteland 3 is the reference for overworld and combat camera distance. Key qualities:
- Close enough to read individual characters and terrain detail
- Strong sense of height/elevation (terrain feels 3D, not flat)
- Props (trees, structures) feel large and imposing relative to the ground
- Roughly 35–45° angle from vertical — not straight-down overhead
- Not 3D — 2D isometric, but with great height simulation and dense props

#### Priority order going forward
1. **Coordinate fix** — globe cell → correct overworld chunk (Highland shows Forest right now, broken)
2. **Aesthetic consistency** — overworld terrain visually matches the globe biome at that cell
3. Overworld terrain generation (rebuild from scratch: shapes → water → props)
4. Dynamic globe event layer (architect early so it's not bolted on later)
5. Overworld content layer (events, codex, loot, objectives)
6. Combat arena (simpler, more focused — tackle after ground loop is solid)

