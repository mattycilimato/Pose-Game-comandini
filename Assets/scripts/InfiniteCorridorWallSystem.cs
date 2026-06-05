using System.Collections.Generic;
using UnityEngine;

public class InfiniteCorridorWallSystem : MonoBehaviour
{
  private class RuntimeWall
  {
    public GameObject root;
    public int hitCount;
    public bool judged;
    public Vector3 spawnPos;
    public Vector3 moveDirection;
    public float judgeDistance;
    public float despawnDistance;
  }

  [Header("Player")]
  [SerializeField] private string playerBodyTag = "PlayerBody";
  [SerializeField] private LayerMask playerBodyLayers = ~0;
  [SerializeField] private Transform playerRoot;

  [Header("Spawn & Movement")]
  [SerializeField] private Vector3 spawnPosition = new(0.4f, 3.45f, -19f);
  [SerializeField] private Vector3 despawnPosition = new(0.4f, 3.45f, 2f);
  [SerializeField] private float passBeyondPlayerDistance = 3f;
  [SerializeField] private float wallSpeed = 4f;
  [SerializeField] private float spawnInterval = 2.4f;
  [SerializeField] private Transform wallTPoseScaleReference;

  [Header("Wall Prefab")]
  [SerializeField] private GameObject wallPrefab;
  [SerializeField] private GameObject[] wallPrefabs;
  [SerializeField] private bool applyScaleFromReference;

  [Header("Wall Visual (Procedural Fallback)")]
  [SerializeField] private Texture2D wallTexture;
  [SerializeField] private Material wallVisualMaterial;
  [SerializeField] private float wallWidth = 6f;
  [SerializeField] private bool preserveTextureAspectRatio = true;
  [SerializeField] private float wallThickness = 0.45f;

  [Header("Collision Frame")]
  [SerializeField] private float sideFrameThickness = 0.35f;
  [SerializeField] private float topBottomFrameThickness = 0.35f;
  [SerializeField] private Vector2 holeCenterNormalized = new(0.5f, 0.44f);
  [SerializeField] private Vector2 holeSizeNormalized = new(0.86f, 0.86f);

  [Header("Gameplay")]
  [SerializeField] private int maxAllowedHitsPerWall = 0;
  [SerializeField] private PoseArcadeScoreUI scoreUI;

  private readonly List<RuntimeWall> _walls = new();
  private float _spawnTimer;
  private bool _playerSetupChecked;
  private int _nextPrefabIndex;

  private void Awake()
  {
    if (scoreUI == null)
    {
      scoreUI = FindFirstObjectByType<PoseArcadeScoreUI>();
    }
  }

  private float WallHeight
  {
    get
    {
      if (!preserveTextureAspectRatio || wallTexture == null || wallTexture.height <= 0)
      {
        return wallWidth;
      }

      return wallWidth * wallTexture.height / wallTexture.width;
    }
  }

  private void Start()
  {
    if (wallTPoseScaleReference == null)
    {
      wallTPoseScaleReference = FindScaleReferenceTransform();
    }

    if (UsesPrefabSpawning())
    {
      Debug.Log($"[WallSystem] Attivo in modalita prefab. Spawn {spawnPosition}, arrivo/despawn {despawnPosition}.");
    }
    else
    {
      Debug.Log($"[WallSystem] Attivo in modalita procedurale. Spawn {spawnPosition}, arrivo/despawn {despawnPosition}.");
    }
  }

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
    var wall = CreateWall();
    _walls.Add(wall);
    Debug.Log($"[WallSystem] Muro spawnato a {spawnPosition} (attivi: {_walls.Count}).");
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

      wall.root.transform.position += wall.moveDirection * wallSpeed * Time.deltaTime;
      var traveled = Vector3.Dot(wall.root.transform.position - wall.spawnPos, wall.moveDirection);

      if (!wall.judged && traveled >= wall.judgeDistance)
      {
        wall.judged = true;
        var collided = wall.hitCount > maxAllowedHitsPerWall;
        if (collided)
        {
          Debug.LogWarning($"[WallSystem] COLLISIONE - wall hit (hits={wall.hitCount}).");
        }
        else
        {
          Debug.Log($"[WallSystem] POSE MATCH OK - wall passed (hits={wall.hitCount}).");
        }

        scoreUI?.ReportWallResult(collided);
      }

