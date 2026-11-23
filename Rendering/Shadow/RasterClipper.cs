// File: Rendering/Shadow/RasterClipper.cs
// Purpose: Near-plane clipping with original barycentric payloads.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal static class RasterClipper
{
    /// <summary>Clipped vertex with raster position and original barycentric payload.</summary>
    internal readonly record struct ClippedVertex(double X, double Y, double Wa, double Wb, double Wc, bool IsOriginal);

    /// <summary>Clipped triangle fan with original-barycentric vertices.</summary>
    internal readonly record struct ClippedTriangle(ClippedVertex A, ClippedVertex B, ClippedVertex C)
    {
        /// <summary>Maps clipped barycentrics back to original triangle barycentrics.</summary>
        public (double Wa, double Wb, double Wc) Map(double qa, double qb, double qc) =>
        (
            qa * A.Wa + qb * B.Wa + qc * C.Wa,
            qa * A.Wb + qb * B.Wb + qc * C.Wb,
            qa * A.Wc + qb * B.Wc + qc * C.Wc
        );
    }

    /// <summary>Visible original-edge subsegment with screen-space endpoints.</summary>
    internal readonly record struct ClippedEdge(double X0, double Y0, double X1, double Y1);

    /// <summary>Clips a world triangle against the camera near plane.</summary>
    public static bool TryClip(
        Triangle tri, Vec3 cameraPosition, CameraBasis basis, int width, int height,
        out ClippedTriangle[] clipped, out ClippedEdge[] edges, out double za, out double zb, out double zc)
    {
        clipped = Array.Empty<ClippedTriangle>();
        edges = Array.Empty<ClippedEdge>();
        za = zb = zc = 0.0;
        if (!CameraProjector.TryGetCameraSpace(tri.A, cameraPosition, basis, out double ax, out double ay, out double az) ||
            !CameraProjector.TryGetCameraSpace(tri.B, cameraPosition, basis, out double bx, out double by, out double bz) ||
            !CameraProjector.TryGetCameraSpace(tri.C, cameraPosition, basis, out double cx, out double cy, out double cz))
            return false;
        za = az;
        zb = bz;
        zc = cz;

        double near = ShadowLimits.CameraNear;
        (double X, double Y, double Z, double Wa, double Wb, double Wc, bool Original)[] input =
        {
            (ax, ay, az, 1.0, 0.0, 0.0, true),
            (bx, by, bz, 0.0, 1.0, 0.0, true),
            (cx, cy, cz, 0.0, 0.0, 1.0, true)
        };
        List<(double X, double Y, double Z, double Wa, double Wb, double Wc, bool Original)> output = new(4);
        for (int i = 0; i < 3; i++)
        {
            var current = input[i];
            var next = input[(i + 1) % 3];
            bool currentIn = current.Z > near;
            bool nextIn = next.Z > near;
            if (currentIn)
                output.Add(current);
            if (currentIn != nextIn)
            {
                double t = (near - current.Z) / (next.Z - current.Z);
                output.Add((
                    current.X + t * (next.X - current.X),
                    current.Y + t * (next.Y - current.Y),
                    current.Z + t * (next.Z - current.Z),
                    current.Wa + t * (next.Wa - current.Wa),
                    current.Wb + t * (next.Wb - current.Wb),
                    current.Wc + t * (next.Wc - current.Wc),
                    false));
            }
        }
        if (output.Count < 3)
            return false;

        ClippedVertex[] verts = new ClippedVertex[output.Count];
        for (int i = 0; i < output.Count; i++)
        {
            var v = output[i];
            if (!CameraProjector.ProjectCameraSpace(v.X, v.Y, Math.Max(v.Z, near), width, height, out double x, out double y))
                return false;
            verts[i] = new ClippedVertex(x, y, v.Wa, v.Wb, v.Wc, v.Original);
        }

        List<ClippedTriangle> triangles = new(2);
        for (int i = 1; i + 1 < verts.Length; i++)
            triangles.Add(new ClippedTriangle(verts[0], verts[i], verts[i + 1]));
        clipped = triangles.ToArray();

        List<ClippedEdge> drawn = new(3);
        for (int i = 0; i < verts.Length; i++)
        {
            ClippedVertex p = verts[i];
            ClippedVertex q = verts[(i + 1) % verts.Length];
            if (p.IsOriginal || q.IsOriginal)
                drawn.Add(new ClippedEdge(p.X, p.Y, q.X, q.Y));
        }
        edges = drawn.ToArray();
        return true;
    }
}
