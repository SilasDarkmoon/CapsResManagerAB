using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Capstones.UnityEngineEx;
using System.Text;
#if !NET_4_6 && !NET_STANDARD_2_0
using Unity.IO.Compression;
using CompressionLevel = Unity.IO.Compression.CompressionLevel;
#else
using System.IO.Compression;
using CompressionLevel = System.IO.Compression.CompressionLevel;
#endif

namespace Capstones.UnityEditorEx
{
    [InitializeOnLoad]
    public class CapsObbMaker_ResBuilderEx : CapsResBuilder.BaseResBuilderEx<CapsObbMaker_ResBuilderEx>
    {
        private static HierarchicalInitializer _Initializer = new HierarchicalInitializer(0);

        public override void Prepare(string output)
        {
            if (PlatDependant.IsFileExist("Assets/StreamingAssets/hasobb.flag.txt"))
            {
                PlatDependant.DeleteFile("Assets/StreamingAssets/hasobb.flag.txt");
            }
        }
    }

    public static class CapsObbMaker
    {
        public struct ObbInfo
        {
            public string Key;
            public string ObbFileName;
            public long MaxSize;
        }

        public static void MakeObb(string dest, params string[] subzips)
        {
            if (!string.IsNullOrEmpty(dest) && subzips != null && subzips.Length > 0)
            {
                HashSet<string> reskeys = new HashSet<string>();
                HashSet<string> sptkeys = new HashSet<string>();
                using (var sdest = PlatDependant.OpenWrite(dest))
                {
                    using (var zdest = new ZipArchive(sdest, ZipArchiveMode.Create))
                    {
                        for (int i = 0; i < subzips.Length; ++i)
                        {
                            try
                            {
                                var sfile = subzips[i];
                                if (PlatDependant.IsFileExist(sfile))
                                {
                                    var key = System.IO.Path.GetFileNameWithoutExtension(sfile).ToLower();
                                    bool isres = false;
                                    bool isspt = false;
                                    HashSet<string> entrynames = new HashSet<string>();
                                    using (var ssrc = PlatDependant.OpenRead(sfile))
                                    {
                                        using (var zsrc = new ZipArchive(ssrc, ZipArchiveMode.Read))
                                        {
                                            foreach (var sentry in zsrc.Entries)
                                            {
                                                var fullname = sentry.FullName;
                                                if (fullname.StartsWith("res/"))
                                                {
                                                    isres = true;
                                                }
                                                else if (fullname.StartsWith("spt/"))
                                                {
                                                    isspt = true;
                                                }
                                                if (entrynames.Add(fullname))
                                                {
                                                    var dentry = zdest.CreateEntry(fullname, isres ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                                                    using (var ses = sentry.Open())
                                                    {
                                                        using (var des = dentry.Open())
                                                        {
                                                            ses.CopyTo(des);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    if (isres)
                                    {
                                        reskeys.Add(key);
                                    }
                                    if (isspt)
                                    {
                                        sptkeys.Add(key);
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                PlatDependant.LogError(e);
                            }
                        }

                        if (reskeys.Count > 0)
                        {
                            var resindex = zdest.CreateEntry("res/index.txt", CompressionLevel.Optimal);
                            using (var sindex = resindex.Open())
                            {
                                using (var swindex = new System.IO.StreamWriter(sindex, System.Text.Encoding.UTF8))
                                {
                                    foreach (var key in reskeys)
                                    {
                                        swindex.WriteLine(key);
                                    }
                                }
                            }
                        }
                        if (sptkeys.Count > 0)
                        {
                            var sptindex = zdest.CreateEntry("spt/index.txt", CompressionLevel.Optimal);
                            using (var sindex = sptindex.Open())
                            {
                                using (var swindex = new System.IO.StreamWriter(sindex, System.Text.Encoding.UTF8))
                                {
                                    foreach (var key in sptkeys)
                                    {
                                        swindex.WriteLine(key);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        public static List<string> MakeObbInFolder(string folder, string dest, IList<ObbInfo> obbs, IList<string> blackList, bool deleteSrc)
        {
            if (string.IsNullOrEmpty(dest))
            {
                dest = "EditorOutput/Build/Latest/";
            }
            if (!folder.EndsWith("/") && !folder.EndsWith("\\"))
            {
                folder = folder + "/";
            }
            var files = PlatDependant.GetAllFiles(folder);
            int fileindex = 0;

            HashSet<string> builtKeys = new HashSet<string>();
            List<string> builtKeysList = new List<string>();
            for (int obbindex = 0; ; ++obbindex)
            {
                ObbInfo curobb;
                if (obbs != null && obbs.Count > 0)
                {
                    if (obbindex < obbs.Count)
                    {
                        curobb = obbs[obbindex];
                    }
                    else
                    {
                        curobb = obbs[obbs.Count - 1];
                    }
                }
                else
                {
                    curobb = new ObbInfo();
                }
                if (string.IsNullOrEmpty(curobb.Key))
                {
                    curobb.Key = "main";
                }
                else
                {
                    curobb.Key = curobb.Key.ToLower();
                }
                if (builtKeys.Contains(curobb.Key) && curobb.Key == "main")
                {
                    curobb.Key = "patch";
                }
                if (builtKeys.Contains(curobb.Key))
                {
                    string ukey = curobb.Key + "-" + obbindex;
                    if (builtKeys.Contains(ukey))
                    {
                        for (int i = 1; ; ++i)
                        {
                            ukey = curobb.Key + "-" + (obbindex + i);
                            if (!builtKeys.Contains(ukey))
                            {
                                break;
                            }
                        }
                    }
                    curobb.Key = ukey;
                }
                builtKeys.Add(curobb.Key);
                builtKeysList.Add(curobb.Key);

                var obbpath = curobb.ObbFileName;
                if (string.IsNullOrEmpty(obbpath))
                {
                    //obbpath = curobb.Key + "." + PlayerSettings.Android.bundleVersionCode + "." + PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android) + ".obb";
                    obbpath = curobb.Key + "." + PlayerSettings.Android.bundleVersionCode + ".obb";
                }
                if (!obbpath.EndsWith(".obb", StringComparison.InvariantCultureIgnoreCase))
                {
                    obbpath += ".obb";
                }
                if (!System.IO.Path.IsPathRooted(obbpath))
                {
                    obbpath = System.IO.Path.Combine(dest, obbpath);
                }

                if (curobb.MaxSize <= 0)
                {
                    curobb.MaxSize = 1024L * 1024L * (1024L * 2L - 10L);
                }

                var tmpdir = obbpath + ".tmp/";
                if (System.IO.Directory.Exists(tmpdir))
                {
                    System.IO.Directory.Delete(tmpdir, true);
                }
                PlatDependant.CreateFolder(tmpdir);
                PlatDependant.CreateFolder(System.IO.Path.GetDirectoryName(obbpath));
                if (PlatDependant.IsFileExist(obbpath))
                {
                    PlatDependant.DeleteFile(obbpath);
                }

                long curobbsize = 0;
                System.IO.FileInfo fileinfo = null;
                for (; fileindex < files.Length; ++fileindex)
                {

                    var file = files[fileindex];
                    if (file.EndsWith(".meta", StringComparison.InvariantCultureIgnoreCase)
                        || file.EndsWith(".manifest", StringComparison.InvariantCultureIgnoreCase)
                        || file.EndsWith(".srcinfo", StringComparison.InvariantCultureIgnoreCase)
                        )
                    {
                        continue;
                    }

                    var part = file.Substring(folder.Length);
                    if (part.StartsWith("res/") || part.StartsWith("spt/"))
                    {
                        bool isValid = true;
                        if (blackList != null)
                        {
                            for (int i = 0; i < blackList.Count; ++i)
                            {
                                var blackItem = blackList[i];
                                if (part.StartsWith(blackItem, StringComparison.InvariantCultureIgnoreCase))
                                {
                                    isValid = false;
                                    break;
                                }
                            }
                        }
                        if (!isValid)
                        {
                            continue;
                        }

                        fileinfo = new System.IO.FileInfo(file);
                        if (curobb.MaxSize - fileinfo.Length < curobbsize)
                        {
                            if (curobbsize == 0)
                            { // big file - this single file is larger than obb's limit.
                                continue;
                            }
                            else
                            {
                                break;
                            }
                        }
                        curobbsize += fileinfo.Length;

                        try
                        {
                            var dst = tmpdir + part;
                            if (deleteSrc)
                            {
                                PlatDependant.MoveFile(file, dst);
                            }
                            else
                            {
                                PlatDependant.CopyFile(file, dst);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }
                }

                try
                {
                    if (curobbsize <= 0 || CapsEditorUtils.ZipFolderNoCompress(tmpdir, obbpath))
                    {
                        System.IO.Directory.Delete(tmpdir, true);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }

                if (fileindex >= files.Length)
                {
                    break;
                }
            }

            string obbRecordFilePath = System.IO.Path.Combine(dest, "obb.txt");
            using (var sw = PlatDependant.OpenWriteText(obbRecordFilePath))
            {
                foreach (var key in builtKeysList)
                {
                    sw.WriteLine(key + "." + PlayerSettings.Android.bundleVersionCode + ".obb");
                }
            }
            return builtKeysList;
        }

        [MenuItem("Res/Build Obb (Default)", priority = 202020)]
        public static void MakeDefaultObb()
        {
            if (System.IO.Directory.Exists("Assets/StreamingAssets"))
            {
                List<string> blacklist = GetDistributeAssetsList("obb-except.txt");

                var built = MakeObbInFolder("Assets/StreamingAssets", "EditorOutput/Build/Latest/obb/", null, blacklist, true);
                using (var sw = PlatDependant.OpenWriteText("Assets/StreamingAssets/hasobb.flag.txt"))
                {
                    foreach (var key in built)
                    {
                        sw.WriteLine(key);
                    }
                }
            }
        }

        public static List<string> GetDistributeAssetsList(string path)
        {
            List<string> blacklist = null;
            var blacklistfile = ResManager.EditorResLoader.CheckDistributePathSafe("~Config~/", path);
            if (blacklistfile != null)
            {
                blacklist = new List<string>();
                using (var sr = PlatDependant.OpenReadText(blacklistfile))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length > 0)
                        {
                            blacklist.Add(line);
                        }
                    }
                }
            }
            return blacklist;
        }
    }
}
