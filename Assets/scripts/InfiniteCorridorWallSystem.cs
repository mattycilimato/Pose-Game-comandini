using System.Collections.Generic;
using UnityEngine;

public class InfiniteCorridorWallSystem : MonoBehaviour
{
  private class RuntimeWall
  {
    public GameObject root;
    public int hitCount;
    public bool judged;
    public float judgeZ;
    public float despawnZ;
  }

  [Header("Player")]
  [SerializeField] private string playerBodyTag = "PlayerBody";
  [SerializeField] private LayerMask playerBodyLayers = ~0;
  [SerializeField] private Transform playerRoot;
  [SerializeField] private float wallCenterYOffsetFromPlayer = 1f;

  [Header("Spawn")]
  [SerializeField] private float wallSpeed = 4f;
  [SerializeField] private float spawnInterval = 2.4f;
  [SerializeField] private float spawnZ = 16f;
  [SerializeField] private float judgeZ = 0.5f;
  [SerializeField] private float despawnZ = -6f;

  [Header("Placeholder Wall Shape")]
  [SerializeField] private float wallWidth = 6f;
  [SerializeField] private float wallHeight = 6f;
  [SerializeField] private float wallThickness = 0.45f;
  [SerializeField] private float sideFrameThickness = 1f;
  [SerializeField] private float topBottomFrameThickness = 1f;
  [SerializeField] private Vector2 holeSize = new(1.6f, 2.8f);
  [SerializeField] private Vector2 randomHoleXRange = new(-1.3f, 1.3f);
  [SerializeField] private Vector2 randomHoleYRange = new(0.4f, 2.2f);
  [SerializeField] private Material wallMaterial;

  [Header("Gameplay")]
  [SerializeField] private int maxAllowedHitsPerWall = 0;

  private readonly List<RuntimeWall> _walls = new();
  private float _spawnTimer;
  private bool _playerSetupChecked;

  private void Update()
  {
    EnsurePlayerCollisionSetupChecked();
    HandleSpawn();
    MoveAndJudgeWalls();
  }

  private void HandleSpawn()
  {
    _spawnTimer += Time.deltaTime;
    if (_spawnTimer < spawnInterval)
    {
      return;
    }

    _spawnTimer = 0f;
    var wall = CreatePlaceholderWall();
    _walls.Add(wall);
  }

  private void MoveAndJudgeWalls()
  {
    for (var i = _walls.Count - 1; i >= 0; i--)
    {
      var wall = _walls[i];
      if (wall.root == null)
      {
        _walls.RemoveAt(i);
        continue;
      }

      wall.root.transform.position += Vector3.back * wallSpeed * Time.deltaTime;
      var z = wall.root.transform.position.z;

      if (!wall.judged && z <= wall.judgeZ)
      {
        wall.judged = true;
        var success = wall.hitCount <= maxAllowedHitsPerWall;
        if (success)
        {
          Debug.Log($"[WallSystem] POSE MATCH OK - wall passed (hits={wall.hitCount}).");
        }
        else
        {
          Debug.LogWarning($"[WallSystem] COLLISIONE - wall hit (hits={wall.hitCount}).");
        }
      }

      if (z <= wall.despawnZ)
      {
        Destroy(wall.root);
        _walls.RemoveAt(i);
      }
    }
  }

  private RuntimeWall CreatePlaceholderWall()
  {
    var root = new GameObject("PoseWall_Runtime");
    root.transform.SetParent(transform, false);
    var playerY = playerRoot != null ? playerRoot.position.y : 0f;
    var wallCenterY = playerY + wallCenterYOffsetFromPlayer;
    root.transform.position = new Vector3(0f, wallCenterY, spawnZ);
    var rb = root.AddComponent<Rigidbody>();
    rb.isKinematic = true;
    rb.useGravity = false;
    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

    var safeHalfHoleWidth = Mathf.Min(holeSize.x * 0.5f, wallWidth * 0.5f - sideFrameThickness * 0.5f - 0.1f);
    var safeHalfHoleHeight = Mathf.Min(holeSize.y * 0.5f, wallHeight * 0.5f - topBottomFrameThickness * 0.5f - 0.1f);
    var holeCenterX = Mathf.Clamp(Random.Range(randomHoleXRange.x, randomHoleXRange.y), -(wallWidth * 0.5f - safeHalfHoleWidth - 0.1f), wallWidth * 0.5f - safeHalfHoleWidth - 0.1f);
    var holeCenterY = Mathf.Clamp(Random.Range(randomHoleYRange.x, randomHoleYRange.y), safeHalfHoleHeight + 0.1f, wallHeight - safeHalfHoleHeight - 0.1f);

    var leftEdge = holeCenterX - safeHalfHoleWidth;
    var rightEdge = holeCenterX + safeHalfHoleWidth;
    var bottomEdge = holeCenterY - safeHalfHoleHeight;
    var topEdge = holeCenterY + safeHalfHoleHeight;

    CreateFramePiece(root.transform, "LeftFrame", new Vector3((-wallWidth * 0.5f + leftEdge) * 0.5f, wallHeight * 0.5f, 0f), new Vector3(leftEdge + wallWidth * 0.5f, wallHeight, wallThickness));
    CreateFramePiece(root.transform, "RightFrame", new Vector3((rightEdge + wallWidth * 0.5f) * 0.5f, wallHeight * 0.5f, 0f), new Vector3(wallWidth * 0.5f - rightEdge, wallHeight, wallThickness));
    CreateFramePiece(root.transform, "BottomFrame", new Vector3(holeCenterX, bottomEdge * 0.5f, 0f), new Vector3(safeHalfHoleWidth * 2f, bottomEdge, wallThickness));
    CreateFramePiece(root.transform, "TopFrame", new Vector3(holeCenterX, (topEdge + wallHeight) * 0.5f, 0f), new Vector3(safeHalfHoleWidth * 2f, wallHeight - topEdge, wallThickness));

    var runtimeWall = new RuntimeWall
    {
      root = root,
      judgeZ = judgeZ,
      despawnZ = despawnZ
    };

    foreach (Transform child in root.transform)
    {
      var reporter = child.gameObject.AddComponent<WallHitReporter>();
      reporter.Initialize(this, runtimeWall, playerBodyTag);
    }

    return runtimeWall;
  }

