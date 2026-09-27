using UnityEngine;

public class pourDebug : MonoBehaviour
{
    [Header("Drop Prefab Settings")]
    public GameObject waterDropPrefab;
    public float dropScale = 0.03f;

    [Header("Flow Control")]
    public float dropsPerSecond = 2f;
    public float initialEjectionSpeed = 0.6f;
    public float downwardForce = 0.2f;

    [Header("Pour Threshold")]
    public float pourTiltAngleThreshold = 20f;

    private float spawnTimer;

    void Update()
    {
        Transform teapotTransform = transform.parent != null ? transform.parent : transform;

        float tiltAngleFromRest = Vector3.Angle(-teapotTransform.up, Vector3.down);
        bool isTiltingPastThreshold = tiltAngleFromRest > pourTiltAngleThreshold;

        if (isTiltingPastThreshold)
        {
            spawnTimer += Time.deltaTime;
            float interval = 1f / dropsPerSecond;

            while (spawnTimer >= interval)
            {
                SpawnDrop();
                spawnTimer -= interval;
            }
        }
        else
        {
            spawnTimer = 0f;
        }
    }

    void SpawnDrop()
    {
        if (waterDropPrefab == null) return;

        GameObject drop = Instantiate(waterDropPrefab, transform.position, Quaternion.identity);
        drop.transform.localScale = Vector3.one * dropScale;

        Rigidbody rb = drop.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = true;

            Vector3 pourDirection = -transform.right;

            Vector3 trajectory = (pourDirection * initialEjectionSpeed) + (Vector3.down * downwardForce);
            rb.linearVelocity = trajectory;
        }
    }
}