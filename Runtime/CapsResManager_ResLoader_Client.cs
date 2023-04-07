using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Object = UnityEngine.Object;

namespace Capstones.UnityEngineEx
{
    public static partial class ResManagerAB
    {
        public partial class ClientResLoader : ResManager.IResLoader
        {
            public ClientResLoader()
            {
                bool useclientloader = false;
#if !UNITY_EDITOR || FORCE_USE_CLIENT_RESLOADER
                useclientloader = true;
#if UNITY_EDITOR
                if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    useclientloader = false;
                }
#endif
#endif
                if (useclientloader)
                {
                    ResManager.ResLoader = this;
                }
            }
            //public void OnEnable() { }
            public void BeforeLoadFirstScene() { }
            public void AfterLoadFirstScene() { }

            public static Dictionary<string, AssetBundleManifest> UnityManifests = new Dictionary<string, AssetBundleManifest>();
            public static void DiscardUnityManifests()
            {
                foreach (var kvpMani in UnityManifests)
                {
                    Object.Destroy(kvpMani.Value);
                }
                UnityManifests.Clear();
            }

            public static CapsResManifest CollapsedManifest = new CapsResManifest();
            private static void ParseManifest(string mod, string[] manibundlenames)
            {
                mod = mod ?? "";
                var lmod = mod.ToLower();
                var umanipath = "res";
                if (mod != "")
                {
                    umanipath = "mod/" + mod + "/" + mod;
                }
                var mbinfo = LoadAssetBundle(umanipath, false, true);
                if (mbinfo != null)
                {
                    if (mbinfo.Bundle != null)
                    {
                        var umanis = mbinfo.Bundle.LoadAllAssets<AssetBundleManifest>();
                        if (umanis != null && umanis.Length > 0)
                        {
                            UnityManifests[mod] = Object.Instantiate<AssetBundleManifest>(umanis[0]);
                        }
                    }
                    mbinfo.AddRef();
                    mbinfo.Release();
                }

                var pre = "mani/m-" + lmod + "-";
                for (int j = 0; j < manibundlenames.Length; ++j)
                {
                    var bundle = manibundlenames[j];
                    if (bundle.StartsWith(pre) && bundle.EndsWith(".m.ab"))
                    {
                        var binfo = LoadAssetBundle(bundle, false, true);
                        if (binfo != null)
                        {
                            if (binfo.Bundle != null)
                            {
                                var dmanis = binfo.Bundle.LoadAllAssets<CapsResOnDiskManifest>();
                                if (dmanis != null)
                                {
                                    for (int k = 0; k < dmanis.Length; ++k)
                                    {
                                        var mani = CapsResManifest.Load(dmanis[k]);
                                        CollapsedManifest.MergeManifest(mani);
                                    }
                                }
                            }
                            binfo.AddRef();
                            binfo.Release();
                        }
                    }
                }
            }
            public static void ParseManifest()
            {
                DiscardUnityManifests();
                CollapsedManifest.DiscardAllNodes();

                var manibundlenames = GetAllResManiBundleNames();

                // the mod ""
                ParseManifest("", manibundlenames);

                var dflags = DistributeFlags.GetValidDistributeFlags();
                for (int i = 0; i < dflags.Length; ++i)
                {
                    ParseManifest(dflags[i], manibundlenames);
                }

                CollapsedManifest.CollapseManifest(dflags);
                CollapsedManifest.TrimExcess();
                ParseBuiltinScenes();
            }

            private static Dictionary<string, int> _BuiltinScenes = new Dictionary<string, int>();
            private static void ParseBuiltinScenes()
            {
                _BuiltinScenes.Clear();
                System.IO.StreamReader sr = null;
                System.IO.Stream stream = null;
                try
                {
                    stream = LoadFileInStreaming("res/builtin-scenes.txt");
                    if (stream != null)
                    {
                        sr = new System.IO.StreamReader(stream);
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var items = line.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                            if (items != null && items.Length > 0)
                            {
                                var scenepath = items[0];
                                int index = -1;
                                if (items.Length >= 2)
                                {
                                    if (!int.TryParse(items[1], out index))
                                    {
                                        index = -1;
                                    }
                                }
                                _BuiltinScenes[scenepath] = index;
                            }
                        }
                    }
                }
                finally
                {
                    if (sr != null)
                    {
                        sr.Dispose();
                    }
                    if (stream != null)
                    {
                        stream.Dispose();
                    }
                }
            }
            public static bool IsBuiltinScene(string path, out int index)
            {
                return _BuiltinScenes.TryGetValue(path, out index);
            }

