﻿using System;
using System.Collections.Generic;
using UnityEditorInternal;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Reflection;
using System.Linq;
using Object = UnityEngine.Object;

namespace ReferenceViewer
{
    public class GenerateAssetData
    {
        private static readonly Type[] ignoreTypes = new[]
        {
            typeof (Rigidbody),
            typeof (Rigidbody2D),
            typeof (Transform),
            typeof (Object)
        };

        public static void Build(string[] assetPaths, Action<AssetData[]> callback)
        {
            var result = new AssetData[0];
            for (var i = 0; i < assetPaths.Length; i++)
            {
                var assetPath = assetPaths[i];
                var assetData = new AssetData
                {
                    path = assetPath,
                    guid = AssetDatabase.AssetPathToGUID(assetPath)
                };

                var progress = (float)i / assetPaths.Length;
                switch (Path.GetExtension(assetPath))
                {
                    case ".prefab":
                        {
                            DisplayProgressBar(assetData.path, progress);
                            var prefab = AssetDatabase.LoadAssetAtPath(assetPath, typeof(Object));
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                            go.hideFlags = HideFlags.HideAndDontSave;

                            SearchGameObject(go, assetData);
                            Object.DestroyImmediate(go);
                        }
                        break;
                    case ".unity":
                        DisplayProgressBar(assetData.path, progress);
                        if (EditorApplication.OpenScene(assetPath))
                        {
                            foreach (var go in Object.FindObjectsOfType<GameObject>())
                            {
                                SearchGameObject(go, assetData, true);
                            }
                        }
                        break;
                    case ".controller":
                        var animator =
                            (AnimatorController)
                                AssetDatabase.LoadAssetAtPath(assetPath, typeof(AnimatorController));
                        for (var j = 0; j < animator.layerCount; j++)
                        {
                            var layer = animator.GetLayer(j);
                            for (var k = 0; k < layer.stateMachine.stateCount; k++)
                            {
                                var motion = layer.stateMachine.GetState(k).GetMotion();
                                if (motion)
                                    AddAttachedAsset(motion, assetData, false);
                            }
                        }
                        break;
                    default:
                        SearchFieldAndProperty(AssetDatabase.LoadAssetAtPath(assetPath, typeof(Object)), assetData);
                        break;
                }
                ArrayUtility.Add(ref result, assetData);

            }
            callback(result);
            EditorUtility.ClearProgressBar();
        }

        private static void DisplayProgressBar(string path, float progress)
        {
            EditorUtility.DisplayProgressBar(Path.GetFileName(path), Mathf.FloorToInt(progress * 100) + "% - " + Path.GetFileName(path), progress);
        }

        private static void SearchGameObject(GameObject go, AssetData assetData, bool isScene = false)
        {
            foreach (var obj in go.GetComponentsInChildren<Component>().Where(obj => obj))
            {
                AddAttachedAsset(obj, assetData, isScene);

                SearchFieldAndProperty(obj, assetData, isScene);
            }
        }

        private static void SearchFieldAndProperty(Object obj, AssetData assetData, bool isScene = false)
        {
            // TODO 
            if (obj is NavMeshAgent)
                return;

            if (ignoreTypes.Contains(obj.GetType()))
                return;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            var fields = obj.GetType().GetFields(flags);
            var properties = obj.GetType().GetProperties(flags);


            foreach (var value in fields.Select(info => info.GetValue(obj) as Object).Where(o => o != null))
            {
                AddAttachedAssets(value, assetData, isScene);
            }

            foreach (var info in properties.Where(info => info.CanRead))
            {
                var isObject = info.PropertyType.IsSubclassOf(typeof(Object)) ||
                               info.PropertyType == typeof(Object);

                if (!isObject || Ignore(obj.GetType(), info.Name)) continue;

                var value = info.GetValue(obj, new object[0]);
                if (value != null)
                    AddAttachedAssets(value, assetData, isScene);
            }
        }

        private static bool Ignore(Type type, string name)
        {
            var ignores = new[]
            {
                new {name = "mesh", type = typeof (MeshFilter)},
                new {name = "material", type = typeof (Renderer)},
                new {name = "material", type = typeof (WheelCollider)},
                new {name = "material", type = typeof (TerrainCollider)},
                new {name = "material", type = typeof (GUIElement)},
            };

            return ignores.Any(ignore => Ignore(type, name, ignore.type, ignore.name));
        }

        private static bool Ignore(Type type, string name, Type ignoreType, string ignoreName)
        {
            var isIgnoreType = type == ignoreType || type.IsSubclassOf(ignoreType);

            return isIgnoreType && name == ignoreName;
        }

        private static void AddAttachedAssets(object value, AssetData assetData, bool isScene)
        {
            var values = new List<Object>();
            if (value.GetType().IsArray)
            {
                values.AddRange(((Array)value).Cast<object>()
                    .Where(v => v != null)
                    .Select(v => v as Object));
            }
            else
            {
                values.Add(value as Object);
            }

            foreach (var v in values)
            {
                AddAttachedAsset(v, assetData, isScene);
            }
        }

        private static void AddAttachedAsset(Object value, AssetData assetData, bool isScene)
        {
            if (!value) return;

            if (value as MonoBehaviour)
            {
                value = MonoScript.FromMonoBehaviour(value as MonoBehaviour);
            }
            else if (value as ScriptableObject)
            {
                value = MonoScript.FromScriptableObject(value as ScriptableObject);
            }
            else if (isScene && PrefabUtility.GetPrefabType(value) == PrefabType.PrefabInstance)
            {
                value = PrefabUtility.GetPrefabParent(value);
            }

            var path = AssetDatabase.GetAssetPath(value);

            if (string.IsNullOrEmpty(path)) return;

            var guid = AssetDatabase.AssetPathToGUID(path);
            if (!assetData.reference.Contains(guid))
                assetData.reference.Add(guid);
        }
    }
}