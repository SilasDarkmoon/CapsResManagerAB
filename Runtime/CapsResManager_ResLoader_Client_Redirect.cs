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
            public class AssetInfo_Redirect : IAssetInfo
            {
                public CapsResManifestItem ManiItem;
                public IAssetInfo Real;

                private void UnloadRaw()
                {
                    if (ManiItem.Attached != null)
                    {
                        ManiItem.Attached = null;
                    }
                }
                public Object Load(Type type)
                {
                    if (Real != null)
                    {
                        return Real.Load(type);
                    }
                    return null;
                }
                public IEnumerator LoadAsync(CoroutineTasks.CoroutineWork req, Type type)
                {
                    if (Real != null)
                    {
                        return Real.LoadAsync(req, type);
                    }
                    else
                    {
                        return CoroutineRunner.GetEmptyEnumerator();
                    }
                }
                public void Unload()
                {
                    if (Real != null)
                    {
                        Real.Unload();
                    }
                    UnloadRaw();
                }
                public void AddRef()
                {
                    if (Real != null)
                    {
                        Real.AddRef();
                    }
                }
                public bool Release()
                {
                    bool alive = false;
                    if (Real != null)
                    {
                        alive = Real.Release();
                    }
                    if (!alive)
                    {
                        UnloadRaw();
                    }
                    return alive;
                }
                public bool CheckAlive()
                {
                    bool alive = false;
                    if (Real != null)
                    {
                        alive = Real.CheckAlive();
                    }
                    if (!alive)
                    {
                        UnloadRaw();
                    }
                    return alive;
                }
                public object Hold()
                {
                    if (Real != null)
                    {
                        return Real.Hold();
                    }
                    else
                    {
                        return null;
                    }
                }
                public void Preload()
                {
                    if (Real != null)
                    {
                        Real.Preload();
                    }
                }
                public IEnumerator PreloadAsync()
                {
                    if (Real != null)
                    {
                        return Real.PreloadAsync();
                    }
                    else
                    {
                        return CoroutineRunner.GetEmptyEnumerator();
                    }
                }
            }

            public class TypedResLoader_Redirect : TypedResLoader_Base
            {
                public override int ResItemType { get { return (int)CapsResManifestItemType.Redirect; } }

                public override IAssetInfo CreateAssetInfo(CapsResManifestItem item)
                {
                    var ai = item.Attached as IAssetInfo;
                    if (ai == null)
                    {
                        AssetInfo_Redirect ain = new AssetInfo_Redirect() { ManiItem = item };
                        item.Attached = ain;
                        ai = ain;
                        var realitem = item.Ref;
                        if (realitem != null)
                        {
                            var air = ClientResLoader.CreateAssetInfo(realitem);
                            if (air != null)
                            {
                                ain.Real = air;
                            }
                        }
                    }
                    return ai;
                }
            }
            public static TypedResLoader_Redirect Instance_TypedResLoader_Redirect = new TypedResLoader_Redirect();
        }
    }
}