            public interface IAssetInfo
            {
                void Preload();
                IEnumerator PreloadAsync();
                Object Load(Type type);
                IEnumerator LoadAsync(CoroutineTasks.CoroutineWork req, Type type);
                void Unload();
                object Hold();
                void AddRef();
                bool Release();
                bool CheckAlive();
            }
            public abstract class AssetInfo_Base : IAssetInfo
            {
                public CapsResManifestItem ManiItem;
                public List<AssetBundleInfo> DepBundles = new List<AssetBundleInfo>();

                protected int RefCnt = 0;
                protected WeakReference HoldHandle;

                public string ConcatAssetPath()
                {
                    var node = ManiItem.Node;
                    System.Text.StringBuilder sbpath = new System.Text.StringBuilder();
                    while (node.Parent != null)
                    {
                        if (sbpath.Length > 0)
                        {
                            sbpath.Insert(0, '/');
                        }
                        sbpath.Insert(0, node.PPath.ToLower());
                        node = node.Parent;
                    }
                    var path = sbpath.ToString();
                    return path;
                }
                public virtual string FormatBundleName()
                {
                    return FormatBundleName(ManiItem);
                }
                public static string FormatBundleName(CapsResManifestItem item)
                {
                    var node = item.Node;
                    var depth = node.GetDepth();
                    string[] parts = new string[depth];
                    for (int i = depth - 1; i >= 0; --i)
                    {
                        parts[i] = node.PPath;
                        node = node.Parent;
                    }

                    var mod = item.Manifest.MFlag;
                    var dist = item.Manifest.DFlag;
                    var rootdepth = 2; // Assets/CapsRes/
                    if (depth > 2 && parts[1] == "Mods")
                    {
                        rootdepth += 2; // Assets/Mods/XXX/CapsRes/
                    }
                    else if (depth > 1 && parts[0] == "Packages")
                    {
                        rootdepth += 1; // Packages/xx.xx.xx/CapsRes/
                    }
                    if (!string.IsNullOrEmpty(dist))
                    {
                        rootdepth += 2; // .../dist/XXX/
                    }

                    System.Text.StringBuilder sbbundle = new System.Text.StringBuilder();
                    sbbundle.Append("m-");
                    sbbundle.Append((mod ?? "").ToLower());
                    sbbundle.Append("-d-");
                    sbbundle.Append((dist ?? "").ToLower());
                    sbbundle.Append("-");
                    for (int i = rootdepth; i < depth - 1; ++i)
                    {
                        if (i > rootdepth)
                        {
                            sbbundle.Append("-");
                        }
                        sbbundle.Append(parts[i].ToLower());
                    }
                    var filename = item.Node.PPath;
                    if (filename.EndsWith(".unity"))
                    {
                        var sceneName = parts[depth - 1];
                        sceneName = sceneName.Substring(0, sceneName.Length - ".unity".Length);
                        sbbundle.Append("-");
                        sbbundle.Append(sceneName.ToLower());
                        sbbundle.Append(".s");
                    }
                    else if (filename.EndsWith(".prefab"))
                    {
                        sbbundle.Append(".o");
                    }
                    sbbundle.Append(".ab");
                    return sbbundle.ToString();
                }

                public abstract Object Load(Type type);
                public abstract IEnumerator LoadAsync(CoroutineTasks.CoroutineWork req, Type type);
                public abstract bool CheckRefAlive();
                public virtual void Unload()
                {
                    if (ManiItem.Attached != null)
                    {
                        foreach (var bundle in DepBundles)
                        {
                            bundle.Release();
                        }

                        ManiItem.Attached = null;
                    }
                }
                public object Hold()
                {
                    var handle = HoldHandle.GetWeakReference<object>();
                    if (handle == null)
                    {
                        handle = new object();
                        HoldHandle = new WeakReference(handle);
                    }
                    return handle;
                }
                public void AddRef()
                {
                    ++RefCnt;
                }
                public bool Release()
                {
                    --RefCnt;
                    return CheckAlive();
                }
                public bool CheckAlive()
                {
                    if (HoldHandle != null)
                    {
                        if (HoldHandle.GetWeakReference<object>() == null)
                        {
                            HoldHandle = null;
                        }
                        else
                        {
                            return true;
                        }
                    }
                    if (RefCnt > 0 || CheckRefAlive())
                    {
                        return true;
                    }
                    Unload();
                    return false;
                }

