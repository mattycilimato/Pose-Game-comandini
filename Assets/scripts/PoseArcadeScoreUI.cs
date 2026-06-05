using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class PoseArcadeScoreUI : MonoBehaviour
{
  public enum PoseRating
  {
    Bad,
    Good,
    Perfect
  }

  [Header("Target")]
  [SerializeField] private Transform playerRoot;
  [SerializeField] private Vector3 feedbackWorldOffset = new(0f, 1.6f, 0f);

  [Header("Label Textures")]
  [SerializeField] private Texture2D badLabelTexture;
  [SerializeField] private Texture2D goodLabelTexture;
  [SerializeField] private Texture2D perfectLabelTexture;

  [Header("Points")]
  [SerializeField] private int badPoints;
  [SerializeField] private int goodPoints = 50;
  [SerializeField] private int perfectPoints = 100;
  [SerializeField] private int consecutiveGoodsForPerfect = 2;

  [Header("Feedback")]
  [SerializeField] private float feedbackDuration = 1.4f;
  [SerializeField] private Vector2 feedbackScreenSize = new(320f, 120f);
  [SerializeField] private Vector2 scorePanelOffset = new(-28f, -28f);

  private Canvas _canvas;
  private Text _scoreText;
  private Image _feedbackImage;
  private RectTransform _feedbackRect;
  private Camera _camera;
  private int _totalScore;
  private int _consecutiveGoods;
  private Coroutine _hideFeedbackRoutine;
  private Sprite _badSprite;
  private Sprite _goodSprite;
  private Sprite _perfectSprite;

  public int TotalScore => _totalScore;

  private void Awake()
  {
    _camera = Camera.main;
    CacheSprites();
    BuildUI();
    UpdateScoreText();
    HideFeedbackImmediate();
  }

  private void LateUpdate()
  {
    UpdateFeedbackScreenPosition();
  }

  public void ReportWallResult(bool collided)
  {
    if (collided)
    {
      _consecutiveGoods = 0;
      ApplyRating(PoseRating.Bad, badPoints);
      return;
    }

    _consecutiveGoods++;
    if (_consecutiveGoods >= consecutiveGoodsForPerfect)
    {
      _consecutiveGoods = 0;
      ApplyRating(PoseRating.Perfect, perfectPoints);
    }
    else
    {
      ApplyRating(PoseRating.Good, goodPoints);
    }
  }

  private void ApplyRating(PoseRating rating, int points)
  {
    _totalScore += points;
    UpdateScoreText();
    ShowFeedback(rating);
    Debug.Log($"[PoseScore] {rating} (+{points}) -> totale {_totalScore}");
  }

  private void ShowFeedback(PoseRating rating)
  {
    if (_feedbackImage == null)
    {
      return;
    }

    _feedbackImage.sprite = rating switch
    {
      PoseRating.Perfect => _perfectSprite,
      PoseRating.Good => _goodSprite,
      _ => _badSprite
    };
    _feedbackImage.enabled = _feedbackImage.sprite != null;
    UpdateFeedbackScreenPosition();

    if (_hideFeedbackRoutine != null)
    {
      StopCoroutine(_hideFeedbackRoutine);
    }

    _hideFeedbackRoutine = StartCoroutine(HideFeedbackAfterDelay());
  }

  private IEnumerator HideFeedbackAfterDelay()
  {
    yield return new WaitForSeconds(feedbackDuration);
    HideFeedbackImmediate();
    _hideFeedbackRoutine = null;
  }

  private void HideFeedbackImmediate()
  {
    if (_feedbackImage != null)
    {
      _feedbackImage.enabled = false;
    }
  }

  private void UpdateFeedbackScreenPosition()
  {
    if (_feedbackImage == null || !_feedbackImage.enabled || playerRoot == null || _camera == null)
    {
      return;
    }

    var worldPos = playerRoot.position + feedbackWorldOffset;
    var screenPos = _camera.WorldToScreenPoint(worldPos);
    _feedbackRect.position = screenPos;
  }

  private void UpdateScoreText()
  {
    if (_scoreText != null)
    {
      _scoreText.text = $"Score: {_totalScore}";
    }
  }

  private void CacheSprites()
  {
    _badSprite = CreateSprite(badLabelTexture);
    _goodSprite = CreateSprite(goodLabelTexture);
    _perfectSprite = CreateSprite(perfectLabelTexture);
  }

  private static Sprite CreateSprite(Texture2D texture)
  {
    if (texture == null)
    {
      return null;
    }

    return Sprite.Create(
      texture,
      new Rect(0f, 0f, texture.width, texture.height),
      new Vector2(0.5f, 0.5f),
      100f);
  }

  private void BuildUI()
  {
    var canvasGo = new GameObject("PoseScoreCanvas");
    canvasGo.transform.SetParent(transform, false);
    _canvas = canvasGo.AddComponent<Canvas>();
    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    _canvas.sortingOrder = 200;
    canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    canvasGo.AddComponent<GraphicRaycaster>();

    var scoreGo = new GameObject("ScorePanel");
    scoreGo.transform.SetParent(canvasGo.transform, false);
    var scoreRect = scoreGo.AddComponent<RectTransform>();
    scoreRect.anchorMin = new Vector2(1f, 1f);
    scoreRect.anchorMax = new Vector2(1f, 1f);
    scoreRect.pivot = new Vector2(1f, 1f);
    scoreRect.anchoredPosition = scorePanelOffset;
    scoreRect.sizeDelta = new Vector2(280f, 64f);

    _scoreText = scoreGo.AddComponent<Text>();
    _scoreText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    _scoreText.fontSize = 36;
    _scoreText.fontStyle = FontStyle.Bold;
    _scoreText.alignment = TextAnchor.UpperRight;
    _scoreText.color = Color.white;
    _scoreText.horizontalOverflow = HorizontalWrapMode.Overflow;

    var feedbackGo = new GameObject("PoseFeedback");
    feedbackGo.transform.SetParent(canvasGo.transform, false);
    _feedbackRect = feedbackGo.AddComponent<RectTransform>();
    _feedbackRect.sizeDelta = feedbackScreenSize;
    _feedbackImage = feedbackGo.AddComponent<Image>();
    _feedbackImage.preserveAspect = true;
    _feedbackImage.raycastTarget = false;
  }
}
