using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>讀取 StreetLampPuzzleController 的兩組設定，只判定是否一致，不控制路燈。</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
[AddComponentMenu("Koto/Puzzle/路燈狀態解謎判定")]
public sealed class StreetLampPuzzleJudge : MonoBehaviour
{
    [Header("既有解謎控制器")]
    [InspectorName("路燈解謎控制器")]
    [Tooltip("直接讀取此 StreetLampPuzzleController 的對照機關與對應機關。未指定時尋找自身或父物件上的控制器。")]
    [SerializeField] private StreetLampPuzzleController puzzleController = null;

    [Header("解謎條件")]
    [InspectorName("故障閃爍狀態需一致")]
    [Tooltip("勾選：兩盞故障路燈須同時運行或同時停止。未勾選：兩盞都持續故障閃爍即可，忽略亮滅。非故障路燈一律比較目前狀態。")]
    [SerializeField] private bool requireMatchingFlickerState = false;

    [Header("解謎成功事件")]
    [Tooltip("每次從未成功轉為成功時觸發，可連結單門機關的 OpenDoor。")]
    [SerializeField] private UnityEvent onPuzzleSolved = new UnityEvent();

    public bool IsSolved { get; private set; }
    public event Action PuzzleSolved;
    public event Action<bool> PuzzleStateChanged;

    private void OnEnable()
    {
        if (puzzleController == null)
            puzzleController = GetComponentInParent<StreetLampPuzzleController>(true);
    }

    private void LateUpdate()
    {
        // 等本幀所有路燈更新完亮／滅後才判定，避免讀到一半更新的狀態。
        bool matches = GroupsMatch();

        if (matches == IsSolved) return;
        IsSolved = matches;
        PuzzleStateChanged?.Invoke(IsSolved);
        if (!IsSolved) return;

        Debug.Log("【路燈解謎判定】兩組路燈全部符合條件，解謎成功。", this);
        PuzzleSolved?.Invoke();
        onPuzzleSolved?.Invoke();
    }

    private bool GroupsMatch()
    {
        if (puzzleController == null) return false;
        var referenceMechanisms = puzzleController.ReferenceMechanisms;
        var operatedMechanisms = puzzleController.OperatedMechanisms;
        if (referenceMechanisms == null || operatedMechanisms == null || referenceMechanisms.Count == 0 ||
            referenceMechanisms.Count != operatedMechanisms.Count) return false;

        for (int index = 0; index < referenceMechanisms.Count; index++)
        {
            MechanismStateController referenceMechanism = referenceMechanisms[index];
            MechanismStateController operatedMechanism = operatedMechanisms[index];
            if (referenceMechanism == null || operatedMechanism == null) return false;
            StreetLampController reference = StreetLampPuzzleController.FindStreetLamp(referenceMechanism);
            StreetLampController operated = StreetLampPuzzleController.FindStreetLamp(operatedMechanism);
            if (reference == null || operated == null || reference == operated ||
                !reference.isActiveAndEnabled || !operated.isActiveAndEnabled) return false;

            bool referenceFault = reference.IsFaultModeActive && !referenceMechanism.IsPermanentlyStopped;
            bool operatedFault = operated.IsFaultModeActive && !operatedMechanism.IsPermanentlyStopped;
            if (!StatesMatch(referenceMechanism.CurrentState, operatedMechanism.CurrentState,
                referenceFault, operatedFault, requireMatchingFlickerState)) return false;
        }
        return true;
    }

    /// <summary>故障與正常模式須一致；只有雙方故障且未要求同步時忽略運行／停止。</summary>
    public static bool StatesMatch(MechanismOperationState referenceState, MechanismOperationState operatedState,
        bool referenceFault, bool operatedFault, bool requireMatchingFlickerState)
    {
        if (referenceFault != operatedFault) return false;
        if (referenceFault && !requireMatchingFlickerState) return true;
        return referenceState == operatedState;
    }
}
