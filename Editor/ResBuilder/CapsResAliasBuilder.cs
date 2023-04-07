using Capstones.UnityEngineEx;
using System.Collections.Generic;
using UnityEditor;

namespace Capstones.UnityEditorEx
{
    [InitializeOnLoad]
    public class CapsResAliasBuilder : CapsResBuilder.BaseResBuilderEx<CapsResAliasBuilder>, ResManager.EditorResLoader.IEditorResLoaderEx
    {
        private static HierarchicalInitializer _Initializer = new HierarchicalInitializer(0);

        static CapsResAliasBuilder()
        {
            ResManager.EditorResLoader.ExLoaders.Add(_BuilderEx);
        }

        public override void Prepare(string output)
        {
            _DelayedRevAliasMap = new Dictionary<string, List<CapsResManifestItem>>();
        }
        public override void Cleanup()
        {
            _DelayedRevAliasMap = null;
        }

        private Dictionary<string, string> _AliasMap;
        private Dictionary<string, List<CapsResManifestItem>> _DelayedRevAliasMap;
        private List<CapsResManifestItem> _CurrentDelayedLink;
        public override string FormatBundleName(string asset, string mod, string dist, string norm)
        {
            _AliasMap = null;
            if (AssetDatabase.GetMainAssetTypeAtPath(asset) == typeof(CapsResAlias))
            {
                _AliasMap = new Dictionary<string, string>();
                var aliasinfo = AssetDatabase.LoadAssetAtPath<CapsResAlias>(asset);
                var target = aliasinfo.Target;
                if (System.IO.Directory.Exists(target))
                { // whole folder
                    var dir = System.IO.Path.GetDirectoryName(asset);
                    var files = System.IO.Directory.GetFiles(target);
                    for (int i = 0; i < files.Length; ++i)
                    {
                        var path = files[i].Replace('\\', '/');
                        var filename = System.IO.Path.GetFileName(path);
                        _AliasMap[System.IO.Path.Combine(dir, filename)] = path;
                    }
                }
                else
                { // link single file
                    var dir = System.IO.Path.GetDirectoryName(asset);
                    var aliasname = System.IO.Path.GetFileNameWithoutExtension(asset);
                    _AliasMap[System.IO.Path.Combine(dir, aliasname)] = target;
                }
            }
            _CurrentDelayedLink = null;
            _DelayedRevAliasMap.TryGetValue(asset, out _CurrentDelayedLink);
            return null;
        }
        public override void ModifyItem(CapsResManifestItem item)
        {
            if (_AliasMap != null)
            {
                var manifest = item.Manifest; // NOTICE: alias can only link to asset in the same mod.
                foreach (var kvp in _AliasMap)
                {
                    var linkpath = kvp.Key;
                    var targetpath = kvp.Value;

                    var linknode = manifest.AddOrGetItem(linkpath);
                    var targetnode = manifest.AddOrGetItem(targetpath);

                    var linkitem = new CapsResManifestItem(linknode);
                    linkitem.Type = (int)CapsResManifestItemType.Redirect;
                    linkitem.BRef = null;
                    linknode.Item = linkitem;

                    if (targetnode.Item != null)
                    {
                        linkitem.Ref = targetnode.Item;
                    }
                    else
                    {
                        List<CapsResManifestItem> delayed;
                        if (!_DelayedRevAliasMap.TryGetValue(targetpath, out delayed))
                        {
                            delayed = new List<CapsResManifestItem>();
                            _DelayedRevAliasMap[targetpath] = delayed;
                        }
                        delayed.Add(linkitem);
                    }
                }
            }

            if (_CurrentDelayedLink != null)
            {
                foreach (var linkitem in _CurrentDelayedLink)
                {
                    linkitem.Ref = item;
                }
            }
        }

        public string FindFile(string path)
        {
            var aliasfile = path + ".asset";
            if (System.IO.File.Exists(aliasfile))
            {
                if (AssetDatabase.GetMainAssetTypeAtPath(aliasfile) == typeof(CapsResAlias))
                {
                    var aliasinfo = AssetDatabase.LoadAssetAtPath<CapsResAlias>(aliasfile);
                    var target = aliasinfo.Target;
                    if (System.IO.File.Exists(target))
                    {
                        return target;
                    }
                }
            }
            var dir = System.IO.Path.GetDirectoryName(path);
            if (System.IO.Directory.Exists(dir))
            {
                var files = System.IO.Directory.GetFiles(dir);
                for (int i = 0; i < files.Length; ++i)
                {
                    var file = files[i].Replace('\\', '/');
                    if (AssetDatabase.GetMainAssetTypeAtPath(file) == typeof(CapsResAlias))
                    {
                        var aliasinfo = AssetDatabase.LoadAssetAtPath<CapsResAlias>(file);
                        var target = aliasinfo.Target;
                        if (System.IO.Directory.Exists(target))
                        {
                            var tarfile = target + "/" + System.IO.Path.GetFileName(path);
                            if (System.IO.File.Exists(tarfile))
                            {
                                return tarfile;
                            }
                        }
                    }
                }
            }
            return null;
        }
    }
}