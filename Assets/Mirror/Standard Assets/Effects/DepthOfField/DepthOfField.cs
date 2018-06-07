using UnityEngine;
using System;
using UnityStandardAssets.CinematicEffects;
namespace MStdioDepthOfField
{
    //Improvement ideas:
    //  Use rgba8 buffer in ldr / in some pass in hdr (in correlation to previous point and remapping coc from -1/0/1 to 0/0.5/1)
    //  Use temporal stabilisation
    //  Add a mode to do bokeh texture in quarter res as well
    //  Support different near and far blur for the bokeh texture
    //  Try distance field for the bokeh texture
    //  Try to separate the output of the blur pass to two rendertarget near+far, see the gain in quality vs loss in performance
    //  Try swirl effect on the samples of the circle blur

    //References :
    //  This DOF implementation use ideas from public sources, a big thank to them :
    //  http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare
    //  http://www.crytek.com/download/Sousa_Graphics_Gems_CryENGINE3.pdf
    //  http://graphics.cs.williams.edu/papers/MedianShaderX6/
    //  http://http.developer.nvidia.com/GPUGems/gpugems_ch24.html
    //  http://vec3.ca/bicubic-filtering-in-fewer-taps/

    [ExecuteInEditMode]
    [AddComponentMenu("Image Effects/Cinematic/Depth Of Field")]
    [RequireComponent(typeof(Camera))]
    public class DepthOfField : MonoBehaviour
    {
        private const float kMaxBlur = 40.0f;

        #region Render passes
        private enum Passes
        {
            BlurAlphaWeighted,
            BoxBlur,
            DilateFgCocFromColor,
            DilateFgCoc,
            CaptureCocExplicit,
            VisualizeCocExplicit,
            CocPrefilter,
            CircleBlur,
            CircleBlurWithDilatedFg,
            CircleBlurLowQuality,
            CircleBlowLowQualityWithDilatedFg,
            MergeExplicit,
            ShapeLowQuality,
            ShapeLowQualityDilateFg,
            ShapeLowQualityMerge,
            ShapeLowQualityMergeDilateFg,
            ShapeMediumQuality,
            ShapeMediumQualityDilateFg,
            ShapeMediumQualityMerge,
            ShapeMediumQualityMergeDilateFg,
            ShapeHighQuality,
            ShapeHighQualityDilateFg,
            ShapeHighQualityMerge,
            ShapeHighQualityMergeDilateFg
        }

        private enum MedianPasses
        {
            Median3,
            Median3X3
        }

        private enum BokehTexturesPasses
        {
            Apply,
            Collect
        }
        #endregion



        #region Settings

        [Serializable]
        public struct FocusSettings
        { 
            [Min(0f), Tooltip("Far blur falloff (in world units).")]
            public float farFalloff;

            public static FocusSettings defaultSettings
            {
                get
                {
                    return new FocusSettings
                    {
                        farFalloff = 2f
                    };
                }
            }
        }

        [Serializable]
        public struct BokehTextureSettings
        {
            [Tooltip("Adding a texture to this field will enable the use of \"Bokeh Textures\". Use with care. This feature is only available on Shader Model 5 compatible-hardware and performance scale with the amount of bokeh.")]
            public Texture2D texture;

            [Range(0.01f, 10f), Tooltip("Maximum size of bokeh textures on screen.")]
            public float scale;

            [Range(0.01f, 100f), Tooltip("Bokeh brightness.")]
            public float intensity;

            [Range(0.01f, 5f), Tooltip("Controls the amount of bokeh textures. Lower values mean more bokeh splats.")]
            public float threshold;

            [Range(0.01f, 1f), Tooltip("Controls the spawn conditions. Lower values mean more visible bokeh.")]
            public float spawnHeuristic;

            public static BokehTextureSettings defaultSettings
            {
                get
                {
                    return new BokehTextureSettings
                    {
                        texture = null,
                        scale = 1f,
                        intensity = 50f,
                        threshold = 2f,
                        spawnHeuristic = 0.15f
                    };
                }
            }
        }
        #endregion


        public FocusSettings focus = FocusSettings.defaultSettings;
        public BokehTextureSettings bokehTexture = BokehTextureSettings.defaultSettings;

        private Shader m_FilmicDepthOfFieldShader;

        public Shader filmicDepthOfFieldShader
        {
            get
            {
                if (m_FilmicDepthOfFieldShader == null)
                    m_FilmicDepthOfFieldShader = Shader.Find("Hidden/DepthOfField/DepthOfField");

                return m_FilmicDepthOfFieldShader;
            }
        }

