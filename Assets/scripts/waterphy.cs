using UnityEngine;
using UnityEngine.InputSystem;

public class waterphy : MonoBehaviour
{
    [Header("Simulation Settings")]
    [SerializeField] ComputeShader computeShader;
    [SerializeField] Material waterMaterial;

    [Header("Simulation Grid (Matched to CPU)")]
    public int resolution = 32;          // Matches cpuwater.cs
    public float puddleSize = 5f;        // Matches cpuwater.cs
    public float damping = 0.96f;        // Matches cpuwater.cs

    [Header("Physical Constants (Matched to CPU)")]
    public float gravity = 9.81f;        // Matches cpuwater.cs
    public float density = 1000f;        // Matches cpuwater.cs
    public float pipeArea = 0.01f;       // Matches cpuwater.cs

    [Header("Mouse Interaction (Matched to CPU)")]
    public float clickForce = 50f;       // Matches cpuwater.cs

    [Header("Collider Settings")]
    public float colliderYOffset = 0.1f;
    public float colliderThickness = 0.1f;
    
    private ComputeBuffer vertexBuffer;
    private ComputeBuffer triangleBuffer;
    private ComputeBuffer heightBuffer;
    private ComputeBuffer extPressureBuf;
    private ComputeBuffer flowsBuffer;
    private ComputeBuffer volumeBuffer;

    private int kernalTriangles;
    private int kernalUpdatePos;
    private int kernalCalculateHeight;
    private int kernalCalculateFlow;
    private int totalTriangle;

    void OnEnable()
    {
        float pipeLength = puddleSize / resolution;
        float startHeight = 0.1f;

        float[] initHeights = new float[resolution * resolution];
        float[] initVolumes = new float[resolution * resolution];

        for (int j = 0; j < resolution * resolution; j++)
        {
            initHeights[j] = startHeight;
            initVolumes[j] = startHeight * (pipeLength * pipeLength);
        }

        BoxCollider col = gameObject.GetComponent<BoxCollider>();
        if (col == null) { col = gameObject.AddComponent<BoxCollider>(); }

        col.size = new Vector3(puddleSize, colliderThickness, puddleSize);
        col.center = new Vector3(0, colliderYOffset - (colliderThickness / 2f), 0);

        heightBuffer = new ComputeBuffer(resolution * resolution, sizeof(float));
        heightBuffer.SetData(initHeights);

        extPressureBuf = new ComputeBuffer(resolution * resolution, sizeof(float));
        extPressureBuf.SetData(new float[resolution * resolution]);

        flowsBuffer = new ComputeBuffer(resolution * resolution * 8, sizeof(float));
        flowsBuffer.SetData(new float[resolution * resolution * 8]);

        volumeBuffer = new ComputeBuffer(resolution * resolution, sizeof(float));
        volumeBuffer.SetData(initVolumes);

        vertexBuffer = new ComputeBuffer(resolution * resolution, 12);

        totalTriangle = (resolution - 1) * (resolution - 1) * 6;
        triangleBuffer = new ComputeBuffer(totalTriangle, sizeof(int));

        kernalTriangles = computeShader.FindKernel("buildTriangle");
        kernalCalculateHeight = computeShader.FindKernel("calculateHeight");
        kernalCalculateFlow = computeShader.FindKernel("calculateFlow");
        kernalUpdatePos = computeShader.FindKernel("updatePos");

        computeShader.SetInt("_resolution", resolution);
        computeShader.SetFloat("_pipeLength", pipeLength);
        computeShader.SetFloat("_gravity", gravity);
        computeShader.SetFloat("_pipeArea", pipeArea);
        computeShader.SetFloat("_density", density);
        computeShader.SetFloat("_damping", damping);

        computeShader.SetBuffer(kernalTriangles, "_triangles", triangleBuffer);

        computeShader.SetBuffer(kernalCalculateFlow, "_height", heightBuffer);
        computeShader.SetBuffer(kernalCalculateFlow, "_extPressure", extPressureBuf);
        computeShader.SetBuffer(kernalCalculateFlow, "_flows", flowsBuffer);
        computeShader.SetBuffer(kernalCalculateFlow, "_position", vertexBuffer);

        computeShader.SetBuffer(kernalCalculateHeight, "_height", heightBuffer);
        computeShader.SetBuffer(kernalCalculateHeight, "_extPressure", extPressureBuf);
        computeShader.SetBuffer(kernalCalculateHeight, "_volume", volumeBuffer);
        computeShader.SetBuffer(kernalCalculateHeight, "_flows", flowsBuffer);
        computeShader.SetBuffer(kernalCalculateHeight, "_position", vertexBuffer);

        computeShader.SetBuffer(kernalUpdatePos, "_height", heightBuffer);
        computeShader.SetBuffer(kernalUpdatePos, "_position", vertexBuffer);

        int group = Mathf.CeilToInt(resolution / 8f);
        computeShader.Dispatch(kernalTriangles, group, group, 1);

        float step = puddleSize / (resolution - 1);
        float offset = puddleSize / 2f;
        computeShader.SetFloat("_step", step);
        computeShader.SetFloat("_offset", offset);
        computeShader.Dispatch(kernalUpdatePos, group, group, 1);
    }

