using System;
using UnityEngine;

public enum HeatRegion { Cold = 0, Virtual, Initial, Ready, Deform }
public enum VertRole { Solid = 0, FlowTrue, FixedPoint, BasePoint }

[System.Serializable]
public struct VertexInfo
{
    public int blockId;
    public HeatRegion region;
    public VertRole role;
    public float theta;
}

[System.Serializable]
public struct FaceInfo
{
    public int v0, v1, v2;
    public float D;
}

public class MakeGrid
{
    public int gridX, gridY, gridZ;
    public Vector3 min, size;

    public struct Voxel
    {
        public int left, right, top, bottom, front, back;
        public int baseVertId;
        public int weight;
    }

    public Voxel[] voxels;

    public MakeGrid(Vector3 min, Vector3 max, int res)
    {
        this.min = min;
        res = Mathf.Max(res, 1);
        size = new Vector3(
            Mathf.Max((max.x - min.x) / res, 1e-4f),
            Mathf.Max((max.y - min.y) / res, 1e-4f),
            Mathf.Max((max.z - min.z) / res, 1e-4f)
        );
        gridX = gridY = gridZ = res;
        voxels = new Voxel[gridX * gridY * gridZ];
        for (int i = 0; i < voxels.Length; i++)
        {
            voxels[i].baseVertId = -1;
            voxels[i].left = voxels[i].right = voxels[i].top =
            voxels[i].bottom = voxels[i].front = voxels[i].back = -1;
        }
    }

    public int FetchIdx(Vector3 p)
    {
        int i = Mathf.Clamp(Mathf.FloorToInt((p.x - min.x) / size.x), 0, gridX - 1);
        int j = Mathf.Clamp(Mathf.FloorToInt((p.y - min.y) / size.y), 0, gridY - 1);
        int k = Mathf.Clamp(Mathf.FloorToInt((p.z - min.z) / size.z), 0, gridZ - 1);
        return i + j * gridX + k * gridX * gridY;
    }

    public void AddVertex(Vector3 p, int vertId, Vector3[] positions)
    {
        int idx = FetchIdx(p);
        int i = Mathf.Clamp(Mathf.FloorToInt((p.x - min.x) / size.x), 0, gridX - 1);
        int j = Mathf.Clamp(Mathf.FloorToInt((p.y - min.y) / size.y), 0, gridY - 1);
        int k = Mathf.Clamp(Mathf.FloorToInt((p.z - min.z) / size.z), 0, gridZ - 1);
        int layer = gridX * gridY;

        if (voxels[idx].weight == 0)
        {
            voxels[idx].baseVertId = vertId;
        }
        else
        {
            int cur = voxels[idx].baseVertId;
            if (positions[vertId].y < positions[cur].y)
                voxels[idx].baseVertId = vertId;
        }
        voxels[idx].weight++;

        voxels[idx].left = (i > 0) ? idx - 1 : -1;
        voxels[idx].right = (i < gridX - 1) ? idx + 1 : -1;
        voxels[idx].bottom = (j > 0) ? idx - gridX : -1;
        voxels[idx].top = (j < gridY - 1) ? idx + gridX : -1;
        voxels[idx].back = (k > 0) ? idx - layer : -1;
        voxels[idx].front = (k < gridZ - 1) ? idx + layer : -1;
    }

    public bool Occupied(int idx)
    {
        return idx >= 0 && idx < voxels.Length && voxels[idx].weight > 0;
    }
}

[RequireComponent(typeof(MeshFilter))]
public class MeltSim : MonoBehaviour
{
    [Header("Heat")]
    public Transform heatSource;
    public float psi = 1f;
    public float deltaR = 0.12f;
    public float r;

    [Header("Grid")]
    public int gridSize = 6;

    [Header("Fold — Dist is ORIGINAL average, never updated")]
    [Range(0.5f, 1.2f)] public float omega = 1.05f;
    [Range(1, 10)] public int chi = 4;
    [Range(0.01f, 0.25f)] public float upsilon = 0.04f;

    [Header("Flow")]
    [Range(0.7f, 0.98f)] public float viscosity = 0.94f;
    public float gravity = 0.35f;

    [Header("Debug")]
    public float originalDist = 0.01f;
    public bool drawRoles = true;

    Mesh meshInstance;
    Vector3[] positions;
    VertexInfo[] verts;
    FaceInfo[] faces;
    MakeGrid grid;

    Vector3[] baseDelta;          // per-block translation this frame
    Vector3[] displacements;
    int[] displacementCounts;

    // mesh-local "down" = world gravity
    Vector3 LocalDown => transform.InverseTransformDirection(Vector3.down).normalized;

    void Awake()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;

        meshInstance = Instantiate(mf.sharedMesh);
        mf.mesh = meshInstance;
        positions = meshInstance.vertices;

