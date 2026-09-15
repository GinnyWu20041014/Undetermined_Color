using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 依照順序比較路燈機關的目標狀態與玩家操作後的實際狀態。
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Puzzle/路燈解謎控制")]
public sealed class StreetLampPuzzleController : MonoBehaviour
{
    [Header("對照組")]
    [InspectorName("對照機關")]
    [Tooltip("依序放入作為答案的機關狀態控制器。")]
    [SerializeField] private List<MechanismStateController> referenceMechanisms =
        new List<MechanismStateController>();

    [Header("玩家操作組")]
    [InspectorName("對應機關")]
    [Tooltip("依照對照組的相同順序放入機關狀態控制器。")]
    [SerializeField] private List<MechanismStateController> operatedMechanisms = new List<MechanismStateController>();

    private readonly HashSet<MechanismStateController> subscribedMechanisms =
        new HashSet<MechanismStateController>();

    private bool evaluationRequested;

    /// <summary>目前兩組是否已完全依序相符。</summary>
    public bool IsSolved { get; private set; }

    /// <summary>每次由不一致轉為正確一致時發出通知。</summary>
    public event Action PuzzleSolved;

    /// <summary>解謎成功或失敗狀態發生改變時通知顯示元件。</summary>
    public event Action<bool> PuzzleStateChanged;

    private void OnEnable()
    {
        SubscribeToMechanisms();
        evaluationRequested = true;
    }

    private void Start()
    {
        EvaluatePuzzle();
    }

    private void LateUpdate()
    {
        if (!evaluationRequested)
        {
            return;
        }

        evaluationRequested = false;
        EvaluatePuzzle();
    }

    private void OnDisable()
    {
        UnsubscribeFromMechanisms();
    }

    private void SubscribeToMechanisms()
    {
        UnsubscribeFromMechanisms();

        SubscribeGroup(referenceMechanisms);
        SubscribeGroup(operatedMechanisms);
    }

    private void SubscribeGroup(List<MechanismStateController> mechanisms)
    {
        if (mechanisms == null)
        {
            return;
        }

        foreach (MechanismStateController mechanism in mechanisms)
        {
            if (mechanism == null || !subscribedMechanisms.Add(mechanism))
            {
                continue;
            }

            mechanism.StateChanged += HandleMechanismStateChanged;
        }
    }

    private void UnsubscribeFromMechanisms()
    {
        foreach (MechanismStateController mechanism in subscribedMechanisms)
        {
            if (mechanism != null)
            {
                mechanism.StateChanged -= HandleMechanismStateChanged;
            }
        }

        subscribedMechanisms.Clear();
    }

    private void HandleMechanismStateChanged(MechanismOperationState state, bool isPaused)
    {
        // 等所有路燈先完成同一幀的亮／滅更新，再進行比較。
        evaluationRequested = true;
    }

    private void EvaluatePuzzle()
    {
        bool matches = referenceMechanisms != null && operatedMechanisms != null &&
                       referenceMechanisms.Count > 0 &&
                       referenceMechanisms.Count == operatedMechanisms.Count;

        if (matches)
        {
            for (int index = 0; index < referenceMechanisms.Count; index++)
            {
                MechanismStateController referenceMechanism = referenceMechanisms[index];
                MechanismStateController operatedMechanism = operatedMechanisms[index];
                if (referenceMechanism == null || operatedMechanism == null ||
                    referenceMechanism.CurrentState != operatedMechanism.CurrentState)
                {
                    matches = false;
                    break;
                }

                StreetLampController referenceLamp = FindStreetLamp(referenceMechanism);
                StreetLampController operatedLamp = FindStreetLamp(operatedMechanism);

                // 操作組只要故障仍在運作就不能破解；必須先用等待回響實際關閉。
                if (operatedLamp != null && operatedLamp.IsFaultModeActive)
                {
                    matches = false;
                    break;
                }

                // 路燈圖片必須與對照組一致；運行為亮、停止為滅。
                if (referenceLamp != null &&
                    (operatedLamp == null || referenceLamp.IsLightVisible != operatedLamp.IsLightVisible))
                {
                    matches = false;
                    break;
                }
            }
        }

        if (matches == IsSolved)
        {
            return;
        }

        IsSolved = matches;
        PuzzleStateChanged?.Invoke(IsSolved);

        if (IsSolved)
        {
            Debug.Log("破解成功", this);
            PuzzleSolved?.Invoke();
        }
    }

    private static StreetLampController FindStreetLamp(MechanismStateController mechanism)
    {
        StreetLampController lamp = mechanism.GetComponent<StreetLampController>();
        if (lamp == null)
        {
            lamp = mechanism.GetComponentInParent<StreetLampController>();
        }

        return lamp != null
            ? lamp
            : mechanism.GetComponentInChildren<StreetLampController>(true);
    }

}
