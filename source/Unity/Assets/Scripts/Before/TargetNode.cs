using System.Collections;
using UnityEngine;

namespace BeforeExperience
{
    public sealed class TargetNode : MonoBehaviour
    {
        private enum NodeState
        {
            Idle,
            Reacting,
            Hidden,
            Appearing
        }

        private const float ReactDuration = 0.26f;
        private const float FragmentDuration = 0.42f;
        private const float RespawnDelay = 0.7f;
        private const float AppearDuration = 0.35f;
        private const float HitSlop = 1.15f;
        private const int FragmentCount = 8;

        [SerializeField] private int targetId;
        [SerializeField] private Vector2 viewportPosition = new Vector2(0.5f, 0.5f);

        private SpriteRenderer spriteRenderer;
        private FeedbackController feedback;
        private TargetSpawner spawner;
        private Camera viewCamera;
        private Color baseColor = Color.white;
        private Vector3 baseScale = Vector3.one;
        private NodeState state = NodeState.Idle;
        private Coroutine lifecycle;
        private Transform[] fragments;
        private SpriteRenderer[] fragmentRenderers;

        public int TargetId => targetId;
        public Vector2 ViewportPosition => viewportPosition;
        public bool IsSelectable => state == NodeState.Idle || state == NodeState.Appearing;
        public float WorldRadius { get; private set; }
        public Vector2 WorldPosition => transform.position;

        public void SetPredictionIdentity(int id, Vector2 normalizedPosition)
        {
            targetId = Mathf.Clamp(id, 0, OnlineIntentPredictor.CandidateCount - 1);
            viewportPosition = new Vector2(
                Mathf.Clamp01(normalizedPosition.x),
                Mathf.Clamp01(normalizedPosition.y));
        }

        public void Configure(
            int id,
            Color color,
            Sprite sprite,
            float worldRadius,
            FeedbackController feedbackController,
            TargetSpawner owner,
            Camera cam)
        {
            targetId = Mathf.Clamp(id, 0, OnlineIntentPredictor.CandidateCount - 1);
            baseColor = color;
            feedback = feedbackController;
            spawner = owner;
            viewCamera = cam;
            WorldRadius = worldRadius;

            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer == null)
            {
                spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
            }

            spriteRenderer.sprite = sprite;
            spriteRenderer.color = baseColor;
            baseScale = Vector3.one * (worldRadius * 2f);
            transform.localScale = baseScale;

            fragments = new Transform[FragmentCount];
            fragmentRenderers = new SpriteRenderer[FragmentCount];
            for (int i = 0; i < FragmentCount; i++)
            {
                GameObject fragment = new GameObject($"Fragment_{i}");
                fragment.transform.SetParent(transform.parent, false);
                SpriteRenderer renderer = fragment.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.color = baseColor;
                fragment.transform.localScale = baseScale * 0.18f;
                fragment.SetActive(false);
                fragments[i] = fragment.transform;
                fragmentRenderers[i] = renderer;
            }
        }

        public void PlaceAt(Vector2 viewport)
        {
            viewportPosition = new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y));
            if (viewCamera != null)
            {
                Vector3 world = viewCamera.ViewportToWorldPoint(
                    new Vector3(viewportPosition.x, viewportPosition.y, -viewCamera.transform.position.z));
                world.z = 0f;
                transform.position = world;
            }
        }

        public bool ContainsWorldPoint(Vector2 worldPoint)
        {
            return IsSelectable
                && Vector2.Distance(worldPoint, (Vector2)transform.position) <= WorldRadius * HitSlop;
        }

        public void React(BeforeEventSource source)
        {
            if (!IsSelectable)
            {
                return;
            }

            if (lifecycle != null)
            {
                StopCoroutine(lifecycle);
            }

            if (feedback != null)
            {
                feedback.PlayReactionTone(targetId);
            }

            lifecycle = StartCoroutine(ReactRoutine());
        }

        public void ResetToIdleAt(Vector2 viewport)
        {
            if (lifecycle != null)
            {
                StopCoroutine(lifecycle);
                lifecycle = null;
            }

            HideFragments();
            state = NodeState.Idle;
            spriteRenderer.enabled = true;
            spriteRenderer.color = baseColor;
            transform.localScale = baseScale;
            PlaceAt(viewport);
        }

        private IEnumerator ReactRoutine()
        {
            state = NodeState.Reacting;
            Vector3 origin = transform.position;
            Vector2[] directions = new Vector2[FragmentCount];
            for (int i = 0; i < FragmentCount; i++)
            {
                float angle = (Mathf.PI * 2f * i / FragmentCount) + Random.Range(-0.25f, 0.25f);
                directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                fragments[i].position = origin;
                fragmentRenderers[i].color = baseColor;
                fragments[i].gameObject.SetActive(true);
            }

            float elapsed = 0f;
            float total = Mathf.Max(ReactDuration, FragmentDuration);
            while (elapsed < total)
            {
                elapsed += Time.deltaTime;

                float pulse = Mathf.Clamp01(elapsed / ReactDuration);
                if (pulse < 1f)
                {
                    float swell = 1f + 0.85f * Mathf.Sin(Mathf.PI * pulse);
                    transform.localScale = baseScale * swell;
                    Color bloom = Color.Lerp(baseColor, Color.white, pulse * 0.65f);
                    bloom.a = 1f - Mathf.SmoothStep(0.35f, 1f, pulse);
                    spriteRenderer.color = bloom;
                }
                else if (spriteRenderer.enabled)
                {
                    spriteRenderer.enabled = false;
                }

                float scatter = Mathf.Clamp01(elapsed / FragmentDuration);
                float travel = WorldRadius * (0.6f + scatter * 2.4f);
                for (int i = 0; i < FragmentCount; i++)
                {
                    fragments[i].position = origin + (Vector3)(directions[i] * travel);
                    Color fade = baseColor;
                    fade.a = 1f - scatter;
                    fragmentRenderers[i].color = fade;
                    fragments[i].localScale = baseScale * (0.18f * (1f - scatter * 0.7f));
                }

                yield return null;
            }

            HideFragments();
            spriteRenderer.enabled = false;
            state = NodeState.Hidden;

            yield return new WaitForSeconds(RespawnDelay);

            Vector2 next = spawner != null ? spawner.FindRespawnPosition(this) : viewportPosition;
            PlaceAt(next);
            spriteRenderer.enabled = true;
            state = NodeState.Appearing;

            float appear = 0f;
            while (appear < AppearDuration)
            {
                appear += Time.deltaTime;
                float u = Mathf.Clamp01(appear / AppearDuration);
                transform.localScale = baseScale * Mathf.Lerp(0.55f, 1f, u);
                Color color = baseColor;
                color.a = u;
                spriteRenderer.color = color;
                yield return null;
            }

            transform.localScale = baseScale;
            spriteRenderer.color = baseColor;
            state = NodeState.Idle;
            lifecycle = null;
        }

        private void HideFragments()
        {
            if (fragments == null)
            {
                return;
            }

            for (int i = 0; i < fragments.Length; i++)
            {
                if (fragments[i] != null)
                {
                    fragments[i].gameObject.SetActive(false);
                }
            }
        }
    }
}
