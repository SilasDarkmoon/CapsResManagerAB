using System;
using System.Collections;
using System.Collections.Generic;
#if !NET_4_6 && !NET_STANDARD_2_0
using Unity.IO.Compression;
#else
using System.IO.Compression;
#endif
using UnityEngine;

using Object = UnityEngine.Object;

namespace Capstones.UnityEngineEx
{
    public static partial class ResManagerAB
    {
        static ResManagerAB() { }
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void OnInit()
        {
        }

        private class ResManager_ABLoader : ResManager.ILifetime
        {
            public ResManager_ABLoader()
            {
                ResManager.AddInitItem(this);
            }
            public int Order { get { return ResManager.LifetimeOrders.ABLoader; } }
            public void Prepare()
            {
            }
            public void Init() { }
            public void Cleanup()
            {
                UnloadAllBundle();
                ResManager.ReloadDistributeFlags();
            }
        }
#pragma warning disable 0414
        private static ResManager_ABLoader i_ResManager_ABLoader = new ResManager_ABLoader();
#pragma warning restore

        public class AssetBundleInfo
        {
            public AssetBundle Bundle = null;
            public string RealName;
            public int RefCnt = 0;
            public bool Permanent = false;
            public bool LeaveAssetOpen = false;
            public AssetBundleCreateRequest AsyncLoading = null;

