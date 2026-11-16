using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// One pickup seat. The rig fills the sit pose; this only remembers it.
    /// </summary>
    public class VehicleSeat : MonoBehaviour
    {
        public int Index { get; set; }
        public Vector3 InteractionPoint { get; private set; }
        public Vector3 SitFeet { get; private set; }
        public Quaternion SitRotation { get; private set; }
        public Vector3 ExitFeet { get; private set; }
        public Quaternion ExitRotation { get; private set; }
        public Collider SeatCollider { get; private set; }

        public void Configure(Transform truck, Vector3 face, bool frontSeat)
        {
            var renderer = GetComponent<Renderer>();
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.one * 0.5f);
            InteractionPoint = bounds.center;

            Vector3 up = truck.up.sqrMagnitude > 0.001f ? truck.up.normalized : Vector3.up;
            Vector3 extents = bounds.extents;
            float halfAlong = Mathf.Abs(extents.x * up.x) + Mathf.Abs(extents.y * up.y) + Mathf.Abs(extents.z * up.z);
            float centerAlong = Vector3.Dot(bounds.center, up);
            float bottom = centerAlong - halfAlong;
            float cushion = bottom + halfAlong * 2f * 0.30f;
            const float hipHeight = 0.9f;
            const float roofClearance = 0.32f;
            SitFeet = bounds.center + up * ((cushion - hipHeight - roofClearance) - centerAlong);

            Vector3 flatFace = Vector3.ProjectOnPlane(face, up);
            if (flatFace.sqrMagnitude < 0.0001f)
                flatFace = Vector3.ProjectOnPlane(truck.forward, up);
            if (flatFace.sqrMagnitude < 0.0001f)
                flatFace = Vector3.forward;
            flatFace.Normalize();
            float sitBack = frontSeat ? 0.18f : 0.02f;
            SitFeet -= flatFace * sitBack;
            SitRotation = Quaternion.LookRotation(flatFace, up);

            Vector3 right = Vector3.ProjectOnPlane(truck.right, up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();
            float side = Vector3.Dot(bounds.center - truck.position, right);
            float sideSign = Mathf.Abs(side) < 0.01f ? 1f : Mathf.Sign(side);
            Vector3 exit = bounds.center + right * (sideSign * 1.4f);
            ExitFeet = exit + up * (bottom - centerAlong);
            RaycastHit[] groundHits = Physics.RaycastAll(exit + up * 3f, -up, 8f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < groundHits.Length; i++)
            {
                if (groundHits[i].collider.transform.IsChildOf(truck))
                    continue;
                if (groundHits[i].distance >= nearest)
                    continue;

                nearest = groundHits[i].distance;
                ExitFeet = groundHits[i].point;
            }

            ExitRotation = SitRotation;
            SeatCollider = GetComponent<Collider>();
        }
    }
}
