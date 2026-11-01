using System;
using Unity.InferenceEngine;
using Unity.Mathematics;
using UnityEngine;

namespace Before.Capture
{
    /// <summary>
    /// Owns the Sentis workers/tensors for the pinned two-stage BlazeHand pipeline
    /// (detector 1x192x192x3 -> best-anchor crop -> landmarker 1x224x224x3 -> 21 xyz landmarks).
    /// Callers must keep at most one DetectAsync in flight and Dispose exactly once.
    /// </summary>
    public sealed class BlazeHandTracker : IDisposable
    {
        public const int LandmarkCount = 21;
        private const int AnchorCount = 2016;
        private const int DetectorInputSize = 192;
        private const int LandmarkerInputSize = 224;

        private Worker detectorWorker;
        private Worker landmarkerWorker;
        private Tensor<float> detectorInput;
        private Tensor<float> landmarkerInput;
        private float[,] anchors;
        private readonly Vector3[] landmarkScratch = new Vector3[LandmarkCount];
        private bool disposed;
        private bool busy;
        private bool disposeRequested;

        public bool IsInitialized { get; private set; }
        public float ScoreThreshold { get; set; } = 0.5f;
        public string BackendName => BackendType.GPUCompute.ToString();

        public void Initialize(ModelAsset detectorAsset, ModelAsset landmarkerAsset, string anchorsCsv)
        {
            if (IsInitialized) throw new InvalidOperationException("BlazeHandTracker already initialized.");
            if (disposed) throw new ObjectDisposedException(nameof(BlazeHandTracker));
            if (detectorAsset == null || landmarkerAsset == null || string.IsNullOrEmpty(anchorsCsv))
            {
                throw new ArgumentException("BlazeHandTracker requires detector, landmarker, and anchors csv.");
            }

            anchors = BlazeMath.LoadAnchors(anchorsCsv, AnchorCount);

            var detectorModel = ModelLoader.Load(detectorAsset);
            var graph = new FunctionalGraph();
            var input = graph.AddInput(detectorModel, 0);
            var outputs = Functional.Forward(detectorModel, input);
            var filtered = BlazeMath.ArgMaxFiltering(outputs[0], outputs[1]);
            detectorModel = graph.Compile(filtered.Item1, filtered.Item2, filtered.Item3);
            detectorWorker = new Worker(detectorModel, BackendType.GPUCompute);

            var landmarkerModel = ModelLoader.Load(landmarkerAsset);
            landmarkerWorker = new Worker(landmarkerModel, BackendType.GPUCompute);

            detectorInput = new Tensor<float>(new TensorShape(1, DetectorInputSize, DetectorInputSize, 3));
            landmarkerInput = new Tensor<float>(new TensorShape(1, LandmarkerInputSize, LandmarkerInputSize, 3));
            IsInitialized = true;
        }

        /// <summary>
        /// Runs one full detector+landmarker pass with async GPU readback.
        /// Returned landmark coordinates are normalized [0,1] in source-image space.
        /// </summary>
        public async Awaitable<HandObservation> DetectAsync(Texture texture)
        {
            if (!IsInitialized || disposed || disposeRequested) throw new OperationCanceledException();
            busy = true;
            try
            {
                return await DetectCore(texture);
            }
            finally
            {
                busy = false;
                if (disposeRequested && !disposed) DisposeNow();
            }
        }

