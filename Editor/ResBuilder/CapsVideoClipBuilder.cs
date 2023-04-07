using Capstones.UnityEngineEx;
using System.Collections.Generic;
using UnityEditor;

namespace Capstones.UnityEditorEx
{
    [InitializeOnLoad]
    public class CapsVideoClipBuilder : CapsResBuilder.BaseResBuilderEx<CapsVideoClipBuilder>
    {
        private static HierarchicalInitializer _Initializer = new HierarchicalInitializer(0);

        public override string FormatBundleName(string asset, string mod, string dist, string norm)
        {
            if (AssetDatabase.GetMainAssetTypeAtPath(asset) == typeof(UnityEngine.Video.VideoClip))
            {
                System.Text.StringBuilder sbbundle = new System.Text.StringBuilder();
                sbbundle.Append("m-");
                sbbundle.Append((mod ?? "").ToLower());
                sbbundle.Append("-d-");
                sbbundle.Append((dist ?? "").ToLower());
                sbbundle.Append("-");
                sbbundle.Append(System.IO.Path.GetDirectoryName(norm).ToLower());
                sbbundle.Replace('\\', '-');
                sbbundle.Replace('/', '-');
                sbbundle.Append(".vc");
                sbbundle.Append(".ab");
                return sbbundle.ToString();
            }
            return null;
        }

        public override void PostBuildWork(string mod, CapsResBuilder.CapsResBuildWork work, string dest)
        {
            var interroot = System.IO.Path.GetDirectoryName(dest) + "/tmp/";
            var interdir = interroot + System.IO.Path.GetFileName(dest);
            System.IO.Directory.CreateDirectory(interdir);

            List<AssetBundleBuild> postabs = new List<AssetBundleBuild>();
            for (int i = 0; i < work.ABs.Length; ++i)
            {
                var ab = work.ABs[i];
                if (ab.assetBundleName.EndsWith(".vc.ab"))
                {
                    postabs.Add(ab);
                }
            }

            BuildPipeline.BuildAssetBundles(interdir, postabs.ToArray(), BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.DeterministicAssetBundle, EditorUserBuildSettings.activeBuildTarget);

            var built = System.IO.Directory.GetFiles(interdir);
            for (int i = 0; i < built.Length; ++i)
            {
                var src = built[i];
                if (src.EndsWith(".vc.ab"))
                {
                    var dst = dest + "/" + System.IO.Path.GetFileName(src);
                    PlatDependant.MoveFile(src, dst);
                }
            }

            System.IO.Directory.Delete(interroot, true);
        }
    }
}