        Bounds b = meshInstance.bounds;
        grid = new MakeGrid(b.min, b.max, gridSize);

        verts = new VertexInfo[positions.Length];
        for (int i = 0; i < positions.Length; i++)
        {
            grid.AddVertex(positions[i], i, positions);
            verts[i].blockId = grid.FetchIdx(positions[i]);
            verts[i].region = HeatRegion.Cold;
            verts[i].role = VertRole.Solid;
            verts[i].theta = (i * 0.61803399f) % 1f;
        }

        int[] tris = meshInstance.triangles;
        faces = new FaceInfo[tris.Length / 3];
        float sum = 0f;
        int nEdge = 0;
        for (int f = 0; f < faces.Length; f++)
        {
            int t = f * 3;
            faces[f].v0 = tris[t];
            faces[f].v1 = tris[t + 1];
            faces[f].v2 = tris[t + 2];
            float d1 = Vector3.Distance(positions[faces[f].v0], positions[faces[f].v1]);
            float d2 = Vector3.Distance(positions[faces[f].v1], positions[faces[f].v2]);
            float d3 = Vector3.Distance(positions[faces[f].v2], positions[faces[f].v0]);
            if (d1 > 1e-4f) { sum += d1; nEdge++; }
            if (d2 > 1e-4f) { sum += d2; nEdge++; }
            if (d3 > 1e-4f) { sum += d3; nEdge++; }
        }
        originalDist = nEdge > 0 ? sum / nEdge : 0.01f;

