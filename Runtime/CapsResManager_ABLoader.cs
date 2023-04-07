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
        public interface IObbEx
        {
            string HostedObbName { get; }
            bool IsRaw { get; } // raw: this is an obb zip file. not-raw: this is contained in some other file.
            bool IsReady { get; }
            string Error { get; }
            void GetProgress(out long progress, out long total);
            string GetContainingFile(); // raw: get the obb zip file. not-raw: get the containing file. check this file exists to determine whether we can load assets from the obb.
            System.IO.Stream OpenWholeObb(System.IO.Stream containingStream); // open the obb zip stream for both raw or not-raw. for raw, return null, means we should open file at GetContainingFile(). for not-raw, the stream is a span of GetContainingFile()。
            string GetEntryPrefix(); // get the entry prefix, null for no prefix
            string FindEntryUrl(string entryname); // for not-raw obb, maybe an asset can be loaded but can not find url of it.
            void Reset();
        }

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
#if !FORCE_DECOMPRESS_ASSETS_ON_ANDROID
                if (Application.platform == RuntimePlatform.Android)
                {
                    _LoadAssetsFromApk = true;
#if !FORCE_DECOMPRESS_ASSETS_FROM_OBB
                    _LoadAssetsFromObb = true;
#endif
                }
#endif
            }
            public int Order { get { return ResManager.LifetimeOrders.ABLoader; } }
            public void Prepare()
            {
                if (Application.platform == RuntimePlatform.Android)
                {
#if DEBUG_OBB_IN_DOWNLOAD_PATH
#if UNITY_ANDROID
                    if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.ExternalStorageRead))
                    {
                        UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.ExternalStorageRead);
                    }
                    if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.ExternalStorageWrite))
                    {
                        UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.ExternalStorageWrite);
                    }
#endif
                    _ObbPath = "/storage/emulated/0/Download/default.obb";
                    _MainObbEx = null;
                    var obb2path = "/storage/emulated/0/Download/obb2.obb";
                    _AllObbPaths = new[] { _ObbPath, obb2path };
                    _AllObbNames = new[] { "testobb", "testobb2" };
                    _AllNonRawExObbs = new IObbEx[_AllObbNames.Length];
