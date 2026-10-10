using UnityEngine;

/// <summary>路燈解謎成功後，將單扇門向世界座標左側滑動；成功後保持開啟。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Puzzle/路燈解謎單門機關")]
public sealed class SingleDoorController : MonoBehaviour
{
    [Header("路燈解謎連結")]
    [InspectorName("路燈解謎控制器")]
    [Tooltip("指定要監聽的路燈解謎控制器；未指定時尋找自身或父物件上的控制器。")]
    [SerializeField] private StreetLampPuzzleController puzzleController;

    [InspectorName("路燈狀態判定腳本")]
    [Tooltip("可指定新的雙路燈判定腳本；有指定時優先使用此判定結果。")]
    [SerializeField] private StreetLampPuzzleJudge puzzleJudge;

    [Header("門的移動")]
    [InspectorName("門物件")]
    [Tooltip("要移動的門 Transform；未指定時移動掛載此腳本的物件。")]
    [SerializeField] private Transform door;

    [InspectorName("向左移動距離")]
    [Tooltip("沿世界座標 X 負方向移動的距離，不受門模型旋轉或縮放影響。")]
    [SerializeField, Min(0f)] private float moveDistance = 1.5f;

    [InspectorName("開門時間")]
    [SerializeField, Min(0.01f)] private float movementDuration = 0.5f;

    private Vector3 openPosition;
    private bool openingRequested;

    private void Awake()
    {
        if (door == null) door = transform;
        openPosition = door.position + Vector3.left * Mathf.Max(0f, moveDistance);
    }

    private void OnEnable()
    {
        if (puzzleJudge == null && puzzleController == null)
            puzzleJudge = GetComponentInParent<StreetLampPuzzleJudge>(true);
        if (puzzleJudge != null)
        {
            puzzleJudge.PuzzleSolved += OpenDoor;
            if (puzzleJudge.IsSolved) OpenDoor();
            return;
        }
        if (puzzleController == null)
            puzzleController = GetComponentInParent<StreetLampPuzzleController>(true);
        if (puzzleController == null) return;

        puzzleController.PuzzleSolved += OpenDoor;
        if (puzzleController.IsSolved) OpenDoor();
    }

    private void Start()
    {
        if (puzzleJudge != null)
        {
            if (puzzleJudge.IsSolved) OpenDoor();
        }
        else if (puzzleController == null)
            Debug.LogWarning("【單門機關】請指定路燈解謎控制器。", this);
        else if (puzzleController.IsSolved)
            OpenDoor();
    }

    private void OnDisable()
    {
        if (puzzleJudge != null) puzzleJudge.PuzzleSolved -= OpenDoor;
        if (puzzleController != null) puzzleController.PuzzleSolved -= OpenDoor;
    }

    private void Update()
    {
        if (!openingRequested || door == null) return;
        float speed = Mathf.Max(0f, moveDistance) / Mathf.Max(0.01f, movementDuration);
        door.position = Vector3.MoveTowards(door.position, openPosition, speed * Time.deltaTime);
    }

    /// <summary>可由 Unity Event 呼叫；重複成功不會讓門繼續往左位移。</summary>
    public void OpenDoor()
    {
        openingRequested = true;
    }
}
