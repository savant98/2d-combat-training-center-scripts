using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 씬 전환 로딩: 다음 씬을 뒤에서 불러오는 동안 암전 위에 로딩 표시를 띄우고, 다 불러오면 넘어간다.
// 씬을 한 번에 불러오면 그동안 화면이 멈춘다. 뒤에서 불러오면 전환 연출과 로딩 표시가 계속 움직이고,
// 연출이 재생되는 시간도 불러오는 데 쓴다. TrainingRoom 입장 로딩과 같은 표시(TrainingRoomLoadingPanel)를 쓴다.
// 쓰는 쪽은 Begin으로 불러오기를 시작하고(연출을 이어서 재생한 뒤) Finish를 기다린다.
public sealed class SceneLoadingSequence : ILoadingSource
{
    // 단계 이름: 씬 데이터를 불러오는 중 → 새 씬을 켜는 중
    public const string LoadStep = "SCENE", StartupStep = "STARTUP";
    // 불러오기는 0.9에서 멈추고, 넘어가도 좋다고 할 때까지 기다린다(Unity의 allowSceneActivation 규칙).
    private const float ReadyProgress = .9f;
    // 진행 막대에서 불러오기가 차지하는 몫. 나머지는 새 씬을 켜는 구간이다.
    private const float LoadShare = .35f;
    // 새 씬을 켜는 구간은 진행률을 알 수 없다(Unity가 알려 주지 않는다). 걸린 시간에 따라 완만하게 채우되 끝까지 채우지는 않는다.
    private const float StartupPace = 2.2f, StartupCeiling = .97f;
    private AsyncOperation operation;
    private string sceneName;

    public bool IsLoading { get; private set; }
    public float LoadingProgress { get; private set; }
    public string LoadingStep { get; private set; } = LoadStep;

    /// <summary>씬을 뒤에서 불러오기 시작하고 로딩 표시를 띄운다. 넘어가는 것은 Finish가 한다.</summary>
    public static SceneLoadingSequence Begin(string sceneName)
    {
        var sequence = new SceneLoadingSequence { sceneName = sceneName, IsLoading = true };
        try
        {
            sequence.operation = SceneManager.LoadSceneAsync(sceneName);
            if (sequence.operation != null) sequence.operation.allowSceneActivation = false;
        }
        catch (Exception exception) { Debug.LogException(exception); sequence.operation = null; }
        // 표시를 켜지 못해도 씬은 불러온다.
        try { TrainingRoomLoadingPanel.Show(sequence); }
        catch (Exception exception) { Debug.LogException(exception); }
        return sequence;
    }

    /// <summary>다 불러올 때까지 기다린 뒤 씬으로 넘어간다. 넘어가면 지금 씬과 로딩 표시는 사라진다.</summary>
    public IEnumerator Finish()
    {
        if (operation == null)
        {
            // 뒤에서 불러오지 못했으면 예전처럼 한 번에 불러온다.
            IsLoading = false;
            SceneManager.LoadScene(sceneName);
            yield break;
        }
        while (operation.progress < ReadyProgress)
        {
            LoadingProgress = LoadShare * Mathf.Clamp01(operation.progress / ReadyProgress);
            yield return null;
        }
        LoadingProgress = LoadShare;
        // 새 씬을 켠다. 켜는 동안에도 화면은 계속 돌고, 다 켜지면 이 씬과 로딩 표시가 사라진다.
        LoadingStep = StartupStep;
        operation.allowSceneActivation = true;
        float began = Time.unscaledTime;
        while (!operation.isDone)
        {
            LoadingProgress = LoadShare + (StartupCeiling - LoadShare) * (1f - Mathf.Exp(-(Time.unscaledTime - began) / StartupPace));
            yield return null;
        }
        IsLoading = false;
    }
}
