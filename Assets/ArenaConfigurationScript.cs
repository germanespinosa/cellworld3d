using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System;

public class ArenaConfigurationScript : MonoBehaviour
{
    private static Mesh occlusionCollisionMesh;

    public GameObject arena_prefab;
    public GameObject occlusion_prefab;
    public bool logDetails = true;
    private string url = string.Empty;
    private string occlusions_url = string.Empty;
    public float occlusionHeight = 5f;
    private string localFilePath;
    private string localOcclusionsFilePath;
    private CellworldGameBridge bridge;

    void Start()
    {
        bridge = GetComponent<CellworldGameBridge>();
        if (bridge == null)
            bridge = FindFirstObjectByType<CellworldGameBridge>();

        url = "https://raw.githubusercontent.com/germanespinosa/cellworld_data/refs/heads/master/world_implementation/hexagonal.canonical";
        localFilePath = Path.Combine(Application.persistentDataPath, "hexagonal_canonical.json");

        string world_name = bridge != null ? bridge.WorldName : "21_05";
        if (string.IsNullOrWhiteSpace(world_name))
        {
            LogWarning("Bridge WorldName was empty. Falling back to default world '21_05'.");
            world_name = "21_05";
        }

        occlusions_url = $"https://raw.githubusercontent.com/germanespinosa/cellworld_data/refs/heads/master/cell_group/hexagonal.{world_name}.occlusions";
        localOcclusionsFilePath = Path.Combine(Application.persistentDataPath, $"hexagonal_{world_name}_occlusions.json");

        LogDetail($"Start(): bridgeFound={bridge != null}, worldName='{world_name}', occlusionHeight={occlusionHeight}");
        LogDetail($"World definition URL: {url}");
        LogDetail($"Occlusions URL: {occlusions_url}");
        LogDetail($"Local world file: {localFilePath}");
        LogDetail($"Local occlusions file: {localOcclusionsFilePath}");

        StartCoroutine(LoadOrDownloadAndSpawn());
    }

    IEnumerator LoadOrDownloadAndSpawn()
    {
        LogDetail("LoadOrDownloadAndSpawn(): begin");

        string json = string.Empty;
        if (File.Exists(localFilePath))
        {
            LogDetail("Loading world definition from local cache.");
            json = File.ReadAllText(localFilePath);
        }
        else
        {
            LogDetail("Downloading world definition.");
            UnityWebRequest request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[ARENA] World definition download failed: " + request.error);
                yield break;
            }

            json = request.downloadHandler.text;
            LogDetail($"World definition downloaded. Bytes={json.Length}");
            File.WriteAllText(localFilePath, json);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("[ARENA] World definition JSON is empty.");
            yield break;
        }