        baseDelta = new Vector3[grid.voxels.Length];
        displacements = new Vector3[positions.Length];
        displacementCounts = new int[positions.Length];
    }

    void Update()
    {
        if (meshInstance == null || heatSource == null) return;

        r += deltaR * Time.deltaTime;

        TagHeatAndRoles();
        MoveBasePoints();     // fills baseDelta[]
        TowFlowVerts();       // apply SAME delta — does not attract to the point
        FoldFaces();

        meshInstance.vertices = positions;
        meshInstance.RecalculateNormals();
        meshInstance.RecalculateBounds();
    }

    float HeatScore(int i)
    {
        Vector3 src = transform.InverseTransformPoint(heatSource.position);
        float d2 = (positions[i] - src).sqrMagnitude;
        float th = verts[i].theta;
        float dr = Mathf.Max(deltaR, 1e-4f);
        return Mathf.Abs(
            Mathf.Sin(Mathf.PI * th / dr) +
            Mathf.Sin(Mathf.PI * th) +
            psi * d2
        );
    }

    void TagHeatAndRoles()
    {
        float r2 = r * r;
        float a = 0.75f * r2, b = 0.50f * r2, c = 0.25f * r2;

        for (int i = 0; i < verts.Length; i++)
        {
            float s = HeatScore(i);
            HeatRegion next =
                s > r2 ? HeatRegion.Cold :
                s > a ? HeatRegion.Virtual :
                s > b ? HeatRegion.Initial :
                s > c ? HeatRegion.Ready :
                         HeatRegion.Deform;

            if ((int)next < (int)verts[i].region) next = verts[i].region;
            verts[i].region = next;

            bool isBase = grid.Occupied(verts[i].blockId) &&
                          grid.voxels[verts[i].blockId].baseVertId == i;

            if (next >= HeatRegion.Ready)
                verts[i].role = isBase ? VertRole.BasePoint : VertRole.FlowTrue;
            else
                verts[i].role = isBase ? VertRole.BasePoint : VertRole.Solid;
        }

        for (int f = 0; f < faces.Length; f++)
        {
            int i0 = faces[f].v0, i1 = faces[f].v1, i2 = faces[f].v2;
            bool hot =
                IsMelting(i0) || IsMelting(i1) || IsMelting(i2);
            if (!hot) continue;
            if (verts[i0].role == VertRole.Solid) verts[i0].role = VertRole.FixedPoint;
            if (verts[i1].role == VertRole.Solid) verts[i1].role = VertRole.FixedPoint;
            if (verts[i2].role == VertRole.Solid) verts[i2].role = VertRole.FixedPoint;
        }
    }

    bool IsMelting(int id)
    {
        var r = verts[id].role;
        return r == VertRole.FlowTrue || r == VertRole.BasePoint;
    }

    void MoveBasePoints()
    {
        Array.Clear(baseDelta, 0, baseDelta.Length);
        Vector3 down = LocalDown;
        float maxStep = originalDist * 0.12f;
        float dt = Time.deltaTime;

        for (int i = 0; i < grid.voxels.Length; i++)
        {
            if (!grid.Occupied(i)) continue;
            int baseId = grid.voxels[i].baseVertId;
            if (baseId < 0) continue;
            if (verts[baseId].region < HeatRegion.Ready) continue;

            var vx = grid.voxels[i];
            bool missSide =
                !grid.Occupied(vx.left) || !grid.Occupied(vx.right) ||
                !grid.Occupied(vx.front) || !grid.Occupied(vx.back);
            bool missFloor = !grid.Occupied(vx.bottom);

            Vector3 before = positions[baseId];
            Vector3 after = before;

            if (missSide && missFloor)
            {
                after += down * (gravity * viscosity * dt);
            }
            else
            {
                int destId = baseId;
                float best = Vector3.Dot(positions[baseId], down);
                int[] ns = { vx.left, vx.right, vx.front, vx.back };
                for (int n = 0; n < 4; n++)
                {
                    if (!grid.Occupied(ns[n])) continue;
                    int nb = grid.voxels[ns[n]].baseVertId;
                    if (nb < 0) continue;
                    float h = Vector3.Dot(positions[nb], down);
                    if (h > best) // more down
                    {
                        best = h;
                        destId = nb;
                    }
                }
                if (destId != baseId)
                {
                    // paper: P = (P - dest)*ω + dest
                    Vector3 dest = positions[destId];
                    Vector3 blended = (before - dest) * viscosity + dest;
                    after = Vector3.MoveTowards(before, blended, maxStep);
                }
            }

            Vector3 delta = Vector3.ClampMagnitude(after - before, maxStep);
            positions[baseId] = before + delta;
            baseDelta[i] = delta;
        }
    }

    void TowFlowVerts()
    {
        for (int v = 0; v < verts.Length; v++)
        {
            if (verts[v].role != VertRole.FlowTrue) continue;
            int bid = verts[v].blockId;
            if (!grid.Occupied(bid)) continue;
            if (grid.voxels[bid].baseVertId == v) continue;
            positions[v] += baseDelta[bid];
        }
    }

    void FoldFaces()
    {
        Array.Clear(displacements, 0, displacements.Length);
        Array.Clear(displacementCounts, 0, displacementCounts.Length);

        float lo = (omega * originalDist) / Mathf.Max(chi, 1);
        float hi = omega * originalDist;
        float maxStep = originalDist * 0.08f;
        Vector3 down = LocalDown;

        for (int f = 0; f < faces.Length; f++)
        {
            int top, mid, bot;
            SortAlongUp(faces[f].v0, faces[f].v1, faces[f].v2, -down, out top, out mid, out bot);

            if (verts[top].role != VertRole.FlowTrue) continue;

            int ds = top;
            int dd = bot;
            Vector3 ps = positions[ds];
            Vector3 pd = positions[dd];
            float D = Vector3.Distance(ps, pd);
            faces[f].D = D;

            int n = 0;
            if (verts[top].role == VertRole.FixedPoint) n++;
            if (verts[mid].role == VertRole.FixedPoint) n++;
            if (verts[bot].role == VertRole.FixedPoint) n++;
            float k = upsilon / (n + 1f);

            Vector3 delta = Vector3.zero;
            if (D > hi)
            {
                // contract only in the ground plane (paper XZ)
                Vector3 off = ps - pd;
                off -= Vector3.Project(off, down);
                delta = -off * k;
            }
            else if (D < lo)
            {
                Vector3 off = ps - pd;
                delta = off * k;
                if (Vector3.Dot(delta, down) < 0f)
                    delta -= Vector3.Project(delta, down); // no up
            }
            else continue;

            displacements[ds] += Vector3.ClampMagnitude(delta, maxStep);
            displacementCounts[ds]++;
        }

        for (int i = 0; i < positions.Length; i++)
        {
            if (displacementCounts[i] > 0)
                positions[i] += displacements[i] / displacementCounts[i];
        }
    }

    void SortAlongUp(int a, int b, int c, Vector3 up, out int top, out int mid, out int bot)
    {
        top = a; mid = b; bot = c;
        if (Vector3.Dot(positions[top], up) < Vector3.Dot(positions[mid], up)) { int t = top; top = mid; mid = t; }
        if (Vector3.Dot(positions[top], up) < Vector3.Dot(positions[bot], up)) { int t = top; top = bot; bot = t; }
        if (Vector3.Dot(positions[mid], up) < Vector3.Dot(positions[bot], up)) { int t = mid; mid = bot; bot = t; }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || positions == null || verts == null || !drawRoles) return;
        float s = originalDist * 0.07f;
        for (int i = 0; i < verts.Length; i++)
        {
            switch (verts[i].role)
            {
                case VertRole.FlowTrue: Gizmos.color = new Color(1f, 0.45f, 0f); break;
                case VertRole.FixedPoint: Gizmos.color = Color.cyan; break;
                case VertRole.BasePoint:
                    if (verts[i].region < HeatRegion.Ready) continue;
                    Gizmos.color = Color.red; break;
                default: continue;
            }
            Gizmos.DrawSphere(transform.TransformPoint(positions[i]), s);
        }
    }
}