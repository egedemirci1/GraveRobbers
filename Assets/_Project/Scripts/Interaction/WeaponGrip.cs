using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Seats a gun on the right side: muzzle forward, right palm on the grip, left palm on the barrel.
    /// Wrist aim comes from the gun, not from the animated hand, so the hold does not spin while walking.
    /// </summary>
    public static class WeaponGrip
    {
        public static void Apply(Transform player, LootItem weapon)
        {
            if (player == null || weapon == null)
                return;

            Transform character = player.Find("Character");
            if (character == null)
                return;

            Transform handR = Find(character, "hand.R");
            Transform handL = Find(character, "hand.L");
            if (handR == null)
                return;

            if (!TryMeasure(weapon, out Vector3 boundsCenter, out Vector3 extents))
                return;

            Vector3 aim = Vector3.ProjectOnPlane(player.forward, Vector3.up);
            if (aim.sqrMagnitude < 0.0001f)
                aim = player.forward;
            aim.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, aim);

            Vector3 longAxis = extents.x >= extents.y && extents.x >= extents.z
                ? Vector3.right
                : extents.y >= extents.z ? Vector3.up : Vector3.forward;
            float halfLength = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));

            // These meshes run along Z with the stock on +Z. +Y is the rail.
            Vector3 muzzleLocal = -longAxis;
            Vector3 upLocal = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(upLocal, muzzleLocal)) > 0.85f)
                upLocal = Vector3.forward;
            upLocal = Vector3.ProjectOnPlane(upLocal, muzzleLocal).normalized;
            Quaternion rotation = Quaternion.LookRotation(aim, Vector3.up)
                * Quaternion.Inverse(Quaternion.LookRotation(muzzleLocal, upLocal));

            float centerAlong = Vector3.Dot(boundsCenter, longAxis);
            float stockEnd = centerAlong + halfLength;
            float muzzleEnd = centerAlong - halfLength;
            float behind = Mathf.Clamp(halfLength * 0.5f, 0.14f, 0.36f);
            float gripAlong = stockEnd - behind;
            float foreAlong = Mathf.Max(gripAlong - Mathf.Clamp(halfLength * 0.85f, 0.2f, 0.48f), muzzleEnd + 0.05f);

            float boreY = boundsCenter.y + extents.y - Mathf.Clamp(extents.y * 0.18f, 0.015f, 0.07f);
            Vector3 gripLocal = boundsCenter + longAxis * (gripAlong - centerAlong);
            Vector3 foreLocal = boundsCenter + longAxis * (foreAlong - centerAlong);
            if (Mathf.Abs(longAxis.y) < 0.5f)
            {
                gripLocal.y = boreY - 0.06f;
                foreLocal.y = boreY - 0.03f;
            }

            Transform chest = Find(character, "spine.003");
            if (chest == null)
                chest = Find(character, "spine.002");
            Transform shoulder = Find(character, "upper_arm.R");
            Vector3 origin = chest != null ? chest.position : handR.position;
            // The pistol grip sits in the right hand, in front of the right shoulder.
            // A rifle is not held on the center line of the chest.
            Vector3 gripWorld = (shoulder != null ? shoulder.position : origin + right * 0.24f) + aim * 0.24f;
            float lateral = Vector3.Dot(gripWorld - origin, right);
            if (lateral < 0.24f)
                gripWorld += right * (0.24f - lateral);
            gripWorld.y = origin.y - 0.08f;

            weapon.PlaceAt(gripWorld - rotation * gripLocal, rotation);
            Vector3 foreWorld = weapon.transform.position + rotation * foreLocal;

            Vector3 rightTarget = gripWorld + right * 0.055f;
            Vector3 leftTarget = foreWorld - right * 0.045f;
            Plant(handR, rightTarget, right, aim);
            Plant(handL, leftTarget, -right, aim);
            PoseHand(handR, aim, -right);
            PoseHand(handL, aim, right);
        }

        static void Plant(Transform hand, Vector3 target, Vector3 outward, Vector3 aim)
        {
            if (hand == null)
                return;

            Transform forearm = hand.parent;
            Transform upper = forearm != null ? forearm.parent : null;
            if (forearm == null || upper == null)
                return;

            Vector3 pole = upper.position + outward * 0.5f - aim * 0.15f + Vector3.down * 0.3f;
            SolveTwoBone(upper, forearm, hand, target, pole);
        }

        static void PoseHand(Transform hand, Vector3 fingerWorld, Vector3 palmWorld)
        {
            if (hand == null)
                return;

            Vector3 fingerLocal = FingerLocal(hand);
            Vector3 palmLocal = PalmLocal(hand, fingerLocal);
            if (fingerLocal.sqrMagnitude < 0.5f || palmLocal.sqrMagnitude < 0.5f)
                return;

            Vector3 finger = fingerWorld.normalized;
            Vector3 palm = Vector3.ProjectOnPlane(palmWorld, finger);
            if (palm.sqrMagnitude < 0.0001f)
                return;
            palm.Normalize();

            Quaternion rotation = Quaternion.FromToRotation(fingerLocal, finger);
            Vector3 palmNow = rotation * palmLocal;
            palmNow = Vector3.ProjectOnPlane(palmNow, finger);
            if (palmNow.sqrMagnitude < 0.0001f)
                return;

            hand.rotation = Quaternion.FromToRotation(palmNow, palm) * rotation;
        }

        /// <summary>
        /// Rigify hands in this project point the fingers along local -Z.
        /// The end bone is read so a different export still lines up.
        /// </summary>
        static Vector3 FingerLocal(Transform hand)
        {
            Transform end = hand.Find(hand.name + "_end");
            if (end == null)
            {
                for (int i = 0; i < hand.childCount; i++)
                {
                    Transform child = hand.GetChild(i);
                    if (child.name.EndsWith("_end"))
                    {
                        end = child;
                        break;
                    }
                }
            }

            if (end != null && end.localPosition.sqrMagnitude > 0.0001f)
                return end.localPosition.normalized;

            return Vector3.back;
        }

        /// <summary>
        /// Right palm faces local +X, left palm faces local -X, so the two palms meet on the gun.
        /// </summary>
        static Vector3 PalmLocal(Transform hand, Vector3 fingerLocal)
        {
            bool left = hand.name.EndsWith(".L");
            Vector3 candidate = left ? Vector3.left : Vector3.right;
            if (Mathf.Abs(Vector3.Dot(candidate, fingerLocal)) > 0.7f)
                candidate = left ? Vector3.down : Vector3.up;

            return Vector3.ProjectOnPlane(candidate, fingerLocal).normalized;
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
            extents = Vector3.one * 0.2f;
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
                    Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((c & 1) * 2 - 1, ((c >> 1) & 1) * 2 - 1, ((c >> 2) & 1) * 2 - 1));
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

        static Transform Find(Transform root, string boneName)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == boneName)
                    return all[i];
            }

            return null;
        }
    }
}