    void OnDisable()
    {
        if (vertexBuffer != null) vertexBuffer.Release();
        if (triangleBuffer != null) triangleBuffer.Release();
        if (heightBuffer != null) heightBuffer.Release();
        if (extPressureBuf != null) extPressureBuf.Release();
        if (flowsBuffer != null) flowsBuffer.Release();
        if (volumeBuffer != null) volumeBuffer.Release();
    }

    void Update()
    {
        float[] pressureData = new float[resolution * resolution];
        float deltaT = Time.deltaTime;
        
        if (float.IsNaN(deltaT) || float.IsInfinity(deltaT) || deltaT <= 0f)
        {
            deltaT = 0.016f;
        }

        // Raycast logic identical to cpuwater.cs
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit))
            {
                if (hit.collider.gameObject == gameObject)
                {
                    Vector3 localPos = transform.InverseTransformPoint(hit.point);

                    float halfSize = puddleSize / 2f;
                    int i = Mathf.FloorToInt(((localPos.x + halfSize) / puddleSize) * resolution);
                    int j = Mathf.FloorToInt(((localPos.z + halfSize) / puddleSize) * resolution);

                    if (i >= 0 && i < resolution && j >= 0 && j < resolution)
                    {
                        float pipeLength = puddleSize / resolution;
                        float area = pipeLength * pipeLength;
                        
                        int idx = (j * resolution) + i;
                        // Scaled exact pressure addition like CPU
                        pressureData[idx] += clickForce / (4f * area);
                    }
                }
            }
        }
        
        extPressureBuf.SetData(pressureData);

        float step = puddleSize / (resolution - 1);
        float offset = puddleSize / 2f;
        computeShader.SetFloat("_step", step);
        computeShader.SetFloat("_offset", offset);
        computeShader.SetInt("_resolution", resolution);

        int group = Mathf.CeilToInt(resolution / 8f);
        
        // Single step per frame to keep physics integration step matching CPU deltaT
        computeShader.SetFloat("_dt", deltaT);
        computeShader.Dispatch(kernalCalculateFlow, group, group, 1);
        computeShader.Dispatch(kernalCalculateHeight, group, group, 1);

        computeShader.Dispatch(kernalUpdatePos, group, group, 1);

        RenderParams rp = new RenderParams(waterMaterial);
        rp.worldBounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
        
        rp.matProps = new MaterialPropertyBlock();
        rp.matProps.SetBuffer("_position", vertexBuffer);
        rp.matProps.SetBuffer("_triangles", triangleBuffer);
        
        Matrix4x4 dynamicMatrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.localScale);
        rp.matProps.SetMatrix("_objectToWorld", dynamicMatrix);
        
        Graphics.RenderPrimitives(rp, MeshTopology.Triangles, totalTriangle, 1);
    }
}