        private Shader m_MedianFilterShader;

        public Shader medianFilterShader
        {
            get
            {
                if (m_MedianFilterShader == null)
                    m_MedianFilterShader = Shader.Find("Hidden/DepthOfField/MedianFilter");

                return m_MedianFilterShader;
            }
        }

        private Shader m_TextureBokehShader;

        public Shader textureBokehShader
        {
            get
            {
                if (m_TextureBokehShader == null)
                    m_TextureBokehShader = Shader.Find("Hidden/DepthOfField/BokehSplatting");

                return m_TextureBokehShader;
            }
        }

        private RenderTextureUtility m_RTU = new RenderTextureUtility();

        private Material m_FilmicDepthOfFieldMaterial;

        public Material filmicDepthOfFieldMaterial
        {
            get
            {
                if (m_FilmicDepthOfFieldMaterial == null)
                    m_FilmicDepthOfFieldMaterial = ImageEffectHelper.CheckShaderAndCreateMaterial(filmicDepthOfFieldShader);

                return m_FilmicDepthOfFieldMaterial;
            }
        }

        private Material m_MedianFilterMaterial;

        public Material medianFilterMaterial
        {
            get
            {
                if (m_MedianFilterMaterial == null)
                    m_MedianFilterMaterial = ImageEffectHelper.CheckShaderAndCreateMaterial(medianFilterShader);

                return m_MedianFilterMaterial;
            }
        }

        private Material m_TextureBokehMaterial;

        public Material textureBokehMaterial
        {
            get
            {
                if (m_TextureBokehMaterial == null)
                    m_TextureBokehMaterial = ImageEffectHelper.CheckShaderAndCreateMaterial(textureBokehShader);

                return m_TextureBokehMaterial;
            }
        }

        private ComputeBuffer m_ComputeBufferDrawArgs;

        public ComputeBuffer computeBufferDrawArgs
        {
            get
            {
                if (m_ComputeBufferDrawArgs == null)
                {
#if UNITY_5_4_OR_NEWER
                    m_ComputeBufferDrawArgs = new ComputeBuffer(1, 16, ComputeBufferType.IndirectArguments);
#else
                    m_ComputeBufferDrawArgs = new ComputeBuffer(1, 16, ComputeBufferType.DrawIndirect);
#endif
                    m_ComputeBufferDrawArgs.SetData(new[] {0, 1, 0, 0});
                }

                return m_ComputeBufferDrawArgs;
            }
        }

        private ComputeBuffer m_ComputeBufferPoints;

        public ComputeBuffer computeBufferPoints
        {
            get
            {
                if (m_ComputeBufferPoints == null)
                    m_ComputeBufferPoints = new ComputeBuffer(90000, 12 + 16, ComputeBufferType.Append);

                return m_ComputeBufferPoints;
            }
        }
        private float m_LastApertureOrientation;
        private Vector4 m_OctogonalBokehDirection1;
        private Vector4 m_OctogonalBokehDirection2;
        private Vector4 m_OctogonalBokehDirection3;
        private Vector4 m_OctogonalBokehDirection4;
        private Vector4 m_HexagonalBokehDirection1;
        private Vector4 m_HexagonalBokehDirection2;
        private Vector4 m_HexagonalBokehDirection3;