      if (traveled >= wall.despawnDistance)
      {
        Destroy(wall.root);
        _walls.RemoveAt(i);
      }
    }
  }

  private RuntimeWall CreateWall()
  {
    if (UsesPrefabSpawning())
    {
      return CreateWallFromPrefab();
    }

    return CreateProceduralWall();
  }

  private RuntimeWall CreateWallFromPrefab()
  {
    var prefab = PickWallPrefab();
    if (prefab == null)
    {
      Debug.LogWarning("[WallSystem] Nessun prefab muro valido assegnato: uso fallback procedurale.");
      return CreateProceduralWall();
    }

    var root = Instantiate(prefab, spawnPosition, prefab.transform.rotation);
    root.name = $"{prefab.name}_Runtime";
    root.transform.SetParent(transform, true);
    EnsureWallRigidbody(root);
    if (applyScaleFromReference)
    {
      ApplyTPoseScale(root.transform);
    }

    var movement = BuildWallMovement();
    var runtimeWall = new RuntimeWall
    {
      root = root,
      spawnPos = spawnPosition,
      moveDirection = movement.moveDirection,
      judgeDistance = movement.judgeDistance,
      despawnDistance = movement.despawnDistance
    };

    AttachHitReporters(root, runtimeWall);
    return runtimeWall;
  }

  private RuntimeWall CreateProceduralWall()
  {
    var wallHeight = WallHeight;
    var root = new GameObject("PoseWall_Runtime");
    root.transform.SetParent(transform, true);
    root.transform.position = spawnPosition;
    EnsureWallRigidbody(root);

    var movement = BuildWallMovement();
    CreateWallVisual(root.transform, wallWidth, wallHeight);
    CreateCollisionFrame(root.transform, wallWidth, wallHeight);
    ApplyTPoseScale(root.transform);

    var runtimeWall = new RuntimeWall
    {
      root = root,
      spawnPos = spawnPosition,
      moveDirection = movement.moveDirection,
      judgeDistance = movement.judgeDistance,
      despawnDistance = movement.despawnDistance
    };

    AttachHitReporters(root, runtimeWall);
    return runtimeWall;
  }

  private (Vector3 moveDirection, float judgeDistance, float despawnDistance) BuildWallMovement()
  {
    var toDespawn = despawnPosition - spawnPosition;
    var despawnDistance = toDespawn.magnitude;
    var moveDirection = toDespawn.sqrMagnitude > 0.0001f ? toDespawn.normalized : Vector3.forward;
    if (despawnDistance <= 0.0001f)
    {
      despawnDistance = 1f;
    }

    var judgeDistance = Mathf.Max(0.05f, despawnDistance - passBeyondPlayerDistance);
    if (playerRoot != null)
    {
      var playerDistanceOnPath = Vector3.Dot(playerRoot.position - spawnPosition, moveDirection);
      judgeDistance = Mathf.Clamp(playerDistanceOnPath, 0.05f, despawnDistance);
    }

    return (moveDirection, judgeDistance, despawnDistance);
  }

  private void EnsureWallRigidbody(GameObject root)
  {
    if (root == null)
    {
      return;
    }

    var rb = root.GetComponent<Rigidbody>();
    if (rb == null)
    {
      rb = root.AddComponent<Rigidbody>();
    }

    rb.isKinematic = true;
    rb.useGravity = false;
    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
  }

  private void AttachHitReporters(GameObject root, RuntimeWall runtimeWall)
  {
    var colliders = root.GetComponentsInChildren<Collider>(true);
    var reporterCount = 0;

    for (var i = 0; i < colliders.Length; i++)
    {
      var collider = colliders[i];
      if (collider == null || !collider.isTrigger)
      {
        continue;
      }

      var reporterHost = collider.gameObject;
      var reporter = reporterHost.GetComponent<WallHitReporter>();
      if (reporter == null)
      {
        reporter = reporterHost.AddComponent<WallHitReporter>();
      }

      reporter.Initialize(this, runtimeWall, playerBodyTag);
      reporterCount++;
    }

    if (reporterCount == 0)
    {
      Debug.LogWarning($"[WallSystem] Il prefab '{root.name}' non ha collider trigger: le collisioni non verranno rilevate.");
    }
  }

  private bool UsesPrefabSpawning()
  {
    return wallPrefab != null || (wallPrefabs != null && wallPrefabs.Length > 0);
  }

  private GameObject PickWallPrefab()
  {
    if (wallPrefabs != null && wallPrefabs.Length > 0)
    {
      var prefab = wallPrefabs[_nextPrefabIndex % wallPrefabs.Length];
      _nextPrefabIndex = (_nextPrefabIndex + 1) % wallPrefabs.Length;
      if (prefab != null)
      {
        return prefab;
      }
    }

    return wallPrefab;
  }

  private Transform FindScaleReferenceTransform()
  {
    var candidates = new[] { "muroTpose", "muro tpose", "muro tPose" };
    for (var i = 0; i < candidates.Length; i++)
    {
      var reference = GameObject.Find(candidates[i]);
      if (reference != null)
      {
        return reference.transform;
      }
    }

    return null;
  }

  private void ApplyTPoseScale(Transform wallRoot)
  {
    if (wallRoot == null || wallTPoseScaleReference == null)
    {
      return;
    }

    wallRoot.localScale = wallTPoseScaleReference.localScale;
  }

  private void CreateWallVisual(Transform parent, float width, float height)
  {
    var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
    go.name = "WallVisual";
    var meshCollider = go.GetComponent<Collider>();
    if (meshCollider != null)
    {
      Destroy(meshCollider);
    }

    go.transform.SetParent(parent, false);
    go.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
    go.transform.localScale = new Vector3(width / 10f, 1f, height / 10f);
    var renderer = go.GetComponent<MeshRenderer>();
    if (renderer == null)
    {
      return;
    }

    if (wallVisualMaterial != null)
    {
      renderer.sharedMaterial = wallVisualMaterial;
      return;
    }

    if (wallTexture == null)
    {
      Debug.LogWarning("[WallSystem] Nessuna texture/materiale muro assegnato: uso il materiale di default.");
      return;
    }

    var shader = Shader.Find("Universal Render Pipeline/Lit");
    if (shader == null)
    {
      Debug.LogWarning("[WallSystem] Shader URP Lit non trovato.");
      return;
    }

    var runtimeMaterial = new Material(shader)
    {
      mainTexture = wallTexture
    };
    runtimeMaterial.SetFloat("_AlphaClip", 1f);
    runtimeMaterial.SetFloat("_Cutoff", 0.15f);
    runtimeMaterial.EnableKeyword("_ALPHATEST_ON");
    renderer.sharedMaterial = runtimeMaterial;
  }

  private void CreateCollisionFrame(Transform parent, float width, float height)
  {
    var halfHoleWidth = Mathf.Clamp(holeSizeNormalized.x * width * 0.5f, 0.2f, width * 0.5f - sideFrameThickness);
    var halfHoleHeight = Mathf.Clamp(holeSizeNormalized.y * height * 0.5f, 0.2f, height * 0.5f - topBottomFrameThickness);
    var holeCenterX = (holeCenterNormalized.x - 0.5f) * width;
    var holeCenterY = holeCenterNormalized.y * height;

    var leftEdge = holeCenterX - halfHoleWidth;
    var rightEdge = holeCenterX + halfHoleWidth;
    var bottomEdge = holeCenterY - halfHoleHeight;
    var topEdge = holeCenterY + halfHoleHeight;

    CreateFramePiece(parent, "LeftFrame", new Vector3((-width * 0.5f + leftEdge) * 0.5f, height * 0.5f, 0f), new Vector3(leftEdge + width * 0.5f, height, wallThickness));
    CreateFramePiece(parent, "RightFrame", new Vector3((rightEdge + width * 0.5f) * 0.5f, height * 0.5f, 0f), new Vector3(width * 0.5f - rightEdge, height, wallThickness));
    CreateFramePiece(parent, "BottomFrame", new Vector3(holeCenterX, bottomEdge * 0.5f, 0f), new Vector3(halfHoleWidth * 2f, bottomEdge, wallThickness));
    CreateFramePiece(parent, "TopFrame", new Vector3(holeCenterX, (topEdge + height) * 0.5f, 0f), new Vector3(halfHoleWidth * 2f, height - topEdge, wallThickness));
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

    var renderer = go.GetComponent<MeshRenderer>();
    if (renderer != null)
    {
      renderer.enabled = false;
    }

    var collider = go.GetComponent<BoxCollider>();
    if (collider == null)
    {
      collider = go.AddComponent<BoxCollider>();
    }
    collider.isTrigger = true;
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
