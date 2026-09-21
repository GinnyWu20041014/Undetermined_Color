using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 5f;

    [Header("組件連結 (不填會自動抓)")]
    public Rigidbody rb;
    public Animator animator;

    [Tooltip("移動方向參考的攝影機；未指定時自動使用標記為 MainCamera 的攝影機。")]
    [SerializeField] private Camera movementCamera;

    [Tooltip("舊角色 Animator 的移動 Bool 參數；Animator 沒有此參數時會略過，由獨立動畫腳本控制。")]
    [SerializeField] private string movingAnimationParameter = "is walking";

    private Vector3 movement;
    private bool isFacingRight = false;
    private bool hasMovingAnimationParameter;

    /// <summary>供玩家動畫腳本讀取目前是否有移動輸入。</summary>
    public bool IsMoving => enabled && movement.sqrMagnitude > 0.0001f;

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponent<Animator>();
        hasMovingAnimationParameter = HasBoolParameter(animator, movingAnimationParameter);

        if (rb != null)
        {
            rb.freezeRotation = true;
        }
    }

    void Update()
    {
        // 1. 移動輸入 (WASD / 方向鍵)
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveZ = Input.GetAxisRaw("Vertical");
        movement = GetCameraRelativeMovement(moveX, moveZ);

        // 2. 保留舊角色動畫；yuan 沒有 is walking，因此會交由獨立動畫腳本控制。
        if (hasMovingAnimationParameter)
        {
            animator.SetBool(movingAnimationParameter, IsMoving);
        }

        // 3. 左右轉向翻轉
        if (moveX > 0 && !isFacingRight)
        {
            Flip();
        }
        else if (moveX < 0 && isFacingRight)
        {
            Flip();
        }
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        // 進行物理平滑移動 (保留原本 Y 軸速度以維持自然重力/貼地)
        Vector3 targetVelocity = movement * moveSpeed;
        rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
    }

    private Vector3 GetCameraRelativeMovement(float horizontal, float vertical)
    {
        if (movementCamera == null)
        {
            movementCamera = Camera.main;
        }

        // 沒有攝影機時維持世界座標移動，避免角色無法操作。
        if (movementCamera == null)
        {
            return new Vector3(horizontal, 0f, vertical).normalized;
        }

        // 將攝影機前方向投影至玩家移動的 XZ 地面，不讓俯角影響速度或高度。
        Transform cameraTransform = movementCamera.transform;
        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            // 攝影機垂直俯視時，以畫面上方作為前進方向。
            forward = Vector3.ProjectOnPlane(cameraTransform.up, Vector3.up);
        }

        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        return (right * horizontal + forward * vertical).normalized;
    }

    void Flip()
    {
        isFacingRight = !isFacingRight;
        Vector3 currentScale = transform.localScale;
        currentScale.x *= -1f;
        transform.localScale = currentScale;
    }

    private static bool HasBoolParameter(Animator targetAnimator, string parameterName)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null ||
            string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in targetAnimator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == parameterName)
            {
                return true;
            }
        }

        return false;
    }
}
