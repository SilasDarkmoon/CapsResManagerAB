using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Capstones.UnityEngineEx;

namespace Capstones.UnityEditorEx
{
    public static class CapsResBuilderChecker
    {
        public static void CheckRes(string output)
        {
            // generate build work
            List<CapsResBuilder.IResBuilderEx> allExBuilders = CapsResBuilder.ResBuilderEx;
            for (int i = 0; i < allExBuilders.Count; ++i)
            {
                allExBuilders[i].Prepare(null);
            }
            Dictionary<string, CapsResBuilder.CapsResBuildWork> buildwork = new Dictionary<string, CapsResBuilder.CapsResBuildWork>();
            var gwork = CapsResBuilder.GenerateBuildWorkAsync(buildwork, null, null);
            while (gwork.MoveNext()) ;
            for (int i = 0; i < allExBuilders.Count; ++i)
            {
                allExBuilders[i].Cleanup();
            }

            // parse asset list in each reskey (m-XXX-d-YYY)
            Dictionary<string, List<string>> reskey2assetlist = new Dictionary<string, List<string>>();
            Dictionary<string, string> asset2reskey = new Dictionary<string, string>();
            HashSet<string> nodepassets = new HashSet<string>();
            foreach (var buildmodwork in buildwork)
            {
                var mod = buildmodwork.Key;
                var work = buildmodwork.Value;

                var manifests = work.Manifests;
                for (int i = 0; i < manifests.Length; ++i)
                {
                    var mani = manifests[i];
                    var opmod = mani.MFlag;
                    var dist = mani.DFlag ?? "";

                    var reskey = "m-" + opmod + "-d-" + dist;
                    List<string> list;
                    if (!reskey2assetlist.TryGetValue(reskey, out list))
                    {
                        list = new List<string>();
                        reskey2assetlist[reskey] = list;
                    }

                    for (int j = 0; j < work.ABs.Length; ++j)
                    {
                        var abinfo = work.ABs[j];
                        var abname = abinfo.assetBundleName;
                        if (abname.EndsWith(".=.ab"))
                        {
                            for (int k = 0; k < abinfo.assetNames.Length; ++k)
                            {
                                var asset = abinfo.assetNames[k];
                                nodepassets.Add(asset);
                            }
                            continue;
                        }
                        if (!string.IsNullOrEmpty(abinfo.assetBundleVariant))
                        {
                            abname += "." + abinfo.assetBundleVariant;
                        }
                        if (CapsResBuilder.IsBundleInModAndDist(abname, opmod, dist))
                        {
                            list.AddRange(abinfo.assetNames);
                            for (int k = 0; k < abinfo.assetNames.Length; ++k)
                            {
                                var asset = abinfo.assetNames[k];
                                asset2reskey[asset] = reskey;
                            }
                        }
                    }
                }
            }

            // parse dep and ref
            Dictionary<string, HashSet<string>> resdep = new Dictionary<string, HashSet<string>>();
            Dictionary<string, HashSet<string>> resref = new Dictionary<string, HashSet<string>>();
            foreach (var kvpassetreskey in asset2reskey)
            {
                var asset = kvpassetreskey.Key;
                //var reskey = kvpassetreskey.Value;

                HashSet<string> deplist;
                if (!resdep.TryGetValue(asset, out deplist))
                {
                    deplist = new HashSet<string>();
                    resdep[asset] = deplist;
                }

                var deps = GetDependencies(asset);
                for (int i = 0; i < deps.Length; ++i)
                {
                    var dep = deps[i];
                    deplist.Add(dep);

                    HashSet<string> reflist;
                    if (!resref.TryGetValue(dep, out reflist))
                    {
                        reflist = new HashSet<string>();
                        resref[dep] = reflist;
                    }
                    reflist.Add(asset);
                }
            }

            using (var sw = PlatDependant.OpenWriteText(output))
            {
                // check cross mod/dist ref
                bool crossdepfound = false;
                foreach (var kvpdep in resdep)
                {
                    var asset = kvpdep.Key;
                    var deps = kvpdep.Value;

                    var assetkey = asset2reskey[asset];
                    foreach (var dep in deps)
                    {
                        if (!asset2reskey.ContainsKey(dep))
                        {
                            continue; // not in build? check later.
                        }
                        var depkey = asset2reskey[dep];
                        if (assetkey == depkey)
                        {
                            continue; // the same package
                        }
                        else if (depkey == "m--d-")
                        {
                            continue; // any asset can refer the assets in main package.
                        }
                        else if (depkey.EndsWith("-d-") && assetkey.StartsWith(depkey))
                        {
                            continue; // the dist package refer non-dist package.
                        }
                        else if (depkey.StartsWith("m--d-") && assetkey.EndsWith(depkey.Substring(2)))
                        {
                            continue; // the mod package refer non-mod package.
                        }
                        else
                        {
                            if (!crossdepfound)
                            {
                                crossdepfound = true;
                                sw.WriteLine("Cross mod/dist reference found! See below:");
                            }
                            sw.Write(asset);
                            sw.Write(" (");
                            sw.Write(assetkey);
                            sw.Write(") -> ");
                            sw.Write(dep);
                            sw.Write(" (");
                            sw.Write(depkey);
                            sw.Write(")");
                            sw.WriteLine();
                        }
                    }
                }
                if (!crossdepfound)
                {
                    sw.WriteLine("No cross mod/dist reference found.");
                }

                // check non build dep
                sw.WriteLine();
                bool nonbuilddepfound = false;
                foreach (var kvpdep in resdep)
                {
                    var asset = kvpdep.Key;
                    var deps = kvpdep.Value;

                    foreach (var dep in deps)
                    {
                        if (!asset2reskey.ContainsKey(dep) && !nodepassets.Contains(dep))
                        {
                            if (dep.StartsWith("Assets/") || dep.StartsWith("Packages/") && !string.IsNullOrEmpty(CapsModEditor.GetAssetModName(dep)))
                            {
                                if (!nonbuilddepfound)
                                {
                                    nonbuilddepfound = true;
                                    sw.WriteLine("Non build dependency found! See below:");
                                }
                                sw.Write(asset);
                                sw.Write(" -> ");
                                sw.Write(dep);
                                sw.WriteLine();
                            }
                        }
                    }
                }
                if (!nonbuilddepfound)
                {
                    sw.WriteLine("No non build dependency found.");
                }
            }

            EditorUtility.OpenWithDefaultApp(output);
        }

