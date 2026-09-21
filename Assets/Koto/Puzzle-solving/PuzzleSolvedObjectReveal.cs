using UnityEngine;

/// <summary>依照路燈或雙門解謎目前結果顯示或隱藏指定物件。</summary>
[DisallowMultipleComponent]
[AddComponentMenu("Koto/Puzzle/解謎成功顯示物件")]
public sealed class PuzzleSolvedObjectReveal : MonoBehaviour
{
    [InspectorName("成功後顯示物件")]
    [Tooltip("遊戲開始時隱藏；解謎成功時顯示，條件再次不一致時重新隱藏。")]
    [SerializeField] private GameObject objectToReveal = null;

    private StreetLampPuzzleController puzzleController;
    private DualDoorPuzzleController doorPuzzleController;

    private void Awake()
    {
        puzzleController = GetComponent<StreetLampPuzzleController>();
        doorPuzzleController = GetComponent<DualDoorPuzzleController>();
        SetObjectVisible(false);
    }

    private void OnEnable()
    {
        if (puzzleController == null)
        {
            puzzleController = GetComponent<StreetLampPuzzleController>();
        }

        if (doorPuzzleController == null)
        {
            doorPuzzleController = GetComponent<DualDoorPuzzleController>();
        }

        if (puzzleController != null)
        {
            puzzleController.PuzzleStateChanged += HandlePuzzleStateChanged;
        }

        if (doorPuzzleController != null)
        {
            doorPuzzleController.PuzzleStateChanged += HandlePuzzleStateChanged;
        }
    }

    private void Start()
    {
        RefreshObjectVisibility();
    }

    private void OnDisable()
    {
        if (puzzleController != null)
        {
            puzzleController.PuzzleStateChanged -= HandlePuzzleStateChanged;
        }
        if (doorPuzzleController != null)
        {
            doorPuzzleController.PuzzleStateChanged -= HandlePuzzleStateChanged;
        }
    }

    private void HandlePuzzleStateChanged(bool isSolved)
    {
        RefreshObjectVisibility();
    }

    private void RefreshObjectVisibility()
    {
        bool solved = (puzzleController != null && puzzleController.IsSolved) ||
                      (doorPuzzleController != null && doorPuzzleController.IsSolved);
        SetObjectVisible(solved);
    }

    private void SetObjectVisible(bool visible)
    {
        if (objectToReveal != null && objectToReveal.activeSelf != visible)
        {
            objectToReveal.SetActive(visible);
        }
    }
}
