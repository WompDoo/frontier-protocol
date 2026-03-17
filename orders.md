# Godot C# Optimization & Refactoring Task

## 🎯 Primary Goal
Optimize the procedural generation pipeline and general codebase to improve frame stability and reduce memory pressure. The project must remain visually and functionally identical to the current state.

---

## 🛠 Refactoring Directives

### 1. C# & Memory Efficiency (GC Reduction)
* **Hot Path Audit:** Analyze `_Process`, `_PhysicsProcess`, and any loops within procedural generation classes. 
* **Heap Allocations:** Replace frequent `new` object allocations with object pooling or struct-based data where possible.
* **LINQ Cleanup:** Replace heavy LINQ queries (`.Where()`, `.Select()`) in generation loops with standard `for` or `foreach` loops to minimize the garbage collector's workload.
* **Collection Optimization:** Pre-size `List<T>` or `Dictionary<K,V>` using the `capacity` constructor if the final size is predictable.

### 2. Godot-Specific Optimizations
* **Node Access:** Ensure `GetNode<T>()` or `%UniqueNodes` are cached in `_Ready()` and not called during runtime loops.
* **Signal Cleanup:** Disconnect signals that are no longer needed or belong to "dead" objects.
* **Resource Management:** Identify and remove unused `.tscn`, `.res`, or `.cs` files that are not referenced in the project.

### 3. Procedural Generation Integrity
* **RNG Stability:** Do not modify the math behind noise generation ($Mathf.PerlinNoise$, etc.) or seed handling. The world must generate exactly as it does now.
* **Vertex/Mesh Data:** Optimize `SurfaceTool` or `MeshDataTool` calls. Avoid redundant mesh rebuilds if the data hasn't changed.

---

## 🚫 Constraints (The "Do Not Touch" List)
* **Graphical Fidelity:** No reduction in texture resolution, shader passes, or particle counts.
* **Physics Accuracy:** Do not change collision layers or solver iterations.
* **Game Logic:** All gameplay features, NPC behaviors, and generation rules must remain intact.

---

## 📈 Future Standards for Claude Code
When writing new code for this project, always:
1.  **Prefer Spans:** Use `ReadOnlySpan<T>` for large data arrays.
2.  **Avoid String Concatenation:** Use `StringBuilder` or string interpolation sparingly in loops.
3.  **Type Safety:** Use `typeof()` and `nameof()` to avoid magic strings.
4.  **Static Analysis:** Flags any code that creates more than $O(n)$ complexity in the generation phase for manual review.