        private int m_BlurParams;
        private int m_BlurCoe;
        private int m_Offsets;
        private int m_BlurredColor;
        private int m_SpawnHeuristic;
        private int m_BokehParams;
        private int m_Convolved_TexelSize;
        private int m_SecondTex;
        private int m_ThirdTex;
        private int m_MainTex;
        private int m_Screen;
        private static int pointBuffer = Shader.PropertyToID("pointBuffer");
        private Camera sceneCam;
        private void Awake()
        {
            sceneCam = GetComponent<Camera>();
            m_BlurParams = Shader.PropertyToID("_BlurParams");
            m_BlurCoe = Shader.PropertyToID("_BlurCoe");
            m_Offsets = Shader.PropertyToID("_Offsets");
            m_BlurredColor = Shader.PropertyToID("_BlurredColor");
            m_SpawnHeuristic = Shader.PropertyToID("_SpawnHeuristic");
            m_BokehParams = Shader.PropertyToID("_BokehParams");
            m_Convolved_TexelSize = Shader.PropertyToID("_Convolved_TexelSize");
            m_SecondTex = Shader.PropertyToID("_SecondTex");
            m_ThirdTex = Shader.PropertyToID("_ThirdTex");
            m_MainTex = Shader.PropertyToID("_MainTex");
            m_Screen = Shader.PropertyToID("_Screen");
            m_FilmicDepthOfFieldShader = Shader.Find("Hidden/DepthOfField/DepthOfField");
            m_TextureBokehShader = Shader.Find("Hidden/DepthOfField/BokehSplatting");
            m_MedianFilterShader = Shader.Find("Hidden/DepthOfField/MedianFilter");
            m_OctogonalBokehDirection1 = new Vector4(0.5f, 0f, 0f, 0f);
            m_OctogonalBokehDirection2 = new Vector4(0f, 0.5f, 1f, 0f);
            m_OctogonalBokehDirection3 = new Vector4(-0.353553f, 0.353553f, 1f, 0f);
            m_OctogonalBokehDirection4 = new Vector4(0.353553f, 0.353553f, 1f, 0f);

            m_HexagonalBokehDirection1 = new Vector4(0.5f, 0f, 0f, 0f);
            m_HexagonalBokehDirection2 = new Vector4(0.25f, 0.433013f, 1f, 0f);
            m_HexagonalBokehDirection3 = new Vector4(0.25f, -0.433013f, 1f, 0f);
        }

        private void OnEnable()
        {
            if (!ImageEffectHelper.IsSupported(filmicDepthOfFieldShader, true, true, this) || !ImageEffectHelper.IsSupported(medianFilterShader, true, true, this))
            {
                enabled = false;
                return;
            }

            if (ImageEffectHelper.supportsDX11 && !ImageEffectHelper.IsSupported(textureBokehShader, true, true, this))
            {
                enabled = false;
                return;
            }

            sceneCam.depthTextureMode |= DepthTextureMode.Depth;
        }

        private void OnDisable()
        {
            ReleaseComputeResources();

            if (m_FilmicDepthOfFieldMaterial != null)
                DestroyImmediate(m_FilmicDepthOfFieldMaterial);

            if (m_TextureBokehMaterial != null)
                DestroyImmediate(m_TextureBokehMaterial);

            if (m_MedianFilterMaterial != null)
                DestroyImmediate(m_MedianFilterMaterial);

            m_FilmicDepthOfFieldMaterial = null;
            m_TextureBokehMaterial = null;
            m_MedianFilterMaterial = null;

            m_RTU.ReleaseAllTemporaryRenderTextures();
        }

        //-------------------------------------------------------------------//
        // Main entry point                                                  //
        //-------------------------------------------------------------------//
        [ImageEffectOpaque]
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (medianFilterMaterial == null || filmicDepthOfFieldMaterial == null)
            {
                Graphics.Blit(source, destination);
                return;
            }
            DoDepthOfField(source, destination);
            

            m_RTU.ReleaseAllTemporaryRenderTextures();
        }