        public static string[] GetDependencies(string asset)
        {
            List<string> deps = new List<string>();
            LinkedList<string> parsingList = new LinkedList<string>();
            HashSet<string> parsingSet = new HashSet<string>();
            if (!string.IsNullOrEmpty(asset) && !CapsResInfoEditor.IsAssetScript(asset))
            {
                parsingSet.Add(asset);
                parsingList.AddLast(asset);
                var node = parsingList.First;
                while (node != null)
                {
                    var cur = node.Value;
                    try
                    {
                        var directdeps = AssetDatabase.GetDependencies(cur, false);
                        if (directdeps != null)
                        {
                            for (int i = 0; i < directdeps.Length; ++i)
                            {
                                var dep = directdeps[i];
                                if (!CapsResInfoEditor.IsAssetScript(dep))
                                {
                                    if (parsingSet.Add(dep))
                                    {
                                        parsingList.AddLast(dep);
                                        deps.Add(dep);
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                    node = node.Next;
                }
            }
            return deps.ToArray();
        }

        public static void GetAssetBundleModAndDist(string abname, out string mod, out string dist)
        {
            mod = null;
            dist = null;
            if (abname == null)
            {
                return;
            }
            abname = abname.ToLower();
            if (abname.EndsWith(".ab"))
            {
                var extindex = abname.IndexOf('.');
                abname = abname.Substring(0, extindex);
            }
            if (!abname.StartsWith("m-") && !abname.StartsWith("d-"))
            {
                return;
            }
            string dpart = null;
            if (abname.StartsWith("m-"))
            {
                var mendIndex = abname.IndexOf("-d-");
                if (mendIndex > 0)
                {
                    mod = abname.Substring("m-".Length, mendIndex - "m-".Length);
                    dpart = abname.Substring(mendIndex + "-d-".Length);
                }
                else
                {
                    return;
                }
            }
            else
            {
                dpart = abname.Substring("d-".Length);
            }

            if (dpart != null)
            {
                var dflags = CapsDistributeEditor.GetAllDistributesCached();
                for (int i = 0; i < dflags.Length; ++i)
                {
                    var dflag = dflags[i].ToLower();
                    if (dpart.StartsWith(dflag))
                    {
                        dist = dflag;
                        return;
                    }
                }

                var dendindex = dpart.IndexOf("-");
                if (dendindex >= 0)
                {
                    dist = dpart.Substring(0, dendindex);
                }
            }
        }

        public static void GetSptModAndDist(string relativePath, out string mod, out string dist)
        {
            if (relativePath == null)
            {
                mod = null;
                dist = null;
                return;
            }
            relativePath = relativePath.Replace('\\', '/');
            relativePath = relativePath.Trim('/');
            if (relativePath.StartsWith("mod/", StringComparison.InvariantCultureIgnoreCase))
            {
                relativePath = relativePath.Substring("mod/".Length);
                relativePath = relativePath.TrimStart('/');
                int modendindex = relativePath.IndexOf('/');
                if (modendindex >= 0)
                {
                    mod = relativePath.Substring(0, modendindex);
                    relativePath = relativePath.Substring(modendindex + 1);
                    relativePath = relativePath.TrimStart('/');
                }
                else
                {
                    mod = relativePath;
                    relativePath = "";
                }
            }
            else
            {
                mod = null;
            }
            if (relativePath.StartsWith("dist/", StringComparison.InvariantCultureIgnoreCase))
            {
                relativePath = relativePath.Substring("dist/".Length);
                relativePath = relativePath.TrimStart('/');
                int distendindex = relativePath.IndexOf('/');
                if (distendindex >= 0)
                {
                    dist = relativePath.Substring(0, distendindex);
                }
                else
                {
                    dist = relativePath;
                }
            }
            else
            {
                dist = null;
            }
        }
    }
}
