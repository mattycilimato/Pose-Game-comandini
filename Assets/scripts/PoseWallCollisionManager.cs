using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class PoseWallCollisionManager : MonoBehaviour
{
  [System.Serializable]
  public class WallResultEvent : UnityEvent<bool, int> { }

  [Header("Wall Setup")]
  [SerializeField] private List<GameObject> wallPrefabs = new();
  [SerializeField] private Transform spawnPoint;
  [SerializeField] private float spawnEverySeconds = 3f;
  [SerializeField] private float wallSpeed = 3f;
  [SerializeField] private float despawnZ = -6f;
  [SerializeField] private float judgeZ = 0.2f;

  [Header("Collision Setup")]
  [SerializeField] private string playerBodyTag = "PlayerBody";
  [SerializeField] private int maxAllowedHits = 0;

  [Header("Events")]
  [SerializeField] private WallResultEvent onWallJudged;

  private float _timer;
  private readonly List<RuntimeWall> _runtimeWalls = new();
  private int _score;

  public class RuntimeWall
  {
    public GameObject gameObject;
    public int hits;
    public bool hasBeenJudged;
  }

  private void Update()
  {
    HandleSpawn();
    MoveAndJudgeWalls();
  }

  private void HandleSpawn()
  {
    if (wallPrefabs.Count == 0 || spawnPoint == null)
    {
      return;
    }

    _timer += Time.deltaTime;
    if (_timer < spawnEverySeconds)
    {
      return;
    }

    _timer = 0f;
    var prefab = wallPrefabs[Random.Range(0, wallPrefabs.Count)];
    var wallGo = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
    var runtime = new RuntimeWall { gameObject = wallGo };

    var listener = wallGo.GetComponent<WallHitListener>();
    if (listener == null)
    {
      listener = wallGo.AddComponent<WallHitListener>();
    }
    listener.Initialize(this, runtime, playerBodyTag);

    _runtimeWalls.Add(runtime);
  }

  private void MoveAndJudgeWalls()
  {
    for (var i = _runtimeWalls.Count - 1; i >= 0; i--)
    {
      var wall = _runtimeWalls[i];
      if (wall.gameObject == null)
      {
        _runtimeWalls.RemoveAt(i);
        continue;
      }

      wall.gameObject.transform.Translate(Vector3.back * wallSpeed * Time.deltaTime, Space.World);
      var z = wall.gameObject.transform.position.z;

      if (!wall.hasBeenJudged && z <= judgeZ)
      {
        wall.hasBeenJudged = true;
        var success = wall.hits <= maxAllowedHits;
        if (success)
        {
          _score++;
        }
        onWallJudged?.Invoke(success, _score);
      }

      if (z <= despawnZ)
      {
        Destroy(wall.gameObject);
        _runtimeWalls.RemoveAt(i);
      }
    }
  }

  public void RegisterHit(RuntimeWall wall)
  {
    wall.hits++;
  }

  private class WallHitListener : MonoBehaviour
  {
    private PoseWallCollisionManager _manager;
    private RuntimeWall _runtimeWall;
    private string _requiredTag;

    public void Initialize(PoseWallCollisionManager manager, RuntimeWall runtimeWall, string requiredTag)
    {
      _manager = manager;
      _runtimeWall = runtimeWall;
      _requiredTag = requiredTag;
    }

    private void OnTriggerEnter(Collider other)
    {
      if (_manager == null || _runtimeWall == null)
      {
        return;
      }

      if (string.IsNullOrEmpty(_requiredTag) || other.CompareTag(_requiredTag))
      {
        _manager.RegisterHit(_runtimeWall);
      }
    }
  }
}
