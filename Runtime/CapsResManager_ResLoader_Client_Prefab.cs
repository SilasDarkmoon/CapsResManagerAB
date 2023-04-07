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
            public class AssetInfo_Prefab : AssetInfo_Base
            {
                protected internal GameObject Prefab;
                protected internal CapsPrefabHolder Holder;

                public override bool CheckRefAlive()
                {
                    return false;
                }

                public override Object Load(Type type)
                {
                    if (!Prefab)
                    {
                        Holder = null;
                        if (ManiItem != null && DepBundles.Count > 0)
                        {
                            var bi = DepBundles[DepBundles.Count - 1];
                            if (bi != null && bi.Bundle != null)
                            {
                                var path = ConcatAssetPath();
                                Prefab = bi.Bundle.LoadAsset<GameObject>(path);
                            }
                        }
                    }
                    if (Prefab)
                    {
                        if (!Holder)
                        {
                            Holder = Prefab.AddComponent<CapsPrefabHolder>();
                            Holder.AssetInfo = this;
                        }
                    }
                    return Prefab;
                }
                public override IEnumerator LoadAsync(CoroutineTasks.CoroutineWork req, Type type)
                {
                    var holdhandle = Hold();
                    try
                    {
                        if (!Prefab)
                        {
                            if (ManiItem != null && DepBundles.Count > 0)
                            {
                                var bi = DepBundles[DepBundles.Count - 1];
                                if (bi != null && bi.Bundle != null)
                                {
                                    var path = ConcatAssetPath();
                                    while (AsyncWorkTimer.Check()) yield return null;

                                    AssetBundleRequest raw = null;
                                    try
                                    {
                                        raw = bi.Bundle.LoadAssetAsync<GameObject>(path);
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
                                            req.Progress = (long)(req.Total * raw.progress);
                                        }
                                        var asset = raw.asset as GameObject;

                                        if (!Prefab)
                                        {
                                            Holder = null;
                                            Prefab = asset;
                                        }
                                    }
                                }
                            }
                        }
                        if (Prefab)
                        {
                            if (!Holder)
                            {
                                Holder = Prefab.AddComponent<CapsPrefabHolder>();
                                Holder.AssetInfo = this;
                            }
                        }
                        req.Result = Prefab;
                    }
                    finally
                    {
                        GC.KeepAlive(holdhandle);
                    }
                }
                public override void Unload()
                {
                    if (Holder)
                    {
#if UNITY_EDITOR
                        Object.DestroyImmediate(Holder);
#else
                        Object.Destroy(Holder);
#endif
                        Holder = null;
                    }
                    base.Unload();
                }
            }

            public class TypedResLoader_Prefab : TypedResLoader_Normal
            {
                public override int ResItemType { get { return (int)CapsResManifestItemType.Prefab; } }

                protected override AssetInfo_Base CreateAssetInfoRaw(CapsResManifestItem item)
                {
                    return new AssetInfo_Prefab() { ManiItem = item };
                }
            }
            public static TypedResLoader_Prefab Instance_TypedResLoader_Prefab = new TypedResLoader_Prefab();
        }
    }
}