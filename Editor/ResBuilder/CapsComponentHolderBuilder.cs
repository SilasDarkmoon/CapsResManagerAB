using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Capstones.UnityEngineEx;

namespace Capstones.UnityEditorEx
{
    [InitializeOnLoad]
    public class CapsComponentHolderBuilder : CapsResBuilder.BaseResBuilderEx<CapsComponentHolderBuilder>
    {
        private static HierarchicalInitializer _Initializer = new HierarchicalInitializer(0);
        
        public override void Prepare(string output)
        { // TODO: if we do BuildComponentHolder() here, it will cost a lot of time, mostly useless.
        }

        [MenuItem("Res/Build Component Holder", priority = 202010)]
        public static void BuildComponentHolder()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);

            HashSet<Type> compTypes = new HashSet<Type>();
            compTypes.Add(typeof(Transform));
            var phpath = "Assets/Mods/" + CapsEditorUtils.__MOD__ + "/Resources/ComponentHolder.prefab";
            GameObject tgo = null;
            if (PlatDependant.IsFileExist(phpath))
            {
                var old = AssetDatabase.LoadMainAssetAtPath(phpath) as GameObject;
                if (old)
                {
                    tgo = GameObject.Instantiate(old);
                    DeleteMissingReference(tgo);
                    var oldcomps = tgo.GetComponentsInChildren(typeof(Component), true);
                    for (int i = 0; i < oldcomps.Length; ++i)
                    {
                        var oldcomp = oldcomps[i];
                        if (oldcomp != null)
                        {
                            compTypes.Add(oldcomp.GetType());
                        }
                    }
                }
            }
            if (tgo == null)
            {
                tgo = new GameObject("ComponentHolder");
                tgo.SetActive(false);
            }

            var soroot = "Assets/Mods/" + CapsEditorUtils.__MOD__ + "/Resources/ScriptableObjects/";
            if (System.IO.Directory.Exists(soroot))
            {
                System.IO.Directory.Delete(soroot, true);
            }
            PlatDependant.CreateFolder(soroot);

            var allassets = AssetDatabase.GetAllAssetPaths();
            for (int i = 0; i < allassets.Length; ++i)
            {
                var asset = allassets[i];
                var pinfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(asset);
                if (pinfo != null)
                {
                    if (pinfo.source != UnityEditor.PackageManager.PackageSource.Local
                        && pinfo.source != UnityEditor.PackageManager.PackageSource.Embedded
                        )
                    {
                        continue;
                    }
                }



                if (asset.EndsWith(".prefab", StringComparison.InvariantCultureIgnoreCase))
                {
                    var prefab = AssetDatabase.LoadMainAssetAtPath(asset) as GameObject;
                    if (prefab == null)
                    {
                        Debug.LogError("Cannot load " + asset);
                        continue;
                    }
                    var comps = prefab.GetComponentsInChildren(typeof(Component), true);
                    for (int j = 0; j < comps.Length; ++j)
                    {
                        var comp = comps[j];
                        if (comp == null)
                        {
                            Debug.LogError("Prefab has invalid component: " + asset);
                            continue;
                        }
                        var compt = comp.GetType();
                        if (compTypes.Add(compt))
                        {
                            // should add this comp to tgo.
                            var hgo = new GameObject(compt.Name);
                            hgo.transform.SetParent(tgo.transform, false);
                            AddComponentWithDep(hgo, compt, compTypes);
                        }
                    }
                }
                else if (asset.EndsWith(".unity", StringComparison.InvariantCultureIgnoreCase)
                    || asset.EndsWith(".u3d", StringComparison.InvariantCultureIgnoreCase))
                {
                    var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(asset, UnityEditor.SceneManagement.OpenSceneMode.Additive);
                    if (!scene.IsValid())
                    {
                        Debug.LogError("Cannot load " + asset);
                        continue;
                    }
                    var roots = scene.GetRootGameObjects();
                    for (int k = 0; k < roots.Length; ++k)
                    {
                        var root = roots[k];
                        var comps = root.GetComponentsInChildren(typeof(Component), true);
                        for (int j = 0; j < comps.Length; ++j)
                        {
                            var comp = comps[j];
                            if (comp == null)
                            {
                                Debug.LogError("Scene has invalid component: " + asset);
                                continue;
                            }
                            var compt = comp.GetType();
                            if (compTypes.Add(compt))
                            {
                                // should add this comp to tgo.
                                var hgo = new GameObject(compt.Name);
                                hgo.transform.SetParent(tgo.transform, false);
                                AddComponentWithDep(hgo, compt, compTypes);
                            }
                        }
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
                }
                else
                {
                    string assettype, assetmod, assetdist;
                    ResManager.GetAssetNormPath(asset, out assettype, out assetmod, out assetdist);
                    if (assettype == "res")
                    {
                        var type = AssetDatabase.GetMainAssetTypeAtPath(asset);
                        if (type != null
                            //&& type.Assembly.FullName != "Assembly-CSharp-firstpass"
                            //&& type.Assembly.FullName != "Assembly-CSharp"
                            && type.IsSubclassOf(typeof(ScriptableObject)))
                        {
                            if (compTypes.Add(type))
                            {
                                try
                                {
                                    var phso = ScriptableObject.CreateInstance(type);
                                    var target = soroot + "PH_" + type.Name + ".asset";
                                    AssetDatabase.CreateAsset(phso, target);
                                }
                                catch (Exception e)
                                {
                                    Debug.LogException(e);
                                }
                            }
                        }
                    }
                }
            }
            PlatDependant.CreateFolder(System.IO.Path.GetDirectoryName(phpath));
            PlatDependant.DeleteFile(phpath);
            PrefabUtility.SaveAsPrefabAsset(tgo, phpath);
            GameObject.DestroyImmediate(tgo);
        }

        private static void SafeAddMissingComponent(GameObject go, Type t)
        {
            try
            {
                if (!go.GetComponent(t))
                {
                    go.AddComponent(t);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
        private static void AddComponentWithDep(GameObject go, Type compt, HashSet<Type> compTypes)
        {
            var deps = compt.GetCustomAttributes(typeof(RequireComponent), true);
            if (deps != null)
            {
                for (int k = 0; k < deps.Length; ++k)
                {
                    var dep = deps[k] as RequireComponent;
                    if (dep != null)
                    {
                        var dep0 = dep.m_Type0;
                        if (dep0 != null && dep0.IsSubclassOf(typeof(Component)))
                        {
                            AddComponentWithDep(go, dep0, compTypes);
                        }
                        var dep1 = dep.m_Type1;
                        if (dep1 != null && dep1.IsSubclassOf(typeof(Component)))
                        {
                            AddComponentWithDep(go, dep1, compTypes);
                        }
                        var dep2 = dep.m_Type2;
                        if (dep2 != null && dep2.IsSubclassOf(typeof(Component)))
                        {
                            AddComponentWithDep(go, dep2, compTypes);
                        }
                    }
                }
            }
            SafeAddMissingComponent(go, compt);
            compTypes.Add(compt);
        }
        private static bool DeleteMissingReference(GameObject root)
        {
            //if (PrefabUtility.GetPrefabInstanceStatus(root) != PrefabInstanceStatus.NotAPrefab)
            //{
            //    return false;
            //}

            bool changed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root) > 0;

            var roottrans = root.transform;
            for (int i = 0; i < roottrans.childCount; ++i)
            {
                var child = roottrans.GetChild(i);
                changed |= DeleteMissingReference(child.gameObject);
            }

            return changed;
        }
    }
}
