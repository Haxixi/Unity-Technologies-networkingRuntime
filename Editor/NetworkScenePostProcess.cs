#if ENABLE_UNET
using System;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityEditor
{
    public class NetworkScenePostProcess : MonoBehaviour
    {
        [PostProcessScene]
        public static void OnPostProcessScene()
        {
            int nextSceneId = 1;
            foreach (NetworkIdentity uv in FindObjectsOfType<NetworkIdentity>())
            {
                // if we had a [ConflictComponent] attribute that would be better than this check.
                // also there is no context about which scene this is in.
                if (uv.GetComponent<NetworkManager>() != null)
                {
                    Debug.LogError("NetworkManager has a NetworkIdentity component. This will cause the NetworkManager object to be disabled, so it is not recommended.");
                }
                if (uv.isClient || uv.isServer)
                    continue;

                uv.gameObject.SetActive(false);
                uv.ForceSceneId(nextSceneId++);
            }
        }
    }
}
#endif //ENABLE_UNET
