using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("移動設定")]
    public float moveSpeed = 5f;

    [Header("組件連結 (不填會自動抓)")]
    public Rigidbody rb;
    public Animator animator;

    private Vector3 movement;
    private bool isFacingRight = false;

    void Start()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponent<Animator>();

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
        movement = new Vector3(moveX, 0f, moveZ).normalized;

        bool isMoving = movement.sqrMagnitude > 0;

        // 2. 更新移動動畫 (已修正為 Animator Controller 中的 "is walking")
        if (animator != null)
        {
            animator.SetBool("is walking", isMoving);
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

    void Flip()
    {
        isFacingRight = !isFacingRight;
        Vector3 currentScale = transform.localScale;
        currentScale.x *= -1f;
        transform.localScale = currentScale;
    }
}