using System;
using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// One pickup seat. Poses stay on the truck so they move when it drives.
    /// </summary>
    public class VehicleSeat : MonoBehaviour
    {
        Transform _truck;
        Vector3 _interactLocal;
        Vector3 _sitLocal;
        Quaternion _sitLocalRot = Quaternion.identity;
        Vector3 _exitLocal;
        Quaternion _exitLocalRot = Quaternion.identity;

        public int Index { get; set; }
        public bool IsDriverSeat { get; private set; }
        public Transform Truck => _truck;
        public Vector3 InteractionPoint => _truck != null ? _truck.TransformPoint(_interactLocal) : transform.position;
        public Vector3 SitFeet => _truck != null ? _truck.TransformPoint(_sitLocal) : transform.position;
        public Quaternion SitRotation => _truck != null ? _truck.rotation * _sitLocalRot : transform.rotation;
        public Vector3 ExitFeet
        {
            get
            {
                Vector3 point = _truck != null ? _truck.TransformPoint(_exitLocal) : transform.position;
                RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 2.5f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
                float nearest = float.MaxValue;
                bool found = false;
                for (int i = 0; i < hits.Length; i++)
                {
                    if (_truck != null && hits[i].collider.transform.IsChildOf(_truck))
                        continue;
                    if (hits[i].distance >= nearest)
                        continue;

                    nearest = hits[i].distance;
                    point = hits[i].point;
                    found = true;
                }

                return found ? point : (_truck != null ? _truck.TransformPoint(_exitLocal) : transform.position);
            }
        }

        public Quaternion ExitRotation
        {
            get
            {
                Quaternion tilted = _truck != null ? _truck.rotation * _exitLocalRot : transform.rotation;
                return UprightYaw(tilted * Vector3.forward);
            }
        }

        public static Quaternion UprightYaw(Vector3 forward)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;
            return Quaternion.LookRotation(flat.normalized, Vector3.up);
        }
        public Collider SeatCollider { get; private set; }

        public void Configure(Transform truck, Vector3 face, bool frontSeat)
        {
            var renderer = GetComponent<Renderer>();
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds(transform.position, Vector3.one * 0.5f);
            _truck = truck;
            IsDriverSeat = name.IndexOf("sol", StringComparison.OrdinalIgnoreCase) >= 0;
            _interactLocal = truck.InverseTransformPoint(bounds.center);

            Vector3 up = truck.up.sqrMagnitude > 0.001f ? truck.up.normalized : Vector3.up;
            Vector3 extents = bounds.extents;
            float halfAlong = Mathf.Abs(extents.x * up.x) + Mathf.Abs(extents.y * up.y) + Mathf.Abs(extents.z * up.z);
            float centerAlong = Vector3.Dot(bounds.center, up);
            float bottom = centerAlong - halfAlong;
            float cushion = bottom + halfAlong * 2f * 0.30f;
            const float hipHeight = 0.9f;
            const float roofClearance = 0.32f;
            Vector3 sitFeet = bounds.center + up * ((cushion - hipHeight - roofClearance) - centerAlong);

            Vector3 flatFace = Vector3.ProjectOnPlane(face, up);
            if (flatFace.sqrMagnitude < 0.0001f)
                flatFace = Vector3.ProjectOnPlane(truck.forward, up);
            if (flatFace.sqrMagnitude < 0.0001f)
                flatFace = Vector3.forward;
            flatFace.Normalize();
            float sitBack = frontSeat ? 0.18f : 0.02f;
            sitFeet -= flatFace * sitBack;
            Quaternion sitRotation = Quaternion.LookRotation(flatFace, up);
            _sitLocal = truck.InverseTransformPoint(sitFeet);
            _sitLocalRot = Quaternion.Inverse(truck.rotation) * sitRotation;

            Vector3 right = Vector3.ProjectOnPlane(truck.right, up);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();
            float side = Vector3.Dot(bounds.center - truck.position, right);
            float sideSign = Mathf.Abs(side) < 0.01f ? 1f : Mathf.Sign(side);
            Vector3 exit = bounds.center + right * (sideSign * 1.4f);
            Vector3 exitFeet = exit + up * (bottom - centerAlong);
            RaycastHit[] groundHits = Physics.RaycastAll(exit + up * 3f, -up, 8f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < groundHits.Length; i++)
            {
                if (groundHits[i].collider.transform.IsChildOf(truck))
                    continue;
                if (groundHits[i].distance >= nearest)
                    continue;

                nearest = groundHits[i].distance;
                exitFeet = groundHits[i].point;
            }

            _exitLocal = truck.InverseTransformPoint(exitFeet);
            _exitLocalRot = Quaternion.Inverse(truck.rotation) * sitRotation;
            SeatCollider = GetComponent<Collider>();
        }
    }
}
