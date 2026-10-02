using System;
using System.Collections;
using UnityEngine;

/// <summary>每隔固定時間切換兩扇門；等待回想可固定單扇門，停止回想可永久停止整個機關。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Puzzle/雙門解謎機關")]
public sealed class DualDoorPuzzleController : MonoBehaviour
{
    [Header("門的設定")]
    [InspectorName("第一扇門")]
    [SerializeField] private PuzzleDoor firstDoor = null;

    [InspectorName("第二扇門")]
    [SerializeField] private PuzzleDoor secondDoor = null;

    [Header("機關設定")]
    [InspectorName("狀態切換間隔")]
    [Tooltip("機關未停止時，每隔多少秒切換兩扇門的開關狀態。")]
    [SerializeField, Min(0.01f)] private float switchInterval = 2f;

    private Coroutine switchingRoutine;
    private bool isPermanentlyStopped;

    public bool IsSolved { get; private set; }
    public bool IsPermanentlyStopped => isPermanentlyStopped;
    public event Action<bool> PuzzleStateChanged;

    private void OnEnable()
    {
        if (firstDoor != null) firstDoor.StateChanged += HandleDoorStateChanged;
        if (secondDoor != null) secondDoor.StateChanged += HandleDoorStateChanged;
        switchingRoutine = StartCoroutine(SwitchDoors());
    }

    private void Start()
    {
        EvaluatePuzzle();
    }

    private void OnDisable()
    {
        if (firstDoor != null) firstDoor.StateChanged -= HandleDoorStateChanged;
        if (secondDoor != null) secondDoor.StateChanged -= HandleDoorStateChanged;
        if (switchingRoutine != null) StopCoroutine(switchingRoutine);
        switchingRoutine = null;
    }

    private IEnumerator SwitchDoors()
    {
        while (true)
        {
            yield return new WaitForSeconds(switchInterval);
            if (isPermanentlyStopped)
            {
                continue;
            }

            if (firstDoor != null && !firstDoor.IsHeldByWaitEcho)
            {
                firstDoor.ToggleDoorState();
            }

            if (secondDoor != null && !secondDoor.IsHeldByWaitEcho)
            {
                secondDoor.ToggleDoorState();
            }
            EvaluatePuzzle();
        }
    }

    private void HandleDoorStateChanged(PuzzleDoor.DoorState state)
    {
        EvaluatePuzzle();
    }

    /// <summary>由停止回想永久停止此雙門機關，門維持當前開關狀態。</summary>
    public void StopMechanismPermanently(UnityEngine.Object requester)
    {
        if (isPermanentlyStopped)
        {
            return;
        }

        isPermanentlyStopped = true;
        string requesterName = requester != null ? requester.name : "停止回想";
        Debug.Log($"【雙門機關】已由「{requesterName}」永久停止，兩扇門維持當前狀態。", this);
        EvaluatePuzzle();
    }

    private void EvaluatePuzzle()
    {
        bool solved = firstDoor != null && secondDoor != null &&
                      firstDoor.IsOpen && secondDoor.IsOpen &&
                      isPermanentlyStopped;
        if (solved == IsSolved)
        {
            return;
        }

        IsSolved = solved;
        PuzzleStateChanged?.Invoke(IsSolved);
        if (IsSolved)
        {
            Debug.Log("【雙門解謎】兩扇門已同時開啟，且機關已停止，解謎成功。", this);
        }
    }
}
