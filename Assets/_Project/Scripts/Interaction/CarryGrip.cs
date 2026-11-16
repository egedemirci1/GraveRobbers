using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Puts the prop in front of the chest and plants each wrist on that prop's outer side.
    /// The left hand stays on the leftmost face, the right hand on the rightmost face.
    /// </summary>
    public static class CarryGrip
    {
        const float MinFront = 0.42f;
        const float ChinBelowHead = 0.1f;
        const float PalmOffset = 0.07f;

        public static void Apply(Transform player, LootItem loot)
        {
            if (player == null || loot == null)
                return;

            Transform character = player.Find("Character");
            if (character == null)
                return;

            Transform handL = Find(character, "hand.L");
            Transform handR = Find(character, "hand.R");
            Transform chest = Find(character, "spine.002");
            Transform head = Find(character, "spine.005");
            if (handL == null || handR == null)
                return;

            if (!TryMeasure(loot, out Vector3 boundsCenter, out Vector3 extents))
                return;

            Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                return;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            float chestForward = chest != null
                ? Vector3.Dot(chest.position - player.position, forward)
                : 0f;
            float near = Mathf.Max(MinFront, chestForward + 0.3f);
            Vector3 center = player.position + forward * (near + extents.z);
            center.y = chest != null ? chest.position.y : (handL.position.y + handR.position.y) * 0.5f;

            if (head != null)
            {
                float chin = head.position.y - ChinBelowHead;
                if (center.y + extents.y > chin)
                    center.y = chin - extents.y;
            }

            if (center.y - extents.y < 0.08f)
                center.y = extents.y + 0.08f;

            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            loot.PlaceAt(center - rotation * boundsCenter, rotation);

            Plant(handL, center, forward, right, extents, -1f);
            Plant(handR, center, forward, right, extents, 1f);
        }

        static void Plant(
            Transform hand,
            Vector3 center,
            Vector3 forward,
            Vector3 right,
            Vector3 extents,
            float sideSign)
        {
            Transform forearm = hand.parent;
            Transform upper = forearm != null ? forearm.parent : null;
            if (forearm == null || upper == null)
                return;

            // Keep the clip's wrist. Rebuilding it every frame points the fingers up and
            // flips the palm as the walk cycle moves the arm.
            Quaternion wristPose = hand.localRotation;

            Vector3 outward = right * sideSign;
            float along = Mathf.Min(0.06f, extents.z * 0.35f) - extents.z;
            float low = center.y - extents.y * 0.45f;
            float high = center.y - extents.y * 0.05f;
            Vector3 face = center + outward * extents.x + forward * along;
            face.y = Mathf.Clamp(hand.position.y, low, high);

            float reach = (Vector3.Distance(upper.position, forearm.position) + Vector3.Distance(forearm.position, hand.position)) * 0.98f;
            Vector3 pole = upper.position + outward * 0.45f - forward * 0.2f + Vector3.down * 0.2f;
            Vector3 target = OnOuterPlane(upper.position, face + outward * PalmOffset, outward, reach);
            SolveTwoBone(upper, forearm, hand, target, pole);
            hand.localRotation = wristPose;
        }

        /// <summary>
        /// Keeps the wrist on the outer side plane. If the arm is short, it slides on that
        /// plane toward the shoulder instead of sinking into the prop.
        /// </summary>
        static Vector3 OnOuterPlane(Vector3 shoulder, Vector3 target, Vector3 outward, float reach)
        {
            Vector3 normal = outward.normalized;
            float planeDistance = Vector3.Dot(target - shoulder, normal);
            if (Mathf.Abs(planeDistance) >= reach)
                return shoulder + (target - shoulder).normalized * reach;

            float radius = Mathf.Sqrt(reach * reach - planeDistance * planeDistance);
            Vector3 circleCenter = shoulder + normal * planeDistance;
            Vector3 toTarget = target - circleCenter;
            if (toTarget.sqrMagnitude <= radius * radius)
                return target;

            if (toTarget.sqrMagnitude < 0.000001f)
                return circleCenter;
            return circleCenter + toTarget.normalized * radius;
        }

        static void SolveTwoBone(Transform upper, Transform forearm, Transform hand, Vector3 target, Vector3 pole)
        {
            float upperLen = Vector3.Distance(upper.position, forearm.position);
            float foreLen = Vector3.Distance(forearm.position, hand.position);
            if (upperLen < 0.02f || foreLen < 0.02f)
                return;

            Vector3 shoulder = upper.position;
            float reach = Mathf.Clamp(
                Vector3.Distance(shoulder, target),
                Mathf.Abs(upperLen - foreLen) + 0.001f,
                upperLen + foreLen - 0.001f);
            Vector3 toTarget = (target - shoulder).normalized;
            float cosShoulder = Mathf.Clamp(
                (upperLen * upperLen + reach * reach - foreLen * foreLen) / (2f * upperLen * reach),
                -1f,
                1f);
            float sinShoulder = Mathf.Sqrt(Mathf.Max(0f, 1f - cosShoulder * cosShoulder));

            Vector3 bend = Vector3.ProjectOnPlane(pole - shoulder, toTarget);
            if (bend.sqrMagnitude < 0.0001f)
                bend = Vector3.ProjectOnPlane(Vector3.down, toTarget);
            if (bend.sqrMagnitude < 0.0001f)
                return;

            Vector3 elbow = shoulder + toTarget * (upperLen * cosShoulder) + bend.normalized * (upperLen * sinShoulder);
            Aim(upper, forearm.position, elbow);
            Aim(forearm, hand.position, target);
        }

        static void Aim(Transform bone, Vector3 currentEnd, Vector3 desiredEnd)
        {
            Vector3 from = currentEnd - bone.position;
            Vector3 to = desiredEnd - bone.position;
            if (from.sqrMagnitude < 0.0000001f || to.sqrMagnitude < 0.0000001f)
                return;

            bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
        }

        static bool TryMeasure(LootItem loot, out Vector3 center, out Vector3 extents)
        {
            center = Vector3.zero;
            extents = Vector3.one * 0.15f;
            MeshFilter[] filters = loot.GetComponentsInChildren<MeshFilter>();
            bool any = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            Quaternion inverse = Quaternion.Inverse(loot.transform.rotation);

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i].sharedMesh;
                if (mesh == null)
                    continue;

                Bounds local = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents, Corner(c));
                    Vector3 offset = inverse * (filters[i].transform.TransformPoint(corner) - loot.transform.position);
                    if (!any)
                    {
                        min = max = offset;
                        any = true;
                    }
                    else
                    {
                        min = Vector3.Min(min, offset);
                        max = Vector3.Max(max, offset);
                    }
                }
            }

            if (!any)
                return false;

            center = (min + max) * 0.5f;
            extents = (max - min) * 0.5f;
            return true;
        }

        static Vector3 Corner(int index)
        {
            return new Vector3((index & 1) * 2 - 1, ((index >> 1) & 1) * 2 - 1, ((index >> 2) & 1) * 2 - 1);
        }

        static Transform Find(Transform root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name)
                    return all[i];
            }

            return null;
        }
    }
}
