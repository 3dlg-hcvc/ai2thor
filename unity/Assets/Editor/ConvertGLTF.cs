using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityGLTF;
using Newtonsoft.Json.Linq;

public class ComponentMetadata
{
    public string name;
    public string tag;
    public string parentId;
    public int layer;
    public bool isActive;
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
}

namespace ExtensionMethods
{
    public static class MyExtensions
    {
    }
}

public class ConvertGLTF : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public static void ExportObject(GameObject game_object, string output_dir)
    {
        MethodInfo export_method = typeof(GLTFExportMenu).GetMethod("Export", BindingFlags.NonPublic | BindingFlags.Static);
        Transform[] transforms = new Transform[] {game_object.transform};
        string output_filename = game_object.name;
        bool already_exist = File.Exists(output_dir + "/" + game_object.name + ".glb");
        int suffix = 1;
        while (already_exist) {
            output_filename = game_object.name + "_" + suffix.ToString();
            already_exist = File.Exists(output_dir + "/" + output_filename + ".glb");
            suffix += 1;
        }
        export_method.Invoke(null, new object[] {transforms, true, output_filename});

        Transform[] children_nodes;
        children_nodes = game_object.GetComponentsInChildren<Transform>(true);
        JObject asset_metadata = new JObject();
        foreach (Transform child in children_nodes) {
            GameObject child_node = child.gameObject;
            Transform parent_transform = child.parent;
            int parent_id = -1;
            if (parent_transform != null)
                parent_id = parent_transform.gameObject.GetInstanceID();
            ComponentMetadata component = new ComponentMetadata();
            component.name = child_node.name;
            component.layer = child_node.layer;
            component.tag = child_node.tag;
            component.isActive = child_node.activeSelf;
            // Vector3 openRot = coo.openPositions[i];
            // Vector3 closeRot = coo.closedPositions[i];
            component.localPosition = child.localPosition;
            component.localRotation = child.localRotation;
            component.localScale = child.localScale;
            component.parentId = parent_id.ToString();
            string component_json = JsonUtility.ToJson(component);
            asset_metadata[child_node.GetInstanceID().ToString()] = JObject.Parse(component_json);

            MonoBehaviour[] scripts;
            scripts = child_node.GetComponents<MonoBehaviour>();
            JObject script_metadata = new JObject();
            foreach (MonoBehaviour script in scripts) {
                string script_name = script.GetType().Name;
                if (script.GetType() == typeof(SimObjPhysics)) {
                    SimObjPhysics sim = (SimObjPhysics) script;
                    sim.Init();
                }
                string script_json = JsonUtility.ToJson(script);
                script_metadata[script_name] = JObject.Parse(script_json);
            }
            if (script_metadata.Count > 0) {
                asset_metadata[child_node.GetInstanceID().ToString()][typeof(MonoBehaviour).Name] = script_metadata;
            }
        }
        File.WriteAllText(output_dir + "/" + output_filename + ".json", asset_metadata.ToString());
    }

