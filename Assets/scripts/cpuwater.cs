using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PuddleInteractionSystem : MonoBehaviour
{
    [Header("Simulation Grid")]
    public int resolution = 32;
    public float puddleSize = 5f;
    public float damping = 0.96f;

    [Header("Physical Constants")]
    public float gravity = 9.81f;
    public float density = 1000f;
    public float pipeArea = 0.01f;

    [Header("Spray Model (Splashes)")]
    public GameObject splashParticlePrefab;
    public float splashThreshold = 2.5f;
    public float particleVolume = 0.001f;

    // Simulation Data
    private float[,] heights;
    private float[,] volumes;
    private float[,] extPressure;
    private float[,,] flows;
    private float pipeLength;

    // Rendering Data
    private Mesh puddleMesh;
    private Vector3[] vertices;

    // Virtual pipes
    private int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

    [Header("Mouse Interaction")]
    public float clickForce = 50f;

    void Start()
    {
        pipeLength = puddleSize / resolution;
        InitializeSimulation();
        GenerateSurfaceMesh();
    }

    void InitializeSimulation()
    {
        heights = new float[resolution, resolution];
        volumes = new float[resolution, resolution];
        extPressure = new float[resolution, resolution];
        flows = new float[resolution, resolution, 8];

        float startHeight = 0.1f;
        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                heights[i, j] = startHeight;
                volumes[i, j] = startHeight * (pipeLength * pipeLength);
            }
        }
    }

    void GenerateSurfaceMesh()
    {
        puddleMesh = new Mesh();
        GetComponent<MeshFilter>().mesh = puddleMesh;

        int vRes = resolution + 1; 
        vertices = new Vector3[vRes * vRes];
        int[] triangles = new int[resolution * resolution * 6];

        float totalWidth = resolution * pipeLength;
        float offset = totalWidth / 2f;

        for (int y = 0; y < vRes; y++)
        {
            for (int x = 0; x < vRes; x++)
            {
                int idx = y * vRes + x;
                vertices[idx] = new Vector3(x * pipeLength - offset, 0, y * pipeLength - offset);

                if (x < resolution && y < resolution)
                {
                    int t = (y * (vRes - 1) + x) * 6;
                    
                    // Clockwise (CW) winding order for Unity standard front-face rendering:
                    // Triangle 1
                    triangles[t]     = idx;
                    triangles[t + 1] = idx + vRes;
                    triangles[t + 2] = idx + vRes + 1;

                    // Triangle 2
                    triangles[t + 3] = idx;
                    triangles[t + 4] = idx + vRes + 1;
                    triangles[t + 5] = idx + 1;
                }
            }
        }

        // Assign data to mesh FIRST
        puddleMesh.vertices = vertices;
        puddleMesh.triangles = triangles;
        puddleMesh.RecalculateNormals();
        puddleMesh.RecalculateBounds();

        // Assign to MeshCollider LAST so physics bboxes build correctly
        MeshCollider col = GetComponent<MeshCollider>();
        if (col == null) col = gameObject.AddComponent<MeshCollider>();
        col.sharedMesh = null; // Reset reference to force physics update
        col.sharedMesh = puddleMesh;
    }

    /// <summary>
    /// Call this method from WaterDrop.cs on trigger enter.
    /// </summary>
	public void ReceiveWaterDrop(Vector3 worldPosition, float impactSpeed, float dropVolume, float impactForce)
	{
		// 1. Convert contact point to local grid coordinates
		Vector3 localPos = transform.InverseTransformPoint(worldPosition);

		float halfSize = ((resolution - 1) * pipeLength) / 2f;

		int i = Mathf.FloorToInt(((localPos.x + halfSize) / (halfSize * 2f)) * resolution);
		int j = Mathf.FloorToInt(((localPos.z + halfSize) / (halfSize * 2f)) * resolution);

		i = Mathf.Clamp(i, 0, resolution - 1);
		j = Mathf.Clamp(j, 0, resolution - 1);

		// 2. Calculate force using the exact grid unit area
		float area = pipeLength * pipeLength;

		// Apply force scaled by impact speed to generate visible ripples
		extPressure[i, j] += (impactForce * impactSpeed) / (4f * area);

		// 3. Add volume to the cell so the water rises over time
		volumes[i, j] += dropVolume;
	}
    void Update()
    {
        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = Camera.main.ScreenPointToRay(mousePos);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.gameObject == gameObject)
                {
                    Vector3 localPos = transform.InverseTransformPoint(hit.point);

                    float halfSize = puddleSize / 2f;
                    int i = Mathf.FloorToInt(((localPos.x + halfSize) / puddleSize) * resolution);
                    int j = Mathf.FloorToInt(((localPos.z + halfSize) / puddleSize) * resolution);

                    if (i >= 0 && i < resolution && j >= 0 && j < resolution)
                    {
                        float area = (puddleSize / (resolution - 1)) * (puddleSize / (resolution - 1));
                        extPressure[i, j] += clickForce / (4f * area);
                    }
                }
            }
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                for (int p = 0; p < 8; p++)
                {
                    int ni = i + dx[p], nj = j + dy[p];
                    if (ni >= 0 && ni < resolution && nj >= 0 && nj < resolution)
                    {
                        float deltaP = (density * gravity * (heights[i, j] - heights[ni, nj])) + extPressure[i, j] - extPressure[ni, nj];
                        float acc = (pipeArea * deltaP) / (density * pipeLength);
                        flows[i, j, p] = (flows[i, j, p] + dt * acc) * damping;
                    }
                }
            }
        }

        for (int i = 0; i < resolution; i++)
        {
            for (int j = 0; j < resolution; j++)
            {
                float flowSum = 0;
                for (int p = 0; p < 8; p++) flowSum += flows[i, j, p];

                float verticalVelocity = (-flowSum / (pipeLength * pipeLength));

                if (verticalVelocity > splashThreshold && splashParticlePrefab != null && volumes[i, j] > particleVolume)
                {
                    //GenerateSplash(i, j, verticalVelocity);
                }

                volumes[i, j] -= dt * flowSum;
                volumes[i, j] = Mathf.Max(0.001f, volumes[i, j]);
                heights[i, j] = volumes[i, j] / (pipeLength * pipeLength);
                extPressure[i, j] = 0;
            }
        }

        UpdateMeshSurface();
    }

    void GenerateSplash(int i, int j, float vVel)
    {
        float offset = ((resolution - 1) * pipeLength) / 2f;
        float xPos = (i * pipeLength) - offset;
        float zPos = (j * pipeLength) - offset;
        float yPos = heights[i, j];

        Vector3 localPos = new Vector3(xPos, yPos, zPos);
        Vector3 worldPos = transform.TransformPoint(localPos);

        GameObject splash = Instantiate(splashParticlePrefab, worldPos, Quaternion.identity);
        Rigidbody rb = splash.GetComponent<Rigidbody>();
        if (rb != null)
        {
            float flowX = (flows[i, j, 0] - flows[i, j, 4]);
            float flowZ = (flows[i, j, 2] - flows[i, j, 6]);
            rb.linearVelocity = transform.TransformDirection(new Vector3(flowX, vVel, flowZ));
        }

        volumes[i, j] -= particleVolume;
    }

    void UpdateMeshSurface()
    {
        int vRes = resolution + 1; // Number of vertices per edge

        for (int y = 0; y < vRes; y++)
        {
            for (int x = 0; x < vRes; x++)
            {
                // Clamp cell indices so boundary vertices sample edge cells safely
                int x0 = Mathf.Clamp(x - 1, 0, resolution - 1);
                int x1 = Mathf.Clamp(x,     0, resolution - 1);
                int y0 = Mathf.Clamp(y - 1, 0, resolution - 1);
                int y1 = Mathf.Clamp(y,     0, resolution - 1);

                // 4-quad spatial average surrounding vertex (x, y)
                float avgH = (heights[x0, y0] + 
                              heights[x1, y0] + 
                              heights[x0, y1] + 
                              heights[x1, y1]) / 4f;

                // Apply directly using correct vertex stride
                vertices[y * vRes + x].y = avgH;
            }
        }

        // Reassign vertices and update mesh bounds
        puddleMesh.vertices = vertices;
        puddleMesh.RecalculateNormals();
        puddleMesh.RecalculateBounds();
    }
}