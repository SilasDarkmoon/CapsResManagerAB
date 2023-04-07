using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Profiling;

namespace Capstones.UnityEngineEx
{
    public class CapsResManifestItem
    {
        // Hierarchy Cache Fields
        public CapsResManifest Manifest;
        public CapsResManifestNode Node;

        // Data Fields
        public int Type; // CapsResManifestItemType and Defined by Other Modules
        public string BRef;
        public CapsResManifestItem Ref;
        public ScriptableObject ExInfo;

        // Runtime
        public object Attached;

        public CapsResManifestItem(CapsResManifestNode node)
        {
            Node = node;
            Manifest = node.Manifest;
        }
    }
    public class CapsResManifestNode
    {
        // Hierarchy Cache Fields
        public CapsResManifest Manifest;
        public CapsResManifestNode Parent;

        // Data Fields
        public string PPath;
        public SortedList<string, CapsResManifestNode> Children;
        public CapsResManifestItem Item;

        public CapsResManifestNode(CapsResManifest manifest)
        {
            Manifest = manifest;
        }
        public CapsResManifestNode(CapsResManifestNode parent, string ppath)
        {
            Parent = parent;
            Manifest = parent.Manifest;
            PPath = ppath;
        }

        public int GetDepth()
        {
            int depth = 0;
            var node = this;
            while (node.Parent != null)
            {
                ++depth;
                node = node.Parent;
            }
            return depth;
        }
        public string GetFullPath()
        {
            System.Text.StringBuilder path = new System.Text.StringBuilder();
            var node = this;
            while (node.Parent != null)
            {
                if (path.Length != 0)
                {
                    path.Insert(0, "/");
                }
                path.Insert(0, node.PPath);
                node = node.Parent;
            }
            return path.ToString();
        }
        public bool TryGetItem(string path, int pathStartIndex, out CapsResManifestNode node)
        {
            if (Children == null)
            {
                node = null;
                return false;
            }
            var splitindex = path.IndexOf('/', pathStartIndex);
            if (splitindex >= 0)
            {
                var part = path.Substring(pathStartIndex, splitindex - pathStartIndex);
                CapsResManifestNode child;
                if (Children.TryGetValue(part, out child))
                {
                    return child.TryGetItem(path, splitindex, out node);
                }
            }
            else
            {
                var part = path.Substring(pathStartIndex);
                return Children.TryGetValue(part, out node);
            }
            node = null;
            return false;
        }

        public void TrimExcess(bool recursive)
        {
            if (Children != null)
            {
                Children.TrimExcess();
                if (recursive)
                {
                    var values = Children.Values;
                    for (int i = 0; i < values.Count; ++i)
                    {
                        values[i].TrimExcess(true);
                    }
                }
            }
        }
    }
    public class CapsResManifest
    {
        public string MFlag;
        public string DFlag;
        public bool InMain;

        public CapsResManifestNode Root;