        private async Awaitable<HandObservation> DetectCore(Texture texture)
        {
            double started = Time.realtimeSinceStartupAsDouble;
            float texWidth = texture.width;
            float texHeight = texture.height;
            float size = Mathf.Max(texture.width, texture.height);

            float scale = size / DetectorInputSize;
            float2x3 m = BlazeMath.Mul(
                BlazeMath.TranslationMatrix(0.5f * (new float2(texWidth, texHeight) + new float2(-size, size))),
                BlazeMath.ScaleMatrix(new float2(scale, -scale)));
            BlazeMath.SampleImageAffine(texture, detectorInput, m);

            detectorWorker.Schedule(detectorInput);

            var idxAwaitable = (detectorWorker.PeekOutput(0) as Tensor<int>).ReadbackAndCloneAsync();
            var scoreAwaitable = (detectorWorker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
            var boxAwaitable = (detectorWorker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();

            using var outputIdx = await idxAwaitable;
            using var outputScore = await scoreAwaitable;
            using var outputBox = await boxAwaitable;
            if (disposed || disposeRequested) throw new OperationCanceledException();

            float score = outputScore[0];
            if (score < ScoreThreshold)
            {
                double endedNoHand = Time.realtimeSinceStartupAsDouble;
                return HandObservation.NoHand(endedNoHand, score, (float)((endedNoHand - started) * 1000.0));
            }

            int idx = outputIdx[0];
            float2 anchorPosition = DetectorInputSize * new float2(anchors[idx, 0], anchors[idx, 1]);
            float2 boxCentre = anchorPosition + new float2(outputBox[0, 0, 0], outputBox[0, 0, 1]);
            float boxSize = math.max(outputBox[0, 0, 2], outputBox[0, 0, 3]);

            float2 kp0 = anchorPosition + new float2(outputBox[0, 0, 4 + 2 * 0 + 0], outputBox[0, 0, 4 + 2 * 0 + 1]);
            float2 kp2 = anchorPosition + new float2(outputBox[0, 0, 4 + 2 * 2 + 0], outputBox[0, 0, 4 + 2 * 2 + 1]);
            float2 delta = kp2 - kp0;
            float2 up = delta / math.length(delta);
            float theta = math.atan2(delta.y, delta.x);
            float rotation = 0.5f * Mathf.PI - theta;
            boxCentre += 0.5f * boxSize * up;
            boxSize *= 2.6f;

            float2 origin2 = new float2(0.5f * LandmarkerInputSize, 0.5f * LandmarkerInputSize);
            float scale2 = boxSize / LandmarkerInputSize;
            float2x3 m2 = BlazeMath.Mul(m,
                BlazeMath.Mul(
                    BlazeMath.Mul(
                        BlazeMath.Mul(
                            BlazeMath.TranslationMatrix(boxCentre),
                            BlazeMath.ScaleMatrix(new float2(scale2, -scale2))),
                        BlazeMath.RotationMatrix(rotation)),
                    BlazeMath.TranslationMatrix(-origin2)));
            if (texture == null) throw new OperationCanceledException(); // destroyed mid-flight (Unity fake-null)
            BlazeMath.SampleImageAffine(texture, landmarkerInput, m2);

            landmarkerWorker.Schedule(landmarkerInput);
            var landmarksAwaitable = (landmarkerWorker.PeekOutput("Identity") as Tensor<float>).ReadbackAndCloneAsync();
            using var landmarks = await landmarksAwaitable;
            if (disposed || disposeRequested) throw new OperationCanceledException();

            for (int i = 0; i < LandmarkCount; i++)
            {
                float2 imagePosition = BlazeMath.Mul(m2, new float2(landmarks[3 * i + 0], landmarks[3 * i + 1]));
                landmarkScratch[i] = new Vector3(
                    imagePosition.x / texWidth,
                    imagePosition.y / texHeight,
                    landmarks[3 * i + 2] / texHeight);
            }

            double ended = Time.realtimeSinceStartupAsDouble;
            return new HandObservation(ended, true, score, (float)((ended - started) * 1000.0), landmarkScratch);
        }

        /// <summary>
        /// Safe to call while a detection is in flight: actual release is deferred
        /// until the in-flight pass finishes, so GPU resources are never torn down mid-use.
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposeRequested = true;
            IsInitialized = false;
            if (!busy) DisposeNow();
        }

        private void DisposeNow()
        {
            if (disposed) return;
            disposed = true;
            detectorWorker?.Dispose();
            landmarkerWorker?.Dispose();
            detectorInput?.Dispose();
            landmarkerInput?.Dispose();
            detectorWorker = null;
            landmarkerWorker = null;
            detectorInput = null;
            landmarkerInput = null;
        }
    }
}
