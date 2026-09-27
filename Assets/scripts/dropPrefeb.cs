using UnityEngine;

public class WaterDrop : MonoBehaviour
{
    [Header("Lifetime Settings")]
    public float maxLifetime = 4.0f;
    public float destroyDelayOnContact = 0.01f;

    [Header("Fluid Interaction")]
    public float dropVolume = 0.002f;
    public float dropImpactForce = 500f;

    [Header("Debug")]
    public bool enableCollisionLogs = true;

    private bool hasHitWater = false;
    private Vector3 lastPosition;

    void Start()
    {
        lastPosition = transform.position;
        Destroy(gameObject, maxLifetime);
    }

    void Update()
    {
        if (hasHitWater) return;

        Vector3 currentPosition = transform.position;
        Vector3 movementVector = currentPosition - lastPosition;
        float distance = movementVector.magnitude;

        if (distance > 0f)
        {
            if (Physics.Raycast(lastPosition, Vector3.down, out RaycastHit hit, distance))
            {
                WaterPhysicsCPU puddleSystem = hit.collider.GetComponent<WaterPhysicsCPU>();
                if (puddleSystem == null)
                {
                    puddleSystem = hit.collider.GetComponentInParent<WaterPhysicsCPU>();
                }

                if (enableCollisionLogs)
                {
                    //**Debug**//
                    //Debug.Log($"[Raycast Hit] Hit: '{hit.collider.name}' | Valid Puddle System: {puddleSystem != null}");
                }

                if (puddleSystem != null)
                {
                    hasHitWater = true;

                    Rigidbody rb = GetComponent<Rigidbody>();
                    float impactSpeed = rb != null ? rb.linearVelocity.magnitude : 1f;
                    if (rb != null) rb.isKinematic = true;

                    puddleSystem.ReceiveWaterDrop(hit.point, impactSpeed, dropVolume, dropImpactForce);

                    Destroy(gameObject, destroyDelayOnContact);
                }
            }
        }

        lastPosition = currentPosition;
    }
}