using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Capstones.UnityEngineEx;
using UnityEditor.Build.Reporting;
#if !NET_4_6 && !NET_STANDARD_2_0
using Unity.IO.Compression;
#else
using System.IO.Compression;
#endif

namespace Capstones.UnityEditorEx
{
    using static Capstones.UnityEditorEx.CapsResBuilder;
    public static class CapsResBuilderAB
    {
        public abstract class BaseResBuilderEx<T> : CapsResBuilder.BaseResBuilderEx<T>, IResBuilderEx where T : CapsResBuilder.BaseResBuilderEx<T>, new()
        {
            public virtual void GenerateBuildWork(string bundleName, IList<string> assets, ref AssetBundleBuild abwork, CapsResBuildWork modwork, int abindex)
            {
            }
            public virtual void PostBuildWork(string mod, CapsResBuildWork work, string dest)
            {
            }
            void IResBuilderEx.GenerateBuildWork(string bundleName, IList<string> assets, IBundleBuildInfo abwork, ICapsResBuildWork modwork, int bindex)
            {
                var realwork = (AssetBundleBuild)abwork.RealInfo;
                var realmwork = (CapsResBuildWork)modwork;
                GenerateBuildWork(bundleName, assets, ref realwork, realmwork, bindex);
                abwork.RealInfo = realwork;
            }

            void IResBuilderEx.PostBuildWork(string mod, ICapsResBuildWork work, string dest)
            {
                var realmwork = (CapsResBuildWork)work;
                PostBuildWork(mod, realmwork, dest);
            }
        }

        public class CapsResBuildWork : ICapsResBuildWork
        {
            public AssetBundleBuild[] ABs;
            public CapsResManifest[] Manifests;
            public HashSet<int> ForceRefreshABs = new HashSet<int>(); // Stores the index in ABs array, which should be deleted before this build (in order to force it to update).
            public Dictionary<string, object> Attached = new Dictionary<string, object>(); // Build time attached extra info.

            public string Phase { get; set; }
            public string OutputDir { get; set; }
            IBundleBuildInfo[] ICapsResBuildWork.BundleBuildInfos
            {
                get
                {
                    if (ABs == null)
                    {
                        return null;
                    }
                    else
                    {
                        var result = new IBundleBuildInfo[ABs.Length];
                        for (int i = 0; i < ABs.Length; ++i)
                        {
                            result[i] = new AssetBundleBuildInfo() { Build = ABs[i] };
                        }
                        return result;
                    }
                }
                set
                {
                    if (value == null)
                    {
                        ABs = null;
                    }
                    else
                    {
                        ABs = new AssetBundleBuild[value.Length];
                        for (int i = 0; i < value.Length; ++i)
                        {
                            ABs[i] = ((AssetBundleBuildInfo)value[i]).Build;
                        }
                    }
                }
            }
            HashSet<int> ICapsResBuildWork.ForceRefreshBundles { get => ForceRefreshABs; set => ForceRefreshABs = value; }
            CapsResManifest[] ICapsResBuildWork.Manifests { get => Manifests; set => Manifests = value; }
            Dictionary<string, object> ICapsResBuildWork.Attached { get => Attached; set => Attached = value; }
        }

        public class AssetBundleBuildInfo : IBundleBuildInfo
        {
            public AssetBundleBuild Build;
            public object RealInfo { get => Build; set => Build = (AssetBundleBuild)value; }

            public string BundleName { get => Build.assetBundleName; set => Build.assetBundleName = value; }
            public IList<string> Assets { get => Build.assetNames; set => Build.assetNames = value.ToArray(); }
            public string FileName
            {
                get
                {
                    if (string.IsNullOrEmpty(Build.assetBundleVariant))
                    {
                        return Build.assetBundleName.ToLower();
                    }
                    else
                    {
                        return Build.assetBundleName.ToLower() + "." + Build.assetBundleVariant.ToLower();
                    }
                }
            }
        }

        public class CapsResBuilderABImp : ICapsResBuilderImp
        {
            public string BundleExtName => ".ab";
            public ICapsResBuildWork CreateWork()
            {
                return new CapsResBuildWork();
            }
            public IBundleBuildInfo CreateBundleBuildInfo()
            {
                return new AssetBundleBuildInfo();
            }

