using System.Globalization;
using Unity.InferenceEngine;
using Unity.Mathematics;
using UnityEngine;

namespace Before.Capture
{
    /// <summary>
    /// Production adapter of the pinned Unity sentis-samples BlazeDetectionSample/Hand math
    /// (commit 2679157ac38406fe6a6d12f6a3067054c5eb5ab8; archived reference in ThirdPartySource/UnityBlazeHand).
    /// Affine sampling uses the pinned ImageTransform.compute from Resources.
    /// </summary>
    public static class BlazeMath
    {
        public static float2x3 Mul(float2x3 a, float2x3 b)
        {
            return new float2x3(
                a[0][0] * b[0][0] + a[1][0] * b[0][1],
                a[0][0] * b[1][0] + a[1][0] * b[1][1],
                a[0][0] * b[2][0] + a[1][0] * b[2][1] + a[2][0],
                a[0][1] * b[0][0] + a[1][1] * b[0][1],
                a[0][1] * b[1][0] + a[1][1] * b[1][1],
                a[0][1] * b[2][0] + a[1][1] * b[2][1] + a[2][1]
            );
        }

        public static float2 Mul(float2x3 a, float2 b)
        {
            return new float2(
                a[0][0] * b.x + a[1][0] * b.y + a[2][0],
                a[0][1] * b.x + a[1][1] * b.y + a[2][1]
            );
        }

        public static float2x3 RotationMatrix(float theta)
        {
            float sinTheta = math.sin(theta);
            float cosTheta = math.cos(theta);
            return new float2x3(
                cosTheta, -sinTheta, 0,
                sinTheta, cosTheta, 0
            );
        }

        public static float2x3 TranslationMatrix(float2 delta)
        {
            return new float2x3(
                1, 0, delta.x,
                0, 1, delta.y
            );
        }

        public static float2x3 ScaleMatrix(float2 scale)
        {
            return new float2x3(
                scale.x, 0, 0,
                0, scale.y, 0
            );
        }

        private static FunctionalTensor ScoreFiltering(FunctionalTensor rawScores, float scoreThreshold)
        {
            return Functional.Sigmoid(Functional.Clamp(rawScores, -scoreThreshold, scoreThreshold));
        }

        public static (FunctionalTensor, FunctionalTensor, FunctionalTensor) ArgMaxFiltering(FunctionalTensor rawBoxes, FunctionalTensor rawScores)
        {
            var detectionScores = ScoreFiltering(rawScores, 100f); // (1, 2016, 1)
            var bestScoreIndex = Functional.ArgMax(rawScores, 1).Squeeze();
            var selectedBoxes = Functional.IndexSelect(rawBoxes, 1, bestScoreIndex).Unsqueeze(0); // (1, 1, 16)
            var selectedScores = Functional.IndexSelect(detectionScores, 1, bestScoreIndex).Unsqueeze(0); // (1, 1, 1)
            return (bestScoreIndex, selectedScores, selectedBoxes);
        }

        private static ComputeShader imageTransformShader;
        private static int imageSampleKernel;
        private static readonly int OptrId = Shader.PropertyToID("Optr");
        private static readonly int XTex2DId = Shader.PropertyToID("X_tex2D");
        private static readonly int OHeightId = Shader.PropertyToID("O_height");
        private static readonly int OWidthId = Shader.PropertyToID("O_width");
        private static readonly int OChannelsId = Shader.PropertyToID("O_channels");
        private static readonly int XHeightId = Shader.PropertyToID("X_height");
        private static readonly int XWidthId = Shader.PropertyToID("X_width");
        private static readonly int AffineMatrixId = Shader.PropertyToID("affineMatrix");

        private static void EnsureShader()
        {
            if (imageTransformShader != null) return;
            imageTransformShader = Resources.Load<ComputeShader>("ComputeShaders/ImageTransform");
            if (imageTransformShader == null)
            {
                throw new System.InvalidOperationException(
                    "ImageTransform.compute not found at Resources/ComputeShaders/ImageTransform");
            }
            imageSampleKernel = imageTransformShader.FindKernel("ImageSample");
        }

        private static int IDivC(int v, int div)
        {
            return (v + div - 1) / div;
        }

        public static void SampleImageAffine(Texture srcTexture, Tensor<float> dstTensor, float2x3 m)
        {
            EnsureShader();
            var tensorData = ComputeTensorData.Pin(dstTensor, false);

            imageTransformShader.SetTexture(imageSampleKernel, XTex2DId, srcTexture);
            imageTransformShader.SetBuffer(imageSampleKernel, OptrId, tensorData.buffer);
            imageTransformShader.SetInt(OHeightId, dstTensor.shape[1]);
            imageTransformShader.SetInt(OWidthId, dstTensor.shape[2]);
            imageTransformShader.SetInt(OChannelsId, dstTensor.shape[3]);
            imageTransformShader.SetInt(XHeightId, srcTexture.height);
            imageTransformShader.SetInt(XWidthId, srcTexture.width);
            imageTransformShader.SetMatrix(AffineMatrixId, new Matrix4x4(
                new Vector4(m[0][0], m[0][1]),
                new Vector4(m[1][0], m[1][1]),
                new Vector4(m[2][0], m[2][1]),
                Vector4.zero));
            imageTransformShader.Dispatch(imageSampleKernel, IDivC(dstTensor.shape[1], 8), IDivC(dstTensor.shape[1], 8), 1);
        }

        public static float[,] LoadAnchors(string csv, int numAnchors)
        {
            var anchors = new float[numAnchors, 4];
            var anchorLines = csv.Split('\n');
            for (int i = 0; i < numAnchors; i++)
            {
                var anchorValues = anchorLines[i].Split(',');
                for (int j = 0; j < 4; j++)
                {
                    anchors[i, j] = float.Parse(anchorValues[j], CultureInfo.InvariantCulture);
                }
            }
            return anchors;
        }
    }
}
