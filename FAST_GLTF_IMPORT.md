# Fast glTF / GLB import

The glTF importer now uses a realtime-preview fast path automatically.

- Parses JSON directly from UTF-8 bytes rather than creating an intermediate string.
- Uses POSITION accessor `min`/`max` values for scene bounds when available; only files without accessor bounds require a full position scan.
- Normalizes imported positions while decoding them instead of making a second vertex pass.
- Avoids allocating an identity index list for non-indexed primitives.
- Preallocates each primitive's triangle list and adds directly to its target group.
- Calculates group pivots without allocating a temporary list containing every vertex.
- Reuses immutable local `Triangle` objects as world geometry when the imported hierarchy has identity transforms.
- Does not rebuild unchanged geometry merely to add the composer's top-level asset wrapper.
- Defers the CPU ray-query BVH for Vulkan raster/compute preview. The BVH is built lazily on the first CPU ray query, CPU ray-traced render, or triangle pick.

After a glTF load, the status line reports phase timings such as JSON, resources, bounds, geometry, and finalization. It also reports whether accessor bounds were used and confirms that the BVH was deferred.

The editor still converts glTF index triples into its canonical `Triangle` objects. The changes above remove the redundant bounds pass, world-triangle clone pass, eager BVH build, and large pivot scratch allocations without replacing the existing editing model.