#if UNITY_EDITOR

    [MenuItem("ConvertGLTF/Convert prefab models to glb format")]
    public static void ConvertObjectToGLB() {
        var path = EditorUtility.SaveFolderPanel("glTF Export Path", GLTFSceneExporter.SaveFolderPath, "ai2thor-object");
        GLTFSceneExporter.SaveFolderPath = path;
        Debug.Log(GLTFSceneExporter.SaveFolderPath);

        // Find all assets labelled with 'architecture' :
        string[] prefab_guids = AssetDatabase.FindAssets("t:Prefab", new string[] {"Assets/Physics/SimObjsPhysics"});

        foreach (string prefab_guid in prefab_guids)
        {
            var prefab_path = AssetDatabase.GUIDToAssetPath(prefab_guid);
            GameObject game_object = AssetDatabase.LoadAssetAtPath<GameObject>(prefab_path);
            Debug.Log(game_object.name);

            ExportObject(game_object, path);
        }
    }

    [MenuItem("ConvertGLTF/Convert scene objects to glb format")]
    public static void ConvertSceneToGLB() {
        var path = EditorUtility.SaveFolderPanel("glTF Export Path", GLTFSceneExporter.SaveFolderPath, "ai2thor-scene");

        string[] scene_guids = AssetDatabase.FindAssets("t:Scene", new string[] {"Assets/Scenes"});
        foreach (string scene_guid in scene_guids) {
            var scene_path = AssetDatabase.GUIDToAssetPath(scene_guid);
            Scene scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scene_path);
            string scene_name = scene.name;
            string floorplan_name = scene_name.Replace("_physics", "");
            GameObject[] roots = scene.GetRootGameObjects();
            foreach (GameObject root in roots) {
                if (root.name == "Objects" || root.name == "Structure") {
                    string output_dir = path + "/" + scene.name + "/" + root.name;
                    Directory.CreateDirectory(output_dir);
                    GLTFSceneExporter.SaveFolderPath = output_dir;
                    Debug.Log(GLTFSceneExporter.SaveFolderPath);

                    Vector3 root_position = root.transform.localPosition;
                    foreach (Transform child in root.transform) {
                        Debug.Log(child.name);
                        if (child.name == "HideAndSeek" || child.name == "HideAndSeekSingleCount")
                            continue;

                        if (child.name == floorplan_name) {
                            Vector3 tmp_position = child.transform.localPosition;
                            foreach (Transform child2 in child.transform) {
                                GameObject game_object2 = child2.gameObject;
                                game_object2.transform.Translate(root_position + tmp_position, Space.World);
                                ExportObject(game_object2, output_dir);
                            }    
                        }
                        else {
                            GameObject game_object = child.gameObject;
                            game_object.transform.Translate(root_position, Space.World);
                            ExportObject(game_object, output_dir);
                        }
                    }
                }
            }
        }
    }

    [MenuItem("ConvertGLTF/Export materials")]
    public static void ExportMaterials() {
        var path = EditorUtility.SaveFolderPanel("Texture Export Path", GLTFSceneExporter.SaveFolderPath, "procthor-material");
        string recources_path = "Assets/Resources/";
        string[] material_guids = AssetDatabase.FindAssets("t:Material", new string[] {recources_path});
        foreach (string material_guid in material_guids) {
            var material_path = AssetDatabase.GUIDToAssetPath(material_guid);

            Debug.Log(material_path);
            Material mat = Resources.Load(material_path.Replace(recources_path, "").Replace(".mat", ""), typeof(Material)) as Material;
            if (mat == null) {
                Debug.Log("Failed to load material: " + material_path);
                continue;
            }

            Shader shader = mat.shader;
            if (shader == null)
                Debug.Log("Failed to load share: " + mat.name);

            string folder_path = path + "/" + mat.name;
            if(!Directory.Exists(folder_path)) {
                Directory.CreateDirectory(folder_path);
            }
            
            JObject metadata = new JObject();
            metadata["Texture"] = new JObject();
            metadata["Float"] = new JObject();
            metadata["Color"] = new JObject();
            for (int i = 0; i < shader.GetPropertyCount(); i++) {
                string property_name = shader.GetPropertyName(i);
                var property_type = shader.GetPropertyType(i);
                if (property_type == UnityEngine.Rendering.ShaderPropertyType.Texture) {
                    Texture texture = mat.GetTexture(property_name);
                    Debug.Log(property_name + " : " + texture?.name, texture);
                    if (texture == null)
                        continue;
                    int instance_id = texture.GetInstanceID();
                    string asset_path = AssetDatabase.GetAssetPath(instance_id);
                    Debug.Log(property_name + "instance id : " + instance_id);
                    Debug.Log(property_name + "asset path : " + asset_path);
                    string file_path = folder_path + "/" + Path.GetFileName(asset_path);
                    if(!File.Exists(file_path)) {
                        FileUtil.CopyFileOrDirectory(asset_path, file_path);
                        metadata["Texture"][property_name] = file_path;
                    }
                }
                else if (property_type == UnityEngine.Rendering.ShaderPropertyType.Float) {
                    float parameter = mat.GetFloat(property_name);
                    metadata["Float"][property_name] = parameter;
                    Debug.Log(property_name + " : " + parameter.ToString());
                }
                else if (property_type == UnityEngine.Rendering.ShaderPropertyType.Color) {
                    Color color = mat.GetColor(property_name);
                    string color_json = JsonUtility.ToJson(color);
                    metadata["Color"][property_name] = JObject.Parse(color_json);
                    Debug.Log(property_name + " : " + color.ToString());
                }
            }
            File.WriteAllText(folder_path + "/" + mat.name + ".json", metadata.ToString());
        }
    }

# endif

}