        private void DoDepthOfField(RenderTexture source, RenderTexture destination)
        {
            float radiusAdjustement = source.height / 720f;

            float textureBokehScale = radiusAdjustement;
            float textureBokehMaxRadius = textureBokehScale * 30f;

            float nearBlurRadius = 40 * radiusAdjustement;
            float maxBlurRadius = nearBlurRadius;
            maxBlurRadius *= 1.2f;
            if (maxBlurRadius < 0.5f)
            {
                Graphics.Blit(source, destination);
                return;
            }

            // Quarter resolution
            int rtW = source.width / 2;
            int rtH = source.height / 2;
            var blurrinessCoe = new Vector4(nearBlurRadius * 0.5f, nearBlurRadius * 0.5f, 0f, 0f);
            var colorAndCoc = m_RTU.GetTemporaryRenderTexture(rtW, rtH);
            var colorAndCoc2 = m_RTU.GetTemporaryRenderTexture(rtW, rtH);

            // Downsample to Color + COC buffer
            Vector4 cocParam;
            Vector4 cocCoe;
            ComputeCocParameters(out cocParam, out cocCoe);
            filmicDepthOfFieldMaterial.SetVector(m_BlurParams, cocParam);
            filmicDepthOfFieldMaterial.SetVector(m_BlurCoe, cocCoe);
            Graphics.Blit(source, colorAndCoc2, filmicDepthOfFieldMaterial, (int)Passes.CaptureCocExplicit);
            var src = colorAndCoc2;
            var dst = colorAndCoc;

            // Collect texture bokeh candidates and replace with a darker pixel
            if (shouldPerformBokeh)
            {
                // Blur a bit so we can do a frequency check
                var blurred = m_RTU.GetTemporaryRenderTexture(rtW, rtH);
                Graphics.Blit(src, blurred, filmicDepthOfFieldMaterial, (int)Passes.BoxBlur);
                filmicDepthOfFieldMaterial.SetVector(m_Offsets, new Vector4(0f, 1.5f, 0f, 1.5f));
                Graphics.Blit(blurred, dst, filmicDepthOfFieldMaterial, (int)Passes.BlurAlphaWeighted);
                filmicDepthOfFieldMaterial.SetVector(m_Offsets, new Vector4(1.5f, 0f, 0f, 1.5f));
                Graphics.Blit(dst, blurred, filmicDepthOfFieldMaterial, (int)Passes.BlurAlphaWeighted);

                // Collect texture bokeh candidates and replace with a darker pixel
                textureBokehMaterial.SetTexture(m_BlurredColor, blurred);
                textureBokehMaterial.SetFloat(m_SpawnHeuristic, bokehTexture.spawnHeuristic);
                textureBokehMaterial.SetVector(m_BokehParams, new Vector4(bokehTexture.scale * textureBokehScale, bokehTexture.intensity, bokehTexture.threshold, textureBokehMaxRadius));
                Graphics.SetRandomWriteTarget(1, computeBufferPoints);
                Graphics.Blit(src, dst, textureBokehMaterial, (int)BokehTexturesPasses.Collect);
                Graphics.ClearRandomWriteTargets();
                SwapRenderTexture(ref src, ref dst);
                m_RTU.ReleaseTemporaryRenderTexture(blurred);
            }

            filmicDepthOfFieldMaterial.SetVector(m_BlurParams, cocParam);
            filmicDepthOfFieldMaterial.SetVector(m_BlurCoe, blurrinessCoe);

            // Dilate near blur factor
            RenderTexture blurredFgCoc = null;

                var blurredFgCoc2 = m_RTU.GetTemporaryRenderTexture(rtW, rtH, 0, RenderTextureFormat.RGHalf);
                blurredFgCoc = m_RTU.GetTemporaryRenderTexture(rtW, rtH, 0, RenderTextureFormat.RGHalf);
                filmicDepthOfFieldMaterial.SetVector(m_Offsets, new Vector4(0f, nearBlurRadius * 0.75f, 0f, 0f));
                Graphics.Blit(src, blurredFgCoc2, filmicDepthOfFieldMaterial, (int)Passes.DilateFgCocFromColor);
                filmicDepthOfFieldMaterial.SetVector(m_Offsets, new Vector4(nearBlurRadius * 0.75f, 0f, 0f, 0f));
                Graphics.Blit(blurredFgCoc2, blurredFgCoc, filmicDepthOfFieldMaterial, (int)Passes.DilateFgCoc);
                m_RTU.ReleaseTemporaryRenderTexture(blurredFgCoc2);
                blurredFgCoc.filterMode = FilterMode.Point;

                Graphics.Blit(src, dst, filmicDepthOfFieldMaterial, (int)Passes.CocPrefilter);
                SwapRenderTexture(ref src, ref dst);

                DoHexagonalBlur(blurredFgCoc, ref src, ref dst, maxBlurRadius);



            // Smooth result

            Graphics.Blit(src, dst, medianFilterMaterial, (int)MedianPasses.Median3X3);
            SwapRenderTexture(ref src, ref dst);
            // Merge to full resolution (with boost) + upsampling (linear or bicubic)
            filmicDepthOfFieldMaterial.SetVector(m_BlurCoe, blurrinessCoe);
            filmicDepthOfFieldMaterial.SetVector(m_Convolved_TexelSize, new Vector4(src.width, src.height, 1f / src.width, 1f / src.height));
            filmicDepthOfFieldMaterial.SetTexture(m_SecondTex, src);
            int mergePass = (int)Passes.MergeExplicit;

            // Apply texture bokeh
            if (shouldPerformBokeh)
            {
                var tmp = m_RTU.GetTemporaryRenderTexture(source.height, source.width, 0, source.format);
                Graphics.Blit(source, tmp, filmicDepthOfFieldMaterial, mergePass);

                Graphics.SetRenderTarget(tmp);
                ComputeBuffer.CopyCount(computeBufferPoints, computeBufferDrawArgs, 0);
                textureBokehMaterial.SetBuffer(pointBuffer, computeBufferPoints);
                textureBokehMaterial.SetTexture(m_MainTex, bokehTexture.texture);
                textureBokehMaterial.SetVector(m_Screen, new Vector3(1f / (1f * source.width), 1f / (1f * source.height), textureBokehMaxRadius));
                textureBokehMaterial.SetPass((int)BokehTexturesPasses.Apply);
                Graphics.DrawProceduralIndirect(MeshTopology.Points, computeBufferDrawArgs, 0);
                Graphics.Blit(tmp, destination); // Hackaround for DX11 flipfun (OPTIMIZEME)
            }
            else
            {
                Graphics.Blit(source, destination, filmicDepthOfFieldMaterial, mergePass);
            }
        }

