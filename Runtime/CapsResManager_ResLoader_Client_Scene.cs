using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Capstones.UnityEngineEx
{
    public static partial class ResManagerAB
    {
        public partial class ClientResLoader
        {
            public class AssetInfo_Scene : AssetInfo_Base
            {
                protected internal bool IsLoading = false;
                protected internal LinkedList<GameObject> SceneAliveIndicators = new LinkedList<GameObject>();

                public override bool CheckRefAlive()
                {
                    if (IsLoading)
                    {
                        return true;
                    }
                    bool alive = false;
                    var node = SceneAliveIndicators.First;
                    while (node != null)
                    {
                        var next = node.Next;
                        if (node.Value)
                        {
                            alive = true;
                        }
                        else
                        {
                            SceneAliveIndicators.Remove(node);
                        }
                        node = next;
                    }
                    return alive;
                }

                public override Object Load(Type type)
                {
                    if (ManiItem != null)
                    {
                        bool additive = type != null;
                        var sceneName = System.IO.Path.GetFileNameWithoutExtension(ManiItem.Node.PPath);
                        IsLoading = true;
                        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
                        UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName, additive ? UnityEngine.SceneManagement.LoadSceneMode.Additive : UnityEngine.SceneManagement.LoadSceneMode.Single);
                    }
                    return null;
                }
                public override IEnumerator LoadAsync(CoroutineTasks.CoroutineWork req, Type type)
                {
                    var holdhandle = Hold();
                    try
                    {
                        if (ManiItem != null)
                        {
                            bool additive = type != null;
                            var sceneName = System.IO.Path.GetFileNameWithoutExtension(ManiItem.Node.PPath);
                            IsLoading = true;
                            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
                            AsyncOperation raw = null;
                            try
                            {
                                raw = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneName, additive ? UnityEngine.SceneManagement.LoadSceneMode.Additive : UnityEngine.SceneManagement.LoadSceneMode.Single);
                            }
                            catch (Exception e)
                            {
                                PlatDependant.LogError(e);
                            }
                            if (raw != null)
                            {
                                while (!raw.isDone)
                                {
                                    yield return null;
                                    req.Progress = (long)(req.Total * raw.progress * 1.11f);
                                }
                            }
                        }
                    }
                    finally
                    {
                        GC.KeepAlive(holdhandle);
                    }
                }

                private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
                {
                    var sceneName = System.IO.Path.GetFileNameWithoutExtension(ManiItem.Node.PPath);
                    if (scene.name == sceneName)
                    {
                        var go = new GameObject("Scene Alive Indicator: " + sceneName);
                        SceneAliveIndicators.AddLast(go);
                    }
                    IsLoading = false;
                    UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
                }
            }

            public class TypedResLoader_Scene : TypedResLoader_Normal
            {
                public override int ResItemType { get { return (int)CapsResManifestItemType.Scene; } }

                protected override AssetInfo_Base CreateAssetInfoRaw(CapsResManifestItem item)
                {
                    return new AssetInfo_Scene() { ManiItem = item };
                }
            }
            public static TypedResLoader_Scene Instance_TypedResLoader_Scene = new TypedResLoader_Scene();
        }
    }
}