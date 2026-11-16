using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Finds the pickup doors and swings each one around the pivot authored on the mesh.
    /// </summary>
    public static class VehicleDoorRig
    {
        const float OpenDegrees = 72f;

        static VehicleDoor[] _doors;
        static bool[] _open;

        public static VehicleDoor[] Doors => _doors;
        public static bool[] OpenState => _open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Ensure();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_doors != null && _doors.Length > 0 && _doors[0] == null)
            {
                _doors = null;
                _open = null;
            }

            Ensure();
        }

        public static void Ensure()
        {
            if (_doors != null && _doors.Length > 0 && _doors[0] != null)
                return;

            _doors = null;
            _open = null;

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
                if (current.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (IsUnderDoor(current, truck))
                    continue;
                if (current.GetComponent<MeshFilter>() == null)
                    continue;
                found.Add(current);
            }

            if (found.Count == 0)
                return;

            found.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            _doors = new VehicleDoor[found.Count];
            _open = new bool[found.Count];
            for (int i = 0; i < found.Count; i++)
                _doors[i] = BuildDoor(found[i], truck, i);
        }

        public static void SetOpen(int index, bool open)
        {
            Ensure();
            if (_doors == null || index < 0 || index >= _doors.Length)
                return;

            if (_open != null && index < _open.Length)
                _open[index] = open;

            _doors[index].SetOpen(open);
        }

        static VehicleDoor BuildDoor(Transform door, Transform truck, int index)
        {
            MeshFilter filter = door.GetComponent<MeshFilter>();
            if (door.GetComponent<Collider>() == null && filter != null && filter.sharedMesh != null)
            {
                var box = door.gameObject.AddComponent<BoxCollider>();
                box.center = filter.sharedMesh.bounds.center;
                box.size = filter.sharedMesh.bounds.size;
            }

            var vehicleDoor = door.GetComponent<VehicleDoor>();
            if (vehicleDoor == null)
                vehicleDoor = door.gameObject.AddComponent<VehicleDoor>();

            vehicleDoor.Index = index;
            vehicleDoor.Configure(door, OutwardYaw(door, truck));
            return vehicleDoor;
        }

        static float OutwardYaw(Transform door, Transform truck)
        {
            var renderer = door.GetComponent<Renderer>();
            Vector3 pivot = door.position;
            Vector3 center = renderer != null ? renderer.bounds.center : pivot + door.forward;
            Vector3 offset = center - pivot;
            if (offset.sqrMagnitude < 0.0001f)
                offset = door.forward;

            Vector3 plus = pivot + Quaternion.AngleAxis(OpenDegrees, door.up) * offset;
            Vector3 minus = pivot + Quaternion.AngleAxis(-OpenDegrees, door.up) * offset;
            float side = Vector3.Dot(center - truck.position, truck.right);
            float sign = Mathf.Abs(side) < 0.001f ? 1f : Mathf.Sign(side);
            float plusOut = Vector3.Dot(plus - truck.position, truck.right) * sign;
            float minusOut = Vector3.Dot(minus - truck.position, truck.right) * sign;
            return plusOut >= minusOut ? OpenDegrees : -OpenDegrees;
        }

        static Transform FindTruck()
        {
            GameObject named = GameObject.Find("pickup");
            if (named != null)
                return named.transform;

            MeshFilter[] filters = UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                return filters[i].transform.parent != null ? filters[i].transform.parent : filters[i].transform;
            }

            return null;
        }

        static bool IsUnderDoor(Transform current, Transform truck)
        {
            Transform parent = current.parent;
            while (parent != null && parent != truck)
            {
                if (parent.name.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                parent = parent.parent;
            }

            return false;
        }
    }
}
