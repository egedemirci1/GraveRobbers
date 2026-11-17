using System;
using System.Collections.Generic;
using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// WheelCollider pickup. The seated driver simulates it; everyone else follows the pose.
    /// </summary>
    public class PickupVehicle : MonoBehaviour
    {
        const float Mass = 2800f;
        const float MaxSpeed = 34f;
        const float MotorTorque = 11000f;
        const float ReverseTorque = 3200f;
        const float BrakeTorque = 4500f;
        const float CoastBrake = 40f;

        struct Wheel
        {
            public WheelCollider Collider;
            public Transform Visual;
            public Vector3 RestLocalPos;
            public Quaternion RestLocalRot;
            public Vector3 RestCenterLocal;
            public Vector3 MeshCenter;
            public Vector3 Axle;
            public bool Steer;
            public bool Drive;
        }

        static PickupVehicle _current;

        Rigidbody _body;
        Wheel[] _wheels;
        Transform _steering;
        Quaternion _steeringRest;
        Vector3 _steeringAxis;
        Vector2 _input;
        float _steer;
        float _spin;
        bool _authority;
        bool _released;
        bool _remote;
        Vector3 _remotePos;
        Quaternion _remoteRot;

        public static PickupVehicle Current => _current;
        public bool HasAuthority => _authority;

        public static void Ensure()
        {
            if (_current != null)
                return;

            GameObject truckObject = GameObject.Find("pickup");
            if (truckObject == null)
                return;

            Transform truck = truckObject.transform;
            Transform[] all = truck.GetComponentsInChildren<Transform>(true);
            var tires = new List<Transform>();
            Transform steering = null;
            for (int i = 0; i < all.Length; i++)
            {
                string name = all[i].name;
                if (name.IndexOf("Tire", StringComparison.OrdinalIgnoreCase) >= 0 && all[i].GetComponent<MeshFilter>() != null)
                    tires.Add(all[i]);
                else if (steering == null && name.IndexOf("Stear", StringComparison.OrdinalIgnoreCase) >= 0)
                    steering = all[i];
            }

            if (tires.Count < 4)
                return;

            StripChildBodies(truck);
            _current = truck.gameObject.AddComponent<PickupVehicle>();
            _current.Build(truck, tires, steering);
        }

        public void SetLocalDriver(bool driving)
        {
            _authority = true;
            _released = !driving;
            _remote = false;
            if (!driving)
                return;

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.isKinematic = false;
            _body.WakeUp();
            SetWheelsEnabled(true);
        }

        public void SetInput(Vector2 move)
        {
            if (_released)
                return;

            _input = Vector2.ClampMagnitude(move, 1f);
        }

        public void ReadState(out Vector3 position, out Quaternion rotation, out Vector3 velocity, out Vector3 angular, out float steer, out float spin)
        {
            position = transform.position;
            rotation = transform.rotation;
            velocity = _body.linearVelocity;
            angular = _body.angularVelocity;
            steer = _steer;
            spin = _spin;
        }

        public void ApplyRemote(Vector3 position, Quaternion rotation, float steer, float spin)
        {
            if (_authority)
                return;

            _remote = true;
            _remotePos = position;
            _remoteRot = rotation;
            _steer = steer;
            _spin = spin;
            _body.isKinematic = true;
            SetWheelsEnabled(false);
        }

        void Build(Transform truck, List<Transform> tires, Transform steering)
        {
            _body = truck.GetComponent<Rigidbody>();
            if (_body == null)
                _body = truck.gameObject.AddComponent<Rigidbody>();

            _body.mass = Mass;
            _body.linearDamping = 0.08f;
            _body.angularDamping = 2.4f;
            _body.automaticCenterOfMass = false;
            _body.automaticInertiaTensor = false;
            const float width = 2f;
            const float height = 1.6f;
            const float length = 5.4f;
            float box = Mass / 12f;
            _body.inertiaTensor = new Vector3(
                box * (height * height + length * length),
                box * (width * width + length * length),
                box * (width * width + height * height));
            _body.inertiaTensorRotation = Quaternion.identity;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.isKinematic = true;
            _body.useGravity = true;

            Vector3 nose = NoseDirection(truck, steering);
            var scored = new List<(Transform visual, float along)>();
            for (int i = 0; i < tires.Count; i++)
            {
                float along = Vector3.Dot(tires[i].position - truck.position, nose);
                scored.Add((tires[i], along));
            }

            scored.Sort((a, b) => b.along.CompareTo(a.along));
            _wheels = new Wheel[scored.Count];
            float floor = float.MaxValue;
            Vector3 hubSum = Vector3.zero;
            for (int i = 0; i < scored.Count; i++)
            {
                Transform visual = scored[i].visual;
                // Rear tires are larger than the bed arch and their mesh sits behind the pivot.
                if (i >= 2)
                    FitRearWheel(visual, truck);
                Vector3 hub = WheelHub(visual, out float radius);
                var colliderObject = new GameObject(visual.name + " Collider");
                colliderObject.transform.SetParent(truck, false);
                colliderObject.transform.SetPositionAndRotation(hub, truck.rotation);
                var wheel = colliderObject.AddComponent<WheelCollider>();
                wheel.radius = radius;
                Tune(wheel);
                wheel.enabled = false;

                _wheels[i] = new Wheel
                {
                    Collider = wheel,
                    Visual = visual,
                    RestLocalPos = visual.localPosition,
                    RestLocalRot = visual.localRotation,
                    RestCenterLocal = truck.InverseTransformPoint(hub),
                    MeshCenter = MeshCenter(visual),
                    Axle = ThinAxis(visual),
                    Steer = i < 2,
                    Drive = i >= 2
                };

                Vector3 hubLocal = truck.InverseTransformPoint(hub);
                hubSum += hubLocal;
                float contact = hubLocal.y - radius;
                if (contact < floor)
                    floor = contact;
            }

            BuildChassis(truck, floor);
            Vector3 center = hubSum / Mathf.Max(1, _wheels.Length);
            center -= truck.InverseTransformDirection(truck.up) * 0.25f;
            _body.centerOfMass = center;

            _steering = steering;
            if (_steering != null)
            {
                _steeringRest = _steering.localRotation;
                _steeringAxis = DominantAxis(_steering.InverseTransformDirection(nose));
            }
        }

        void FixedUpdate()
        {
            if (!_authority || _body == null)
                return;

            _body.isKinematic = false;
            Vector3 velocity = _body.linearVelocity;
            float forwardSpeed = Vector3.Dot(velocity, transform.forward);
            float speed = velocity.magnitude;
            float throttle = Mathf.Clamp(_input.y, -1f, 1f);
            float steerInput = Mathf.Clamp(_input.x, -1f, 1f);
            if (_released)
                throttle = 0f;

            float steerLimit = Mathf.Lerp(14f, 32f, Mathf.InverseLerp(18f, 3f, speed));
            float steerTarget = steerInput * steerLimit;
            _steer = Mathf.Lerp(_steer, steerTarget, 1f - Mathf.Exp(-7f * Time.fixedDeltaTime));

            float motor = 0f;
            float brake = _released ? 4000f : CoastBrake;
            if (!_released && throttle > 0.05f)
            {
                if (forwardSpeed < -0.6f)
                {
                    brake = BrakeTorque;
                }
                else
                {
                    float pull = Mathf.InverseLerp(MaxSpeed, 4f, speed);
                    motor = MotorTorque * throttle * Mathf.Lerp(0.75f, 1f, pull);
                    brake = 0f;
                }
            }
            else if (!_released && throttle < -0.05f)
            {
                if (forwardSpeed > 1.2f)
                {
                    brake = BrakeTorque;
                }
                else
                {
                    motor = -ReverseTorque * -throttle;
                    brake = 0f;
                }
            }

            float rpm = 0f;
            int rpmCount = 0;
            for (int i = 0; i < _wheels.Length; i++)
            {
                WheelCollider wheel = _wheels[i].Collider;
                wheel.steerAngle = _wheels[i].Steer ? _steer : 0f;
                wheel.motorTorque = _wheels[i].Drive ? motor : 0f;
                wheel.brakeTorque = brake;
                rpm += wheel.rpm;
                rpmCount++;
            }

            if (rpmCount > 0)
                _spin += rpm / rpmCount * 6f * Time.fixedDeltaTime;

            if (_released && speed < 0.4f && _body.angularVelocity.magnitude < 0.35f)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.isKinematic = true;
                _authority = false;
                _released = false;
                _input = Vector2.zero;
                SetWheelsEnabled(false);
            }
        }

        void Update()
        {
            if (_body == null)
                return;

            if (!_authority && _remote)
            {
                float follow = 1f - Mathf.Exp(-14f * Time.deltaTime);
                transform.SetPositionAndRotation(
                    Vector3.Lerp(transform.position, _remotePos, follow),
                    Quaternion.Slerp(transform.rotation, _remoteRot, follow));
            }

            UpdateVisuals();
        }

        void UpdateVisuals()
        {
            for (int i = 0; i < _wheels.Length; i++)
            {
                Transform visual = _wheels[i].Visual;
                if (visual == null)
                    continue;

                Transform parent = visual.parent != null ? visual.parent : transform;
                Vector3 pivot = parent.TransformPoint(_wheels[i].RestCenterLocal);
                Quaternion restWorld = parent.rotation * _wheels[i].RestLocalRot;
                Quaternion steer = _wheels[i].Steer ? Quaternion.AngleAxis(_steer, transform.up) : Quaternion.identity;
                Vector3 axle = _wheels[i].Axle.sqrMagnitude > 0.001f ? _wheels[i].Axle.normalized : Vector3.right;
                Quaternion spun = steer * restWorld * Quaternion.AngleAxis(_spin, axle);
                float scale = visual.lossyScale.x;
                Vector3 center = _wheels[i].MeshCenter * scale;
                visual.SetPositionAndRotation(pivot - spun * center, spun);
            }

            if (_steering != null)
            {
                float wheelAngle = -_steer / 32f * 200f;
                _steering.localRotation = _steeringRest * Quaternion.AngleAxis(wheelAngle, _steeringAxis);
            }
        }

        void SetWheelsEnabled(bool enabled)
        {
            for (int i = 0; i < _wheels.Length; i++)
            {
                if (_wheels[i].Collider != null)
                    _wheels[i].Collider.enabled = enabled;
            }
        }

        static void Tune(WheelCollider wheel)
        {
            wheel.suspensionDistance = 0.1f;
            wheel.forceAppPointDistance = 0.02f;
            wheel.mass = 40f;
            wheel.wheelDampingRate = 2.5f;
            JointSpring spring = wheel.suspensionSpring;
            spring.spring = 95000f;
            spring.damper = 14000f;
            spring.targetPosition = 0.5f;
            wheel.suspensionSpring = spring;

            WheelFrictionCurve forward = wheel.forwardFriction;
            forward.extremumSlip = 0.35f;
            forward.extremumValue = 1.05f;
            forward.asymptoteSlip = 0.7f;
            forward.asymptoteValue = 0.6f;
            forward.stiffness = 1.4f;
            wheel.forwardFriction = forward;

            WheelFrictionCurve side = wheel.sidewaysFriction;
            side.extremumSlip = 0.25f;
            side.extremumValue = 1.05f;
            side.asymptoteSlip = 0.5f;
            side.asymptoteValue = 0.75f;
            side.stiffness = 1.55f;
            wheel.sidewaysFriction = side;
        }

        static void BuildChassis(Transform truck, float wheelFloor)
        {
            bool started = false;
            Bounds local = new Bounds(Vector3.zero, Vector3.zero);
            Renderer[] renderers = truck.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                string name = renderers[i].name;
                if (name.IndexOf("Tire", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (name.IndexOf("Collider", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                Bounds world = renderers[i].bounds;
                Vector3 ext = world.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 corner = truck.InverseTransformPoint(world.center + Vector3.Scale(ext, new Vector3(x, y, z)));
                            if (!started)
                            {
                                local = new Bounds(corner, Vector3.zero);
                                started = true;
                            }
                            else
                            {
                                local.Encapsulate(corner);
                            }
                        }
                    }
                }
            }

            if (!started)
                return;

            if (local.min.y < wheelFloor + 0.16f)
            {
                Vector3 min = local.min;
                min.y = wheelFloor + 0.16f;
                if (min.y < local.max.y - 0.2f)
                    local.SetMinMax(min, local.max);
            }

            var chassis = new GameObject("Chassis");
            chassis.transform.SetParent(truck, false);
            chassis.transform.localPosition = local.center;
            chassis.transform.localRotation = Quaternion.identity;
            var box = chassis.AddComponent<BoxCollider>();
            box.size = local.size;
            // The body shell must not steal the door ray or block the doorway.
            box.isTrigger = true;
        }

        static Vector3 NoseDirection(Transform truck, Transform steering)
        {
            Vector3 up = truck.up.sqrMagnitude > 0.001f ? truck.up.normalized : Vector3.up;
            if (steering != null)
            {
                Vector3 toCab = Vector3.ProjectOnPlane(steering.position - truck.position, up);
                if (toCab.sqrMagnitude > 0.05f)
                    return toCab.normalized;
            }

            return Vector3.ProjectOnPlane(truck.forward, up).normalized;
        }

        static void FitRearWheel(Transform visual, Transform truck)
        {
            var renderer = visual.GetComponent<Renderer>();
            if (renderer == null)
                return;

            // The bed arch is about two thirds of the modeled tire. Shrink around
            // the ground contact so the top clears the bed and the truck stays level.
            const float fit = 0.62f;
            Vector3 up = truck.up.sqrMagnitude > 0.001f ? truck.up.normalized : Vector3.up;
            Bounds bounds = renderer.bounds;
            float radius = Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z));
            Vector3 center = bounds.center;
            Vector3 bottom = center - up * radius;
            Vector3 originToCenter = center - visual.position;

            visual.localScale *= fit;
            Vector3 scaledCenter = visual.position + originToCenter * fit;
            Vector3 desiredCenter = bottom + up * (radius * fit);
            visual.position += desiredCenter - scaledCenter;
        }

        static Vector3 WheelHub(Transform visual, out float radius)
        {
            var renderer = visual.GetComponent<Renderer>();
            if (renderer == null)
            {
                radius = 0.4f;
                return visual.position;
            }

            Vector3 extents = renderer.bounds.extents;
            radius = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z));
            return renderer.bounds.center;
        }

        static Vector3 MeshCenter(Transform visual)
        {
            var filter = visual.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return Vector3.zero;
            return filter.sharedMesh.bounds.center;
        }

        static Vector3 ThinAxis(Transform visual)
        {
            var filter = visual.GetComponent<MeshFilter>();
            Vector3 size = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds.size : Vector3.one;
            Vector3 local = Vector3.right;
            float min = size.x;
            if (size.y < min)
            {
                min = size.y;
                local = Vector3.up;
            }

            if (size.z < min)
                local = Vector3.forward;
            return local;
        }

        static Vector3 DominantAxis(Vector3 local)
        {
            float ax = Mathf.Abs(local.x);
            float ay = Mathf.Abs(local.y);
            float az = Mathf.Abs(local.z);
            if (ax >= ay && ax >= az)
                return new Vector3(Mathf.Sign(local.x == 0f ? 1f : local.x), 0f, 0f);
            if (ay >= ax && ay >= az)
                return new Vector3(0f, Mathf.Sign(local.y == 0f ? 1f : local.y), 0f);
            return new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? 1f : local.z));
        }

        static void StripChildBodies(Transform truck)
        {
            Rigidbody[] bodies = truck.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].transform != truck)
                    UnityEngine.Object.Destroy(bodies[i]);
            }
        }
    }
}