        SpawnData data = null;
        try
        {
            data = JsonUtility.FromJson<SpawnData>(json);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ARENA] Failed to parse world definition JSON: {ex}");
            Debug.LogError("[ARENA] World JSON preview: " + Truncate(json, 300));
            yield break;
        }

        if (data == null)
        {
            Debug.LogError("[ARENA] Parsed world definition is null.");
            Debug.LogError("[ARENA] World JSON preview: " + Truncate(json, 300));
            yield break;
        }

        if (data.cell_locations == null)
        {
            Debug.LogError("[ARENA] World definition is missing cell_locations.");
            Debug.LogError("[ARENA] World JSON preview: " + Truncate(json, 300));
            yield break;
        }

        LogDetail($"Parsed world definition. cellCount={data.cell_locations.Count}, size={data.cell_transformation?.size}, rotation={data.cell_transformation?.rotation}");

        string occlusionsJson = string.Empty;
        if (File.Exists(localOcclusionsFilePath))
        {
            LogDetail("Loading occlusions from local cache.");
            occlusionsJson = File.ReadAllText(localOcclusionsFilePath);
        }
        else
        {
            LogDetail("Downloading occlusions.");
            UnityWebRequest request = UnityWebRequest.Get(occlusions_url);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[ARENA] Occlusions download failed: " + request.error);
                yield break;
            }

            occlusionsJson = request.downloadHandler.text;
            LogDetail($"Occlusions downloaded. Bytes={occlusionsJson.Length}");
            File.WriteAllText(localOcclusionsFilePath, occlusionsJson);
        }

        if (string.IsNullOrWhiteSpace(occlusionsJson))
        {
            Debug.LogError("[ARENA] Occlusions JSON is empty.");
            yield break;
        }

        int[] occlusionIndicesArray = null;
        try
        {
            occlusionIndicesArray = JsonHelper.FromJson<int>(occlusionsJson);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ARENA] Failed to parse occlusions JSON: {ex}");
            Debug.LogError("[ARENA] Occlusions JSON preview: " + Truncate(occlusionsJson, 300));
            yield break;
        }

        if (occlusionIndicesArray == null)
        {
            Debug.LogError("[ARENA] Parsed occlusions list is null. The occlusions file is likely not a JSON array.");
            Debug.LogError("[ARENA] Occlusions JSON preview: " + Truncate(occlusionsJson, 300));
            yield break;
        }
        int index = 0;
        int spawnedOcclusionCount = 0;
        float arenaScale = bridge.positionScale;

        //Vector3 arenaPosition = new Vector3(data.space.center.x * arenaScale, occlusionHeight / 2f, data.space.center.y * arenaScale);
        //GameObject arena = Instantiate(
        //    occlusion_prefab,
        //    arenaPosition,
        //    Quaternion.Euler(0f, -data.space.transformation.rotation, 0f));
        //arena.transform.localScale *= arenaScale;

        HashSet<int> occlusionIndices = new HashSet<int>(occlusionIndicesArray);
        LogDetail($"Parsed occlusions. occlusionCount={occlusionIndices.Count}");

        foreach (var location in data.cell_locations)
        {
            if (occlusionIndices.Contains(index))
            {
                if (occlusion_prefab == null)
                {
                    Debug.LogError($"[ARENA] occlusion_prefab is not assigned. Failed while spawning cell index {index}.");
                    yield break;
                }

                Vector3 occlusionPosition = new Vector3(location.x * arenaScale, occlusionHeight / 2f, location.y * arenaScale);
                GameObject occlusion = Instantiate(
                    occlusion_prefab,
                    occlusionPosition,
                    Quaternion.Euler(0f, data.cell_transformation.rotation, 0f));

                Vector3 occlusionScale = occlusion.transform.localScale;
                occlusionScale.x *= data.cell_transformation.size * arenaScale;
                occlusionScale.z *= data.cell_transformation.size * arenaScale;
                occlusionScale.y = occlusionHeight;
                occlusion.transform.localScale = occlusionScale;
                EnsureSolidOcclusionCollider(occlusion);
                spawnedOcclusionCount++;

                if (logDetails)
                    Debug.Log($"[ARENA] Spawned occlusion at cellIndex={index}, position={occlusionPosition}, scale={occlusionScale}");
            }
            index++;
        }

        LogDetail($"Spawn complete. Spawned {spawnedOcclusionCount} occlusions across {data.cell_locations.Count} cells.");
    }

    private static void EnsureSolidOcclusionCollider(GameObject occlusion)
    {
        // The prefab is made from six thin wall colliders and has a hollow center.
        // A matching solid hull closes the seams and prevents any transient penetration from trapping the player.
        MeshCollider solidCollider = occlusion.GetComponent<MeshCollider>();
        if (solidCollider == null)
            solidCollider = occlusion.AddComponent<MeshCollider>();

        solidCollider.convex = true;
        solidCollider.sharedMesh = GetOcclusionCollisionMesh();

        // Avoid overlapping contacts from the original six wall colliders. The new
        // convex hull supplies the same side boundaries plus a closed interior.
        foreach (BoxCollider wallCollider in occlusion.GetComponentsInChildren<BoxCollider>())
            wallCollider.enabled = false;
    }

    private static Mesh GetOcclusionCollisionMesh()
    {
        if (occlusionCollisionMesh != null)
            return occlusionCollisionMesh;

        float halfHeight = 0.5f;
        float halfHexHeight = Mathf.Sqrt(3f) / 4f;
        Vector3[] vertices =
        {
            new Vector3( 0.50f, -halfHeight,  0f),
            new Vector3( 0.25f, -halfHeight,  halfHexHeight),
            new Vector3(-0.25f, -halfHeight,  halfHexHeight),
            new Vector3(-0.50f, -halfHeight,  0f),
            new Vector3(-0.25f, -halfHeight, -halfHexHeight),
            new Vector3( 0.25f, -halfHeight, -halfHexHeight),
            new Vector3( 0.50f,  halfHeight,  0f),
            new Vector3( 0.25f,  halfHeight,  halfHexHeight),
            new Vector3(-0.25f,  halfHeight,  halfHexHeight),
            new Vector3(-0.50f,  halfHeight,  0f),
            new Vector3(-0.25f,  halfHeight, -halfHexHeight),
            new Vector3( 0.25f,  halfHeight, -halfHexHeight)
        };

        List<int> triangles = new List<int>
        {
            0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 5, 4,
            6, 7, 8, 6, 8, 9, 6, 9, 10, 6, 10, 11
        };

        for (int i = 0; i < 6; i++)
        {
            int next = (i + 1) % 6;
            triangles.Add(i);
            triangles.Add(next);
            triangles.Add(i + 6);
            triangles.Add(next);
            triangles.Add(next + 6);
            triangles.Add(i + 6);
        }

        occlusionCollisionMesh = new Mesh
        {
            name = "Runtime Hexagonal Occlusion Collider",
            vertices = vertices,
            triangles = triangles.ToArray()
        };
        occlusionCollisionMesh.RecalculateNormals();
        occlusionCollisionMesh.RecalculateBounds();
        return occlusionCollisionMesh;
    }

    private void LogDetail(string message)
    {
        if (logDetails)
            Debug.Log("[ARENA] " + message);
    }

    private void LogWarning(string message)
    {
        Debug.LogWarning("[ARENA] " + message);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value.Substring(0, maxLength) + "...";
    }
}

[System.Serializable]
public class Transformation
{
    public float size;
    public float rotation;
}

[System.Serializable]
public class CWSpace
{
    public Location center;
    public Transformation transformation;
}

[System.Serializable]
public class SpawnData
{
    public List<Location> cell_locations;
    public Transformation cell_transformation;
    public CWSpace space;
}

[System.Serializable]
public class Location
{
    public float x;
    public float y;
}