                protected bool _PreloadReady = false;
                public virtual void Preload()
                {
                    if (_PreloadReady)
                    {
                        for (int i = 0; i < DepBundles.Count; ++i)
                        {
                            var bi = DepBundles[i];
                            if (bi == null || bi.Bundle == null)
                            {
                                _PreloadReady = false;
                                break;
                            }
                        }
                    }
                    if (_PreloadReady)
                    {
                        return;
                    }
                    var oldbundles = new List<AssetBundleInfo>(DepBundles);
                    DepBundles.Clear();

                    var opmod = ManiItem.Manifest.MFlag;
                    var mod = opmod;
                    if (ManiItem.Manifest.InMain)
                    {
                        mod = "";
                    }

                    string bundle = ManiItem.BRef;
                    if (string.IsNullOrEmpty(bundle))
                    {
                        bundle = FormatBundleName();
                    }

                    var cabi = LoadAssetBundleEx(mod, bundle, true);
                    if (cabi != null)
                    {
                        AssetBundleManifest umani;
                        if (UnityManifests.TryGetValue(mod, out umani) && umani)
                        {
                            var deps = umani.GetAllDependencies(bundle);
                            if (deps != null)
                            {
                                for (int i = 0; i < deps.Length; ++i)
                                {
                                    var dep = deps[i];
                                    var bi = LoadAssetBundleEx(mod, dep, false);
                                    if (bi != null)
                                    {
                                        bi.AddRef();
                                        DepBundles.Add(bi);
                                    }
                                }
                            }
                        }

                        cabi.AddRef();
                        DepBundles.Add(cabi);
                    }

                    for (int i = 0; i < oldbundles.Count; ++i)
                    {
                        var bi = oldbundles[i];
                        if (bi != null)
                        {
                            bi.Release();
                        }
                    }
                    _PreloadReady = true;
                }
                public virtual IEnumerator PreloadAsync()
                {
                    if (_PreloadReady)
                    {
                        for (int i = 0; i < DepBundles.Count; ++i)
                        {
                            var bi = DepBundles[i];
                            if (bi == null || bi.Bundle == null)
                            {
                                _PreloadReady = false;
                                break;
                            }
                        }
                        if (_PreloadReady)
                        {
                            yield break;
                        }
                    }

                    List<AssetBundleInfo> bundles = new List<AssetBundleInfo>();
                    try
                    {
                        var opmod = ManiItem.Manifest.MFlag;
                        var mod = opmod;
                        if (ManiItem.Manifest.InMain)
                        {
                            mod = "";
                        }

                        string bundle = ManiItem.BRef;
                        if (string.IsNullOrEmpty(bundle))
                        {
                            bundle = FormatBundleName();
                        }

                        var cabi = LoadAssetBundleExAsync(mod, bundle, true);
                        if (cabi != null)
                        {
                            cabi.AddRef();
                            bundles.Add(cabi);
                            if (_PreloadReady) { yield break; }

                            AssetBundleManifest umani;
                            if (UnityManifests.TryGetValue(mod, out umani) && umani)
                            {
                                var deps = umani.GetAllDependencies(bundle);
                                if (deps != null)
                                {
                                    for (int i = 0; i < deps.Length; ++i)
                                    {
                                        var dep = deps[i];
                                        var bi = LoadAssetBundleExAsync(mod, dep, false);
                                        if (bi != null)
                                        {
                                            bi.AddRef();
                                            bundles.Insert(bundles.Count - 1, bi);
                                            if (_PreloadReady) { yield break; }
                                        }
                                    }
                                }
                            }

                            while (true)
                            {
                                bool allloaded = true;
                                for (int i = 0; i < bundles.Count; ++i)
                                {
                                    var bi = bundles[i];
                                    if (bi.AsyncLoading != null)
                                    {
                                        if (bi.AsyncLoading.isDone)
                                        {
                                            bi.FinishAsyncLoading();
                                        }
                                        else
                                        {
                                            allloaded = false;
                                            yield return null;
                                            break;
                                        }
                                    }
                                }

                                if (_PreloadReady) { yield break; }
                                if (allloaded)
                                {
                                    break;
                                }
                            }

                            for (int i = 0; i < DepBundles.Count; ++i)
                            {
                                var bi = DepBundles[i];
                                if (bi != null)
                                {
                                    bi.Release();
                                }
                            }
                            DepBundles.Clear();
                            DepBundles.AddRange(bundles);
                            bundles.Clear();
                            _PreloadReady = true;
                        }
                    }
                    finally
                    {
                        if (bundles != null)
                        {
                            for (int i = 0; i < bundles.Count; ++i)
                            {
                                bundles[i].Release();
                            }
                        }
                    }
                }
            }