            public TaskProgress Prepare(string outputdir)
            {
                return DeleteBuiltResWithNonExistingAssets(outputdir);
            }

            public void Cleanup(ICapsResBuildWork work)
            {
                if (work.Phase == "manifest")
                {
                    CleanupManifestDir(work);
                }
                else if (work.Phase == "force")
                {
                    CleanupForceBuildBundles(work);
                }
                else if (work.Phase == "res")
                {
                    CleanupStreamingAssets(work);
                }
            }

            public void Build(ICapsResBuildWork work)
            {
                var bundleInfos = work.BundleBuildInfos;
                AssetBundleBuild[] listBuild = new AssetBundleBuild[bundleInfos.Length];
                for (int i = 0; i < listBuild.Length; ++i)
                {
                    listBuild[i] = (AssetBundleBuild)bundleInfos[i].RealInfo;
                }
                var buildopt = BuildAssetBundleOptions.DeterministicAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression;
                BuildTarget buildtar = EditorUserBuildSettings.activeBuildTarget;
                BuildPipeline.BuildAssetBundles(work.OutputDir, listBuild, buildopt, buildtar);
            }

            public Dictionary<string, IList<string>> GroupBuiltFiles(string outputdir)
            {
                Dictionary<string, IList<string>> result = new Dictionary<string, IList<string>>();
                // parse mdtokens (e.g. m--d-)
                List<string> mdtokens = new List<string>();
                var manifiles = PlatDependant.GetAllFiles(outputdir + "/res/mani/");
                for (int i = 0; i < manifiles.Length; ++i)
                {
                    var manifile = manifiles[i];
                    if (manifile.EndsWith(".m.ab"))
                    {
                        var mdtoken = System.IO.Path.GetFileName(manifile);
                        mdtoken = mdtoken.Substring(0, mdtoken.Length - ".m.ab".Length);
                        mdtokens.Add(mdtoken);
                    }
                }
                // group files
                var allmods = CapsModEditor.GetAllModsOrPackages();
                for (int i = 0; i < mdtokens.Count; ++i)
                {
                    var mdtoken = mdtokens[i];
                    string mod = "";
                    string dist = "";
                    if (mdtoken.StartsWith("m-"))
                    {
                        var mendi = mdtoken.IndexOf("-d-");
                        if (mendi >= 0)
                        {
                            mod = mdtoken.Substring("m-".Length, mendi - "m-".Length);

                            dist = mdtoken.Substring(mendi + "-d-".Length);
                        }
                    }
                    else if (mdtoken.StartsWith("d-"))
                    {
                        dist = mdtoken.Substring("d-".Length);
                    }
                    if (!string.IsNullOrEmpty(mod))
                    {
                        foreach (var realmod in allmods)
                        {
                            if (realmod.Equals(mod, StringComparison.InvariantCultureIgnoreCase))
                            {
                                mod = realmod;
                                break;
                            }
                        }
                    }
                    bool inPackage = !string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(CapsModEditor.GetPackageName(mod));
                    string opmod = mod;
                    var moddesc = ResManager.GetDistributeDesc(mod);
                    bool isMainPackage = inPackage && !CapsModEditor.ShouldTreatPackageAsMod(CapsModEditor.GetPackageName(mod));
                    if (moddesc == null || moddesc.InMain || isMainPackage)
                    {
                        mod = "";
                        if (moddesc != null && moddesc.IsOptional && !isMainPackage)
                        {
                            opmod = moddesc.Mod;
                        }
                    }

                    List<string> entries = new List<string>();
                    // abs
                    var abdir = outputdir + "/res";
                    if (!string.IsNullOrEmpty(mod))
                    {
                        abdir += "/mod/" + mod;
                    }

                    if (System.IO.Directory.Exists(abdir))
                    {
                        try
                        {
                            var files = System.IO.Directory.GetFiles(abdir);
                            for (int j = 0; j < files.Length; ++j)
                            {
                                var file = files[j];
                                if (!file.EndsWith(".ab"))
                                {
                                    var sub = System.IO.Path.GetFileName(file);
                                    var split = sub.LastIndexOf(".ab.");
                                    if (split < 0)
                                    {
                                        continue;
                                    }
                                    var ext = sub.Substring(split + ".ab.".Length);
                                    if (ext.Contains("."))
                                    {
                                        continue;
                                    }
                                    if (ext == "manifest")
                                    {
                                        continue;
                                    }
                                }
                                {
                                    var bundle = file.Substring(abdir.Length + 1);
                                    if (IsBundleInModAndDist(bundle, opmod, dist))
                                    {
                                        var entry = file.Substring(outputdir.Length + 1);
                                        entries.Add(entry);
                                        entries.Add(entry + ".manifest");
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                    if (entries.Count > 0)
                    {
                        var reskey = "m-" + opmod.ToLower() + "-d-" + dist.ToLower();
                        // unity build mani
                        var umani = abdir + "/" + (string.IsNullOrEmpty(mod) ? "res" : mod);
                        umani = umani.Substring(outputdir.Length + 1);
                        entries.Add(umani);
                        entries.Add(umani + ".manifest");
                        // mani
                        var mani = "m-" + opmod.ToLower() + "-d-" + dist.ToLower() + ".m.ab";
                        mani = "res/mani/" + mani;
                        entries.Add(mani);
                        entries.Add(mani + ".manifest");
                        entries.Add("res/mani/mani");
                        entries.Add("res/mani/mani.manifest");
                        // version
                        entries.Add("res/version.txt");
                        // dversion
                        var dversion = "res/version/" + reskey + ".txt";
                        PlatDependant.CopyFile(outputdir + "/res/version.txt", outputdir + "/" + dversion);
                        entries.Add(dversion);

                        result[reskey] = entries;
                        //var zipfile = outzipdir + reskey + ".zip";
                        //zips.Add(new Pack<string, string, IList<string>>(zipfile, outputDir, entries));
                        //var workz = MakeZipAsync(zipfile, outputDir, entries, winprog);
                        //while (workz.MoveNext())
                        //{
                        //    if (winprog != null)
                        //    {
                        //        yield return workz.Current;
                        //    }
                        //}
                    }
                }
                return result;
            }
        }

        public static TaskProgress DeleteBuiltResWithNonExistingAssets(string dir)
        {
            List<string> manifestFiles = new List<string>();
            if (System.IO.Directory.Exists(dir))
            {
                var allfiles = PlatDependant.GetAllFiles(dir);
                for (int i = 0; i < allfiles.Length; ++i)
                {
                    var file = allfiles[i];
                    if (file.EndsWith(".manifest", StringComparison.InvariantCultureIgnoreCase))
                    {
                        manifestFiles.Add(file);
                    }
                }
            }
            TaskProgress fullprog = new TaskProgress();
            fullprog.Total = manifestFiles.Count;
            Action<TaskProgress> work = prog =>
            {
                long index = 0;
                while ((index = System.Threading.Interlocked.Increment(ref fullprog.Length) - 1) < manifestFiles.Count)
                {
                    var item = manifestFiles[(int)index];
                    var assets = GetAssetPathsInAssetBundleManifest(item);
                    bool hasNonExisting = false;
                    for (int i = 0; i < assets.Length; ++i)
                    {
                        if (!PlatDependant.IsFileExist(assets[i]))
                        {
                            hasNonExisting = true;
                            break;
                        }
                    }
                    if (hasNonExisting)
                    {
                        var abfile = item.Substring(0, item.Length - ".manifest".Length);
                        PlatDependant.DeleteFile(abfile);
                        PlatDependant.DeleteFile(item);
                    }
                }
                fullprog.Done = true;
            };
            for (int i = 0; i < System.Environment.ProcessorCount; ++i)
            {
                PlatDependant.RunBackgroundLongTime(work);
            }
            return fullprog;
        }
        public static string[] GetAssetPathsInAssetBundleManifest(string manifestFile)
        {
            List<string> assets = new List<string>();
            if (PlatDependant.IsFileExist(manifestFile))
            {
                bool started = false;
                foreach (var line in System.IO.File.ReadLines(manifestFile))
                {
                    if (started)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }
                        else if (line.StartsWith("- "))
                        {
                            var asset = line.Substring("- ".Length);
                            assets.Add(asset);
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        if (line == "Assets:")
                        {
                            started = true;
                        }
                    }
                }
            }
            return assets.ToArray();
        }

        public static void CleanupManifestDir(ICapsResBuildWork work)
        {
            HashSet<string> maniFileNames = new HashSet<string>();
            foreach (var bundleInfo in work.BundleBuildInfos)
            {
                maniFileNames.Add(bundleInfo.BundleName.ToLower());
            }
            var outmanidir = work.OutputDir;
            var manifiles = PlatDependant.GetAllFiles(outmanidir);
            var maniext = ".m.ab";
            for (int i = 0; i < manifiles.Length; ++i)
            {
                var file = manifiles[i];
                var filename = file.Substring(outmanidir.Length + 1);
                if (filename.EndsWith(maniext))
                {
                    if (!maniFileNames.Contains(filename))
                    {
                        PlatDependant.DeleteFile(file);
                        PlatDependant.DeleteFile(file + ".manifest");
                    }
                }
            }
        }
        public static void CleanupForceBuildBundles(ICapsResBuildWork work)
        {
            var dest = work.OutputDir;
            var bundleInfos = work.BundleBuildInfos;
            HashSet<string> buildFiles = new HashSet<string>();
            for (int i = 0; i < bundleInfos.Length; ++i)
            {
                if (!work.ForceRefreshBundles.Contains(i))
                {
                    buildFiles.Add(bundleInfos[i].FileName);
                }
            }
            var files = System.IO.Directory.GetFiles(dest);
            for (int i = 0; i < files.Length; ++i)
            {
                var file = files[i];
                if (!file.EndsWith(".ab"))
                {
                    var sub = System.IO.Path.GetFileName(file);
                    var split = sub.LastIndexOf(".ab.");
                    if (split < 0)
                    {
                        continue;
                    }
                    var ext = sub.Substring(split + ".ab.".Length);
                    if (ext.Contains("."))
                    {
                        continue;
                    }
                    if (ext == "manifest")
                    {
                        continue;
                    }
                }
                {
                    var fileName = System.IO.Path.GetFileName(file);
                    if (!buildFiles.Contains(fileName))
                    {
                        PlatDependant.DeleteFile(file);
                        PlatDependant.DeleteFile(file + ".manifest");
                    }
                }
            }
        }
        public static void CleanupStreamingAssets(ICapsResBuildWork work)
        {
            var dest = work.OutputDir;
            if (!dest.EndsWith("/") && !dest.EndsWith("\\"))
            {
                dest += "/";
            }
            PlatDependant.DeleteFile(dest + "mani/mani");

            var allbuildfiles = PlatDependant.GetAllFiles(dest);
            for (int i = 0; i < allbuildfiles.Length; ++i)
            {
                var file = allbuildfiles[i];
                if (file.EndsWith(".manifest"))
                {
                    PlatDependant.DeleteFile(file);
                }
            }
        }

        private class CapsResBuilderPreExport : UnityEditor.Build.IPreprocessBuild
        {
            public int callbackOrder { get { return 0; } }

            public void OnPreprocessBuild(BuildTarget target, string path)
            {
                using (var sw = PlatDependant.OpenWriteText("Assets/StreamingAssets/res/index.txt"))
                {
                    string maniroot = "Assets/StreamingAssets/res/mani/";
                    if (PlatDependant.IsFileExist("Assets/StreamingAssets/hasobb.flag.txt"))
                    {
                        maniroot = "EditorOutput/Build/Latest/res/mani/";
                    }
                    var files = PlatDependant.GetAllFiles(maniroot);
                    if (files != null)
                    {
                        for (int i = 0; i < files.Length; ++i)
                        {
                            var file = files[i];
                            if (file.EndsWith(".m.ab"))
                            {
                                var key = file.Substring(maniroot.Length, file.Length - maniroot.Length - ".m.ab".Length);
                                sw.WriteLine(key);
                            }
                        }
                    }
                }
                using (var sw = PlatDependant.OpenWriteText("Assets/StreamingAssets/res/builtin-scenes.txt"))
                {
                    var scenes = EditorBuildSettings.scenes;
                    int index = 0;
                    for (int i = 0; i < scenes.Length; ++i)
                    {
                        var sceneinfo = scenes[i];
                        if (sceneinfo.enabled)
                        {
                            var guid = sceneinfo.guid.ToString();
                            var scenepath = AssetDatabase.GUIDToAssetPath(guid);
                            sw.Write(scenepath);
                            sw.Write("|");
                            sw.WriteLine(index++);
                        }
                    }
                }
            }
        }
    }
}