        //-------------------------------------------------------------------//
        // Blurs                                                             //
        //-------------------------------------------------------------------//
        private void DoHexagonalBlur(RenderTexture blurredFgCoc, ref RenderTexture src, ref RenderTexture dst, float maxRadius)
        {
            int blurPass;
            int blurPassMerge;
            GetDirectionalBlurPassesFromRadius(blurredFgCoc, maxRadius, out blurPass, out blurPassMerge);
            filmicDepthOfFieldMaterial.SetTexture(m_SecondTex, blurredFgCoc);
            var tmp = m_RTU.GetTemporaryRenderTexture(src.width, src.height, 0, src.format);

            filmicDepthOfFieldMaterial.SetVector(m_Offsets, m_HexagonalBokehDirection1);
            Graphics.Blit(src, tmp, filmicDepthOfFieldMaterial, blurPass);

            filmicDepthOfFieldMaterial.SetVector(m_Offsets, m_HexagonalBokehDirection2);
            Graphics.Blit(tmp, src, filmicDepthOfFieldMaterial, blurPass);

            filmicDepthOfFieldMaterial.SetVector(m_Offsets, m_HexagonalBokehDirection3);
            filmicDepthOfFieldMaterial.SetTexture(m_ThirdTex, src);
            Graphics.Blit(tmp, dst, filmicDepthOfFieldMaterial, blurPassMerge);
            m_RTU.ReleaseTemporaryRenderTexture(tmp);
            SwapRenderTexture(ref src, ref dst);
        }

        private void DoOctogonalBlur(RenderTexture blurredFgCoc, ref RenderTexture src, ref RenderTexture dst, float maxRadius)
        {

            int blurPass;
            int blurPassMerge;
            GetDirectionalBlurPassesFromRadius(blurredFgCoc, maxRadius, out blurPass, out blurPassMerge);
            filmicDepthOfFieldMaterial.SetTexture(m_SecondTex, blurredFgCoc);
            var tmp = m_RTU.GetTemporaryRenderTexture(src.width, src.height, 0, src.format);

            filmicDepthOfFieldMaterial.SetVector(m_Offsets, m_OctogonalBokehDirection1);
            Graphics.Blit(src, tmp, filmicDepthOfFieldMaterial, blurPass);

            filmicDepthOfFieldMaterial.SetVector(m_Offsets, m_OctogonalBokehDirection2);
            Graphics.Blit(tmp, dst, filmicDepthOfFieldMaterial, blurPass);

            filmicDepthOfFieldMaterial.SetVector(m_Offsets, m_OctogonalBokehDirection3);
            Graphics.Blit(src, tmp, filmicDepthOfFieldMaterial, blurPass);

            filmicDepthOfFieldMaterial.SetVector(m_Offsets, m_OctogonalBokehDirection4);
            filmicDepthOfFieldMaterial.SetTexture(m_ThirdTex, dst);
            Graphics.Blit(tmp, src, filmicDepthOfFieldMaterial, blurPassMerge);
            m_RTU.ReleaseTemporaryRenderTexture(tmp);
        }

        private void DoCircularBlur(RenderTexture blurredFgCoc, ref RenderTexture src, ref RenderTexture dst, float maxRadius)
        {
            int bokehPass;

            if (blurredFgCoc != null)
            {
                filmicDepthOfFieldMaterial.SetTexture(m_SecondTex, blurredFgCoc);
                bokehPass = (maxRadius > 10f) ? (int)Passes.CircleBlurWithDilatedFg : (int)Passes.CircleBlowLowQualityWithDilatedFg;
            }
            else
            {
                bokehPass = (maxRadius > 10f) ? (int)Passes.CircleBlur : (int)Passes.CircleBlurLowQuality;
            }

            Graphics.Blit(src, dst, filmicDepthOfFieldMaterial, bokehPass);
            SwapRenderTexture(ref src, ref dst);
        }

