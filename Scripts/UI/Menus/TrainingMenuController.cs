using DG.Tweening;
using System;
using TMPro;
using Unity.Cinemachine;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class TrainingMenuController : MonoBehaviour
{
    [Header("TrainingMenu Settings")]
    [SerializeField] private SelectableMenuUI menu;
    [SerializeField] private Animator animator;
    [SerializeField] private CinemachineCamera virtualCamera;

    [Header("TrainingMenu Data")]
    [SerializeField] private TrainingDefinitionSO currentTraining;
    [SerializeField] private TextMeshProUGUI hitCountText;
    [SerializeField] private TextMeshProUGUI timeTakenText;
    [SerializeField] private TextMeshProUGUI gradeText;

    public event Action OnConfirmEnterRoom;

    private bool isOpen;
    public void Show()
    {
        if (isOpen)
            return;

        isOpen = true;

        menu.OnSelected += HandleSelect;
        menu.OnCanceled += HandleCancel;

        animator.SetBool("Open", true);
        virtualCamera.Priority = 999;
        UIManager.Instance.HUDVisible(false);
        menu.Open();

        if (currentTraining == null)
            return;

        TrainingRoomSaveData best = DataManager.Instance.GetTrainingRoomRecord(currentTraining.trainingId);
        if (best != null)
        {
            hitCountText.text = best.bestHitCount == int.MaxValue ? "-" : best.bestHitCount.ToString();
            timeTakenText.text = best.bestTimeSeconds == float.MaxValue ? "-" : TrainingScoreFormatHelper.FormatTime(best.bestTimeSeconds);

            TrainingScoreFormatHelper.GradeToText(best.bestGrade, gradeText);
        }
        else
        {
            hitCountText.text = "-";
            timeTakenText.text = "-";
            TrainingScoreFormatHelper.GradeToText(default, gradeText);
        }
        
    }
    private void HandleSelect(int index)
    {
        switch (index)
        {
            case 0:
                HandleCancel();
                break;
            case 1:

                OnConfirmEnterRoom?.Invoke();
                Cleanup();
                break;
        }
    }
    private void HandleCancel()
    {
        Cleanup();
    }
    private void Cleanup()
    {
        if (!isOpen)
            return;

        isOpen = false;

        menu.OnSelected -= HandleSelect;
        menu.OnCanceled -= HandleCancel;

        animator.SetBool("Open", false);
        virtualCamera.Priority = -1;
        UIManager.Instance.HUDVisible(true);
        menu.Close();

        GameManager.Instance.Player.AbilityManager.InterruptAllAbilities();
    }
}
