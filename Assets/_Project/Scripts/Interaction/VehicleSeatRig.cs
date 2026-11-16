using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Finds the four pickup seats and keeps track of who is sitting.
    /// </summary>
    public static class VehicleSeatRig
    {
        static VehicleSeat[] _seats;
        static long[] _occupant;

        public static VehicleSeat[] Seats => _seats;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Ensure();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_seats != null && _seats.Length > 0 && _seats[0] == null)
            {
                _seats = null;
                _occupant = null;
            }

            Ensure();
        }

        public static void Ensure()
        {
            if (_seats != null && _seats.Length > 0 && _seats[0] != null)
                return;

            _seats = null;
            _occupant = null;

            Transform truck = FindTruck();
            if (truck == null)
                return;

            var found = new List<Transform>();
            Transform[] all = truck.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform current = all[i];
                if (current == truck)
                    continue;
                if (current.name.IndexOf("Koltuk", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (IsUnderSeat(current, truck))
                    continue;
                if (current.GetComponent<MeshFilter>() == null)
                    continue;
                found.Add(current);
            }

            if (found.Count == 0)
                return;

            found.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            Vector3 face = NoseDirection(truck, found);
            float seatAverage = 0f;
            var alongNose = new float[found.Count];
            for (int i = 0; i < found.Count; i++)
            {
                var renderer = found[i].GetComponent<Renderer>();
                Vector3 point = renderer != null ? renderer.bounds.center : found[i].position;
                alongNose[i] = Vector3.Dot(point - truck.position, face);
                seatAverage += alongNose[i];
            }

            seatAverage /= found.Count;
            _seats = new VehicleSeat[found.Count];
            _occupant = new long[found.Count];
            for (int i = 0; i < found.Count; i++)
            {
                _occupant[i] = -1;
                _seats[i] = BuildSeat(found[i], truck, face, alongNose[i] > seatAverage, i);
            }
        }

        public static bool TryClaim(int index, ulong clientId)
        {
            Ensure();
            if (_occupant == null || index < 0 || index >= _occupant.Length)
                return false;

            long id = (long)clientId;
            if (_occupant[index] >= 0 && _occupant[index] != id)
                return false;

            for (int i = 0; i < _occupant.Length; i++)
            {
                if (_occupant[i] == id)
                    _occupant[i] = -1;
            }

            _occupant[index] = id;
            return true;
        }

        public static void Release(ulong clientId)
        {
            if (_occupant == null)
                return;

            long id = (long)clientId;
            for (int i = 0; i < _occupant.Length; i++)
            {
                if (_occupant[i] == id)
                    _occupant[i] = -1;
            }
        }

        public static int IndexOf(ulong clientId)
        {
            if (_occupant == null)
                return -1;

            long id = (long)clientId;
            for (int i = 0; i < _occupant.Length; i++)
            {
                if (_occupant[i] == id)
                    return i;
            }

            return -1;
        }

        static VehicleSeat BuildSeat(Transform seat, Transform truck, Vector3 face, bool frontSeat, int index)
        {
            MeshFilter filter = seat.GetComponent<MeshFilter>();
            if (seat.GetComponent<Collider>() == null && filter != null && filter.sharedMesh != null)
            {
                var box = seat.gameObject.AddComponent<BoxCollider>();
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }

            if (seat.GetComponent<Rigidbody>() == null)
            {
                var body = seat.gameObject.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
            }

            var vehicleSeat = seat.GetComponent<VehicleSeat>();
            if (vehicleSeat == null)
                vehicleSeat = seat.gameObject.AddComponent<VehicleSeat>();

            vehicleSeat.Index = index;
            vehicleSeat.Configure(truck, face, frontSeat);
            return vehicleSeat;
        }

        static Vector3 NoseDirection(Transform truck, List<Transform> seats)
        {
            Vector3 up = truck.up.sqrMagnitude > 0.001f ? truck.up.normalized : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(truck.forward, up);
            Vector3 right = Vector3.ProjectOnPlane(truck.right, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            forward.Normalize();
            right.Normalize();

            Renderer[] renderers = truck.GetComponentsInChildren<Renderer>(true);
            float forwardSpan = SpanAlong(renderers, truck.position, forward, out float forwardMin, out float forwardMax);
            float rightSpan = SpanAlong(renderers, truck.position, right, out float rightMin, out float rightMax);
            bool alongForward = forwardSpan >= rightSpan;
            Vector3 axis = alongForward ? forward : right;
            float min = alongForward ? forwardMin : rightMin;
            float max = alongForward ? forwardMax : rightMax;

            float seatAverage = 0f;
            for (int i = 0; i < seats.Count; i++)
            {
                var renderer = seats[i].GetComponent<Renderer>();
                Vector3 point = renderer != null ? renderer.bounds.center : seats[i].position;
                seatAverage += Vector3.Dot(point - truck.position, axis);
            }

            seatAverage /= Mathf.Max(1, seats.Count);
            bool noseIsMin = Mathf.Abs(seatAverage - min) < Mathf.Abs(seatAverage - max);
            return noseIsMin ? -axis : axis;
        }

        static float SpanAlong(Renderer[] renderers, Vector3 origin, Vector3 axis, out float min, out float max)
        {
            min = 0f;
            max = 0f;
            bool started = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds bounds = renderers[i].bounds;
                Vector3 extents = bounds.extents;
                float center = Vector3.Dot(bounds.center - origin, axis);
                float half = Mathf.Abs(extents.x * axis.x) + Mathf.Abs(extents.y * axis.y) + Mathf.Abs(extents.z * axis.z);
                float low = center - half;
                float high = center + half;
                if (!started)
                {
                    min = low;
                    max = high;
                    started = true;
                }
                else
                {
                    min = Mathf.Min(min, low);
                    max = Mathf.Max(max, high);
                }
            }

            return max - min;
        }

        static Transform FindTruck()
        {
            GameObject named = GameObject.Find("pickup");
            if (named != null)
                return named.transform;

            MeshFilter[] filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].name.IndexOf("Koltuk", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                return filters[i].transform.parent != null ? filters[i].transform.parent : filters[i].transform;
            }

            return null;
        }

        static bool IsUnderSeat(Transform current, Transform truck)
        {
            Transform parent = current.parent;
            while (parent != null && parent != truck)
            {
                if (parent.name.IndexOf("Koltuk", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                parent = parent.parent;
            }

            return false;
        }
    }
}
