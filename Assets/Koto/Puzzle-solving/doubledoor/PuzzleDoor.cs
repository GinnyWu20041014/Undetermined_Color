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
    [Tooltip("沿左右門片的排列方向向外滑動 1.5 世界單位，不使用模型自身的 X 軸。")]
    [SerializeField] private Transform leftDoorPanel = null;

    [InspectorName("右側門片")]
    [Tooltip("沿左右門片的排列方向向外滑動 1.5 世界單位，不使用模型自身的 X 軸。")]
    [SerializeField] private Transform rightDoorPanel = null;

    [InspectorName("門片移動時間")]
    [Tooltip("開門或關門時，門片滑動到目標位置所需的秒數。")]
    [SerializeField, Min(0.01f)] private float movementDuration = 0.5f;

    private const float OpenDistance = 1.5f;

    private Vector3 leftClosedPosition;
    private Vector3 rightClosedPosition;
    private float openingDistance;
    private Vector3 localOpeningDirection;
    private readonly HashSet<UnityEngine.Object> waitEchoRequesters =
        new HashSet<UnityEngine.Object>();

    public DoorState CurrentState => currentState;
    public bool IsOpen => CurrentState == DoorState.開啟;
    public bool IsHeldByWaitEcho => waitEchoRequesters.Count > 0;

    public event Action<DoorState> StateChanged;

    private void Awake()
    {
        // 以整扇門作共同座標系，避免 FBX 的匯入軸向和門片父物件不同造成錯位。
        if (leftDoorPanel != null) leftClosedPosition = transform.InverseTransformPoint(leftDoorPanel.position);
        if (rightDoorPanel != null) rightClosedPosition = transform.InverseTransformPoint(rightDoorPanel.position);
        Vector3 direction = transform.right;
        if (leftDoorPanel != null && rightDoorPanel != null)
        {
            direction = GetPanelCenter(rightDoorPanel) - GetPanelCenter(leftDoorPanel);
            if (direction.sqrMagnitude < 0.000001f)
                direction = rightDoorPanel.position - leftDoorPanel.position;
            if (direction.sqrMagnitude < 0.000001f) direction = transform.right;
        }
        localOpeningDirection = transform.InverseTransformDirection(direction.normalized);
        SetDoorState(currentState, false, true);
    }

    private void Update()
    {
        float speed = OpenDistance / Mathf.Max(0.01f, movementDuration);
        float step = speed * Time.deltaTime;

        openingDistance = Mathf.MoveTowards(openingDistance, IsOpen ? OpenDistance : 0f, step);
        ApplyPanelPositions();
    }

    private void ApplyPanelPositions()
    {
        // 開合軸取自兩門片的實際排列，並隨整扇門旋轉；模型軸向不參與判定。
        Vector3 offset = transform.TransformDirection(localOpeningDirection).normalized * openingDistance;
        if (leftDoorPanel != null)
            leftDoorPanel.position = transform.TransformPoint(leftClosedPosition) - offset;
        if (rightDoorPanel != null)
            rightDoorPanel.position = transform.TransformPoint(rightClosedPosition) + offset;
    }

    private static Vector3 GetPanelCenter(Transform panel)
    {
        // 左右 FBX 可能共用原點，因此優先使用可見模型的中心。
        Renderer[] renderers = panel.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return panel.position;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
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
            openingDistance = IsOpen ? OpenDistance : 0f;
            ApplyPanelPositions();
        }

        if (notify)
        {
            StateChanged?.Invoke(CurrentState);
            Debug.Log($"【解謎門】{name} 已切換為：{CurrentState}。", this);
        }
    }
}
