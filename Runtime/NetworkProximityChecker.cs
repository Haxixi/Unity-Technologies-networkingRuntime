#if ENABLE_UNET
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Networking
{
    [AddComponentMenu("Network/NetworkProximityChecker")]
    [RequireComponent(typeof(NetworkIdentity))]
    public class NetworkProximityChecker : NetworkBehaviour
    {
        public enum CheckMethod
        {
            Physics3D,
            Physics2D
        };

        public int visRange = 10;
        public float visUpdateInterval = 1.0f; // in seconds
        public CheckMethod checkMethod = CheckMethod.Physics3D;

        public bool forceHidden = false;

        float m_VisUpdateTime;

        void Update()
        {
            if (!NetworkServer.active)
                return;

            if (Time.time - m_VisUpdateTime > visUpdateInterval)
            {
                GetComponent<NetworkIdentity>().RebuildObservers(false);
                m_VisUpdateTime = Time.time;
            }
        }

        // called when a new player enters
        public override bool OnCheckObserver(NetworkConnection newObserver)
        {
            if (forceHidden)
                return false;

            // this cant use newObserver.playerControllers[0]. must iterate to find a valid player.
            GameObject player = null;
            foreach (var p in newObserver.playerControllers)
            {
                if (p != null && p.gameObject != null)
                {
                    player = p.gameObject;
                    break;
                }
            }
            if (player == null)
                return false;

            var pos = player.transform.position;
            return (pos - transform.position).magnitude < visRange;
        }

        public override bool OnRebuildObservers(HashSet<NetworkConnection> observers, bool initial)
        {
            if (forceHidden)
            {
                // ensure player can still see themself
                var uv = GetComponent<NetworkIdentity>();
                if (uv.connectionToClient != null)
                {
                    observers.Add(uv.connectionToClient);
                }
                return true;
            }

            // find players within range
            switch (checkMethod)
            {
                case CheckMethod.Physics3D:
                {
                    var hits = Physics.OverlapSphere(transform.position, visRange);
                    foreach (var hit in hits)
                    {
                        // (if an object has a connectionToClient, it is a player)
                        var uv = hit.GetComponent<NetworkIdentity>();
                        if (uv != null && uv.connectionToClient != null)
                        {
                            observers.Add(uv.connectionToClient);
                        }
                    }
                    return true;
                }

                case CheckMethod.Physics2D:
                {
                    var hits = Physics2D.OverlapCircleAll(transform.position, visRange);
                    foreach (var hit in hits)
                    {
                        // (if an object has a connectionToClient, it is a player)
                        var uv = hit.GetComponent<NetworkIdentity>();
                        if (uv != null && uv.connectionToClient != null)
                        {
                            observers.Add(uv.connectionToClient);
                        }
                    }
                    return true;
                }
            }
            return false;
        }

        // called hiding and showing objects on the host
        public override void OnSetLocalVisibility(bool vis)
        {
            SetVis(gameObject, vis);
        }

        static void SetVis(GameObject go, bool vis)
        {
            foreach (var r in go.GetComponents<Renderer>())
            {
                r.enabled = vis;
            }
            for (int i = 0; i < go.transform.childCount; i++)
            {
                var t = go.transform.GetChild(i);
                SetVis(t.gameObject, vis);
            }
        }
    }
}
#endif
