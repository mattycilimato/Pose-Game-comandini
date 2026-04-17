using System.Collections;
using TMPro;
using UnityEngine;

public class PoseUIFeedback : MonoBehaviour
{
    public PoseGameManager gameManager;
    public TMP_Text statusText;

    [Header("Visual")]
    public float okFlashSeconds = 1.5f;
    public Color okColor = new Color(0.1f, 1f, 0.2f, 1f);
    public Color normalColor = Color.white;

    void OnEnable()
    {
        if (gameManager != null)
            gameManager.OnPoseCompleted += HandlePoseCompleted;
    }

    void OnDisable()
    {
        if (gameManager != null)
            gameManager.OnPoseCompleted -= HandlePoseCompleted;
    }

    void Update()
    {
        if (gameManager == null || statusText == null) return;

        var target = gameManager.GetCurrentTargetPose();
        if (target == null)
        {
            statusText.text = "<b>FINITO!</b>";
            statusText.color = okColor;
            return;
        }

        statusText.color = normalColor;
        statusText.text = $"Fai la posa:\n<size=90><b>{target.poseName}</b></size>";
    }

    void HandlePoseCompleted(int index)
    {
        if (statusText == null) return;
        StopAllCoroutines();
        StartCoroutine(OkFlashRoutine());
    }

    IEnumerator OkFlashRoutine()
    {
        statusText.text = "<size=140><b>OK!</b></size>";
        statusText.color = okColor;
        yield return new WaitForSeconds(okFlashSeconds);
    }
}