            public AssetBundleInfo(AssetBundle ab)
            {
                Bundle = ab;
                //RefCnt = 0;
            }
            public AssetBundleInfo(AssetBundleCreateRequest asyncloading)
            {
                AsyncLoading = asyncloading;
                //RefCnt = 0;
            }
            public bool IsAsyncLoading
            {
                get
                {
                    return AsyncLoading != null && !AsyncLoading.isDone;
                }
            }
            public bool FinishAsyncLoading()
            {
                if (AsyncLoading != null)
                {
                    Bundle = AsyncLoading.assetBundle; // getting assetBundle from AssetBundleCreateRequest will force an immediate load
                    AsyncLoading = null;
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public int AddRef()
            {
                return ++RefCnt;
            }

            public int Release()
            {
                var rv = --RefCnt;
                if (rv <= 0 && !Permanent)
                {
                    UnloadBundle();
                }
                return rv;
            }
            public bool UnloadBundle()
            {
                FinishAsyncLoading();
                if (Bundle != null)
                {
                    Bundle.Unload(!LeaveAssetOpen);
                    Bundle = null;
                    return true;
                }
                return false;
            }
        }
        public static Dictionary<string, AssetBundleInfo> LoadedAssetBundles = new Dictionary<string, AssetBundleInfo>();

        public static string GetLoadedBundleRealName(string bundle)
        {
            if (LoadedAssetBundles.ContainsKey(bundle))
            {
                var abi = LoadedAssetBundles[bundle];
                if (abi != null && abi.RealName != null)
                {
                    return abi.RealName;
                }
                return bundle;
            }
            return null;
        }

        public static AssetBundleInfo LoadAssetBundle(string name, bool asyncLoad, bool ignoreError)
        {
            return LoadAssetBundle(name, null, asyncLoad, ignoreError);
        }
        public static AssetBundleInfo LoadAssetBundle(string name, string norm, bool asyncLoad, bool ignoreError)
        {
            norm = norm ?? name;
            if (string.IsNullOrEmpty(name))
            {
                if (!ignoreError) PlatDependant.LogError("Loading an ab with empty name.");
                return null;
            }
            AssetBundleInfo abi = null;
            if (LoadedAssetBundles.TryGetValue(norm, out abi))
            {
                if (abi == null || abi.Bundle != null || abi.AsyncLoading != null)
                {
                    if (!asyncLoad && abi != null)
                    {
                        abi.FinishAsyncLoading();
                    }
                    if (abi != null && abi.RealName != null && abi.RealName != name)
                    {
                        //abi.Bundle.Unload(true);
                        //abi.Bundle = null;
                        if (!ignoreError) PlatDependant.LogWarning("Try load duplicated " + norm + ". Current: " + abi.RealName + ". Try: " + name);
                    }
                    //else
                    {
                        if (abi == null)
                        {
                            if (!ignoreError) PlatDependant.LogError("Cannot find (cached)ab: " + norm);
                        }
                        return abi;
                    }
                }
            }
            abi = null;

            AssetBundle bundle = null;
            AssetBundleCreateRequest abrequest = null;
            if (!ResManager.SkipPending)
            {
                if (PlatDependant.IsFileExist(ThreadSafeValues.UpdatePath + "/pending/res/ver.txt"))
                {
                    string path = ThreadSafeValues.UpdatePath + "/pending/res/" + name;
                    if (PlatDependant.IsFileExist(path))
                    {
                        try
                        {
                            if (asyncLoad)
                            {
                                abrequest = AssetBundle.LoadFromFileAsync(path);
                            }
                            else
                            {
                                bundle = AssetBundle.LoadFromFile(path);
                            }
                        }
                        catch (Exception e)
                        {
                            if (!ignoreError) PlatDependant.LogError(e);
                        }
                    }
                }
            }
            if (bundle == null && abrequest == null)
            {
                if (!ResManager.SkipUpdate)
                {
                    string path = ThreadSafeValues.UpdatePath + "/res/" + name;
                    if (PlatDependant.IsFileExist(path))
                    {
                        try
                        {
                            if (asyncLoad)
                            {
                                abrequest = AssetBundle.LoadFromFileAsync(path);
                            }
                            else
                            {
                                bundle = AssetBundle.LoadFromFile(path);
                            }
                        }
                        catch (Exception e)
                        {
                            if (!ignoreError) PlatDependant.LogError(e);
                        }
                    }
                }
            }
            if (bundle == null && abrequest == null)
            {
                if (Application.streamingAssetsPath.Contains("://"))
                {
                    if (Application.platform == RuntimePlatform.Android && ResManager.LoadAssetsFromApk)
                    {
                        var realpath = "res/" + name;
                        if (!ResManager.SkipObb && ResManager.LoadAssetsFromObb && ResManager.ObbEntryType(realpath) == ResManager.ZipEntryType.Uncompressed)
                        {
                            string path = realpath;

                            var allobbs = ResManager.AllObbZipArchives;
                            for (int z = allobbs.Length - 1; z >= 0; --z)
                            {
                                if (!PlatDependant.IsFileExist(ResManager.AllObbPaths[z]))
                                { // means the obb is to be downloaded.
                                    continue;
                                }

                                var zip = allobbs[z];
                                string entryname = path;
                                if (ResManager.AllNonRawExObbs[z] != null)
                                {
                                    var obbpre = ResManager.AllNonRawExObbs[z].GetEntryPrefix();
                                    if (obbpre != null)
                                    {
                                        entryname = obbpre + entryname;
                                    }
                                }
                                int retryTimes = 10;
                                long offset = -1;
                                for (int i = 0; i < retryTimes; ++i)
                                {
                                    Exception error = null;
                                    do
                                    {
                                        ZipArchive za = zip;
                                        if (za == null)
                                        {
                                            if (!ignoreError) PlatDependant.LogError("Obb Archive Cannot be read.");
                                            break;
                                        }
                                        try
                                        {
                                            var entry = za.GetEntry(entryname);
                                            if (entry != null)
                                            {
                                                using (var srcstream = entry.Open())
                                                {
                                                    offset = ResManager.AllObbFileStreams[z].Position;
                                                }
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            error = e;
                                            break;
                                        }
                                    } while (false);
                                    if (error != null)
                                    {
                                        if (i == retryTimes - 1)
                                        {
                                            if (!ignoreError) PlatDependant.LogError(error);
                                        }
                                        else
                                        {
                                            if (!ignoreError) PlatDependant.LogError(error);
                                            if (!ignoreError) PlatDependant.LogInfo("Need Retry " + i);
                                        }
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                                if (offset >= 0)
                                {
                                    if (asyncLoad)
                                    {
                                        abrequest = AssetBundle.LoadFromFileAsync(ResManager.AllObbPaths[z], 0, (ulong)offset);
                                    }
                                    else
                                    {
                                        bundle = AssetBundle.LoadFromFile(ResManager.AllObbPaths[z], 0, (ulong)offset);
                                    }
                                    break;
                                }
                            }
                        }
                        else if (!ResManager.SkipPackage)
                        {
                            ZipArchiveEntry entry = null;
                            if (ResManager.AndroidApkZipArchive != null && (entry = ResManager.AndroidApkZipArchive.GetEntry("assets/res/" + name)) != null)
                            {
                                long offset = -1;
                                if (entry.CompressedLength == entry.Length)
                                {
                                    try
                                    {
                                        using (var srcstream = entry.Open())
                                        {
                                            offset = ResManager.AndroidApkFileStream.Position;
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        if (!ignoreError) PlatDependant.LogError(e);
                                    }
                                }
                                if (offset >= 0)
                                {
                                    string path = Application.dataPath;
                                    try
                                    {
                                        if (asyncLoad)
                                        {
                                            abrequest = AssetBundle.LoadFromFileAsync(path, 0, (ulong)offset);
                                        }
                                        else
                                        {
                                            bundle = AssetBundle.LoadFromFile(path, 0, (ulong)offset);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        if (!ignoreError) PlatDependant.LogError(e);
                                    }
                                }
                                else
                                {
                                    string path = Application.streamingAssetsPath + "/res/" + name;
                                    try
                                    {
                                        if (asyncLoad)
                                        {
                                            abrequest = AssetBundle.LoadFromFileAsync(path);
                                        }
                                        else
                                        {
                                            bundle = AssetBundle.LoadFromFile(path);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        if (!ignoreError) PlatDependant.LogError(e);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (!ResManager.SkipPackage)
                    {
                        string path = Application.streamingAssetsPath + "/res/" + name;
                        if (PlatDependant.IsFileExist(path))
                        {
                            try
                            {
                                if (asyncLoad)
                                {
                                    abrequest = AssetBundle.LoadFromFileAsync(path);
                                }
                                else
                                {
                                    bundle = AssetBundle.LoadFromFile(path);
                                }
                            }
                            catch (Exception e)
                            {
                                if (!ignoreError) PlatDependant.LogError(e);
                            }
                        }
                    }
                }
            }

            if (bundle != null)
            {
                abi = new AssetBundleInfo(bundle) { RealName = name };
            }
            else if (abrequest != null)
            {
                abi = new AssetBundleInfo(abrequest) { RealName = name };
            }
            else
            {
                if (!ignoreError) PlatDependant.LogError("Cannot load ab: " + norm);
            }
            LoadedAssetBundles[norm] = abi;
            return abi;
        }
        public static AssetBundleInfo LoadAssetBundle(string name, bool asyncLoad)
        {
            return LoadAssetBundle(name, asyncLoad, false);
        }
        public static AssetBundleInfo LoadAssetBundle(string name)
        {
            return LoadAssetBundle(name, false);
        }
        public static AssetBundleInfo LoadAssetBundleIgnoreError(string name)
        {
            return LoadAssetBundle(name, false, true);
        }
        public static AssetBundleInfo LoadAssetBundleAsync(string name)
        {
            return LoadAssetBundle(name, true);
        }
        public static AssetBundleInfo LoadAssetBundleIgnoreErrorAsync(string name)
        {
            return LoadAssetBundle(name, true, true);
        }
        public static AssetBundleInfo LoadAssetBundle(string mod, string name, bool asyncLoad)
        {
            return LoadAssetBundle(mod, name, null, asyncLoad);
        }
        public static AssetBundleInfo LoadAssetBundle(string mod, string name, string norm, bool asyncLoad)
        {
            if (string.IsNullOrEmpty(mod))
            {
                return LoadAssetBundle(name, norm, asyncLoad, false);
            }
            else
            {
                return LoadAssetBundle("mod/" + mod + "/" + name, norm, asyncLoad, false);
            }
        }
        public static bool FindLoadedAssetBundle(string name, string norm, out AssetBundleInfo abi)
        {
            norm = norm ?? name;
            if (string.IsNullOrEmpty(name))
            {
                abi = null;
                return false;
            }
            abi = null;
            if (LoadedAssetBundles.TryGetValue(norm, out abi))
            {
                if (abi == null || abi.Bundle != null || abi.AsyncLoading != null)
                {
                    return true;
                }
            }
            abi = null;
            return false;
        }
        public static bool FindLoadedAssetBundle(string mod, string name, string norm, out AssetBundleInfo abi)
        {
            if (string.IsNullOrEmpty(mod))
            {
                return FindLoadedAssetBundle(name, norm, out abi);
            }
            else
            {
                return FindLoadedAssetBundle("mod/" + mod + "/" + name, norm, out abi);
            }
        }
        public static void ForgetMissingAssetBundles()
        {
            List<string> missingNames = new List<string>();
            foreach (var kvp in LoadedAssetBundles)
            {
                if (kvp.Value == null)
                {
                    missingNames.Add(kvp.Key);
                }
            }
            for (int i = 0; i < missingNames.Count; ++i)
            {
                var name = missingNames[i];
                LoadedAssetBundles.Remove(name);
            }
        }

        public interface IAssetBundleLoaderEx
        {
            bool LoadAssetBundle(string mod, string name, bool asyncLoad, bool isContainingBundle, out AssetBundleInfo bi);
        }
        public static readonly List<IAssetBundleLoaderEx> AssetBundleLoaderEx = new List<IAssetBundleLoaderEx>();
        public static AssetBundleInfo LoadAssetBundleEx(string mod, string name, bool isContainingBundle)
        {
            return LoadAssetBundleEx(mod, name, false, isContainingBundle);
        }
        public static AssetBundleInfo LoadAssetBundleExAsync(string mod, string name, bool isContainingBundle)
        {
            return LoadAssetBundleEx(mod, name, true, isContainingBundle);
        }
        public static AssetBundleInfo LoadAssetBundleEx(string mod, string name, bool asyncLoad, bool isContainingBundle)
        {
            AssetBundleInfo bi;
            if (FindLoadedAssetBundle(mod, name, null, out bi))
            {
                if (!asyncLoad && bi != null)
                {
                    bi.FinishAsyncLoading();
                }
                return bi;
            }
            for (int i = 0; i < AssetBundleLoaderEx.Count; ++i)
            {
                if (AssetBundleLoaderEx[i].LoadAssetBundle(mod, name, asyncLoad, isContainingBundle, out bi))
                {
                    return bi;
                }
            }
            return LoadAssetBundle(mod, name, asyncLoad);
        }
        public static string[] GetAllBundleNames(string pre)
        {
            pre = pre ?? "";
            var dir = pre;
            if (!pre.EndsWith("/"))
            {
                var index = pre.LastIndexOf('/');
                if (index < 0)
                {
                    dir = "";
                }
                else
                {
                    dir = pre.Substring(0, index);
                }
            }

            HashSet<string> foundSet = new HashSet<string>();
            List<string> found = new List<string>();

            if (!ResManager.SkipPending)
            {
                if (PlatDependant.IsFileExist(ThreadSafeValues.UpdatePath + "/pending/res/ver.txt"))
                {
                    string resdir = ThreadSafeValues.UpdatePath + "/pending/res/";
                    string path = resdir + dir;
                    var files = PlatDependant.GetAllFiles(path);
                    for (int i = 0; i < files.Length; ++i)
                    {
                        var file = files[i].Substring(resdir.Length);
                        if (dir == pre || file.StartsWith(pre))
                        {
                            if (foundSet.Add(file))
                            {
                                found.Add(file);
                            }
                        }
                    }
                }
            }
            if (!ResManager.SkipUpdate)
            {
                string resdir = ThreadSafeValues.UpdatePath + "/res/";
                string path = resdir + dir;
                var files = PlatDependant.GetAllFiles(path);
                for (int i = 0; i < files.Length; ++i)
                {
                    var file = files[i].Substring(resdir.Length);
                    if (dir == pre || file.StartsWith(pre))
                    {
                        if (foundSet.Add(file))
                        {
                            found.Add(file);
                        }
                    }
                }
            }

            if (Application.streamingAssetsPath.Contains("://"))
            {
                if (Application.platform == RuntimePlatform.Android && ResManager.LoadAssetsFromApk)
                {
                    if (!ResManager.SkipObb && ResManager.LoadAssetsFromObb)
                    {
                        var allobbs = ResManager.AllObbZipArchives;
                        if (allobbs != null)
                        {
                            for (int z = 0; z < allobbs.Length; ++z)
                            {
                                if (!PlatDependant.IsFileExist(ResManager.AllObbPaths[z]))
                                { // means the obb is to be downloaded.
                                    continue;
                                }

                                var zip = allobbs[z];
                                string obbpre = null;
                                if (ResManager.AllNonRawExObbs[z] != null)
                                {
                                    obbpre = ResManager.AllNonRawExObbs[z].GetEntryPrefix();
                                }
                                int retryTimes = 10;
                                for (int i = 0; i < retryTimes; ++i)
                                {
                                    Exception error = null;
                                    do
                                    {
                                        ZipArchive za = zip;
                                        if (za == null)
                                        {
                                            PlatDependant.LogError("Obb Archive Cannot be read.");
                                            break;
                                        }
                                        try
                                        {
                                            var entries = za.Entries;
                                            foreach (var entry in entries)
                                            {
                                                if (entry.CompressedLength == entry.Length)
                                                {
                                                    var name = entry.FullName;
                                                    if (obbpre == null || name.StartsWith(obbpre))
                                                    {
                                                        if (obbpre != null)
                                                        {
                                                            name = name.Substring(obbpre.Length);
                                                        }
                                                        name = name.Substring("res/".Length);
                                                        if (name.StartsWith(pre))
                                                        {
                                                            if (foundSet.Add(name))
                                                            {
                                                                found.Add(name);
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            error = e;
                                            break;
                                        }
                                    } while (false);
                                    if (error != null)
                                    {
                                        if (i == retryTimes - 1)
                                        {
                                            PlatDependant.LogError(error);
                                        }
                                        else
                                        {
                                            PlatDependant.LogError(error);
                                            PlatDependant.LogInfo("Need Retry " + i);
                                        }
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!ResManager.SkipPackage)
                    {
                        int retryTimes = 10;
                        for (int i = 0; i < retryTimes; ++i)
                        {
                            Exception error = null;
                            do
                            {
                                ZipArchive za = ResManager.AndroidApkZipArchive;
                                if (za == null)
                                {
                                    PlatDependant.LogError("Apk Archive Cannot be read.");
                                    break;
                                }
                                try
                                {
                                    var entries = za.Entries;
                                    foreach (var entry in entries)
                                    {
                                        var name = entry.FullName.Substring("assets/res/".Length);
                                        if (name.StartsWith(pre))
                                        {
                                            if (foundSet.Add(name))
                                            {
                                                found.Add(name);
                                            }
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    error = e;
                                    break;
                                }
                            } while (false);
                            if (error != null)
                            {
                                if (i == retryTimes - 1)
                                {
                                    PlatDependant.LogError(error);
                                }
                                else
                                {
                                    PlatDependant.LogError(error);
                                    PlatDependant.LogInfo("Need Retry " + i);
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                if (!ResManager.SkipPackage)
                {
                    string resdir = Application.streamingAssetsPath + "/res/";
                    string path = resdir + dir;
                    var files = PlatDependant.GetAllFiles(path);
                    for (int i = 0; i < files.Length; ++i)
                    {
                        var file = files[i].Substring(resdir.Length);
                        if (dir == pre || file.StartsWith(pre))
                        {
                            if (foundSet.Add(file))
                            {
                                found.Add(file);
                            }
                        }
                    }
                }
            }

            return found.ToArray();
        }
        public static string[] GetAllResManiBundleNames()
        {
            var dir = "mani/";
            HashSet<string> foundSet = new HashSet<string>();
            List<string> found = new List<string>();

            if (!ResManager.SkipPending)
            {
                if (PlatDependant.IsFileExist(ThreadSafeValues.UpdatePath + "/pending/res/ver.txt"))
                {
                    string resdir = ThreadSafeValues.UpdatePath + "/pending/res/";
                    string path = resdir + dir;
                    var files = PlatDependant.GetAllFiles(path);
                    for (int i = 0; i < files.Length; ++i)
                    {
                        var file = files[i].Substring(resdir.Length);
                        if (file.EndsWith(".m.ab"))
                        {
                            if (foundSet.Add(file))
                            {
                                found.Add(file);
                            }
                        }
                    }
                }
            }
            if (!ResManager.SkipUpdate)
            {
                string resdir = ThreadSafeValues.UpdatePath + "/res/";
                string path = resdir + dir;
                var files = PlatDependant.GetAllFiles(path);
                for (int i = 0; i < files.Length; ++i)
                {
                    var file = files[i].Substring(resdir.Length);
                    if (file.EndsWith(".m.ab"))
                    {
                        if (foundSet.Add(file))
                        {
                            found.Add(file);
                        }
                    }
                }
            }

            if (Application.streamingAssetsPath.Contains("://"))
            {
                if (Application.platform == RuntimePlatform.Android && ResManager.LoadAssetsFromApk)
                {
                    if (!ResManager.SkipObb && ResManager.LoadAssetsFromObb)
                    {
                        var allobbs = ResManager.AllObbZipArchives;
                        if (allobbs != null)
                        {
                            for (int z = 0; z < allobbs.Length; ++z)
                            {
                                if (!PlatDependant.IsFileExist(ResManager.AllObbPaths[z]))
                                { // means the obb is to be downloaded.
                                    continue;
                                }

                                var zip = allobbs[z];
                                string obbpre = null;
                                if (ResManager.AllNonRawExObbs[z] != null)
                                {
                                    obbpre = ResManager.AllNonRawExObbs[z].GetEntryPrefix();
                                }
                                int retryTimes = 10;
                                for (int i = 0; i < retryTimes; ++i)
                                {
                                    Exception error = null;
                                    do
                                    {
                                        ZipArchive za = zip;
                                        if (za == null)
                                        {
                                            PlatDependant.LogError("Obb Archive Cannot be read.");
                                            break;
                                        }
                                        try
                                        {
                                            var indexentryname = "res/index.txt";
                                            if (obbpre != null)
                                            {
                                                indexentryname = obbpre + indexentryname;
                                            }
                                            var indexentry = za.GetEntry(indexentryname);
                                            if (indexentry == null)
                                            {
                                                var entries = za.Entries;
                                                foreach (var entry in entries)
                                                {
                                                    if (entry.CompressedLength == entry.Length)
                                                    {
                                                        var name = entry.FullName;
                                                        if (obbpre == null || name.StartsWith(obbpre))
                                                        {
                                                            if (obbpre != null)
                                                            {
                                                                name = name.Substring(obbpre.Length);
                                                            }
                                                            name = name.Substring("res/".Length);
                                                            if (name.StartsWith(dir) && name.EndsWith(".m.ab"))
                                                            {
                                                                if (foundSet.Add(name))
                                                                {
                                                                    found.Add(name);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                using (var stream = indexentry.Open())
                                                {
                                                    using (var sr = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                                                    {
                                                        while (true)
                                                        {
                                                            var line = sr.ReadLine();
                                                            if (line == null)
                                                            {
                                                                break;
                                                            }
                                                            if (line != "")
                                                            {
                                                                var name = dir + line.Trim() + ".m.ab";
                                                                if (foundSet.Add(name))
                                                                {
                                                                    found.Add(name);
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            error = e;
                                            break;
                                        }
                                    } while (false);
                                    if (error != null)
                                    {
                                        if (i == retryTimes - 1)
                                        {
                                            PlatDependant.LogError(error);
                                        }
                                        else
                                        {
                                            PlatDependant.LogError(error);
                                            PlatDependant.LogInfo("Need Retry " + i);
                                        }
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }

                    if (!ResManager.SkipPackage)
                    {
                        int retryTimes = 10;
                        for (int i = 0; i < retryTimes; ++i)
                        {
                            Exception error = null;
                            do
                            {
                                ZipArchive za = ResManager.AndroidApkZipArchive;
                                if (za == null)
                                {
                                    PlatDependant.LogError("Apk Archive Cannot be read.");
                                    break;
                                }
                                try
                                {
                                    var indexentry = za.GetEntry("assets/res/index.txt");
                                    if (indexentry == null)
                                    {
                                        var entries = za.Entries;
                                        foreach (var entry in entries)
                                        {
                                            var name = entry.FullName.Substring("assets/res/".Length);
                                            if (name.StartsWith(dir) && name.EndsWith(".m.ab"))
                                            {
                                                if (foundSet.Add(name))
                                                {
                                                    found.Add(name);
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        using (var stream = indexentry.Open())
                                        {
                                            using (var sr = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                                            {
                                                while (true)
                                                {
                                                    var line = sr.ReadLine();
                                                    if (line == null)
                                                    {
                                                        break;
                                                    }
                                                    if (line != "")
                                                    {
                                                        var name = dir + line.Trim() + ".m.ab";
                                                        if (foundSet.Add(name))
                                                        {
                                                            found.Add(name);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    error = e;
                                    break;
                                }
                            } while (false);
                            if (error != null)
                            {
                                if (i == retryTimes - 1)
                                {
                                    PlatDependant.LogError(error);
                                }
                                else
                                {
                                    PlatDependant.LogError(error);
                                    PlatDependant.LogInfo("Need Retry " + i);
                                }
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
            }
            else
            {
                if (!ResManager.SkipPackage)
                {
                    string resdir = Application.streamingAssetsPath + "/res/";
                    string path = resdir + dir;
                    var files = PlatDependant.GetAllFiles(path);
                    for (int i = 0; i < files.Length; ++i)
                    {
                        var file = files[i].Substring(resdir.Length);
                        if (file.EndsWith(".m.ab"))
                        {
                            if (foundSet.Add(file))
                            {
                                found.Add(file);
                            }
                        }
                    }
                }
            }

            return found.ToArray();
        }
        
        public static void UnloadUnusedBundle()
        {
            foreach (var kvpb in LoadedAssetBundles)
            {
                var abi = kvpb.Value;
                if (abi != null && !abi.Permanent && abi.RefCnt <= 0)
                {
                    abi.UnloadBundle();
                }
            }
        }
        public static void UnloadAllBundleSoft()
        {
            var newLoadedAssetBundles = new Dictionary<string, AssetBundleInfo>();
            foreach (var abi in LoadedAssetBundles)
            {
                if (abi.Value != null && !abi.Value.Permanent)
                {
                    abi.Value.FinishAsyncLoading();
                    if (abi.Value.Bundle != null)
                    {
                        abi.Value.Bundle.Unload(false);
                        abi.Value.Bundle = null;
                    }
                }
                else if (abi.Value != null)
                {
                    newLoadedAssetBundles[abi.Key] = abi.Value;
                }
            }
            LoadedAssetBundles = newLoadedAssetBundles;
        }
        public static void UnloadAllBundle()
        {
            foreach (var kvpb in LoadedAssetBundles)
            {
                var abi = kvpb.Value;
                if (abi != null)
                {
                    abi.UnloadBundle();
                }
            }
            LoadedAssetBundles.Clear();
        }
        public static void UnloadNonPermanentBundle()
        {
            var newLoadedAssetBundles = new Dictionary<string, AssetBundleInfo>();
            foreach (var abi in LoadedAssetBundles)
            {
                if (abi.Value != null && !abi.Value.Permanent)
                {
                    abi.Value.UnloadBundle();
                }
                else if (abi.Value != null)
                {
                    newLoadedAssetBundles[abi.Key] = abi.Value;
                }
            }
            LoadedAssetBundles = newLoadedAssetBundles;
        }
    }
}
