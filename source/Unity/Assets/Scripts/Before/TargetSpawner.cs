using System.Collections.Generic;
using UnityEngine;

namespace BeforeExperience
{
    public sealed class TargetSpawner : MonoBehaviour
    {
        public const int TargetCount = OnlineIntentPredictor.CandidateCount;

        private const float MarginX = 0.12f;
        private const float MarginYBottom = 0.14f;
        private const float MarginYTop = 0.84f;
        private const float MinSeparationViewport = 0.2f;
        private const float TargetWorldRadius = 0.42f;
        private const int RespawnAttempts = 40;

        private static readonly Color[] Palette =
        {
            new Color(0.72f, 0.82f, 1.00f),
            new Color(0.70f, 0.95f, 0.88f),
            new Color(0.95f, 0.86f, 0.70f),
            new Color(0.93f, 0.75f, 0.85f),
            new Color(0.80f, 0.76f, 0.98f),
            new Color(0.86f, 0.94f, 0.70f)
        };

        private static readonly Vector2[] InitialLayout =
        {
            new Vector2(0.24f, 0.66f),
            new Vector2(0.50f, 0.72f),
            new Vector2(0.76f, 0.66f),
            new Vector2(0.24f, 0.30f),
            new Vector2(0.50f, 0.24f),
            new Vector2(0.76f, 0.30f)
        };

        private readonly List<TargetNode> targets = new List<TargetNode>(TargetCount);
        private Camera viewCamera;
        private Sprite circleSprite;

        public IReadOnlyList<TargetNode> Targets => targets;

        public void Initialize(Camera cam, FeedbackController feedback)
        {
            viewCamera = cam;
            circleSprite = CreateCircleSprite();

            for (int id = 0; id < TargetCount; id++)
            {
                GameObject node = new GameObject($"Target_{id}");
                node.transform.SetParent(transform, false);
                TargetNode target = node.AddComponent<TargetNode>();
                target.Configure(id, Palette[id], circleSprite, TargetWorldRadius, feedback, this, cam);
                targets.Add(target);
            }

            ResetLayout();
        }

        public void ResetLayout()
        {
            for (int id = 0; id < targets.Count; id++)
            {
                targets[id].ResetToIdleAt(InitialLayout[id]);
            }
        }

        public TargetNode FindTargetAtWorldPoint(Vector2 worldPoint)
        {
            TargetNode best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i].ContainsWorldPoint(worldPoint))
                {
                    continue;
                }

                float distance = Vector2.Distance(worldPoint, targets[i].WorldPosition);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = targets[i];
                }
            }

            return best;
        }

        public Vector2 FindRespawnPosition(TargetNode requester)
        {
            float aspect = viewCamera != null ? viewCamera.aspect : 16f / 9f;
            Vector2 bestCandidate = new Vector2(
                Random.Range(MarginX, 1f - MarginX),
                Random.Range(MarginYBottom, MarginYTop));
            float bestSeparation = -1f;

            for (int attempt = 0; attempt < RespawnAttempts; attempt++)
            {
                Vector2 candidate = new Vector2(
                    Random.Range(MarginX, 1f - MarginX),
                    Random.Range(MarginYBottom, MarginYTop));

                float nearest = float.MaxValue;
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i] == requester)
                    {
                        continue;
                    }

                    Vector2 delta = candidate - targets[i].ViewportPosition;
                    delta.x *= aspect;
                    nearest = Mathf.Min(nearest, delta.magnitude / Mathf.Max(1f, aspect));
                }

                if (nearest >= MinSeparationViewport)
                {
                    return candidate;
                }

                if (nearest > bestSeparation)
                {
                    bestSeparation = nearest;
                    bestCandidate = candidate;
                }
            }

            return bestCandidate;
        }

        private static Sprite CreateCircleSprite()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float half = (size - 1) * 0.5f;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float radial = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = 1f - Mathf.SmoothStep(0.62f, 0.86f, radial);
                    float glow = (1f - Mathf.SmoothStep(0.2f, 1f, radial)) * 0.35f;
                    float alpha = Mathf.Clamp01(core + glow);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
        }
    }
}
