using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Composer;

/// <summary>Headless end-to-end regression test for baked transforms and hierarchy.</summary>
internal static class TransformRegressionTest
{
    public static int Run()
    {
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "LightingShowcaseComposerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string modelPath = Path.Combine(temporaryDirectory, "triangle.obj");
        File.WriteAllText(modelPath, """
            o Triangle
            v 0 0 0
            v 2 0 0
            v 0 1 0
            f 1 2 3
            """);

        try
        {
            using ComposerSceneSession session = new();
            int rootId = session.Insert(modelPath, CancellationToken.None);
            SceneObjectInfo rootInfo = session.GetObjectInfos().First(info => info.Id == rootId);
            if (rootInfo.ChildCount < 1)
                return Fail("Inserted model was not wrapped in an expandable parent node.");

            ComposerModelEvidence original = session.GetModelEvidence(rootId)
                ?? throw new InvalidOperationException("Inserted model evidence was not found.");
            bool updated = session.UpdateObject(
                rootId,
                "Triangle",
                visible: true,
                position: new Vec3(2.5, -1.25, 3.0),
                rotationRadians: new Vec3(0.0, 0.0, Math.PI * 0.5),
                scale: new Vec3(2.0, 3.0, 1.0));
            if (!updated)
                return Fail("ComposerSceneSession.UpdateObject returned false.");

            ComposerObjectState transformedState = session.GetObjectState(rootId)
                ?? throw new InvalidOperationException("Transformed root node was not found.");
            AssertNear(transformedState.Position, Vec3.Zero, "baked position identity");
            AssertNear(transformedState.Rotation, Vec3.Zero, "baked rotation identity");
            AssertNear(transformedState.Scale, new Vec3(1, 1, 1), "baked scale identity");

            ComposerModelEvidence transformed = session.GetModelEvidence(rootId)
                ?? throw new InvalidOperationException("Transformed model evidence was not found.");
            if (original.LocalGeometryHash == transformed.LocalGeometryHash)
                return Fail("Underlying local triangle geometry did not change.");

            if (session.Undo() != rootId)
                return Fail("Undo did not restore the transformed node selection.");
            ComposerModelEvidence undone = session.GetModelEvidence(rootId)
                ?? throw new InvalidOperationException("Undone model evidence was not found.");
            if (undone.LocalGeometryHash != original.LocalGeometryHash)
                return Fail("Undo did not restore the original local triangle geometry.");

            if (session.Redo() != rootId)
                return Fail("Redo did not restore the transformed node selection.");
            ComposerModelEvidence redone = session.GetModelEvidence(rootId)
                ?? throw new InvalidOperationException("Redone model evidence was not found.");
            if (redone.LocalGeometryHash != transformed.LocalGeometryHash)
                return Fail("Redo did not restore the baked geometry.");

            Console.WriteLine("Hierarchy, baked transform, undo and redo regression test passed.");
            return 0;
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); }
            catch { }
        }
    }

    private static bool Near(Vec3 left, Vec3 right)
    {
        const double tolerance = 1e-7;
        return Math.Abs(left.X - right.X) <= tolerance &&
               Math.Abs(left.Y - right.Y) <= tolerance &&
               Math.Abs(left.Z - right.Z) <= tolerance;
    }

    private static void AssertNear(Vec3 actual, Vec3 expected, string operation)
    {
        if (!Near(actual, expected))
            throw new InvalidOperationException(
                $"Transform regression failed for {operation}: expected ({expected.X}, {expected.Y}, {expected.Z}), " +
                $"got ({actual.X}, {actual.Y}, {actual.Z}).");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
