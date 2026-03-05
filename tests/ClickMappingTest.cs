/// <summary>
/// Standalone tests for the globe click-to-cell coordinate mapping.
/// Run with: dotnet run --project tests/ClickMappingTest.csproj
///
/// Godot SphereMesh UV convention:
///   vertex.x = sin(phi) * cos(lat)
///   vertex.z = cos(phi) * cos(lat)
///   UV.x     = Wrap(phi, 0, 2π) / 2π    (seam at phi=0, i.e. +Z direction)
///   UV.y     = 0.5 - lat / π            (0 = north pole +Y, 1 = south pole -Y)
/// </summary>

using System;
using System.Collections.Generic;

static class ClickMappingTest
{
    const float Pi  = MathF.PI;
    const float Tau = 2f * Pi;
    const int   GridLon = 24;   // GlobeView.GridDivLon
    const int   GridLat = 12;   // GlobeView.GridDivLat

    static float Wrap(float v, float min, float max)
    {
        float range = max - min;
        return v - range * MathF.Floor((v - min) / range);
    }

    // Mirrors TryClickGridCell math exactly
    static (int gi, int gj, float targetRot) ClickToCell(float hitX, float hitY, float hitZ)
    {
        float lat = MathF.Asin(Math.Clamp(hitY, -1f, 1f));
        float phi = MathF.Atan2(hitX, hitZ);           // Godot: x=sin(phi), z=cos(phi)
        float u   = Wrap(phi, 0f, Tau) / Tau;
        float v   = 0.5f - lat / Pi;
        int   gi  = Math.Clamp((int)(u * GridLon), 0, GridLon - 1);
        int   gj  = Math.Clamp((int)(v * GridLat), 0, GridLat - 1);
        return (gi, gj, -phi);
    }

    // Mirrors the grid shader cell-index computation for a given (phi, lat) world point
    static (int ci, int cj) ShaderCell(float phi, float lat)
    {
        float uv_x = Wrap(phi, 0f, Tau) / Tau;
        float uv_y = 0.5f - lat / Pi;
        return ((int)(uv_x * GridLon), (int)(uv_y * GridLat));
    }

    static int _pass, _fail;

    static void Assert(bool cond, string msg)
    {
        if (cond) { _pass++; Console.WriteLine($"  PASS  {msg}"); }
        else       { _fail++; Console.WriteLine($"  FAIL  {msg}"); }
    }

    static void AssertEq(int a, int b, string msg) =>
        Assert(a == b, $"{msg}  (got {a}, expected {b})");

    static void AssertNear(float a, float b, string msg, float tol = 1e-4f) =>
        Assert(MathF.Abs(a - b) < tol, $"{msg}  (got {a:F5}, expected {b:F5})");

    static void Main()
    {
        Console.WriteLine("── Click → Cell mapping ───────────────────────────────────────────────");

        // Front face: camera is at +Z, so +Z face is directly in front
        {
            var (gi, gj, rot) = ClickToCell(0, 0, 1);
            AssertEq(0,  gi,  "+Z front face → gi=0  (UV.x seam is at +Z)");
            AssertEq(6,  gj,  "+Z equator     → gj=6  (equator is centre row)");
            AssertNear(0f, rot, "+Z front face → no snap rotation needed");
        }

        // Right face (+X): phi=π/2, UV.x=0.25, gi=6
        {
            var (gi, gj, rot) = ClickToCell(1, 0, 0);
            AssertEq(6,  gi,  "+X right face  → gi=6  (UV.x=0.25)");
            AssertNear(-Pi/2f, rot, "+X face snap rotates -π/2");
        }

        // Back face (−Z): phi=π, UV.x=0.5, gi=12
        {
            var (gi, gj, rot) = ClickToCell(0, 0, -1);
            AssertEq(12, gi, "−Z back face   → gi=12 (UV.x=0.5)");
        }

        // Left face (−X): phi=−π/2 → Wrap → 3π/2, UV.x=0.75, gi=18
        {
            var (gi, gj, rot) = ClickToCell(-1, 0, 0);
            AssertEq(18, gi, "−X left face   → gi=18 (UV.x=0.75)");
            AssertNear(Pi/2f, rot, "−X face snap rotates +π/2");
        }

        // North pole (+Y): lat=π/2, v=0, gj=0
        {
            var (gi, gj, _) = ClickToCell(0, 1, 0.001f); // tiny z to avoid atan2 ambiguity
            AssertEq(0,  gj, "North pole     → gj=0");
        }

        // South pole (−Y): lat=−π/2, v=1, gj=11 (clamped)
        {
            var (gi, gj, _) = ClickToCell(0, -1, 0.001f);
            AssertEq(11, gj, "South pole     → gj=11");
        }

        Console.WriteLine();
        Console.WriteLine("── Click gi/gj matches grid shader ci/cj ──────────────────────────────");

        // For several points, verify click gi/gj == shader ci/cj
        // Poles are degenerate (atan2(0,0) undefined); exclude them from this check.
        var samples = new (float phi, float lat, string label)[]
        {
            (0f,         0f,       "+Z equator"),
            (Pi/2f,      0f,       "+X equator"),
            (Pi,         0f,       "−Z equator"),
            (Pi/4f,      Pi/6f,    "NE quadrant"),
            (-Pi/3f,     -Pi/4f,   "SW quadrant"),
        };

        foreach (var (phi, lat, label) in samples)
        {
            float hitX = MathF.Sin(phi) * MathF.Cos(lat);
            float hitY = MathF.Sin(lat);
            float hitZ = MathF.Cos(phi) * MathF.Cos(lat);

            var (gi, gj, _) = ClickToCell(hitX, hitY, hitZ);
            var (ci, cj)    = ShaderCell(phi, lat);

            Assert(gi == ci && gj == cj,
                $"{label,-24}  click=({gi},{gj})  shader=({ci},{cj})");
        }

        Console.WriteLine();
        Console.WriteLine("── Snap-to-target rotation ─────────────────────────────────────────────");

        // After rotation by targetRot, the hit phi should face +Z (phi_world = 0)
        foreach (var (phi, lat, label) in samples[..5])
        {
            float hitX = MathF.Sin(phi) * MathF.Cos(lat);
            float hitY = MathF.Sin(lat);
            float hitZ = MathF.Cos(phi) * MathF.Cos(lat);

            var (_, _, targetRot) = ClickToCell(hitX, hitY, hitZ);
            float phiAfterSnap = phi + targetRot;   // phi_world = phi_local + Rotation.Y
            // Should be 0 (mod 2π)
            float residual = Wrap(phiAfterSnap, -Pi, Pi);
            AssertNear(0f, residual, $"{label,-24}  snap phi residual", tol: 1e-4f);
        }

        Console.WriteLine();
        Console.WriteLine($"Results: {_pass} passed, {_fail} failed");
        Environment.Exit(_fail > 0 ? 1 : 0);
    }
}