#else
                    bool hasobb = false;
                    string mainobbpath = null;
                    IObbEx mainobbex = null;
                    List<Pack<string, string>> obbs = new List<Pack<string, string>>();

                    using (var stream = LoadFileInStreaming("hasobb.flag.txt"))
                    {
                        if (stream != null)
                        {
                            hasobb = true;

                            string appid = Application.identifier;
                            string obbroot = Application.persistentDataPath;
                            int obbrootindex = obbroot.IndexOf(appid);
                            if (obbrootindex > 0)
                            {
                                obbroot = obbroot.Substring(0, obbrootindex);
                            }
                            obbrootindex = obbroot.LastIndexOf("/Android");
                            if (obbrootindex > 0)
                            {
                                obbroot = obbroot.Substring(0, obbrootindex);
                            }
                            if (!obbroot.EndsWith("/") && !obbroot.EndsWith("\\"))
                            {
                                obbroot += "/";
                            }
                            obbroot += "Android/obb/" + appid + "/";

                            using (var sr = new System.IO.StreamReader(stream))
                            {
                                string line;
                                while ((line = sr.ReadLine()) != null)
                                {
                                    var parts = line.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (parts != null && parts.Length > 0)
                                    {
                                        var obbname = parts[0];
                                        string obbpath = null;
                                        if (AllExObbs.ContainsKey(obbname))
                                        {
                                            var oex = AllExObbs[obbname];
                                            obbpath = oex.GetContainingFile();
                                        }
                                        else
                                        {
                                            int obbver = 0;
                                            if (parts.Length > 1)
                                            {
                                                var val = parts[1];
                                                if (!int.TryParse(val, out obbver))
                                                {
                                                    obbpath = val;
                                                }
                                            }
                                            if (obbpath == null)
                                            {
                                                if (obbver <= 0)
                                                {
                                                    obbver = AppVer;
                                                }
                                                obbpath = obbname + "." + obbver + "." + appid + ".obb";
                                            }
                                            if (!obbpath.Contains("/") && !obbpath.Contains("\\"))
                                            {
                                                obbpath = obbroot + obbpath;
                                            }

                                            if (!PlatDependant.IsFileExist(obbpath))
                                            { // use updatepath as obb path
                                                obbpath = ThreadSafeValues.UpdatePath + "/obb/" + obbname + "." + obbver + ".obb";
                                            }
                                        }

                                        obbs.Add(new Pack<string, string>(obbname, obbpath));
                                        if (obbname == "main")
                                        {
                                            mainobbpath = obbpath;
                                            if (AllExObbs.ContainsKey(obbname))
                                            {
                                                var oex = AllExObbs[obbname];
                                                if (!oex.IsRaw)
                                                {
                                                    mainobbex = oex;
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (mainobbpath == null)
                            {
                                mainobbpath = obbroot + "main." + AppVer + "." + appid + ".obb";

                                if (!PlatDependant.IsFileExist(mainobbpath))
                                { // use updatepath as obb path
                                    mainobbpath = ThreadSafeValues.UpdatePath + "/obb/main." + AppVer + ".obb";
                                }
                                
                                obbs.Insert(0, new Pack<string, string>("main", mainobbpath));
                            }
                        }
                    }

                    if (hasobb)
                    {
                        _ObbPath = mainobbpath;
                        _MainObbEx = mainobbex;
                        _AllObbPaths = new string[obbs.Count];
                        _AllObbNames = new string[obbs.Count];
                        _AllNonRawExObbs = new IObbEx[obbs.Count];
                        for (int i = 0; i < obbs.Count; ++i)
                        {
                            _AllObbPaths[i] = obbs[i].t2;
                            string obbname = _AllObbNames[i] = obbs[i].t1;
                            if (AllExObbs.ContainsKey(obbname))
                            {
                                var oex = AllExObbs[obbname];
                                if (!oex.IsRaw)
                                {
                                    _AllNonRawExObbs[i] = oex;
                                }
                            }
                        }
                    }
                    else
                    {
                        _ObbPath = null;
                        _MainObbEx = null;
                        _AllObbPaths = null;
                        _AllObbNames = null;
                        _AllNonRawExObbs = null;
                    }
#endif
                }
            }
            public void Init() { }
            public void Cleanup()
            {
                UnloadAllBundle();
                UnloadAllObbs();
                ResManager.ReloadDistributeFlags();
            }
        }
#pragma warning disable 0414
        private static ResManager_ABLoader i_ResManager_ABLoader = new ResManager_ABLoader();
#pragma warning restore

        private static bool _LoadAssetsFromApk;
        public static bool LoadAssetsFromApk
        {
            get { return _LoadAssetsFromApk; }
        }
        private static bool _LoadAssetsFromObb;
        public static bool LoadAssetsFromObb
        {
            get { return _LoadAssetsFromObb; }
        }

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

        public static bool SkipPending = true;
        public static bool SkipUpdate = false;
        public static bool SkipObb = false;
        public static bool SkipPackage = false;
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
            if (!SkipPending)
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
                if (!SkipUpdate)
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
                    if (Application.platform == RuntimePlatform.Android && _LoadAssetsFromApk)
                    {
                        var realpath = "res/" + name;
                        if (!SkipObb && _LoadAssetsFromObb && ObbEntryType(realpath) == ZipEntryType.Uncompressed)
                        {
                            string path = realpath;

                            var allobbs = AllObbZipArchives;
                            for (int z = allobbs.Length - 1; z >= 0; --z)
                            {
                                if (!PlatDependant.IsFileExist(AllObbPaths[z]))
                                { // means the obb is to be downloaded.
                                    continue;
                                }

                                var zip = allobbs[z];
                                string entryname = path;
                                if (AllNonRawExObbs[z] != null)
                                {
                                    var obbpre = AllNonRawExObbs[z].GetEntryPrefix();
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
                                                    offset = AllObbFileStreams[z].Position;
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
                                        abrequest = AssetBundle.LoadFromFileAsync(AllObbPaths[z], 0, (ulong)offset);
                                    }
                                    else
                                    {
                                        bundle = AssetBundle.LoadFromFile(AllObbPaths[z], 0, (ulong)offset);
                                    }
                                    break;
                                }
                            }
                        }
                        else if (!SkipPackage)
                        {
                            ZipArchiveEntry entry = null;
                            if (AndroidApkZipArchive != null && (entry = AndroidApkZipArchive.GetEntry("assets/res/" + name)) != null)
                            {
                                long offset = -1;
                                if (entry.CompressedLength == entry.Length)
                                {
                                    try
                                    {
                                        using (var srcstream = entry.Open())
                                        {
                                            offset = AndroidApkFileStream.Position;
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
                    if (!SkipPackage)
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

        // TODO: in server?
        public static System.IO.Stream LoadFileInStreaming(string file)
        {
            return LoadFileInStreaming("", file, false, false);
        }
        public static System.IO.Stream LoadFileInStreaming(string prefix, string file, bool variantModAndDist, bool ignoreHotUpdate)
        {
            List<string> allflags;
            if (variantModAndDist)
            {
                var flags = ResManager.GetValidDistributeFlags();
                allflags = new List<string>(flags.Length + 1);
                allflags.Add(null);
                allflags.AddRange(flags);
            }
            else
            {
                allflags = new List<string>(1) { null };
            }

            if (!SkipPending && !ignoreHotUpdate)
            {
                string root = ThreadSafeValues.UpdatePath + "/pending/";
                for (int n = allflags.Count - 1; n >= 0; --n)
                {
                    var dist = allflags[n];
                    for (int m = allflags.Count - 1; m >= 0; --m)
                    {
                        var mod = allflags[m];
                        var moddir = "";
                        if (mod != null)
                        {
                            moddir = "mod/" + mod + "/";
                        }
                        if (dist != null)
                        {
                            moddir += "dist/" + dist + "/";
                        }
                        var path = root + prefix + moddir + file;
                        if (PlatDependant.IsFileExist(path))
                        {
                            return PlatDependant.OpenRead(path);
                        }
                    }
                }
            }
            if (!SkipUpdate && !ignoreHotUpdate)
            {
                string root = ThreadSafeValues.UpdatePath + "/";
                for (int n = allflags.Count - 1; n >= 0; --n)
                {
                    var dist = allflags[n];
                    for (int m = allflags.Count - 1; m >= 0; --m)
                    {
                        var mod = allflags[m];
                        var moddir = "";
                        if (mod != null)
                        {
                            moddir = "mod/" + mod + "/";
                        }
                        if (dist != null)
                        {
                            moddir += "dist/" + dist + "/";
                        }
                        var path = root + prefix + moddir + file;
                        if (PlatDependant.IsFileExist(path))
                        {
                            return PlatDependant.OpenRead(path);
                        }
                    }
                }
            }
            if (ThreadSafeValues.AppStreamingAssetsPath.Contains("://"))
            {
                if (ThreadSafeValues.AppPlatform == RuntimePlatform.Android.ToString() && _LoadAssetsFromApk)
                {
                    var allobbs = AllObbZipArchives;
                    if (!SkipObb && _LoadAssetsFromObb && allobbs != null)
                    {
                        for (int n = allflags.Count - 1; n >= 0; --n)
                        {
                            var dist = allflags[n];
                            for (int m = allflags.Count - 1; m >= 0; --m)
                            {
                                var mod = allflags[m];
                                var moddir = "";
                                if (mod != null)
                                {
                                    moddir = "mod/" + mod + "/";
                                }
                                if (dist != null)
                                {
                                    moddir += "dist/" + dist + "/";
                                }
                                var entryname = prefix + moddir + file;

                                for (int z = allobbs.Length - 1; z >= 0; --z)
                                {
                                    if (!PlatDependant.IsFileExist(AllObbPaths[z]))
                                    { // means the obb is to be downloaded.
                                        continue;
                                    }
                                    
                                    var zip = allobbs[z];
                                    string fullentryname = entryname;
                                    if (AllNonRawExObbs[z] != null)
                                    {
                                        var obbpre = AllNonRawExObbs[z].GetEntryPrefix();
                                        if (obbpre != null)
                                        {
                                            fullentryname = obbpre + fullentryname;
                                        }
                                    }
                                    int retryTimes = 3;
                                    for (int i = 0; i < retryTimes; ++i)
                                    {
                                        ZipArchive za = zip;
                                        if (za == null)
                                        {
                                            PlatDependant.LogError("Obb Archive Cannot be read.");
                                            if (i != retryTimes - 1)
                                            {
                                                PlatDependant.LogInfo("Need Retry " + i);
                                            }
                                            continue;
                                        }

                                        try
                                        {
                                            var entry = za.GetEntry(fullentryname);
                                            if (entry != null)
                                            {
                                                return entry.Open();
                                            }
                                            break;
                                        }
                                        catch (Exception e)
                                        {
                                            PlatDependant.LogError(e);
                                            if (i != retryTimes - 1)
                                            {
                                                PlatDependant.LogInfo("Need Retry " + i);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (!SkipPackage)
                    {
                        for (int n = allflags.Count - 1; n >= 0; --n)
                        {
                            var dist = allflags[n];
                            for (int m = allflags.Count - 1; m >= 0; --m)
                            {
                                var mod = allflags[m];
                                var moddir = "";
                                if (mod != null)
                                {
                                    moddir = "mod/" + mod + "/";
                                }
                                if (dist != null)
                                {
                                    moddir += "dist/" + dist + "/";
                                }
                                var entryname = prefix + moddir + file;

                                int retryTimes = 3;
                                for (int i = 0; i < retryTimes; ++i)
                                {
                                    ZipArchive za = AndroidApkZipArchive;
                                    if (za == null)
                                    {
                                        PlatDependant.LogError("Apk Archive Cannot be read.");
                                        if (i != retryTimes - 1)
                                        {
                                            PlatDependant.LogInfo("Need Retry " + i);
                                        }
                                        continue;
                                    }

                                    try
                                    {
                                        var entry = za.GetEntry("assets/" + entryname);
                                        if (entry != null)
                                        {
                                            return entry.Open();
                                        }
                                        break;
                                    }
                                    catch (Exception e)
                                    {
                                        PlatDependant.LogError(e);
                                        if (i != retryTimes - 1)
                                        {
                                            PlatDependant.LogInfo("Need Retry " + i);
                                        }
                                    }
                                }

                            }
                        }
                    }
                }
            }
            else
            {
                if (!SkipPackage)
                {
                    string root = ThreadSafeValues.AppStreamingAssetsPath + "/";
                    for (int n = allflags.Count - 1; n >= 0; --n)
                    {
                        var dist = allflags[n];
                        for (int m = allflags.Count - 1; m >= 0; --m)
                        {
                            var mod = allflags[m];
                            var moddir = "";
                            if (mod != null)
                            {
                                moddir = "mod/" + mod + "/";
                            }
                            if (dist != null)
                            {
                                moddir += "dist/" + dist + "/";
                            }
                            var path = root + prefix + moddir + file;
                            if (PlatDependant.IsFileExist(path))
                            {
                                return PlatDependant.OpenRead(path);
                            }
                        }
                    }
                }
            }
            return null;
        }
        public static string FindUrlInStreaming(string file)
        {
            return FindUrlInStreaming("", file, false, false);
        }
        public static string FindUrlInStreaming(string prefix, string file, bool variantModAndDist, bool ignoreHotUpdate)
        {
            List<string> allflags;
            if (variantModAndDist)
            {
                var flags = ResManager.GetValidDistributeFlags();
                allflags = new List<string>(flags.Length + 1);
                allflags.Add(null);
                allflags.AddRange(flags);
            }
            else
            {
                allflags = new List<string>(1) { null };
            }

            if (!SkipPending && !ignoreHotUpdate)
            {
                string root = ThreadSafeValues.UpdatePath + "/pending/";
                for (int n = allflags.Count - 1; n >= 0; --n)
                {
                    var dist = allflags[n];
                    for (int m = allflags.Count - 1; m >= 0; --m)
                    {
                        var mod = allflags[m];
                        var moddir = "";
                        if (mod != null)
                        {
                            moddir = "mod/" + mod + "/";
                        }
                        if (dist != null)
                        {
                            moddir += "dist/" + dist + "/";
                        }
                        var path = root + prefix + moddir + file;
                        if (PlatDependant.IsFileExist(path))
                        {
                            return path;
                        }
                    }
                }
            }
            if (!SkipUpdate && !ignoreHotUpdate)
            {
                string root = ThreadSafeValues.UpdatePath + "/";
                for (int n = allflags.Count - 1; n >= 0; --n)
                {
                    var dist = allflags[n];
                    for (int m = allflags.Count - 1; m >= 0; --m)
                    {
                        var mod = allflags[m];
                        var moddir = "";
                        if (mod != null)
                        {
                            moddir = "mod/" + mod + "/";
                        }
                        if (dist != null)
                        {
                            moddir += "dist/" + dist + "/";
                        }
                        var path = root + prefix + moddir + file;
                        if (PlatDependant.IsFileExist(path))
                        {
                            return path;
                        }
                    }
                }
            }
            if (ThreadSafeValues.AppStreamingAssetsPath.Contains("://"))
            {
                if (ThreadSafeValues.AppPlatform == RuntimePlatform.Android.ToString() && _LoadAssetsFromApk)
                {
                    var allobbs = AllObbZipArchives;
                    if (!SkipObb && _LoadAssetsFromObb && allobbs != null)
                    {
                        for (int n = allflags.Count - 1; n >= 0; --n)
                        {
                            var dist = allflags[n];
                            for (int m = allflags.Count - 1; m >= 0; --m)
                            {
                                var mod = allflags[m];
                                var moddir = "";
                                if (mod != null)
                                {
                                    moddir = "mod/" + mod + "/";
                                }
                                if (dist != null)
                                {
                                    moddir += "dist/" + dist + "/";
                                }
                                var entryname = prefix + moddir + file;

                                for (int z = allobbs.Length - 1; z >= 0; --z)
                                {
                                    if (!PlatDependant.IsFileExist(AllObbPaths[z]))
                                    { // means the obb is to be downloaded.
                                        continue;
                                    }

                                    if (AllNonRawExObbs[z] != null)
                                    {
                                        var result = AllNonRawExObbs[z].FindEntryUrl(entryname);
                                        if (result != null)
                                        {
                                            return result;
                                        }
                                    }
                                    if (AllNonRawExObbs[z] == null || AllNonRawExObbs[z].GetEntryPrefix() != null)
                                    {
                                        var zip = allobbs[z];
                                        var fullentryname = entryname;
                                        if (AllNonRawExObbs[z] != null)
                                        {
                                            fullentryname = AllNonRawExObbs[z].GetEntryPrefix() + fullentryname;
                                        }
                                        int retryTimes = 3;
                                        for (int i = 0; i < retryTimes; ++i)
                                        {
                                            ZipArchive za = zip;
                                            if (za == null)
                                            {
                                                PlatDependant.LogError("Obb Archive Cannot be read.");
                                                if (i != retryTimes - 1)
                                                {
                                                    PlatDependant.LogInfo("Need Retry " + i);
                                                }
                                                continue;
                                            }

                                            try
                                            {
                                                var entry = za.GetEntry(fullentryname);
                                                if (entry != null)
                                                {
                                                    return "jar:file://" + AllObbPaths[z] + "!/" + fullentryname;
                                                }
                                                break;
                                            }
                                            catch (Exception e)
                                            {
                                                PlatDependant.LogError(e);
                                                if (i != retryTimes - 1)
                                                {
                                                    PlatDependant.LogInfo("Need Retry " + i);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (!SkipPackage)
                    {
                        for (int n = allflags.Count - 1; n >= 0; --n)
                        {
                            var dist = allflags[n];
                            for (int m = allflags.Count - 1; m >= 0; --m)
                            {
                                var mod = allflags[m];
                                var moddir = "";
                                if (mod != null)
                                {
                                    moddir = "mod/" + mod + "/";
                                }
                                if (dist != null)
                                {
                                    moddir += "dist/" + dist + "/";
                                }
                                var entryname = prefix + moddir + file;

                                int retryTimes = 3;
                                for (int i = 0; i < retryTimes; ++i)
                                {
                                    ZipArchive za = AndroidApkZipArchive;
                                    if (za == null)
                                    {
                                        PlatDependant.LogError("Apk Archive Cannot be read.");
                                        if (i != retryTimes - 1)
                                        {
                                            PlatDependant.LogInfo("Need Retry " + i);
                                        }
                                        continue;
                                    }

                                    try
                                    {
                                        var entry = za.GetEntry("assets/" + entryname);
                                        if (entry != null)
                                        {
                                            return ThreadSafeValues.AppStreamingAssetsPath + "/" + entryname;
                                        }
                                        break;
                                    }
                                    catch (Exception e)
                                    {
                                        PlatDependant.LogError(e);
                                        if (i != retryTimes - 1)
                                        {
                                            PlatDependant.LogInfo("Need Retry " + i);
                                        }
                                    }
                                }

                            }
                        }
                    }
                }
            }
            else
            {
                if (!SkipPackage)
                {
                    string root = ThreadSafeValues.AppStreamingAssetsPath + "/";
                    for (int n = allflags.Count - 1; n >= 0; --n)
                    {
                        var dist = allflags[n];
                        for (int m = allflags.Count - 1; m >= 0; --m)
                        {
                            var mod = allflags[m];
                            var moddir = "";
                            if (mod != null)
                            {
                                moddir = "mod/" + mod + "/";
                            }
                            if (dist != null)
                            {
                                moddir += "dist/" + dist + "/";
                            }
                            var path = root + prefix + moddir + file;
                            if (PlatDependant.IsFileExist(path))
                            {
                                return path;
                            }
                        }
                    }
                }
            }
            return null;
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

            if (!SkipPending)
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
            if (!SkipUpdate)
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
                if (Application.platform == RuntimePlatform.Android && _LoadAssetsFromApk)
                {
                    if (!SkipObb && _LoadAssetsFromObb)
                    {
                        var allobbs = AllObbZipArchives;
                        if (allobbs != null)
                        {
                            for (int z = 0; z < allobbs.Length; ++z)
                            {
                                if (!PlatDependant.IsFileExist(AllObbPaths[z]))
                                { // means the obb is to be downloaded.
                                    continue;
                                }

                                var zip = allobbs[z];
                                string obbpre = null;
                                if (AllNonRawExObbs[z] != null)
                                {
                                    obbpre = AllNonRawExObbs[z].GetEntryPrefix();
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

                    if (!SkipPackage)
                    {
                        int retryTimes = 10;
                        for (int i = 0; i < retryTimes; ++i)
                        {
                            Exception error = null;
                            do
                            {
                                ZipArchive za = AndroidApkZipArchive;
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
                if (!SkipPackage)
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

            if (!SkipPending)
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
            if (!SkipUpdate)
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
                if (Application.platform == RuntimePlatform.Android && _LoadAssetsFromApk)
                {
                    if (!SkipObb && _LoadAssetsFromObb)
                    {
                        var allobbs = AllObbZipArchives;
                        if (allobbs != null)
                        {
                            for (int z = 0; z < allobbs.Length; ++z)
                            {
                                if (!PlatDependant.IsFileExist(AllObbPaths[z]))
                                { // means the obb is to be downloaded.
                                    continue;
                                }

                                var zip = allobbs[z];
                                string obbpre = null;
                                if (AllNonRawExObbs[z] != null)
                                {
                                    obbpre = AllNonRawExObbs[z].GetEntryPrefix();
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

                    if (!SkipPackage)
                    {
                        int retryTimes = 10;
                        for (int i = 0; i < retryTimes; ++i)
                        {
                            Exception error = null;
                            do
                            {
                                ZipArchive za = AndroidApkZipArchive;
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
                if (!SkipPackage)
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

        public static int GetAppVer()
        {
            int versionCode = CrossEvent.TrigClrEvent<int>("SDK_GetAppVerCode");
            if (versionCode <= 0)
            { // the cross call failed. we parse it from the string like "1.0.0.25"
                var vername = ThreadSafeValues.AppVerName;
                if (!int.TryParse(vername, out versionCode))
                {
                    int split = vername.LastIndexOf(".");
                    if (split > 0)
                    {
                        var verlastpart = vername.Substring(split + 1);
                        int.TryParse(verlastpart, out versionCode);
                    }
                }
            }
            return versionCode;
        }
        private static int? _cached_AppVer;
        public static int AppVer
        {
            get
            {
                if (_cached_AppVer == null)
                {
                    _cached_AppVer = GetAppVer();
                }
                return (int)_cached_AppVer;
            }
        }

        #region Zip Archive on Android APK
        [ThreadStatic] private static System.IO.Stream _AndroidApkFileStream;
        [ThreadStatic] private static ZipArchive _AndroidApkZipArchive;
        public static System.IO.Stream AndroidApkFileStream
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    bool disposed = false;
                    try
                    {
                        if (_AndroidApkFileStream == null)
                        {
                            disposed = true;
                        }
                        else if (!_AndroidApkFileStream.CanSeek)
                        {
                            disposed = true;
                        }
                    }
                    catch
                    {
                        disposed = true;
                    }
                    if (disposed)
                    {
                        _AndroidApkFileStream = null;
                        _AndroidApkFileStream = PlatDependant.OpenRead(ThreadSafeValues.AppDataPath);
                    }
                }
                catch (Exception e)
                {
                    PlatDependant.LogError(e);
                }
#endif
                return _AndroidApkFileStream;
            }
        }
        public static ZipArchive AndroidApkZipArchive
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    bool disposed = false;
                    try
                    {
                        if (_AndroidApkZipArchive == null)
                        {
                            disposed = true;
                        }
                        else
                        {
#if !NET_4_6 && !NET_STANDARD_2_0
                            _AndroidApkZipArchive.ThrowIfDisposed();
#else
                            { var entries = _AndroidApkZipArchive.Entries; }
#endif
                            if (_AndroidApkZipArchive.Mode == ZipArchiveMode.Create)
                            {
                                disposed = true;
                            }
                        }
                    }
                    catch
                    {
                        disposed = true;
                    }
                    if (disposed)
                    {
                        _AndroidApkZipArchive = null;
                        _AndroidApkZipArchive = new ZipArchive(AndroidApkFileStream);
                    }
                }
                catch (Exception e)
                {
                    PlatDependant.LogError(e);
                }
#endif
                return _AndroidApkZipArchive;
            }
        }

        private static string _ObbPath;
        public static string ObbPath
        {
            get { return _ObbPath; }
        }
        private static IObbEx _MainObbEx;
        public static IObbEx MainObbEx
        {
            get { return _MainObbEx; }
        }
        private static string[] _AllObbPaths;
        public static string[] AllObbPaths
        {
            get { return _AllObbPaths; }
        }
        private static string[] _AllObbNames;
        public static string[] AllObbNames
        {
            get { return _AllObbNames; }
        }
        public static readonly Dictionary<string, IObbEx> AllExObbs = new Dictionary<string, IObbEx>();
        private static IObbEx[] _AllNonRawExObbs;
        public static IObbEx[] AllNonRawExObbs
        {
            get { return _AllNonRawExObbs; }
        }

        [ThreadStatic] private static System.IO.Stream _ObbFileStream;
        [ThreadStatic] private static ZipArchive _ObbZipArchive;
        public static System.IO.Stream ObbFileStream
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_ObbPath != null)
                {
                    try
                    {
                        bool disposed = false;
                        try
                        {
                            if (_ObbFileStream == null)
                            {
                                disposed = true;
                            }
                            else if (!_ObbFileStream.CanSeek)
                            {
                                disposed = true;
                            }
                        }
                        catch
                        {
                            disposed = true;
                        }
                        if (disposed)
                        {
                            _ObbFileStream = null;
                            _ObbFileStream = PlatDependant.OpenRead(_ObbPath);
                        }
                    }
                    catch (Exception e)
                    {
                        PlatDependant.LogError(e);
                    }
                }
                else
                {
                    _ObbFileStream = null;
                }
#endif
                return _ObbFileStream;
            }
        }
        public static ZipArchive ObbZipArchive
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_ObbPath != null && ObbFileStream != null)
                {
                    try
                    {
                        bool disposed = false;
                        try
                        {
                            if (_ObbZipArchive == null)
                            {
                                disposed = true;
                            }
                            else
                            {
#if !NET_4_6 && !NET_STANDARD_2_0
                                _ObbZipArchive.ThrowIfDisposed();
#else
                                { var entries = _ObbZipArchive.Entries; }
#endif
                                if (_ObbZipArchive.Mode == ZipArchiveMode.Create)
                                {
                                    disposed = true;
                                }
                            }
                        }
                        catch
                        {
                            disposed = true;
                        }
                        if (disposed)
                        {
                            _ObbZipArchive = null;
                            if (_MainObbEx != null)
                            {
                                _ObbZipArchive = new ZipArchive(_MainObbEx.OpenWholeObb(ObbFileStream) ?? ObbFileStream);
                            }
                            else
                            {
                                _ObbZipArchive = new ZipArchive(ObbFileStream);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        PlatDependant.LogError(e);
                    }
                }
                else
                {
                    _ObbZipArchive = null;
                }
#endif
                return _ObbZipArchive;
            }
        }
        [ThreadStatic] private static System.IO.Stream[] _AllObbFileStreams;
        [ThreadStatic] private static ZipArchive[] _AllObbZipArchives;
        public static System.IO.Stream[] AllObbFileStreams
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_AllObbPaths != null)
                {
                    if (_AllObbFileStreams == null)
                    {
                        _AllObbFileStreams = new System.IO.Stream[_AllObbPaths.Length];
                    }
                    for (int i = 0; i < _AllObbFileStreams.Length; ++i)
                    {
                        try
                        {
                            bool disposed = false;
                            try
                            {
                                if (_AllObbFileStreams[i] == null)
                                {
                                    disposed = true;
                                }
                                else if (!_AllObbFileStreams[i].CanSeek)
                                {
                                    disposed = true;
                                }
                            }
                            catch
                            {
                                disposed = true;
                            }
                            if (disposed)
                            {
                                _AllObbFileStreams[i] = null;
                                _AllObbFileStreams[i] = PlatDependant.OpenRead(_AllObbPaths[i]);
                            }
                        }
                        catch (Exception e)
                        {
                            PlatDependant.LogError(e);
                        }
                    }
                }
                else
                {
                    _AllObbFileStreams = null;
                }
#endif
                return _AllObbFileStreams;
            }
        }
        public static ZipArchive[] AllObbZipArchives
        {
            get
            {
                var filestreams = AllObbFileStreams;
#if UNITY_ANDROID && !UNITY_EDITOR
                if (_AllObbPaths != null && filestreams != null)
                {
                    if (_AllObbZipArchives == null)
                    {
                        _AllObbZipArchives = new ZipArchive[filestreams.Length];
                    }
                    for (int i = 0; i < _AllObbZipArchives.Length; ++i)
                    {
                        try
                        {
                            bool disposed = false;
                            try
                            {
                                if (_AllObbZipArchives[i] == null)
                                {
                                    disposed = true;
                                }
                                else
                                {
#if !NET_4_6 && !NET_STANDARD_2_0
                                    _AllObbZipArchives[i].ThrowIfDisposed();
#else
                                    { var entries = _AllObbZipArchives[i].Entries; }
#endif
                                    if (_AllObbZipArchives[i].Mode == ZipArchiveMode.Create)
                                    {
                                        disposed = true;
                                    }
                                }
                            }
                            catch
                            {
                                disposed = true;
                            }
                            if (disposed)
                            {
                                _AllObbZipArchives[i] = null;
                                if (filestreams[i] != null)
                                {
                                    if (_AllNonRawExObbs[i] != null)
                                    {
                                        _AllObbZipArchives[i] = new ZipArchive(_AllNonRawExObbs[i].OpenWholeObb(filestreams[i]) ?? filestreams[i]);
                                    }
                                    else
                                    {
                                        _AllObbZipArchives[i] = new ZipArchive(filestreams[i]);
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            PlatDependant.LogError(e);
                        }
                    }
                }
                else
                {
                    _AllObbZipArchives = null;
                }
#endif
                return _AllObbZipArchives;
            }
        }

        public enum ZipEntryType
        {
            NonExist = 0,
            Compressed = 1,
            Uncompressed = 2,
        }
        public static ZipEntryType ObbEntryType(string file)
        {
            ZipEntryType result = ZipEntryType.NonExist;
            var allarchives = AllObbZipArchives;
            if (allarchives != null)
            {
                for (int n = allarchives.Length - 1; n >= 0; --n)
                {
                    if (!PlatDependant.IsFileExist(AllObbPaths[n]))
                    { // means the obb is to be downloaded.
                        continue;
                    }

                    var archive = allarchives[n];
                    int retryTimes = 10;
                    for (int i = 0; i < retryTimes; ++i)
                    {
                        Exception error = null;
                        do
                        {
                            ZipArchive za = archive;
                            if (za == null)
                            {
                                error = new Exception("Obb Archive Cannot be read.");
                                break;
                            }

                            try
                            {
                                var entry = za.GetEntry(file);
                                if (entry != null)
                                {
                                    result = ZipEntryType.Compressed;
                                    if (entry.CompressedLength == entry.Length)
                                    {
                                        result = ZipEntryType.Uncompressed;
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
                                throw error;
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

                    if (result != ZipEntryType.NonExist)
                    {
                        break;
                    }
                }
            }
            return result;
        }
        public static bool IsFileInObb(string file)
        {
            return ObbEntryType(file) != ZipEntryType.NonExist;
        }

        public static void UnloadAllObbs()
        {
            if (_ObbZipArchive != null)
            {
                try
                {
                    _ObbZipArchive.Dispose();
                }
                catch (Exception e)
                {
                    PlatDependant.LogError(e);
                }
                _ObbZipArchive = null;
            }
            if (_ObbFileStream != null)
            {
                try
                {
                    _ObbFileStream.Dispose();
                }
                catch (Exception e)
                {
                    PlatDependant.LogError(e);
                }
                _ObbFileStream = null;
            }
            if (_AllObbZipArchives != null)
            {
                for (int i = 0; i < _AllObbZipArchives.Length; ++i)
                {
                    if (_AllObbZipArchives[i] != null)
                    {
                        try
                        {
                            _AllObbZipArchives[i].Dispose();
                        }
                        catch (Exception e)
                        {
                            PlatDependant.LogError(e);
                        }
                    }
                }
                _AllObbZipArchives = null;
            }
            if (_AllObbFileStreams != null)
            {
                for (int i = 0; i < _AllObbFileStreams.Length; ++i)
                {
                    if (_AllObbFileStreams[i] != null)
                    {
                        try
                        {
                            _AllObbFileStreams[i].Dispose();
                        }
                        catch (Exception e)
                        {
                            PlatDependant.LogError(e);
                        }
                    }
                }
                _AllObbFileStreams = null;
            }
        }
#endregion
    }
}
