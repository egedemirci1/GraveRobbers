using Unity.Netcode.Components;
using UnityEngine;

namespace GraveRobbers.Networking
{
    /// <summary>
    /// Owner-authoritative transform sync (client writes, others interpolate).
    /// </summary>
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CanCommitToTransform = IsOwner;
        }
    }
}