        public void DiscardAllNodes()
        {
            Root = null;
        }
        private static readonly char[] _DefaultSeperateChars = new[] { '\\', '/' };
        public bool TryGetItem(string path, out CapsResManifestNode item, params char[] seperateChars)
        {
            if (seperateChars == null || seperateChars.Length <= 0)
            {
                seperateChars = _DefaultSeperateChars;
            }
            item = null;
            if (path != null)
            {
                int sindex = 0;
                CapsResManifestNode curNode = Root;
                while (curNode != null)
                {
                    int eindex = path.IndexOfAny(seperateChars, sindex);
                    if (eindex >= 0)
                    {
                        var part = path.Substring(sindex, eindex - sindex);
                        if (curNode.Children != null)
                        {
                            if (!curNode.Children.TryGetValue(part, out curNode))
                            {
                                item = null;
                                return false;
                            }
                        }
                        else
                        {
                            item = null;
                            return false;
                        }
                        sindex = eindex + 1;
                    }
                    else
                    {
                        var part = path.Substring(sindex);
                        if (curNode.Children != null)
                        {
                            return curNode.Children.TryGetValue(part, out item);
                        }
                        else
                        {
                            item = null;
                            return false;
                        }
                    }
                }
            }
            return false;
        }
        public bool TryGetItemIgnoreExt(string path, out CapsResManifestNode item, params char[] seperateChars)
        {
            if (seperateChars == null || seperateChars.Length <= 0)
            {
                seperateChars = _DefaultSeperateChars;
            }
            item = null;
            if (path != null)
            {
                int sindex = 0;
                CapsResManifestNode curNode = Root;
                while (curNode != null)
                {
                    int eindex = path.IndexOfAny(seperateChars, sindex);
                    CapsResManifestNode foundNode = null;
                    while (eindex >= 0)
                    {
                        var part = path.Substring(sindex, eindex - sindex);
                        if (curNode.Children != null)
                        {
                            if (curNode.Children.TryGetValue(part, out foundNode))
                            {
                                break;
                            }
                            else
                            {
                                eindex = path.IndexOfAny(seperateChars, eindex + 1);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    if (foundNode != null)
                    {
                        curNode = foundNode;
                        sindex = eindex + 1;
                    }
                    else
                    {
                        var part = path.Substring(sindex);
                        if (curNode.Children != null)
                        {
                            var children = curNode.Children;
                            var mini = 0;
                            var maxi = children.Count - 1;
                            var ci = maxi / 2;
                            while (true)
                            {
                                var child = children.Values[ci];
                                var compareResult = string.Compare(part, 0, child.PPath, 0, part.Length);
                                if (compareResult == 0)
                                {
                                    var ppath = child.PPath;
                                    if (ppath.Length == part.Length)
                                    {
                                        item = child;
                                        return true;
                                    }
                                    if (ppath[part.Length] == '.')
                                    {
                                        var lastIndex = ppath.LastIndexOf('.');
                                        if (lastIndex == part.Length)
                                        {
                                            item = child;
                                            return true;
                                        }
                                        else
                                        {
                                            // this is so bad...  TestSpt.lua - TestSpt.Z.lua
                                            var ii = ci;
                                            while ((--ii) >= mini)
                                            {
                                                var ichild = children.Values[ii];
                                                var ippath = ichild.PPath;
                                                if (!ippath.StartsWith(part))
                                                {
                                                    break;
                                                }
                                                if (ippath.Length == part.Length)
                                                {
                                                    item = child;
                                                    return true;
                                                }
                                                if (ippath[part.Length] == '.')
                                                {
                                                    lastIndex = ippath.LastIndexOf('.');
                                                    if (lastIndex == part.Length)
                                                    {
                                                        item = child;
                                                        return true;
                                                    }
                                                }
                                                else
                                                {
                                                    break;
                                                }
                                            }
                                            ii = ci;
                                            while ((++ii) <= maxi)
                                            {
                                                var ichild = children.Values[ii];
                                                var ippath = ichild.PPath;
                                                if (!ippath.StartsWith(part))
                                                {
                                                    break;
                                                }
                                                if (ippath.Length == part.Length)
                                                {
                                                    item = child;
                                                    return true;
                                                }
                                                if (ippath[part.Length] == '.')
                                                {
                                                    lastIndex = ippath.LastIndexOf('.');
                                                    if (lastIndex == part.Length)
                                                    {
                                                        item = child;
                                                        return true;
                                                    }
                                                }
                                                else
                                                {
                                                    break;
                                                }
                                            }
                                            break;
                                        }
                                    }
                                    else
                                    {
                                        compareResult = Comparer<char>.Default.Compare('.', ppath[part.Length]);
                                    }
                                }
                                if (compareResult > 0)
                                {
                                    var ni = ci + 1;
                                    if (ni > maxi)
                                    {
                                        break;
                                    }
                                    mini = ni;
                                    ci = (ni + maxi) / 2;
                                }
                                else
                                {
                                    var ni = ci - 1;
                                    if (ni < mini)
                                    {
                                        break;
                                    }
                                    maxi = ni;
                                    ci = (ni + mini) / 2;
                                }
                            }
                        }
                        item = null;
                        return false;
                    }
                }
            }
            return false;
        }
        public bool TryGetItem(string path, out CapsResManifestNode item)
        {
            return TryGetItem(path, out item, null);
        }
        public CapsResManifestNode GetItem(string path, params char[] seperateChars)
        {
            CapsResManifestNode rv;
            TryGetItem(path, out rv, seperateChars);
            return rv;
        }
        public CapsResManifestNode GetItem(string path)
        {
            CapsResManifestNode rv;
            TryGetItem(path, out rv);
            return rv;
        }
        public CapsResManifestNode AddOrGetItem(string path, params char[] seperateChars)
        {
            if (seperateChars == null || seperateChars.Length <= 0)
            {
                seperateChars = _DefaultSeperateChars;
            }
            CapsResManifestNode item = null;
            if (path != null)
            {
                if (Root == null)
                {
                    Root = new CapsResManifestNode(this);
                }
                CapsResManifestNode curNode = Root;
                int sindex = 0;
                while (curNode != null)
                {
                    if (curNode.Children == null)
                    {
                        curNode.Children = new SortedList<string, CapsResManifestNode>();
                    }
                    int eindex = path.IndexOfAny(seperateChars, sindex);
                    if (eindex >= 0)
                    {
                        var part = path.Substring(sindex, eindex - sindex);
                        CapsResManifestNode child;
                        if (!curNode.Children.TryGetValue(part, out child))
                        {
                            child = new CapsResManifestNode(curNode, part);
                            curNode.Children[part] = child;
                        }
                        curNode = child;
                        sindex = eindex + 1;
                    }
                    else
                    {
                        var part = path.Substring(sindex);
                        if (!curNode.Children.TryGetValue(part, out item))
                        {
                            item = new CapsResManifestNode(curNode, part);
                            curNode.Children[part] = item;
                        }
                        break;
                    }
                }
            }
            return item;
        }
        public CapsResManifestNode AddOrGetItem(string path)
        {
            return AddOrGetItem(path, null);
        }
        public void TrimExcess()
        {
            if (Root != null)
            {
                Root.TrimExcess(true);
            }
        }
        public static CapsResManifest Load(CapsResOnDiskManifest ondisk)
        {
            if (ondisk == null) return null;
            CapsResManifest rv = new CapsResManifest();

            rv.MFlag = ondisk.MFlag;
            rv.DFlag = ondisk.DFlag;
            rv.InMain = ondisk.InMain;

            if (ondisk.Assets != null && ondisk.Assets.Length > 0)
            {
                List<CapsResManifestNode> nodeStack = new List<CapsResManifestNode>();
                var parsedNodes = new CapsResManifestNode[ondisk.Assets.Length];
                for (int i = 0; i < ondisk.Assets.Length; ++i)
                {
                    var curDiskNode = ondisk.Assets[i];
                    var curlvl = curDiskNode.Level;
                    if (nodeStack.Count > curlvl)
                    {
                        var removecnt = nodeStack.Count - curlvl;
                        nodeStack.RemoveRange(nodeStack.Count - removecnt, removecnt);
                    }
                    while (nodeStack.Count < curlvl)
                    {
                        // something goes wrong here.
                        nodeStack.Add(null);
                    }

                    CapsResManifestNode curNode;
                    if (curlvl == 0)
                    {
                        curNode = new CapsResManifestNode(rv);
                        rv.Root = curNode;
                        //curNode.PPath = curDiskNode.PPath;
                    }
                    else
                    {
                        var parNode = nodeStack[curlvl - 1];
                        var ppath = curDiskNode.PPath;
                        curNode = new CapsResManifestNode(parNode, ppath);
                        if (parNode.Children == null)
                        {
                            parNode.Children = new SortedList<string, CapsResManifestNode>();
                        }
                        parNode.Children[ppath] = curNode;
                    }
                    nodeStack.Add(curNode);
                    parsedNodes[i] = curNode;

                    if (curDiskNode.Item != null && curDiskNode.Item.Type != 0)
                    {
                        var item = new CapsResManifestItem(curNode);
                        curNode.Item = item;
                        item.Type = curDiskNode.Item.Type;
                        if (curDiskNode.Item.BRef > 0)
                        {
                            if (ondisk.Bundles != null && ondisk.Bundles.Length > curDiskNode.Item.BRef)
                            {
                                item.BRef = ondisk.Bundles[curDiskNode.Item.BRef];
                            }
                        }
                        if (curDiskNode.Item.ExInfo != null)
                        {
                            item.ExInfo = UnityEngine.Object.Instantiate<ScriptableObject>(curDiskNode.Item.ExInfo);
                        }
                    }
                }
                for (int i = 0; i < ondisk.Assets.Length; ++i)
                {
                    var curDiskNode = ondisk.Assets[i];
                    if (curDiskNode.Item != null && curDiskNode.Item.Type != 0 && curDiskNode.Item.Ref > 0)
                    {
                        parsedNodes[i].Item.Ref = parsedNodes[curDiskNode.Item.Ref].Item;
                    }
                }
            }

            rv.TrimExcess();

            return rv;
        }
        public static CapsResOnDiskManifest Save(CapsResManifest inmem)
        {
            if (inmem == null) return null;
            CapsResOnDiskManifest rv = ScriptableObject.CreateInstance<CapsResOnDiskManifest>();
            rv.MFlag = inmem.MFlag;
            rv.DFlag = inmem.DFlag;
            rv.InMain = inmem.InMain;

            Dictionary<CapsResManifestItem, int> itemlines = new Dictionary<CapsResManifestItem, int>();
            Dictionary<CapsResOnDiskManifestItem, CapsResManifestItem> unknownRefItem = new Dictionary<CapsResOnDiskManifestItem, CapsResManifestItem>();
            Dictionary<string, int> bundlelines = new Dictionary<string, int>();
            List<string> bundles = new List<string>();
            bundles.Add("");
            List<CapsResOnDiskManifestNode> assets = new List<CapsResOnDiskManifestNode>();
            Action<CapsResManifestNode, int> SaveNode = null;
            SaveNode = (node, lvl) =>
            {
                var linen = assets.Count;
                var asset = new CapsResOnDiskManifestNode();
                asset.Level = lvl;
                asset.PPath = node.PPath;
                assets.Add(asset);
                if (node.Item != null)
                {
                    itemlines[node.Item] = linen;
                    CapsResOnDiskManifestItem item = new CapsResOnDiskManifestItem();
                    asset.Item = item;
                    item.Type = node.Item.Type;
                    if (node.Item.BRef != null)
                    {
                        var bref = node.Item.BRef;
                        int brefline;
                        if (!bundlelines.TryGetValue(bref, out brefline))
                        {
                            brefline = bundles.Count;
                            bundlelines[bref] = brefline;
                            bundles.Add(bref);
                        }
                        item.BRef = brefline;
                    }
                    if (node.Item.Ref != null)
                    {
                        var aref = node.Item.Ref;
                        int arefline;
                        if (itemlines.TryGetValue(aref, out arefline))
                        {
                            item.Ref = arefline;
                        }
                        else
                        {
                            unknownRefItem[item] = aref;
                        }
                    }
                    if (node.Item.ExInfo != null)
                    {
                        item.ExInfo = UnityEngine.Object.Instantiate<ScriptableObject>(node.Item.ExInfo);
                    }
                }
                if (node.Children != null)
                {
                    var values = node.Children.Values;
                    for (int i = 0; i < values.Count; ++i)
                    {
                        SaveNode(values[i], lvl + 1);
                    }
                }
            };

            if (inmem.Root != null)
            {
                SaveNode(inmem.Root, 0);
            }

            foreach (var kvpu in unknownRefItem)
            {
                int aref;
                if (itemlines.TryGetValue(kvpu.Value, out aref))
                {
                    kvpu.Key.Ref = aref;
                }
            }

            if (bundles.Count > 1)
            {
                rv.Bundles = bundles.ToArray();
            }
            if (assets.Count > 0)
            {
                rv.Assets = assets.ToArray();
            }

            return rv;
        }

        public static void MergeManifestNode(CapsResManifestNode mynode, CapsResManifestNode thnode, bool ignoreExt)
        {
            if (thnode.Children != null && thnode.Children.Count > 0)
            {
                if (mynode.Children == null)
                {
                    mynode.Children = new SortedList<string, CapsResManifestNode>();
                }
                foreach (var kvpChild in thnode.Children)
                {
                    var key = kvpChild.Key;
                    if (ignoreExt && kvpChild.Value.Item != null)
                    {
                        key = System.IO.Path.GetFileNameWithoutExtension(key);
                    }
                    CapsResManifestNode mychild;
                    if (!mynode.Children.TryGetValue(key, out mychild))
                    {
                        mychild = new CapsResManifestNode(mynode, key);
                        mynode.Children[key] = mychild;
                    }
                    if (kvpChild.Value.Item != null)
                    {
                        CapsResManifestItem item;
                        item = new CapsResManifestItem(mychild);
                        item.Type = (int)CapsResManifestItemType.Redirect;
                        item.Ref = kvpChild.Value.Item;
                        mychild.Item = item;
                    }
                    MergeManifestNode(mychild, kvpChild.Value, ignoreExt);
                }
            }
        }

        private static readonly string[] _MergeManifestModsRoots = new[] { "Assets/Mods", "Packages" };
        public void MergeManifest(CapsResManifest other)
        {
            if (other == null || other.Root == null || other.Root.Children == null)
            {
                return;
            }
            if (Root == null)
            {
                Root = new CapsResManifestNode(this);
            }
            CapsResManifestNode mynode = Root;
            CapsResManifestNode thnode;
            if (other.TryGetItem("Assets/CapsRes", out thnode))
            {
                MergeManifestNode(mynode, thnode, false);
            }
            //if (other.TryGetItem("Assets/CapsSpt", out thnode))
            //{
            //    MergeManifestNode(mynode, thnode);
            //}
            for (int i = 0; i < _MergeManifestModsRoots.Length; ++i)
            {
                if (other.TryGetItem(_MergeManifestModsRoots[i], out thnode))
                {
                    if (thnode.Children != null)
                    {
                        foreach (var kvpChild in thnode.Children)
                        {
                            var child = kvpChild.Value;
                            if (child.Children != null)
                            {
                                CapsResManifestNode thresnode;
                                if (child.Children.TryGetValue("CapsRes", out thresnode))
                                {
                                    MergeManifestNode(mynode, thresnode, false);
                                }
                                //if (child.Children.TryGetValue("CapsSpt", out thresnode))
                                //{
                                //    MergeManifestNode(mynode, thresnode);
                                //}
                            }
                        }
                    }
                }
            }
        }
        private static void CollapseManifestNode(CapsResManifestNode mynode, CapsResManifestNode thnode)
        {
            if (thnode.Children != null && thnode.Children.Count > 0)
            {
                if (mynode.Children == null)
                {
                    mynode.Children = new SortedList<string, CapsResManifestNode>();
                }
                foreach (var kvpChild in thnode.Children)
                {
                    CapsResManifestNode mychild;
                    if (!mynode.Children.TryGetValue(kvpChild.Key, out mychild))
                    {
                        mychild = kvpChild.Value;
                        mychild.Parent = mynode;
                        mynode.Children[kvpChild.Key] = mychild;
                        continue;
                    }
                    if (kvpChild.Value.Item != null)
                    {
                        CapsResManifestItem item;
                        item = kvpChild.Value.Item;
                        item.Node = mychild;
                        mychild.Item = item;
                    }
                    CollapseManifestNode(mychild, kvpChild.Value);
                }
            }
        }
        public void CollapseManifest(IList<string> flags)
        {
            if (Root != null && Root.Children != null)
            {
                CapsResManifestNode distnode;
                if (Root.Children.TryGetValue("dist", out distnode))
                {
                    Root.Children.Remove("dist");
                    if (distnode.Children != null)
                    {
                        for (int i = 0; i < flags.Count; ++i)
                        {
                            var flag = flags[i];
                            CapsResManifestNode flagnode;
                            if (distnode.Children.TryGetValue(flag, out flagnode))
                            {
                                CollapseManifestNode(Root, flagnode);
                            }
                        }
                    }
                }
            }
        }

        public IEnumerable<CapsResManifestNode> GetNodes()
        {
            if (Root != null)
            {
                Queue<CapsResManifestNode> nodes = new Queue<CapsResManifestNode>();
                nodes.Enqueue(Root);
                while (nodes.Count > 0)
                {
                    var node = nodes.Dequeue();
                    var children = node.Children;
                    if (children != null)
                    {
                        for (int i = 0; i < children.Count; ++i)
                        {
                            nodes.Enqueue(children.Values[i]);
                        }
                    }
                    yield return node;
                }
            }
        }
        public IEnumerable<CapsResManifestNode> Nodes { get { return GetNodes(); } }
        public IEnumerable<CapsResManifestItem> GetItems()
        {
            foreach (var node in Nodes)
            {
                if (node.Item != null)
                {
                    yield return node.Item;
                }
            }
        }
        public IEnumerable<CapsResManifestItem> Items { get { return GetItems(); } }
    }
}
