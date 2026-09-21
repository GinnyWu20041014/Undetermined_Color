using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>控制一扇由左右兩個門片組成的解謎門。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Puzzle/解謎門")]
public sealed class PuzzleDoor : MonoBehaviour
{
    public enum DoorState
    {
        關閉,
        開啟
    }

    [Header("門的狀態")]
    [InspectorName("目前狀態")]
    [Tooltip("門片會依這個狀態滑向開啟或關閉位置；雙門機關會直接切換此狀態。")]
    [FormerlySerializedAs("initialState")]
    [SerializeField] private DoorState currentState = DoorState.關閉;

    [Header("回想放置")]
    [InspectorName("回想放置點")]
    [Tooltip("放入可讓玩家放置回想的 GameObject；請將該物件設為放置目標 Tag。")]
    [SerializeField] private GameObject echoPlacementPoint = null;

    [Header("門片物件")]
    [InspectorName("左側門片")]
    [Tooltip("開門時只會往本地 X 軸左方移動 1.5。")]
    [SerializeField] private Transform leftDoorPanel = null;

    [InspectorName("右側門片")]
    [Tooltip("開門時只會往本地 X 軸右方移動 1.5。")]
    [SerializeField] private Transform rightDoorPanel = null;

    [InspectorName("門片移動時間")]
    [Tooltip("開門或關門時，門片滑動到目標位置所需的秒數。")]
    [SerializeField, Min(0.01f)] private float movementDuration = 0.5f;

    private const float OpenDistance = 1.5f;

    private Vector3 leftClosedPosition;
    private Vector3 rightClosedPosition;
    private readonly HashSet<UnityEngine.Object> waitEchoRequesters =
        new HashSet<UnityEngine.Object>();

    public DoorState CurrentState => currentState;
    public bool IsOpen => CurrentState == DoorState.開啟;
    public bool IsHeldByWaitEcho => waitEchoRequesters.Count > 0;

    public event Action<DoorState> StateChanged;

    private void Awake()
    {
        if (leftDoorPanel != null) leftClosedPosition = leftDoorPanel.localPosition;
        if (rightDoorPanel != null) rightClosedPosition = rightDoorPanel.localPosition;
        SetDoorState(currentState, false, true);
    }

    private void Update()
    {
        float speed = OpenDistance / Mathf.Max(0.01f, movementDuration);
        float step = speed * Time.deltaTime;

        if (leftDoorPanel != null)
        {
            Vector3 position = leftDoorPanel.localPosition;
            float targetX = IsOpen ? leftClosedPosition.x - OpenDistance : leftClosedPosition.x;
            position.x = Mathf.MoveTowards(position.x, targetX, step);
            leftDoorPanel.localPosition = position;
        }

        if (rightDoorPanel != null)
        {
            Vector3 position = rightDoorPanel.localPosition;
            float targetX = IsOpen ? rightClosedPosition.x + OpenDistance : rightClosedPosition.x;
            position.x = Mathf.MoveTowards(position.x, targetX, step);
            rightDoorPanel.localPosition = position;
        }
    }

    public void ToggleDoorState()
    {
        if (IsHeldByWaitEcho)
        {
            return;
        }

        SetDoorState(IsOpen ? DoorState.關閉 : DoorState.開啟);
    }

    public void SetDoorState(DoorState state)
    {
        if (IsHeldByWaitEcho)
        {
            return;
        }

        SetDoorState(state, true, false);
    }

    /// <summary>判斷物件是否為這扇門指定的回想放置點。</summary>
    public bool IsEchoPlacementTarget(GameObject target)
    {
        if (echoPlacementPoint == null || target == null)
        {
            return false;
        }

        Transform pointTransform = echoPlacementPoint.transform;
        Transform targetTransform = target.transform;
        return target == echoPlacementPoint ||
               targetTransform.IsChildOf(pointTransform) ||
               pointTransform.IsChildOf(targetTransform);
    }

    /// <summary>由等待回想固定這一扇門的當前開關狀態。</summary>
    public void HoldDoor(UnityEngine.Object requester)
    {
        if (requester != null && waitEchoRequesters.Add(requester))
        {
            Debug.Log($"【解謎門】{name} 已被等待回想固定為：{CurrentState}。", this);
        }
    }

    /// <summary>取回等待回想後，恢復接收機關的定時切換。</summary>
    public void ReleaseDoor(UnityEngine.Object requester)
    {
        if (requester != null && waitEchoRequesters.Remove(requester))
        {
            Debug.Log($"【解謎門】{name} 已解除等待回想固定。", this);
        }
    }

    private void SetDoorState(DoorState state, bool notify, bool moveImmediately)
    {
        currentState = state;

        if (moveImmediately)
        {
            if (leftDoorPanel != null)
            {
                leftDoorPanel.localPosition = IsOpen
                    ? leftClosedPosition + Vector3.left * OpenDistance
                    : leftClosedPosition;
            }

            if (rightDoorPanel != null)
            {
                rightDoorPanel.localPosition = IsOpen
                    ? rightClosedPosition + Vector3.right * OpenDistance
                    : rightClosedPosition;
            }
        }

        if (notify)
        {
            StateChanged?.Invoke(CurrentState);
            Debug.Log($"【解謎門】{name} 已切換為：{CurrentState}。", this);
        }
    }
}
