using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System;

// 直接播放指定片段，避免舊 Animator 的 smell 狀態與自動轉場中斷召喚／死亡。
[DisallowMultipleComponent]
public sealed class SmallMonsterAnimation : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField, InspectorName("行走動畫")] private AnimationClip walk;
    [SerializeField, InspectorName("召喚動畫")] private AnimationClip summon;
    [SerializeField, InspectorName("攻擊動畫")] private AnimationClip attack;
    [SerializeField, InspectorName("死亡動畫")] private AnimationClip death;
    // 分裂時會複製正在播放死亡動畫的母體，連同原始姿勢一起保存，避免複製死亡姿勢作為預設值。
    [Serializable] private struct BonePose
    {
        public Transform bone;
        public Vector3 position, scale;
        public Quaternion rotation;
    }
    [Serializable] private struct RendererPose
    {
        public SpriteRenderer renderer;
        public Material[] materials;
    }
    [SerializeField, HideInInspector] private BonePose[] restPose;
    [SerializeField, HideInInspector] private RendererPose[] restRenderers;
    private PlayableGraph graph;
    private AnimationClipPlayable playable;
    private AnimationClip current;
    public float SummonDuration => Duration(summon);
    public float AttackDuration => Duration(attack);
    public float DeathDuration => Duration(death);
    private void Awake() => CacheRestPose();
    private void CacheRestPose()
    {
        if (restPose != null && restPose.Length > 0) return;
        var transforms = GetComponentsInChildren<Transform>(true);
        restPose = new BonePose[transforms.Length - 1];
        int index = 0;
        foreach (var bone in transforms)
        {
            if (bone == transform) continue;
            restPose[index++] = new BonePose { bone = bone, position = bone.localPosition,
                rotation = bone.localRotation, scale = bone.localScale };
        }
        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        restRenderers = new RendererPose[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            restRenderers[i] = new RendererPose { renderer = renderers[i], materials = renderers[i].sharedMaterials };
    }
    private void RestoreRestPose()
    {
        CacheRestPose();
        foreach (var pose in restPose)
        {
            if (pose.bone == null) continue;
            pose.bone.SetLocalPositionAndRotation(pose.position, pose.rotation);
            pose.bone.localScale = pose.scale;
        }
        if (restRenderers == null) return;
        foreach (var pose in restRenderers)
        {
            if (pose.renderer == null) continue;
            pose.renderer.sharedMaterials = pose.materials;
            pose.renderer.SetPropertyBlock(null);
        }
    }
    private static float Duration(AnimationClip clip) => clip != null ? Mathf.Max(0.01f, clip.length) : 0.5f;
    public void PlaySummon() => Play(summon, true);
    public void PlayAttack() => Play(attack, true);
    public void PlayDeath() => Play(death, true);
    public void Locomotion(bool moving)
    {
        Play(walk, false);
        if (playable.IsValid()) playable.SetSpeed(moving ? 1 : 0);
    }
    private void Play(AnimationClip clip, bool restart)
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null || clip == null) return;
        if (!restart && current == clip && graph.IsValid()) return;
        if (graph.IsValid()) graph.Destroy();
        RestoreRestPose();
        graph = PlayableGraph.Create("分裂小球動畫");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        playable = AnimationClipPlayable.Create(graph, clip);
        var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
        output.SetSourcePlayable(playable);
        current = clip;
        graph.Play();
    }
    private void OnDisable() { if (graph.IsValid()) graph.Destroy(); current = null; }
}
