using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Capstones.UnityEngineEx;
using Capstones.UnityEditorEx;

namespace Capstones.UnityEditorEx
{
    public static class CapsDelayedObbMaker
    {
        [MenuItem("Res/Build Obb (Delayed)", priority = 202021)]
        public static void MakeDelayedObb()
        {
            if (System.IO.Directory.Exists("Assets/StreamingAssets"))
            {
                List<string> blacklist = CapsObbMaker.GetDistributeAssetsList("obb-except.txt");
                var delayedInfo = GetNoDelayObbWhiteAndBlackList();

                var built = MakeObbWithDelayedInFolder("Assets/StreamingAssets", "EditorOutput/Build/Latest/obb/", null, blacklist, delayedInfo, true);
                using (var sw = PlatDependant.OpenWriteText("Assets/StreamingAssets/hasobb.flag.txt"))
                {
                    foreach (var key in built)
                    {
                        sw.WriteLine(key);
                    }
                }
            }
        }

        public static List<string> MakeObbWithDelayedInFolder(string folder, string dest, IList<CapsObbMaker.ObbInfo> obbs, IList<string> blackList, WhiteAndBlackList delayedObbInfo, bool deleteSrc)
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

            HashSet<int> obbs_index_to_remove = null;
            List<CapsObbMaker.ObbInfo> main_ex_obbs = null;
            if (obbs != null)
            {
                for (int i = 0; i < obbs.Count; ++i)
                {
                    if (obbs[i].Key.StartsWith("main", StringComparison.InvariantCultureIgnoreCase) && !obbs[i].Key.Equals("main", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (main_ex_obbs == null)
                        {
                            main_ex_obbs = new List<CapsObbMaker.ObbInfo>();
                        }
                        main_ex_obbs.Add(obbs[i]);
                        if (obbs_index_to_remove == null)
                        {
                            obbs_index_to_remove = new HashSet<int>();
                        }
                        obbs_index_to_remove.Add(i);
                    }
                }
            }
            if (obbs_index_to_remove != null)
            {
                var newobbs = new List<CapsObbMaker.ObbInfo>();
                for (int i = 0; i < obbs.Count; ++i)
                {
                    if (!obbs_index_to_remove.Contains(i))
                    {
                        newobbs.Add(obbs[i]);
                    }
                }
                obbs = newobbs;
            }

            HashSet<string> builtKeys = new HashSet<string>();
            List<string> builtKeysList = new List<string>();
            int mainObbIndex = 0;
            for (int obbindex = 0; ; ++obbindex)
            {
                CapsObbMaker.ObbInfo curobb;
                if (obbs != null && obbs.Count > 0)
                {
                    if (obbindex == 0)
                    {
                        if (mainObbIndex > 0)
                        {
                            if (main_ex_obbs != null)
                            {
                                if (mainObbIndex <= main_ex_obbs.Count)
                                {
                                    curobb = main_ex_obbs[mainObbIndex - 1];
                                }
                                else
                                {
                                    curobb = main_ex_obbs[main_ex_obbs.Count - 1];
                                }
                            }
                            else
                            {
                                curobb = new CapsObbMaker.ObbInfo();
                            }
                        }
                        else
                        {
                            curobb = obbs[0];
                            for (int i = 0; i < obbs.Count; ++i)
                            {
                                if (obbs[i].Key.Equals("main", StringComparison.InvariantCultureIgnoreCase))
                                {
                                    curobb = obbs[i];
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        curobb = obbs[obbs.Count - 1];
                        for (int i = obbs.Count - 1; i >= 0; --i)
                        {
                            if (!obbs[i].Key.Equals("main", StringComparison.InvariantCultureIgnoreCase))
                            {
                                curobb = obbs[i];
                                break;
                            }
                        }
                        for (int i = obbindex - 1; i < obbs.Count; ++i)
                        {
                            if (!obbs[i].Key.Equals("main", StringComparison.InvariantCultureIgnoreCase))
                            {
                                curobb = obbs[i];
                                break;
                            }
                        }
                    }
                }
                else
                {
                    curobb = new CapsObbMaker.ObbInfo();
                }
                if (string.IsNullOrEmpty(curobb.Key))
                {
                    curobb.Key = "main";
                }
                else
                {
                    curobb.Key = curobb.Key.ToLower();
                }
                //if (builtKeys.Contains(curobb.Key) && curobb.Key == "main")
                //{
                //    curobb.Key = "delayed";
                //}
                if (builtKeys.Contains(curobb.Key))
                {
                    curobb.ObbFileName = null;
                    var oldkey = curobb.Key;
                    var splitindex = oldkey.LastIndexOf('-');
                    if (splitindex > 0)
                    {
                        var expart = oldkey.Substring(splitindex + 1);
                        int exindex;
                        if (int.TryParse(expart, out exindex))
                        {
                            oldkey = oldkey.Substring(0, exindex);
                        }
                    }
                    string ukey;
                    for (int i = 1; ; ++i)
                    {
                        ukey = oldkey + "-" + i;
                        if (!builtKeys.Contains(ukey))
                        {
                            break;
                        }
                    }
                    curobb.Key = ukey;
                }
                builtKeys.Add(curobb.Key);
                builtKeysList.Add(curobb.Key);

                bool isMainObb = curobb.Key == "main" || mainObbIndex > 0;
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
                    //if (!isMainObb)
                    {
                        if (!System.IO.File.Exists(file))
                        {
                            continue;
                        }
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

                        if (delayedObbInfo.WhiteList != null && delayedObbInfo.WhiteList.Count > 0)
                        {
                            var isinmain = false;
                            if (part.StartsWith("spt/"))
                            {
                                isinmain = true;
                            }
                            else
                            {
                                var sub = part.Substring("res/".Length);
                                isinmain = MatchWhiteAndBlackList(sub, delayedObbInfo, true);
                            }
                            if (isinmain != isMainObb)
                            {
                                continue;
                            }
                        }

                        fileinfo = new System.IO.FileInfo(file);
                        if (curobb.MaxSize - fileinfo.Length < curobbsize)
                        {
                            if (curobbsize == 0)
                            { // big file - this single file is larger than obb's limit.
                                Debug.LogError(file + " is too large. It will be dropped.");
                                continue;
                            }
                            else
                            {
                                //if (isMainObb && delayedObbInfo.WhiteList != null && delayedObbInfo.WhiteList.Count > 0)
                                //{
                                //    Debug.LogError("main.obb is too large. Remaining files will be dropped.");
                                //}
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

                if (isMainObb)
                {
                    if (fileindex >= files.Length)
                    {
                        mainObbIndex = 0;
                        fileindex = 0;
                    }
                    else
                    {
                        ++mainObbIndex;
                        --obbindex;
                    }
                }
                else if (fileindex >= files.Length)
                {
                    break;
                }
            }

            string obbRecordFilePath = dest + "obb.txt";
            using (var sw = PlatDependant.OpenWriteText(obbRecordFilePath))
            {
                foreach (var key in builtKeysList)
                {
                    sw.WriteLine(key + "." + PlayerSettings.Android.bundleVersionCode + ".obb");
                }
            }
            return builtKeysList;
        }

        public static void GetWhiteAndBlackList(string path, List<string> whitelist, List<string> blacklist)
        {
            var listfile = ResManager.EditorResLoader.CheckDistributePathSafe("~Config~/", path);
            if (listfile != null)
            {
                using (var sr = PlatDependant.OpenReadText(listfile))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.Length > 0)
                        {
                            if (line.StartsWith("--"))
                            {
                                blacklist.Add(line.Substring(2));
                            }
                            else
                            {
                                whitelist.Add(line);
                            }
                        }
                    }
                }
            }
        }
        public struct WhiteAndBlackList
        {
            public List<string> WhiteList;
            public List<string> BlackList;
        }
        public static WhiteAndBlackList GetNoDelayObbWhiteAndBlackList()
        {
            WhiteAndBlackList lists = new WhiteAndBlackList();
            lists.WhiteList = new List<string>();
            lists.BlackList = new List<string>();

            GetWhiteAndBlackList("obb-nodelay.txt", lists.WhiteList, lists.BlackList);
            GetWhiteAndBlackList("obb-nodelay-override.txt", lists.WhiteList, lists.BlackList);

            return lists;
        }
        public static bool MatchTemplate(string checkingstr, string templatestr, bool ignorecase)
        {
            var flags = ResManager.GetValidDistributeFlags();
            int tokenindex = -1;
            int tokenendindex = -1;
            int csindex = 0;
            int tsindex = 0;

            while ((tokenindex = templatestr.IndexOf("{", tsindex)) >= 0 && (tokenendindex = templatestr.IndexOf("}", tokenindex)) > tokenindex)
            {
                var cntnotoken = tokenindex - tsindex;
                if (csindex + cntnotoken > checkingstr.Length)
                {
                    return false;
                }
                if (string.Compare(checkingstr, csindex, templatestr, tsindex, cntnotoken, ignorecase) != 0)
                {
                    return false;
                }
                csindex += cntnotoken;
                tsindex += cntnotoken;

                var token = templatestr.Substring(tokenindex + 1, tokenendindex - tokenindex - 1);
                if (token == "" || token == "m" || token == "d" || token == "0" || token == "1" || token == "mod" || token == "dist")
                {
                    bool fit = false;
                    for (int f = 0; f < flags.Length; ++f)
                    {
                        var flag = flags[f];
                        if (checkingstr.IndexOf(flag, csindex, ignorecase ? StringComparison.InvariantCultureIgnoreCase : 0) == csindex)
                        {
                            fit = true;
                            csindex += flag.Length;
                            tsindex += (token.Length + 2);
                            break;
                        }
                    }
                    if (!fit)
                    {
                        return false;
                    }
                }
                else
                {
                    var tokenlen = token.Length + 2;
                    if (csindex + tokenlen > checkingstr.Length)
                    {
                        return false;
                    }
                    if (string.Compare(checkingstr, csindex, templatestr, tsindex, tokenlen, ignorecase) != 0)
                    {
                        return false;
                    }
                    csindex += tokenlen;
                    tsindex += tokenlen;
                }
            }

            var cntrest = templatestr.Length - tsindex;
            if (csindex + cntrest > checkingstr.Length)
            {
                return false;
            }
            if (string.Compare(checkingstr, csindex, templatestr, tsindex, cntrest, ignorecase) != 0)
            {
                return false;
            }

            return true;
        }
        public static bool MatchWhiteAndBlackList(string checkingstr, WhiteAndBlackList lists, bool ignorecase)
        {
            if (lists.WhiteList == null)
            {
                return false;
            }
            if (lists.BlackList != null)
            {
                for (int i = 0; i < lists.BlackList.Count; ++i)
                {
                    if (MatchTemplate(checkingstr, lists.BlackList[i], ignorecase))
                    {
                        return false;
                    }
                }
            }
            //if (lists.WhiteList != null)
            {
                for (int i = 0; i < lists.WhiteList.Count; ++i)
                {
                    if (MatchTemplate(checkingstr, lists.WhiteList[i], ignorecase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}


