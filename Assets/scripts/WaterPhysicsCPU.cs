using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WaterPhysicsCPU : MonoBehaviour
{
    public int gridResolution = 32;
    public float domainSize = 5f;
    public float dampingFactor = 0.96f;

    public float gravityConstant = 9.81f;
    public float fluidDensity = 1000f;
    public float pipeCrossSectionArea = 0.01f;

    public float mouseInteractionForce = 50f;

    private float[,] gridHeights;
    private float[,] gridVolumes;
    private float[,] externalPressure;
    private float[,,] pipeFlows;
    private float cellPipeLength;

    private Mesh waterMesh;
    private Vector3[] meshVertices;

    private int[] neighborOffsetX = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private int[] neighborOffsetY = { 0, 1, 1, 1, 0, -1, -1, -1 };

    void Start()
    {
        cellPipeLength = domainSize / gridResolution;
        InitializeSimulation();
        GenerateSurfaceMesh();
    }

    void InitializeSimulation()
    {
        gridHeights = new float[gridResolution, gridResolution];
        gridVolumes = new float[gridResolution, gridResolution];
        externalPressure = new float[gridResolution, gridResolution];
        pipeFlows = new float[gridResolution, gridResolution, 8];

        float initialWaterHeight = 0.1f;
        for (int x = 0; x < gridResolution; x++)
        {
            for (int y = 0; y < gridResolution; y++)
            {
                gridHeights[x, y] = initialWaterHeight;
                gridVolumes[x, y] = initialWaterHeight * (cellPipeLength * cellPipeLength);
            }
        }
    }

    void GenerateSurfaceMesh()
    {
        waterMesh = new Mesh();
        GetComponent<MeshFilter>().mesh = waterMesh;

        int vertexResolution = gridResolution + 1; 
        meshVertices = new Vector3[vertexResolution * vertexResolution];
        int[] triangles = new int[gridResolution * gridResolution * 6];

        float totalWidth = gridResolution * cellPipeLength;
        float gridOffset = totalWidth / 2f;

        for (int y = 0; y < vertexResolution; y++)
        {
            for (int x = 0; x < vertexResolution; x++)
            {
                int vertexIndex = y * vertexResolution + x;
                meshVertices[vertexIndex] = new Vector3(x * cellPipeLength - gridOffset, 0, y * cellPipeLength - gridOffset);

                if (x < gridResolution && y < gridResolution)
                {
                    int triangleIndex = (y * (vertexResolution - 1) + x) * 6;
                    
                    triangles[triangleIndex]     = vertexIndex;
                    triangles[triangleIndex + 1] = vertexIndex + vertexResolution;
                    triangles[triangleIndex + 2] = vertexIndex + vertexResolution + 1;

                    triangles[triangleIndex + 3] = vertexIndex;
                    triangles[triangleIndex + 4] = vertexIndex + vertexResolution + 1;
                    triangles[triangleIndex + 5] = vertexIndex + 1;
                }
            }
        }

        waterMesh.vertices = meshVertices;
        waterMesh.triangles = triangles;
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = waterMesh;
    }

    public void ReceiveWaterDrop(Vector3 worldPosition, float impactSpeed, float dropVolume, float impactForce)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPosition);

        float halfSize = ((gridResolution - 1) * cellPipeLength) / 2f;
        //origin shift
        int gridX = Mathf.FloorToInt(((localPos.x + halfSize) / (halfSize * 2f)) * gridResolution); 
        int gridY = Mathf.FloorToInt(((localPos.z + halfSize) / (halfSize * 2f)) * gridResolution);

        gridX = Mathf.Clamp(gridX, 0, gridResolution - 1);
        gridY = Mathf.Clamp(gridY, 0, gridResolution - 1);

        float cellArea = cellPipeLength * cellPipeLength;

        externalPressure[gridX, gridY] += (impactForce * impactSpeed) / (4f * cellArea);

        gridVolumes[gridX, gridY] += dropVolume;
    }

    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 mousePosition = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.gameObject == gameObject)
                {
                    Vector3 localPos = transform.InverseTransformPoint(hit.point);

                    float halfSize = domainSize / 2f;
                    int gridX = Mathf.FloorToInt(((localPos.x + halfSize) / domainSize) * gridResolution);
                    int gridY = Mathf.FloorToInt(((localPos.z + halfSize) / domainSize) * gridResolution);

                    if (gridX >= 0 && gridX < gridResolution && gridY >= 0 && gridY < gridResolution)
                    {
                        float cellArea = (domainSize / (gridResolution - 1)) * (domainSize / (gridResolution - 1));
                        externalPressure[gridX, gridY] += mouseInteractionForce / (4f * cellArea);
                    }
                }
            }
        }
    }

    void FixedUpdate()
    {
        float deltaTime = Time.fixedDeltaTime;

        for (int x = 0; x < gridResolution; x++)
        {
            for (int y = 0; y < gridResolution; y++)
            {
                for (int pipeIndex = 0; pipeIndex < 8; pipeIndex++)
                {
                    int neighborX = x + neighborOffsetX[pipeIndex];
                    int neighborY = y + neighborOffsetY[pipeIndex];
                    if (neighborX >= 0 && neighborX < gridResolution && neighborY >= 0 && neighborY < gridResolution)
                    {
                        float pressureDelta = (fluidDensity * gravityConstant * (gridHeights[x, y] - gridHeights[neighborX, neighborY])) + externalPressure[x, y] - externalPressure[neighborX, neighborY];
                        float flowAcceleration = (pipeCrossSectionArea * pressureDelta) / (fluidDensity * cellPipeLength);
                        pipeFlows[x, y, pipeIndex] = (pipeFlows[x, y, pipeIndex] + deltaTime * flowAcceleration) * dampingFactor;
                    }
                }
            }
        }

        for (int x = 0; x < gridResolution; x++)
        {
            for (int y = 0; y < gridResolution; y++)
            {
                float flowSum = 0;
                for (int pipeIndex = 0; pipeIndex < 8; pipeIndex++) flowSum += pipeFlows[x, y, pipeIndex];

                gridVolumes[x, y] -= deltaTime * flowSum;
                gridVolumes[x, y] = Mathf.Max(0.001f, gridVolumes[x, y]);
                gridHeights[x, y] = gridVolumes[x, y] / (cellPipeLength * cellPipeLength);
                externalPressure[x, y] = 0;
            }
        }

        UpdateMeshSurface();
    }


    void UpdateMeshSurface()
    {
        int vertexResolution = gridResolution + 1;

        for (int y = 0; y < vertexResolution; y++)
        {
            for (int x = 0; x < vertexResolution; x++)
            {
                int x0 = Mathf.Clamp(x - 1, 0, gridResolution - 1);
                int x1 = Mathf.Clamp(x,     0, gridResolution - 1);
                int y0 = Mathf.Clamp(y - 1, 0, gridResolution - 1);
                int y1 = Mathf.Clamp(y,     0, gridResolution - 1);

                float averageHeight = (gridHeights[x0, y0] + 
                                      gridHeights[x1, y0] + 
                                      gridHeights[x0, y1] + 
                                      gridHeights[x1, y1]) / 4f;

                meshVertices[y * vertexResolution + x].y = averageHeight;
            }
        }

        waterMesh.vertices = meshVertices;
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();
    }
}