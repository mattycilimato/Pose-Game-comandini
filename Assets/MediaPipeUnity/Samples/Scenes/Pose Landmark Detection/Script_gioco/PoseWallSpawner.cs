using UnityEngine;

public class PoseWallSpawner : MonoBehaviour
{
    [Header("Riferimenti")]
    public PoseGameManager gameManager;
    public Transform playerTransform;
    public Transform spawnPoint;
    public GameObject wallPrefab;

    [Header("Movimento muro")]
    public float wallSpeed = 2.5f;
    public float checkDistanceFromPlayer = 0.75f;
    public float spawnDelaySeconds = 0.6f;
    public bool startOnEnable = true;

    [Header("Debug")]
    public bool running = false;
    public int spawnedPoseIndex = -1;

    private bool _waitingSpawn;

    private void OnEnable()
    {
        if (startOnEnable)
        {
            StartRun();
        }
    }

    public void StartRun()
    {
        if (gameManager == null)
        {
            Debug.LogWarning("PoseWallSpawner: gameManager non assegnato.");
            return;
        }

        if (spawnPoint == null)
        {
            spawnPoint = transform;
        }

        running = true;
        _waitingSpawn = false;
        spawnedPoseIndex = -1;
        TrySpawnNext();
    }

    public void StopRun()
    {
        running = false;
    }

    public void NotifyWallPassed(int poseIndex)
    {
        if (!running) return;
        spawnedPoseIndex = poseIndex;
        Invoke(nameof(TrySpawnNext), spawnDelaySeconds);
    }

    public void NotifyGameOver(int poseIndex)
    {
        running = false;
        Debug.Log("Game Over: posa non matchata sul muro index " + poseIndex);
    }

    private void TrySpawnNext()
    {
        if (!running || gameManager == null || gameManager.isGameOver) return;
        if (_waitingSpawn) return;

        int index = gameManager.currentPoseIndex;
        if (gameManager.posesSequence == null || index < 0 || index >= gameManager.posesSequence.Length) return;

        var poseDef = gameManager.posesSequence[index];
        if (poseDef == null)
        {
            Debug.LogWarning("PoseWallSpawner: PoseDefinition nulla a index " + index);
            return;
        }

        SpawnWall(poseDef, index);
    }

    private void SpawnWall(PoseDefinition poseDefinition, int poseIndex)
    {
        if (wallPrefab == null)
        {
            Debug.LogWarning("PoseWallSpawner: wallPrefab non assegnato.");
            return;
        }

        Vector3 position = spawnPoint != null ? spawnPoint.position : transform.position;
        Quaternion rotation = spawnPoint != null ? spawnPoint.rotation : transform.rotation;

        GameObject wallGo = Instantiate(wallPrefab, position, rotation);
        var wall = wallGo.GetComponent<PoseWall>();
        if (wall == null)
        {
            Debug.LogError("PoseWallSpawner: il prefab muro non ha componente PoseWall.");
            Destroy(wallGo);
            return;
        }

        float checkZ = playerTransform != null
            ? playerTransform.position.z + checkDistanceFromPlayer
            : checkDistanceFromPlayer;

        wall.Initialize(this, gameManager, poseDefinition, poseIndex, wallSpeed, checkZ);
    }
}