            public interface ITypedResLoader
            {
                IAssetInfo CreateAssetInfo(CapsResManifestItem item);
            }
            public abstract class TypedResLoader_Base : ITypedResLoader
            {
                public abstract int ResItemType { get; }
                public abstract IAssetInfo CreateAssetInfo(CapsResManifestItem item);

                public TypedResLoader_Base()
                {
                    TypedResLoaders = TypedResLoaders ?? new Dictionary<int, ITypedResLoader>();
                    TypedResLoaders[ResItemType] = this;
                }
            }
            public static Dictionary<int, ITypedResLoader> TypedResLoaders;

            public static IAssetInfo CreateAssetInfo(CapsResManifestItem item)
            {
                var restype = item.Type;
                ITypedResLoader loader;
                if (TypedResLoaders != null && TypedResLoaders.TryGetValue(restype, out loader) && loader != null)
                {
                    return loader.CreateAssetInfo(item);
                }
                else
                {
                    return Instance_TypedResLoader_Normal.CreateAssetInfo(item);
                }
            }

            private static Object LoadAsset(CapsResManifestItem item, Type type)
            {
                var ai = CreateAssetInfo(item);
                if (ai != null)
                {
                    ai.Preload();
                    return ai.Load(type);
                }
                else
                {
                    return null;
                }
            }
            public static Object LoadAsset(string asset, Type type)
            {
#if COMPATIBLE_RESMANAGER_V1
                asset = CompatibleAssetName(asset);
#endif
                CapsResManifestNode node;
                if (CollapsedManifest.TryGetItem(asset, out node) && node.Item != null)
                {
                    return LoadAsset(node.Item, type);
                }
                return null;
            }
            public static void LoadLevel(string name, bool additive)
            {
#if COMPATIBLE_RESMANAGER_V1
                name = CompatibleAssetName(name);
#endif
                CapsResManifestNode node;
                if (CollapsedManifest.TryGetItem(name, out node) && node.Item != null)
                {
                    var sceneitem = node.Item;
                    while (sceneitem.Ref != null)
                    {
                        sceneitem = sceneitem.Ref;
                    }
                    var scenepath = sceneitem.Node.GetFullPath();
                    int sceneindex;
                    if (IsBuiltinScene(scenepath, out sceneindex))
                    {
                        if (sceneindex >= 0)
                        {
                            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneindex, additive ? UnityEngine.SceneManagement.LoadSceneMode.Additive : UnityEngine.SceneManagement.LoadSceneMode.Single);
                        }
                        else
                        {
                            UnityEngine.SceneManagement.SceneManager.LoadScene(System.IO.Path.GetFileNameWithoutExtension(node.PPath), additive ? UnityEngine.SceneManagement.LoadSceneMode.Additive : UnityEngine.SceneManagement.LoadSceneMode.Single);
                        }
                    }
                    else
                    {
                        LoadAsset(node.Item, additive ? typeof(object) : null);
                    }
                }
                else
                {
                    int sceneindex;
                    if (IsBuiltinScene(name, out sceneindex))
                    {
                        if (sceneindex >= 0)
                        {
                            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneindex, additive ? UnityEngine.SceneManagement.LoadSceneMode.Additive : UnityEngine.SceneManagement.LoadSceneMode.Single);
                        }
                        else
                        {
                            UnityEngine.SceneManagement.SceneManager.LoadScene(System.IO.Path.GetFileNameWithoutExtension(name), additive ? UnityEngine.SceneManagement.LoadSceneMode.Additive : UnityEngine.SceneManagement.LoadSceneMode.Single);
                        }
                    }
                }
            }

            private static IEnumerator LoadAssetAsyncWork(CoroutineTasks.CoroutineWorkSingle req, string asset, Type type)
            {
                req.Total = 10000;
                while (AsyncWorkTimer.Check()) yield return null;
                req.Progress = 50;
                while (ResManager.IsCollectingGarbage) yield return null;
                req.Progress = 100;

                ResManager.DelayGarbageCollectTo(System.Environment.TickCount + 10000);

#if COMPATIBLE_RESMANAGER_V1
                asset = CompatibleAssetName(asset);
#endif
                CapsResManifestNode node;
                if (CollapsedManifest.TryGetItem(asset, out node) && node.Item != null)
                {
                    var sceneitem = node.Item;
                    while (sceneitem.Ref != null)
                    {
                        sceneitem = sceneitem.Ref;
                    }
                    var scenepath = sceneitem.Node.GetFullPath();
                    int sceneindex;
                    if (IsBuiltinScene(scenepath, out sceneindex))
                    {
                        AsyncOperation op;
                        if (sceneindex >= 0)
                        {
                            op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneindex, type == null ? UnityEngine.SceneManagement.LoadSceneMode.Single : UnityEngine.SceneManagement.LoadSceneMode.Additive);
                        }
                        else
                        {
                            op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(System.IO.Path.GetFileNameWithoutExtension(node.PPath), type == null ? UnityEngine.SceneManagement.LoadSceneMode.Single : UnityEngine.SceneManagement.LoadSceneMode.Additive);
                        }

                        while (!op.isDone)
                        {
                            req.Progress = 100 + (long)((10000 - 100) * op.progress);
                            yield return null;
                        }

                        ResManager.DelayGarbageCollectTo(int.MinValue);
                        //req.Result = ???
                        yield break;
                    }
                    var item = node.Item;
                    var ai = CreateAssetInfo(item);
                    ResManager.DelayGarbageCollectTo(System.Environment.TickCount + 10000);
                    var prework = ai.PreloadAsync();
                    if (prework != null)
                    {
                        while (prework.MoveNext())
                        {
                            yield return prework.Current;
                        }
                    }
                    req.Progress = 150;
                    ResManager.DelayGarbageCollectTo(System.Environment.TickCount + 10000);
                    var work = new CoroutineTasks.CoroutineWorkSingle();
                    work.SetWork(ai.LoadAsync(work, type));
                    while (work.MoveNext())
                    {
                        req.Progress = 150 + (long)((10000 - 150) * ((double)work.Progress / (double)work.Total));
                        yield return null;
                    }
                    req.Result = work.Result;
                }
                else
                {
                    int sceneindex;
                    if (IsBuiltinScene(asset, out sceneindex))
                    {
                        AsyncOperation op;
                        if (sceneindex >= 0)
                        {
                            op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(sceneindex, type == null ? UnityEngine.SceneManagement.LoadSceneMode.Single : UnityEngine.SceneManagement.LoadSceneMode.Additive);
                        }
                        else
                        {
                            op = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(System.IO.Path.GetFileNameWithoutExtension(asset), type == null ? UnityEngine.SceneManagement.LoadSceneMode.Single : UnityEngine.SceneManagement.LoadSceneMode.Additive);
                        }

                        while (!op.isDone)
                        {
                            req.Progress = 100 + (long)((10000 - 100) * op.progress);
                            yield return null;
                        }

                        ResManager.DelayGarbageCollectTo(int.MinValue);
                        //req.Result = ???
                        yield break;
                    }
                }

                ResManager.DelayGarbageCollectTo(int.MinValue);
            }
            public static CoroutineTasks.CoroutineWork LoadAssetAsync(string asset, Type type)
            {
                var work = new CoroutineTasks.CoroutineMonitorRaw();
                work.SetWork(LoadAssetAsyncWork(work, asset, type));
                work.TryStart();
                return work;
            }
            public static CoroutineTasks.CoroutineWork LoadLevelAsync(string name, bool additive)
            {
                ResManager.StartGarbageCollectLite();
                var work = LoadAssetAsync(name, additive ? typeof(object) : null);
                work.OnDone += ResManager.StartGarbageCollectLite;
                return work;
            }

            public Object LoadRes(string asset, Type type)
            {
                return LoadAsset(asset, type);
            }
            public void LoadScene(string name, bool additive)
            {
                LoadLevel(name, additive);
            }
            public CoroutineTasks.CoroutineWork LoadResAsync(string asset, Type type)
            {
                return LoadAssetAsync(asset, type);
            }
            public CoroutineTasks.CoroutineWork LoadSceneAsync(string name, bool additive)
            {
                return LoadLevelAsync(name, additive);
            }
            public int Order { get { return ResManager.LifetimeOrders.ResLoader; } }
            public void Prepare() { }
            public void Init()
            {
                ParseManifest();
            }
            public void Cleanup()
            {
                UnloadAllRes(true);
                CollapsedManifest.DiscardAllNodes();
                DiscardUnityManifests();
                ResManager.StartGarbageCollectDeep();
            }
            public void UnloadUnusedRes()
            {
                if (CollapsedManifest != null)
                {
                    foreach (var item in CollapsedManifest.Items)
                    {
                        var ai = item.Attached as IAssetInfo;
                        if (ai != null)
                        {
                            ai.CheckAlive();
                        }
                    }
                }
            }
            public void UnloadAllRes(bool unloadPermanentBundle)
            {
                if (unloadPermanentBundle)
                {
                    if (CollapsedManifest != null)
                    {
                        foreach (var item in CollapsedManifest.Items)
                        {
                            var ai = item.Attached as IAssetInfo;
                            if (ai != null)
                            {
                                ai.Unload();
                            }
                        }
                    }
                    UnloadAllBundle();

                    //// sometimes, the sprite whose atlas was unloaded, will remain alive to the next scene.
                    //// and then the sprite would rebuild itself when the atlas is loaded again.
                    //// so we do a force unload here.
                    //var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                    //for (int i = 0; i < sprites.Length; ++i)
                    //{
                    //    Resources.UnloadAsset(sprites[i]);
                    //}
                }
                else
                {
                    if (CollapsedManifest != null)
                    {
                        foreach (var item in CollapsedManifest.Items)
                        {
                            var curitem = item;
                            while (curitem.Ref != null)
                            {
                                curitem = curitem.Ref;
                            }
                            var ai = curitem.Attached as AssetInfo_Base;
                            if (ai != null)
                            {
                                var bundles = ai.DepBundles;
                                if (bundles == null || bundles.Count == 0 || (bundles.Count > 0 && !bundles[bundles.Count - 1].Permanent))
                                {
                                    ai.Unload();
                                }
                            }
                        }
                        UnloadNonPermanentBundle();
                        foreach (var item in CollapsedManifest.Items)
                        {
                            var ai = item.Attached as IAssetInfo;
                            if (ai != null)
                            {
                                ai.CheckAlive();
                            }
                        }
                    }
                    else
                    {
                        UnloadNonPermanentBundle();
                    }

                    //// sometimes, the sprite whose atlas was unloaded, will remain alive to the next scene.
                    //// and then the sprite would rebuild itself when the atlas is loaded again.
                    //// so we do a force unload here.
                    //var sprites = Resources.FindObjectsOfTypeAll<Sprite>();
                    //for (int i = 0; i < sprites.Length; ++i)
                    //{
                    //    var sprite = sprites[i];
                    //    if (sprite && !sprite.texture)
                    //    {
                    //        Resources.UnloadAsset(sprites[i]);
                    //    }
                    //}
                }
            }
            public void MarkPermanent(string assetname)
            {
                if (CollapsedManifest != null)
                {
                    CapsResManifestNode node;
                    CollapsedManifest.TryGetItem(assetname, out node);
                    if (node != null && node.Item != null)
                    {
                        var item = node.Item;
                        while (item.Ref != null)
                        {
                            item = item.Ref;
                        }
                        var ai = item.Attached as AssetInfo_Base;
                        if (ai != null)
                        {
                            var bundles = ai.DepBundles;
                            if (bundles != null)
                            {
                                foreach (var bundle in bundles)
                                {
                                    if (bundle != null)
                                    {
                                        bundle.Permanent = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public static ClientResLoader ClientResLoaderInstance = new ClientResLoader();
    }
}