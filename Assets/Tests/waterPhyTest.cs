using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class WaterPhysicsTests
{
    private ComputeBuffer testBuffer;

    [Test]
    public void TriangleBuffer_GeneratesCorrectClockwiseWinding()
    {
        // 1. LOAD THE SHADER MANUALLY
        // You MUST update this path to where your compute shader actually lives in your project!
        ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/waterphy.compute");
        
        Assert.IsNotNull(shader, "Failed to load Compute Shader! Check the file path.");

        // 2. SETUP THE GPU DATA
        int res = 3; 
        int length = (res - 1) * (res - 1) * 6;
        testBuffer = new ComputeBuffer(length, sizeof(int));

        // 3. EXECUTE THE GPU KERNEL
        int kernel = shader.FindKernel("buildTriangle");
        shader.SetInt("_resolution", res);
        shader.SetBuffer(kernel, "_triangles", testBuffer);
        
        // Dispatch 1 group (since 3x3 easily fits inside an 8x8 thread group)
        shader.Dispatch(kernel, 1, 1, 1); 

        // 4. PULL THE DATA BACK TO THE CPU
        int[] triangles = new int[length];
        testBuffer.GetData(triangles);

        // 5. RUN THE ASSERTIONS
        int expectedBottomLeft = res;
        int expectedBottomRight = res + 1;

        // Triangle 1
        Assert.AreEqual(0, triangles[0], "Triangle 1 TL is wrong.");
        Assert.AreEqual(1, triangles[1], "Triangle 1 TR is wrong.");
        Assert.AreEqual(expectedBottomLeft, triangles[2], "Triangle 1 BL is wrong.");

        // Triangle 2
        Assert.AreEqual(expectedBottomRight, triangles[3], "Triangle 2 BR is wrong.");
        Assert.AreEqual(expectedBottomLeft, triangles[4], "Triangle 2 BL is wrong.");
        Assert.AreEqual(1, triangles[5], "Triangle 2 TR is wrong.");
    }

    // 6. GUARANTEE MEMORY CLEANUP
    [TearDown]
    public void Cleanup()
    {
        // Even if the Assertions fail and crash the test, this runs and prevents VRAM leaks
        if (testBuffer != null)
        {
            testBuffer.Release();
            testBuffer = null;
        }
    }
}