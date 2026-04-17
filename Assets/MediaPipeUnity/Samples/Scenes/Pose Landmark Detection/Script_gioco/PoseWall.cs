using TMPro;
using UnityEngine;

public class PoseWall : MonoBehaviour
{
    [Header("Visual opzionali")]
    public SpriteRenderer poseSpriteRenderer;
    public TMP_Text poseLabel;

    private PoseWallSpawner _spawner;
    private PoseGameManager _gameManager;
    private int _poseIndex;
    private float _speed;
    private float _checkZ;
    private bool _resolved;

    public void Initialize(
        PoseWallSpawner spawner,
        PoseGameManager gameManager,
        PoseDefinition poseDefinition,
        int poseIndex,
        float speed,
        float checkZ)
    {
        _spawner = spawner;
        _gameManager = gameManager;
        _poseIndex = poseIndex;
        _speed = speed;
        _checkZ = checkZ;

        if (poseSpriteRenderer != null)
        {
            poseSpriteRenderer.sprite = poseDefinition != null ? poseDefinition.previewSprite : null;
            poseSpriteRenderer.enabled = poseSpriteRenderer.sprite != null;
        }

        if (poseLabel != null)
        {
            var poseName = poseDefinition != null ? poseDefinition.poseName : "Pose";
            poseLabel.text = string.IsNullOrWhiteSpace(poseName) ? "Pose" : poseName;
        }
    }

    private void Update()
    {
        if (_resolved || _gameManager == null) return;

        transform.position += Vector3.back * (_speed * Time.deltaTime);

        if (transform.position.z <= _checkZ)
        {
            ResolveWall();
        }
    }

    private void ResolveWall()
    {
        _resolved = true;

        bool passed = _gameManager.CompletePoseFromObstacle(_poseIndex);
        if (passed)
        {
            _spawner?.NotifyWallPassed(_poseIndex);
            Destroy(gameObject);
            return;
        }

        _gameManager.TriggerGameOver();
        _spawner?.NotifyGameOver(_poseIndex);
        Destroy(gameObject);
    }
}