        //-------------------------------------------------------------------//
        // Helpers                                                           //
        //-------------------------------------------------------------------//
        private void ComputeCocParameters(out Vector4 blurParams, out Vector4 blurCoe)
        {

            float focusDistance = 0;
            float farFalloff = focus.farFalloff * 2f;
            float farPlane = 0.1f;
            farPlane += (farFalloff * 0.5f);
            focusDistance = (-0.05f + farPlane) * 0.5f;

            float focusDistance01 = focusDistance / sceneCam.farClipPlane;
            float nearDistance01 = -0.05f / sceneCam.farClipPlane;
            float farDistance01 = farPlane / sceneCam.farClipPlane;

            var dof = farPlane + 0.05f;
            var dof01 = farDistance01 - nearDistance01;
            var farFalloff01 = farFalloff / dof;
            float nearFocusRange01 = (dof01 * 0.5f);
            float farFocusRange01 = (1f - farFalloff01) * (dof01 * 0.5f);

            if (focusDistance01 <= nearDistance01)
                focusDistance01 = nearDistance01 + 1e-6f;
            if (focusDistance01 >= farDistance01)
                focusDistance01 = farDistance01 - 1e-6f;

            if ((focusDistance01 - nearFocusRange01) <= nearDistance01)
                nearFocusRange01 = focusDistance01 - nearDistance01 - 1e-6f;
            if ((focusDistance01 + farFocusRange01) >= farDistance01)
                farFocusRange01 = farDistance01 - focusDistance01 - 1e-6f;

            float a1 = 1f / (nearDistance01 - focusDistance01 + nearFocusRange01);
            float a2 = 1f / (farDistance01 - focusDistance01 - farFocusRange01);
            float b1 = 1f - a1 * nearDistance01;
            float b2 = 1f - a2 * farDistance01;
            blurParams = new Vector4(-a1, -b1, a2, b2);
            blurCoe = new Vector4(0f, 0f, (b2 - b1) / (a1 - a2), 0f);
        }

        private void ReleaseComputeResources()
        {
            if (m_ComputeBufferDrawArgs != null)
                m_ComputeBufferDrawArgs.Release();

            if (m_ComputeBufferPoints != null)
                m_ComputeBufferPoints.Release();

            m_ComputeBufferDrawArgs = null;
            m_ComputeBufferPoints = null;
        }


        private bool shouldPerformBokeh
        {
            get { return ImageEffectHelper.supportsDX11 && bokehTexture.texture != null && textureBokehMaterial; }
        }

        private static void Rotate2D(ref Vector4 direction, float cosinus, float sinus)
        {
            var source = direction;
            direction.x = source.x * cosinus - source.y * sinus;
            direction.y = source.x * sinus + source.y * cosinus;
        }

        private static void SwapRenderTexture(ref RenderTexture src, ref RenderTexture dst)
        {
            RenderTexture tmp = dst;
            dst = src;
            src = tmp;
        }

        private static void GetDirectionalBlurPassesFromRadius(RenderTexture blurredFgCoc, float maxRadius, out int blurPass, out int blurAndMergePass)
        {
            if (blurredFgCoc == null)
            {
                if (maxRadius > 10f)
                {
                    blurPass = (int)Passes.ShapeHighQuality;
                    blurAndMergePass = (int)Passes.ShapeHighQualityMerge;
                }
                else if (maxRadius > 5f)
                {
                    blurPass = (int)Passes.ShapeMediumQuality;
                    blurAndMergePass = (int)Passes.ShapeMediumQualityMerge;
                }
                else
                {
                    blurPass = (int)Passes.ShapeLowQuality;
                    blurAndMergePass = (int)Passes.ShapeLowQualityMerge;
                }
            }
            else
            {
                if (maxRadius > 10f)
                {
                    blurPass = (int)Passes.ShapeHighQualityDilateFg;
                    blurAndMergePass = (int)Passes.ShapeHighQualityMergeDilateFg;
                }
                else if (maxRadius > 5f)
                {
                    blurPass = (int)Passes.ShapeMediumQualityDilateFg;
                    blurAndMergePass = (int)Passes.ShapeMediumQualityMergeDilateFg;
                }
                else
                {
                    blurPass = (int)Passes.ShapeLowQualityDilateFg;
                    blurAndMergePass = (int)Passes.ShapeLowQualityMergeDilateFg;
                }
            }
        }
    }
}
