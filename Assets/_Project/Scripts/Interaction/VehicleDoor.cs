using UnityEngine;

namespace GraveRobbers.Interaction
{
    /// <summary>
    /// Swings a door around the pivot already stored on its transform, local Y only.
    /// </summary>
    public class VehicleDoor : MonoBehaviour
    {
        const float MoveDuration = 0.32f;

        Transform _hinge;
        Quaternion _closed = Quaternion.identity;
        float _openAngle;
        float _angle;
        float _from;
        float _to;
        float _moveTime = MoveDuration;
        bool _open;

        public int Index { get; set; }
        public bool IsOpen => _open;
        public Vector3 InteractionPoint { get; private set; }

        public void Configure(Transform hinge, float openAngle)
        {
            _hinge = hinge;
            _closed = hinge.localRotation;
            _openAngle = openAngle;
            _angle = 0f;
            _from = 0f;
            _to = 0f;
            _moveTime = MoveDuration;
            _open = false;
            var renderer = GetComponent<Renderer>();
            InteractionPoint = renderer != null ? renderer.bounds.center : transform.position;
        }

        public void SetOpen(bool open)
        {
            _open = open;
            _from = _angle;
            _to = open ? _openAngle : 0f;
            _moveTime = 0f;
        }

        void Update()
        {
            if (_hinge == null)
                return;

            if (_moveTime < MoveDuration)
            {
                _moveTime += Time.deltaTime;
                float u = Mathf.Clamp01(_moveTime / MoveDuration);
                float eased = 1f - (1f - u) * (1f - u) * (1f - u);
                _angle = Mathf.Lerp(_from, _to, eased);
                _hinge.localRotation = _closed * Quaternion.Euler(0f, _angle, 0f);
            }

            var renderer = GetComponent<Renderer>();
            if (renderer != null)
                InteractionPoint = renderer.bounds.center;
        }
    }
}
