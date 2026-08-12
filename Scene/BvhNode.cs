/*
 * This file belongs to the renderer-neutral scene layer, which is the shared source of truth for geometry,
 * transforms, grouping, materials, resources, and serialization-facing state. Higher layers manipulate these
 * abstractions rather than maintaining parallel copies of scene data.
 */
using LightingShowcase.Rendering;

namespace LightingShowcase.SceneGraph;

// BvhNode is one node in a hierarchy/acceleration structure; its fields connect local data to parent/child
// traversal rather than representing an independent scene object.
/// <summary>Recursive bounding volume hierarchy node for accelerating ray/triangle queries.</summary>
public sealed class BvhNode
{
    private const int LeafSize = 8;

    private readonly BvhNode? left;
    private readonly BvhNode? right;
    private readonly Triangle[]? triangles;

    public Aabb Bounds { get; }
    // Each BVH node owns one contiguous range of the working triangle list while it is built. Small ranges become
    // leaves; larger ranges are sorted and split recursively, so the finished tree can reject whole spatial regions
    // before testing individual triangles.
    private BvhNode(List<Triangle> source, int start, int count)
    {
        Bounds = ComputeBounds(source, start, count);

        // Ranges of eight triangles or fewer become leaves because another hierarchy level would cost more
        // traversal overhead than a handful of direct triangle tests.
        if (count <= LeafSize)
        {
            triangles = new Triangle[count];
            for (int i = 0; i < count; i++)
                triangles[i] = source[start + i];
            return;
        }

        int axis = LongestAxis(Bounds);
        // Interior nodes partition geometry spatially: centroids are sorted along the box’s longest axis and the
        // contiguous range is split in half. This is inexpensive to build and usually produces useful pruning for
        // editor scenes.
        source.Sort(start, count, Comparer<Triangle>.Create((a, b) => CompareCentroid(a, b, axis)));

        int leftCount = count / 2;
        int rightCount = count - leftCount;

        left = new BvhNode(source, start, leftCount);
        right = new BvhNode(source, start + leftCount, rightCount);
    }

    // Build builds a bounding-volume hierarchy over a triangle range. It computes node bounds, chooses the longest
    // spatial axis, sorts triangle centroids on that axis, and recursively splits until leaves are small enough for
    // direct triangle tests.
    /// <summary>Builds default scene content or acceleration data depending on the owning class.</summary>
    public static BvhNode? Build(List<Triangle> triangles)
    {
        if (triangles.Count == 0)
            return null;

        List<Triangle> sorted = new(triangles);
        return new BvhNode(sorted, 0, sorted.Count);
    }

    // Intersect finds the nearest ray hit below the caller’s distance limit. Node bounds reject whole subtrees
    // cheaply; leaves test triangles, while interior nodes recurse with the closest distance found so farther hits
    // cannot replace nearer ones.
    /// <summary>Tests a ray against the primitive or bounds and returns hit information.</summary>
    public Hit? Intersect(Ray ray, double tMin, double tMax)
    {
        // Rejecting the node AABB before descending is the main acceleration step: a miss here skips every triangle
        // in the entire subtree.
        if (!Bounds.Intersect(ray, tMin, tMax))
            return null;

        if (triangles != null)
            return IntersectLeaf(ray, tMin, tMax);

        Hit? leftHit = left?.Intersect(ray, tMin, tMax);
        double closestSoFar = leftHit?.T ?? tMax;
        Hit? rightHit = right?.Intersect(ray, tMin, closestSoFar);

        return rightHit ?? leftHit;
    }
    // AnyIntersection implements the cheaper “is anything in the way?” query used by visibility and shadows. It
    // returns as soon as a valid hit is found instead of constructing the globally nearest hit.
    public bool AnyIntersection(Ray ray, double tMin, double tMax)
    {
        // Rejecting the node AABB before descending is the main acceleration step: a miss here skips every triangle
        // in the entire subtree.
        if (!Bounds.Intersect(ray, tMin, tMax))
            return false;

        if (triangles != null)
        {
            foreach (Triangle triangle in triangles)
            {
                Hit? hit = triangle.Intersect(ray);
                if (hit != null && hit.T > tMin && hit.T < tMax)
                    return true;
            }

            return false;
        }

        return (left?.AnyIntersection(ray, tMin, tMax) ?? false)
            || (right?.AnyIntersection(ray, tMin, tMax) ?? false);
    }