  private void EnsurePlayerCollisionSetupChecked()
  {
    if (_playerSetupChecked)
    {
      return;
    }

    _playerSetupChecked = true;
    if (string.IsNullOrEmpty(playerBodyTag))
    {
      Debug.LogWarning("[WallSystem] playerBodyTag e' vuoto: tutte le collisioni trigger verranno considerate valide.");
      return;
    }

    GameObject[] taggedObjects;
    try
    {
      taggedObjects = GameObject.FindGameObjectsWithTag(playerBodyTag);
    }
    catch (UnityException)
    {
      Debug.LogWarning($"[WallSystem] Tag '{playerBodyTag}' non esiste nel progetto. Aggiungi il tag o cambia Player Body Tag.");
      return;
    }

    if (taggedObjects == null || taggedObjects.Length == 0)
    {
      Debug.LogWarning($"[WallSystem] Nessun GameObject con tag '{playerBodyTag}' trovato: le collisioni non verranno contate.");
      return;
    }

    var colliderCount = 0;
    for (var i = 0; i < taggedObjects.Length; i++)
    {
      colliderCount += taggedObjects[i].GetComponentsInChildren<Collider>(true).Length;
    }

    if (colliderCount == 0)
    {
      Debug.LogWarning($"[WallSystem] Trovato tag '{playerBodyTag}' ma nessun Collider associato: aggiungi Collider al player.");
    }
    else
    {
      Debug.Log($"[WallSystem] Player collision setup OK. Tagged objects: {taggedObjects.Length}, colliders: {colliderCount}.");
    }

    var wallLayer = gameObject.layer;
    for (var i = 0; i < taggedObjects.Length; i++)
    {
      var playerLayer = taggedObjects[i].layer;
      if (Physics.GetIgnoreLayerCollision(wallLayer, playerLayer))
      {
        Debug.LogWarning($"[WallSystem] Layer collision disabilitata tra wall layer '{LayerMask.LayerToName(wallLayer)}' e player layer '{LayerMask.LayerToName(playerLayer)}'.");
      }
    }
  }

  private void CreateFramePiece(Transform parent, string pieceName, Vector3 localPosition, Vector3 localScale)
  {
    if (localScale.x <= 0.01f || localScale.y <= 0.01f || localScale.z <= 0.01f)
    {
      return;
    }

    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
    go.name = pieceName;
    go.transform.SetParent(parent, false);
    go.transform.localPosition = localPosition;
    go.transform.localScale = localScale;

    var collider = go.GetComponent<BoxCollider>();
    if (collider == null)
    {
      collider = go.AddComponent<BoxCollider>();
    }
    collider.isTrigger = true;

    if (wallMaterial != null)
    {
      var renderer = go.GetComponent<MeshRenderer>();
      if (renderer != null)
      {
        renderer.sharedMaterial = wallMaterial;
      }
    }
  }

  private void RegisterHit(RuntimeWall wall)
  {
    if (wall == null || wall.judged)
    {
      return;
    }

    wall.hitCount++;
    Debug.Log($"[WallSystem] Collision detected with current wall. hits={wall.hitCount}");
  }

  private class WallHitReporter : MonoBehaviour
  {
    private InfiniteCorridorWallSystem _owner;
    private RuntimeWall _wall;
    private string _requiredTag;

    public void Initialize(InfiniteCorridorWallSystem owner, RuntimeWall wall, string requiredTag)
    {
      _owner = owner;
      _wall = wall;
      _requiredTag = requiredTag;
    }

    private void OnTriggerEnter(Collider other)
    {
      if (_owner == null || _wall == null)
      {
        return;
      }

      if (_owner.IsPlayerCollider(other, _requiredTag))
      {
        _owner.RegisterHit(_wall);
      }
    }
  }

  private bool IsPlayerCollider(Collider collider, string requiredTag)
  {
    if (collider == null)
    {
      return false;
    }

    var layerMatch = (playerBodyLayers.value & (1 << collider.gameObject.layer)) != 0;
    if (layerMatch)
    {
      return true;
    }

    if (!string.IsNullOrEmpty(requiredTag) && collider.CompareTag(requiredTag))
    {
      return true;
    }

    return false;
  }
}
