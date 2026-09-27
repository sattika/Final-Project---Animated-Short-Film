using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class melt : MonoBehaviour
{
    [Header("Heat Source (Section A)")]
    public Transform heatSource;
    public float deltaR = 0.1f;       // Radius expansion increment per frame (Δr)
    public float psi = 0.05f;         // Distance weighting factor (ψ)

    [Header("Deformation Parameters (Sections C & D)")]
    [Range(0.1f, 1.0f)] public float viscosityW = 0.7f; // Target edge scale (ω)
    public float flowRateV = 1.2f;                       // Flow rate (υ)
    public float frictionMu = 4.0f;                      // Ground friction (μ)
    public float floorY = 0.0f;                          // Floor plane height

    // Paper Enums
    public enum BoundaryZone { Cold, Virtual, Initial, CombustionReady, Deform }
    public enum VertexCategory { FixedPoint, FlowTrue, BasePoint }

    private Mesh mesh;
    private Vector3[] vertices;
    private int[] triangles;
    private int[] neighborCounts;       // n (adjacency degree)

    private float currentR = 0.05f;    // Dynamic growing radius r
    private BoundaryZone[] zones;
    private VertexCategory[] categories;

    void Start()
    {
        MeshFilter filter = GetComponent<MeshFilter>();

        // Check if the mesh is readable before accessing properties
        if (!filter.sharedMesh.isReadable)
        {
            Debug.LogError($"Mesh '{filter.sharedMesh.name}' is not readable! Please check 'Read/Write Enabled' in the asset's Import Settings.", this);
            enabled = false; // Disable script to prevent spamming errors
            return;
        }

        mesh = filter.mesh; // Clones the mesh instance
        vertices = mesh.vertices;
        triangles = mesh.triangles;

        zones = new BoundaryZone[vertices.Length];
        categories = new VertexCategory[vertices.Length];

        BuildAdjacency();
    }

    void BuildAdjacency()
    {
        neighborCounts = new int[vertices.Length];
        for (int i = 0; i < triangles.Length; i += 3)
        {
            neighborCounts[triangles[i]] += 2;
            neighborCounts[triangles[i + 1]] += 2;
            neighborCounts[triangles[i + 2]] += 2;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        currentR += deltaR * dt; // Radius expansion: R = r + Δr

        Vector3 centroid = ComputeCentroid();

        // -------------------------------------------------------------
        // SECTION A: Irregular Heat Boundary & Zone Evaluation
        // Formula: R^2 = | sin(πθ/Δr) + sin(πθ) + ψ((x-x0)^2 + (y-y0)^2 + (z-z0)^2) |
        // -------------------------------------------------------------
        EvaluateSectionA();

        // -------------------------------------------------------------
        // SECTION B: Vertex Categorization
        // Classifies into FixedPoint, FlowTrue, or BasePoint
        // -------------------------------------------------------------
        EvaluateSectionB();

        // -------------------------------------------------------------
        // SECTION C & D: Deformation Loop
        // -------------------------------------------------------------
        Vector3[] deltas = new Vector3[vertices.Length];
        float totalYCollapsed = 0.0f;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int idxS = triangles[i];
            int idxA = triangles[i + 1];
            int idxD = triangles[i + 2];

            // Sort by Y height: Ds (Top), Da (Middle), Dd (Bottom)
            if (vertices[idxA].y > vertices[idxS].y) Swap(ref idxS, ref idxA);
            if (vertices[idxD].y > vertices[idxS].y) Swap(ref idxS, ref idxD);
            if (vertices[idxD].y > vertices[idxA].y) Swap(ref idxA, ref idxD);

            // SECTION B RULE: Only move if vertex is FlowTrue. FixedPoints stay pinned.
            if (categories[idxS] != VertexCategory.FlowTrue) continue;

            Vector3 Ds = vertices[idxS];
            Vector3 Dd = vertices[idxD];

            // SECTION C: Vertical Height Collapse
            float currentDist = Vector3.Distance(Ds, Dd);
            float targetDist = viscosityW * currentDist;

            if (currentDist > targetDist && Ds.y > floorY)
            {
                float yShrink = (currentDist - targetDist) * flowRateV * dt;
                deltas[idxS].y -= yShrink;
                totalYCollapsed += yShrink;
            }

            // SECTION C: Horizontal Slumping (Parberry Formula: (Ds_xz - Dd_xz) * υ / (n + 1))
            Vector2 diffXZ = new Vector2(Ds.x - Dd.x, Ds.z - Dd.z);
            int n = neighborCounts[idxS];
            Vector2 slumpStep = diffXZ * (flowRateV / (n + 1)) * dt;

            deltas[idxS].x -= slumpStep.x;
            deltas[idxS].z -= slumpStep.y;
        }

        // Apply Section C deltas to FlowTrue vertices
        for (int i = 0; i < vertices.Length; i++)
        {
            if (categories[i] == VertexCategory.FlowTrue)
            {
                vertices[i] += deltas[i];
            }
        }

        // SECTION D: Base Point Outward Puddle Expansion
        for (int i = 0; i < vertices.Length; i++)
        {
            if (categories[i] == VertexCategory.BasePoint)
            {
                vertices[i].y = floorY; // Lock Y coordinate to floor plane

                // Outward vector relative to object centroid (Cx, Cz)
                Vector2 outDir = new Vector2(vertices[i].x - centroid.x, vertices[i].z - centroid.z);
                if (outDir.sqrMagnitude > 0.0001f) outDir.Normalize();

                float expansion = (totalYCollapsed / frictionMu) * dt;
                vertices[i].x += outDir.x * expansion;
                vertices[i].z += outDir.y * expansion;
            }
        }

        // Refresh Mesh Renderer
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    void EvaluateSectionA()
    {
        if (heatSource == null) return;
        Vector3 hPos = transform.InverseTransformPoint(heatSource.position);

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            float distSq = (v.x - hPos.x) * (v.x - hPos.x) +
                           (v.y - hPos.y) * (v.y - hPos.y) +
                           (v.z - hPos.z) * (v.z - hPos.z);

            // Pseudo-random angle θ based on vertex position for reproducible noise
            float theta = Mathf.Repeat(v.x * 12.9898f + v.z * 78.233f, 2.0f * Mathf.PI);

            // Parberry Section A Equation for R^2
            float noise = Mathf.Sin((Mathf.PI * theta) / deltaR) + Mathf.Sin(Mathf.PI * theta);
            float rSquaredCalc = Mathf.Abs(noise + (psi * distSq));

            float targetR2 = currentR * currentR;

            // Map into Parberry's 4 Zones
            if (rSquaredCalc <= targetR2)
                zones[i] = BoundaryZone.Deform;
            else if (rSquaredCalc <= targetR2 * 1.5f)
                zones[i] = BoundaryZone.CombustionReady;
            else if (rSquaredCalc <= targetR2 * 2.5f)
                zones[i] = BoundaryZone.Initial;
            else
                zones[i] = BoundaryZone.Virtual;
        }
    }

    void EvaluateSectionB()
    {
        for (int i = 0; i < vertices.Length; i++)
        {
            // 1. Reached floor -> BasePoint
            if (vertices[i].y <= floorY)
            {
                categories[i] = VertexCategory.BasePoint;
            }
            // 2. Heated and active in Deform or Combustion zone -> FlowTrue
            else if (zones[i] == BoundaryZone.Deform || zones[i] == BoundaryZone.CombustionReady)
            {
                categories[i] = VertexCategory.FlowTrue;
            }
            // 3. Unheated non-melting area connected to melting region -> FixedPoint
            else
            {
                categories[i] = VertexCategory.FixedPoint;
            }
        }
    }

    Vector3 ComputeCentroid()
    {
        Vector3 c = Vector3.zero;
        foreach (var v in vertices) c += v;
        return c / vertices.Length;
    }

    void Swap(ref int a, ref int b) { int tmp = a; a = b; b = tmp; }
}