    // ShadowOpacity estimates how much light survives transparent blockers by sampling intersections/material alpha
    // and reducing the remaining light, so shadows can be partial rather than only binary.
    /// <summary>Accumulates approximate opacity along a shadow ray, allowing transparent glTF material to transmit light.</summary>
    public double ShadowOpacity(Ray ray, double tMin, double tMax, int maxSamples)
    {
        if (maxSamples <= 0 || !Bounds.Intersect(ray, tMin, tMax))
            return 0.0;

        if (triangles != null)
        {
            double remaining = 1.0;
            int samples = 0;
            foreach (Triangle triangle in triangles)
            {
                Hit? hit = triangle.Intersect(ray);
                if (hit == null || hit.T <= tMin || hit.T >= tMax)
                    continue;

                double opacity = hit.Material.SampleAlpha(hit.TextureU, hit.TextureV) * (1.0 - hit.Material.Transmission * 0.82);
                // Opacity is accumulated multiplicatively as transmitted light. Two 50% blockers therefore leave
                // 25% light rather than simply adding to 100% opacity, which better approximates layered
                // transparent materials.
                remaining *= 1.0 - Math.Clamp(opacity, 0.0, 1.0);
                samples++;
                if (remaining <= 0.02 || samples >= maxSamples)
                    break;
            }

            return 1.0 - remaining;
        }

        double leftOpacity = left?.ShadowOpacity(ray, tMin, tMax, maxSamples) ?? 0.0;
        if (leftOpacity >= 0.98)
            return leftOpacity;
        double rightOpacity = right?.ShadowOpacity(ray, tMin, tMax, maxSamples) ?? 0.0;
        return 1.0 - (1.0 - leftOpacity) * (1.0 - rightOpacity);
    }
    // IntersectLeaf tests only the triangles assigned to one leaf and keeps the nearest hit that is closer than the
    // current limit.
    private Hit? IntersectLeaf(Ray ray, double tMin, double tMax)
    {
        Hit? closest = null;
        double closestSoFar = tMax;

        foreach (Triangle triangle in triangles!)
        {
            Hit? hit = triangle.Intersect(ray);
            if (hit != null && hit.T > tMin && hit.T < closestSoFar)
            {
                closestSoFar = hit.T;
                closest = hit;
            }
        }

        return closest;
    }
    // ComputeBounds unions the bounds of the node’s triangle range so an AABB can reject rays before any
    // per-triangle tests.
    private static Aabb ComputeBounds(List<Triangle> source, int start, int count)
    {
        Aabb bounds = source[start].Bounds;
        for (int i = 1; i < count; i++)
            bounds = Aabb.Surrounding(bounds, source[start + i].Bounds);
        return bounds;
    }
    // LongestAxis chooses the largest X/Y/Z extent of a bounding box; that axis is used to partition triangles
    // spatially during BVH construction.
    private static int LongestAxis(Aabb bounds)
    {
        double x = bounds.Max.X - bounds.Min.X;
        double y = bounds.Max.Y - bounds.Min.Y;
        double z = bounds.Max.Z - bounds.Min.Z;

        if (x >= y && x >= z) return 0;
        if (y >= z) return 1;
        return 2;
    }
    // CompareCentroid orders triangles by centroid coordinate on the selected split axis before the range is
    // divided into child nodes.
    private static int CompareCentroid(Triangle a, Triangle b, int axis)
    {
        double ca = axis switch
        {
            0 => a.Centroid.X,
            1 => a.Centroid.Y,
            _ => a.Centroid.Z
        };

        double cb = axis switch
        {
            0 => b.Centroid.X,
            1 => b.Centroid.Y,
            _ => b.Centroid.Z
        };

        return ca.CompareTo(cb);
    }
}
