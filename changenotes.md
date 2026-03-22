# Frontier Protocol — Change Notes

## 2026-03-17 — 3D Terrain (Option C) + Bug Fixes

### TileHeight 32 → 48
- `Chunk.cs`: TileHeight raised from 32 to 48 (~48° apparent camera angle vs old ~30°)
- `ChunkTerrainMesh.cs`: TH/HTH constants updated to match (48/24)
- `DebugHUD.cs`: biome map TileH updated proportionally (9f → 13.5f)

### Globe → overworld biome mismatch fix
- `GlobeView.GetBiomeForChunk` now reads `_gridData[gi,gj].DominantBiome` instead of
  re-running `ChunkGenerator.GetBiome()` (which used independent noise and was a no-op)

### Option C — 2.5D terrain (3D SubViewport + 2D sprites)
**New files:**
- `scripts/world/OverworldRenderer.cs` — CanvasLayer(-5) containing a SubViewport with
  Camera3D at RotationDegrees(-45,0,0). Manages TerrainChunk3D nodes. Syncs camera each
  frame to the 2D Camera2D. Starts hidden; WorldScene enables it on EnterIso.
  Camera math: Size = vpHeight / Camera2D.Zoom.Y; Position = (camX, CamHeight, camY·√2 + CamHeight)
- `scripts/world/TerrainChunk3D.cs` — Node3D that generates a 3D heightfield mesh from
  ChunkData. Vertex formula: x3d=(u-v)·HTW, y3d=h·√2, z3d=(u+v)·HTH·√2. Unshaded vertex
  colors. Same noise/height/water-feature logic as ChunkTerrainMesh.

**Updated files:**
- `scripts/world/ChunkManager.cs` — Finds OverworldRenderer sibling in _Ready(); sets
  Chunk.UseThreeDRenderer=true; calls AddChunk/RemoveChunk/ClearAll alongside 2D chunks
- `scripts/world/Chunk.cs` — UseThreeDRenderer flag; skips ChunkTerrainMesh when true
- `scenes/world.tscn` — OverworldRenderer node added as CanvasLayer(-5)
- `scripts/world/WorldScene.cs` — EnterGlobe/EnterIso call _overworld.SetActive(false/true)

### Bug fixes (3D terrain session)
- **Globe leak in overworld mode**: OverworldRenderer now starts Visible=false; SetActive()
  toggles both Visible and SubViewport.UpdateMode (Disabled in globe, Always in iso)
- **Transparent background**: SubViewport.TransparentBg=true; env.BackgroundColor=(0,0,0,0)
  — stars from 2D StarBackground layer show through void/water areas
- **Land tile color bug**: TerrainChunk3D.GetTileColor now uses tile TYPE to decide water vs
  land color, not avgH. Previously, Grassland tiles in ocean-noise areas had their height
  pushed below WaterThreshold by the globe macro elevation bias, causing them to render as
  dark ocean instead of green land.
- **Land tile height clamp**: land tiles (baseH ≥ WaterThreshold) now clamped to min ShoreH,
  preventing them from visually sinking below the water line in ocean-noise areas.

### Known issues / not yet fixed
- Globe blue background: atmosphere shader fills more screen area at certain zoom/planet angles;
  pre-existing visual, unrelated to 3D terrain changes
- Overworld biome debug HUD shows noise-based biome (not actual loaded biome); cosmetic only
- Props not yet in 3D terrain (planned next)
- Cliff faces not yet in 3D terrain (natural heightfield provides depth cues instead)
