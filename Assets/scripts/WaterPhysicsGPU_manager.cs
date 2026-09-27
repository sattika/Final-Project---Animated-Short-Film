using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterPhysicsGPU_manager : MonoBehaviour
{
    [SerializeField] private ComputeShader computeShader;
    [SerializeField] private Material waterMaterial;

    public int gridResolution = 32;
    public float domainSize = 5f;
    public float dampingFactor = 0.96f;

    public float gravityConstant = 9.81f;
    public float fluidDensity = 1000f;
    public float pipeCrossSectionArea = 0.01f;

    public float mouseInteractionForce = 50f;

    private float colliderYOffset = 0.1f;
    private float colliderThickness = 0.1f;

    private ComputeBuffer vertexBuffer;
    private ComputeBuffer triangleBuffer;
    private ComputeBuffer heightBuffer;
    private ComputeBuffer externalPressureBuffer;
    private ComputeBuffer fluidFlowsBuffer;
    private ComputeBuffer fluidVolumeBuffer;

    private int buildTrianglesKernel;
    private int updatePositionsKernel;
    private int calculateHeightsKernel;
    private int calculateFlowsKernel;
    private int totalTriangleIndices;

    // CPU Mesh storage
    private Mesh waterMesh;
    private Vector3[] cpuPositions;
    private int[] cpuTriangles;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;

    void OnEnable()
    {
        float cellPipeLength = domainSize / gridResolution;
        float initialWaterHeight = 0.1f;

        float[] initialHeights = new float[gridResolution * gridResolution];
        float[] initialVolumes = new float[gridResolution * gridResolution];

        for (int index = 0; index < gridResolution * gridResolution; index++)
        {
            initialHeights[index] = initialWaterHeight;
            initialVolumes[index] = initialWaterHeight * (cellPipeLength * cellPipeLength);
        }

        BoxCollider boxCollider = gameObject.GetComponent<BoxCollider>();
        if (boxCollider == null) 
        { 
            boxCollider = gameObject.AddComponent<BoxCollider>(); 
        }

        boxCollider.size = new Vector3(domainSize, colliderThickness, domainSize);
        boxCollider.center = new Vector3(0f, colliderYOffset - (colliderThickness / 2f), 0f);

        heightBuffer = new ComputeBuffer(gridResolution * gridResolution, sizeof(float));
        heightBuffer.SetData(initialHeights);

        externalPressureBuffer = new ComputeBuffer(gridResolution * gridResolution, sizeof(float));
        externalPressureBuffer.SetData(new float[gridResolution * gridResolution]);

        fluidFlowsBuffer = new ComputeBuffer(gridResolution * gridResolution * 8, sizeof(float));
        fluidFlowsBuffer.SetData(new float[gridResolution * gridResolution * 8]);

        fluidVolumeBuffer = new ComputeBuffer(gridResolution * gridResolution, sizeof(float));
        fluidVolumeBuffer.SetData(initialVolumes);

        vertexBuffer = new ComputeBuffer(gridResolution * gridResolution, 12);

        totalTriangleIndices = (gridResolution - 1) * (gridResolution - 1) * 6;
        triangleBuffer = new ComputeBuffer(totalTriangleIndices, sizeof(int));

        buildTrianglesKernel = computeShader.FindKernel("buildTriangle");
        calculateHeightsKernel = computeShader.FindKernel("calculateHeight");
        calculateFlowsKernel = computeShader.FindKernel("calculateFlow");
        updatePositionsKernel = computeShader.FindKernel("updatePos");

        computeShader.SetInt("_gridResolution", gridResolution);
        computeShader.SetFloat("_cellPipeLength", cellPipeLength);
        computeShader.SetFloat("_gravityConstant", gravityConstant);
        computeShader.SetFloat("_pipeCrossSectionArea", pipeCrossSectionArea);
        computeShader.SetFloat("_fluidDensity", fluidDensity);
        computeShader.SetFloat("_dampingFactor", dampingFactor);

        computeShader.SetBuffer(buildTrianglesKernel, "_triangleBuffer", triangleBuffer);

        computeShader.SetBuffer(calculateFlowsKernel, "_heightBuffer", heightBuffer);
        computeShader.SetBuffer(calculateFlowsKernel, "_externalPressureBuffer", externalPressureBuffer);
        computeShader.SetBuffer(calculateFlowsKernel, "_flowsBuffer", fluidFlowsBuffer);
        computeShader.SetBuffer(calculateFlowsKernel, "_positionBuffer", vertexBuffer);

        computeShader.SetBuffer(calculateHeightsKernel, "_heightBuffer", heightBuffer);
        computeShader.SetBuffer(calculateHeightsKernel, "_externalPressureBuffer", externalPressureBuffer);
        computeShader.SetBuffer(calculateHeightsKernel, "_volumeBuffer", fluidVolumeBuffer);
        computeShader.SetBuffer(calculateHeightsKernel, "_flowsBuffer", fluidFlowsBuffer);
        computeShader.SetBuffer(calculateHeightsKernel, "_positionBuffer", vertexBuffer);

        computeShader.SetBuffer(updatePositionsKernel, "_heightBuffer", heightBuffer);
        computeShader.SetBuffer(updatePositionsKernel, "_positionBuffer", vertexBuffer);

        int threadGroupCount = Mathf.CeilToInt(gridResolution / 8f);
        computeShader.Dispatch(buildTrianglesKernel, threadGroupCount, threadGroupCount, 1);

        float gridStep = domainSize / (gridResolution - 1);
        float gridOffset = domainSize / 2f;
        computeShader.SetFloat("_gridStep", gridStep);
        computeShader.SetFloat("_gridOffset", gridOffset);
        computeShader.Dispatch(updatePositionsKernel, threadGroupCount, threadGroupCount, 1);

        // --- Setup Mesh Components ---
        meshFilter = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.material = waterMaterial;

        waterMesh = new Mesh();
        waterMesh.MarkDynamic();

        cpuPositions = new Vector3[gridResolution * gridResolution];
        cpuTriangles = new int[totalTriangleIndices];

        triangleBuffer.GetData(cpuTriangles);
        waterMesh.vertices = cpuPositions;
        waterMesh.triangles = cpuTriangles;
    }

    void OnDisable()
    {
        if (vertexBuffer != null) vertexBuffer.Release();
        if (triangleBuffer != null) triangleBuffer.Release();
        if (heightBuffer != null) heightBuffer.Release();
        if (externalPressureBuffer != null) externalPressureBuffer.Release();
        if (fluidFlowsBuffer != null) fluidFlowsBuffer.Release();
        if (fluidVolumeBuffer != null) fluidVolumeBuffer.Release();
    }

    void Update()
    {
        float[] externalPressureData = new float[gridResolution * gridResolution];
        float frameDeltaTime = Time.deltaTime;

        if (float.IsNaN(frameDeltaTime) || float.IsInfinity(frameDeltaTime) || frameDeltaTime <= 0f)
        {
            frameDeltaTime = 0.016f;
        }

        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 mouseScreenPosition = Mouse.current.position.ReadValue();
            Ray mouseRay = Camera.main.ScreenPointToRay(mouseScreenPosition);
            RaycastHit raycastHit;

            if (Physics.Raycast(mouseRay, out raycastHit))
            {
                if (raycastHit.collider.gameObject == gameObject)
                {
                    Vector3 localHitPosition = transform.InverseTransformPoint(raycastHit.point);

                    float halfDomainSize = domainSize / 2f;
                    int gridCellX = Mathf.FloorToInt(((localHitPosition.x + halfDomainSize) / domainSize) * gridResolution);
                    int gridCellY = Mathf.FloorToInt(((localHitPosition.z + halfDomainSize) / domainSize) * gridResolution);

                    if (gridCellX >= 0 && gridCellX < gridResolution && gridCellY >= 0 && gridCellY < gridResolution)
                    {
                        float cellPipeLength = domainSize / gridResolution;
                        float cellArea = cellPipeLength * cellPipeLength;

                        int flatGridIndex = (gridCellY * gridResolution) + gridCellX;
                        externalPressureData[flatGridIndex] += mouseInteractionForce / (4f * cellArea);
                    }
                }
            }
        }

        externalPressureBuffer.SetData(externalPressureData);

        float gridStep = domainSize / (gridResolution - 1);
        float gridOffset = domainSize / 2f;
        computeShader.SetFloat("_gridStep", gridStep);
        computeShader.SetFloat("_gridOffset", gridOffset);
        computeShader.SetInt("_gridResolution", gridResolution);

        int threadGroupCount = Mathf.CeilToInt(gridResolution / 8f);

        computeShader.SetFloat("_deltaTime", frameDeltaTime);
        computeShader.Dispatch(calculateFlowsKernel, threadGroupCount, threadGroupCount, 1);
        computeShader.Dispatch(calculateHeightsKernel, threadGroupCount, threadGroupCount, 1);
        computeShader.Dispatch(updatePositionsKernel, threadGroupCount, threadGroupCount, 1);

        vertexBuffer.GetData(cpuPositions);   
        waterMesh.vertices = cpuPositions;         
        waterMesh.RecalculateNormals();              
        waterMesh.RecalculateBounds();             
        
        meshFilter.sharedMesh = waterMesh;
    }
}