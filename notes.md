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
