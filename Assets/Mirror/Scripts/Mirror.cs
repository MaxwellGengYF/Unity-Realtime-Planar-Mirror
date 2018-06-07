using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System;
using System.Collections.Generic;
using MStdioDepthOfField;

public class Mirror : MonoBehaviour
{

    public string ReflectionSample = "_ReflectionTex";
    public static bool s_InsideRendering = false;
    private int uniqueTextureID = -1;
    
    [HideInInspector]
    public int textureSize
    {
        get
        {
            return m_TextureSize;
        }
        set
        {
            if (!Application.isPlaying)
            {
                m_TextureSize = Mathf.Clamp(value, 1, 2048);
            }
        }
    }

    [HideInInspector]
    public int m_TextureSize = 256;
    [HideInInspector]
    public float m_ClipPlaneOffset = 0.01f;
    [Tooltip("With lots of small mirrors in the same plane position, you can add several SmallMirrors components and manage them with only one Mirror component to significantly save cost")]
    public SmallMirrors[] allMirrors = new SmallMirrors[0];

    public enum AntiAlias
    {
        X1 = 1,
        X2 = 2,
        X4 = 4,
        X8 = 8
    }
    [Tooltip("The normal transform(transform.up as normal)")]
    public Transform normalTrans;
    
    public enum RenderQuality
    {
        Default,
        High,
        Medium,
        Low,
        VeryLow
    }

    private RenderTexture m_ReflectionTexture = null;
    [HideInInspector]
    public bool useDistanceCull = false;
    [HideInInspector] public float m_SqrMaxdistance = 2500f;
    [HideInInspector] public float m_maxDistance = 50f;

    public float maxDistance
    {
        get
        {
            return m_maxDistance;
        }
        set
        {
            m_maxDistance = value;
            m_SqrMaxdistance = value * value;
        }
    }

    [HideInInspector]
    public float[] layerCullingDistances = new float[32];
    [HideInInspector]
    public Renderer render;
    private Camera cam;
    private Camera reflectionCamera;
    private Transform refT;
    private Transform camT;
    private List<Material> allMats = new List<Material>();
    private Action postProcessAction;

    private bool billboard;
    private bool softVeg;
    private bool softParticle;
    private AnisotropicFiltering ani;
    private ShadowResolution shaR;
    private ShadowQuality shadowQuality;
    private CommandBuffer buffer;
    private float widthHeightRate;
    [Header("AA & Post Processing Set")]
    [Tooltip("MSAA anti alias")]
    public AntiAlias MSAA = AntiAlias.X8;
    public bool addPostProcessingComponent = false;
    public bool enableDepthOfField;
    public float smoothness = 50;
    private const float roughOffset = 50f;
    private const float smoothOffset = 1e-5f;
    private Camera depthCam;
    private RenderTexture depthTexture;
    private DepthOfField dof;
    
    [Header("Optimization & Culling")]
    [Tooltip("Reflection Quality")]
    public RenderQuality renderQuality = RenderQuality.Default;
    [Tooltip("Mirror mask")]
    public LayerMask m_ReflectLayers = -1;
    public bool enableSelfCullingDistance = true;
    void Awake()
    {
        buffer = new CommandBuffer();
        uniqueTextureID = Shader.PropertyToID(ReflectionSample);
        if (!normalTrans)
        {
            normalTrans = new GameObject("Normal Trans").transform;
            normalTrans.position = transform.position;
            normalTrans.rotation = transform.rotation;
            normalTrans.SetParent(transform);
        }
        render = GetComponent<Renderer>();
        if (!render || !render.sharedMaterial)
        {
            Destroy(this);
        }
        for (int i = 0; i < allMirrors.Length; ++i)
        {
            allMirrors[i].manager = this;
        }
        for (int i = 0, length = render.sharedMaterials.Length; i < length; ++i)
        {
            Material m = render.sharedMaterials[i];
            if (!allMats.Contains(m))
                allMats.Add(m);
        }
        for (int i = 0; i < allMirrors.Length; ++i)
        {
            Renderer r = allMirrors[i].GetRenderer();
            for (int a = 0, length = r.sharedMaterials.Length; a < length; ++a)
            {
                Material m = r.sharedMaterials[a];
                if (!allMats.Contains(m))
                    allMats.Add(m);
            }
        }
        switch (renderQuality)
        {
            case RenderQuality.Default:
                postProcessAction = () => reflectionCamera.Render();
                break;
            case RenderQuality.High:
                postProcessAction = () =>
                {
                    billboard = QualitySettings.billboardsFaceCameraPosition;
                    QualitySettings.billboardsFaceCameraPosition = false;
                    softParticle = QualitySettings.softParticles;
                    softVeg = QualitySettings.softVegetation;
                    QualitySettings.softParticles = false;
                    QualitySettings.softVegetation = false;
                    ani = QualitySettings.anisotropicFiltering;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    shaR = QualitySettings.shadowResolution;
                    QualitySettings.shadowResolution = ShadowResolution.High;
                    reflectionCamera.Render();
                    QualitySettings.softParticles = softParticle;
                    QualitySettings.softVegetation = softVeg;
                    QualitySettings.billboardsFaceCameraPosition = billboard;
                    QualitySettings.anisotropicFiltering = ani;
                    QualitySettings.shadowResolution = shaR;
                };
                break;
            case RenderQuality.Medium:
                postProcessAction = () =>
                {
                    softParticle = QualitySettings.softParticles;
                    softVeg = QualitySettings.softVegetation;
                    QualitySettings.softParticles = false;
                    QualitySettings.softVegetation = false;
                    billboard = QualitySettings.billboardsFaceCameraPosition;
                    QualitySettings.billboardsFaceCameraPosition = false;
                    shadowQuality = QualitySettings.shadows;
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    ani = QualitySettings.anisotropicFiltering;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    shaR = QualitySettings.shadowResolution;
                    QualitySettings.shadowResolution = ShadowResolution.Low;
                    reflectionCamera.Render();
                    QualitySettings.softParticles = softParticle;
                    QualitySettings.softVegetation = softVeg;
                    QualitySettings.shadows = shadowQuality;
                    QualitySettings.billboardsFaceCameraPosition = billboard;
                    QualitySettings.anisotropicFiltering = ani;
                    QualitySettings.shadowResolution = shaR;
                };
                break;
            case RenderQuality.Low:
                postProcessAction = () =>
                {
                    softParticle = QualitySettings.softParticles;
                    softVeg = QualitySettings.softVegetation;
                    QualitySettings.softParticles = false;
                    QualitySettings.softVegetation = false;

                    shadowQuality = QualitySettings.shadows;
                    QualitySettings.shadows = ShadowQuality.Disable;

                    ani = QualitySettings.anisotropicFiltering;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    reflectionCamera.Render();
                    QualitySettings.softParticles = softParticle;
                    QualitySettings.softVegetation = softVeg;

                    QualitySettings.shadows = shadowQuality;
                    QualitySettings.anisotropicFiltering = ani;
                };
                break;
            case RenderQuality.VeryLow:
                postProcessAction = () =>
                {
                    ani = QualitySettings.anisotropicFiltering;
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                    reflectionCamera.Render();
                    QualitySettings.anisotropicFiltering = ani;
                };
                break;
        }

        m_SqrMaxdistance = m_maxDistance * m_maxDistance;
        widthHeightRate = (float)Screen.height / (float)Screen.width;
        m_ReflectionTexture = new RenderTexture(m_TextureSize, (int)(m_TextureSize * widthHeightRate + 0.5), 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Default);
        m_ReflectionTexture.name = "ReflectionTex " + GetInstanceID();
        m_ReflectionTexture.isPowerOfTwo = true;
        m_ReflectionTexture.filterMode = FilterMode.Trilinear;
        m_ReflectionTexture.antiAliasing = (int)MSAA;
        GameObject go = new GameObject("MirrorCam", typeof(Camera), typeof(FlareLayer));
        //go.hideFlags = HideFlags.HideAndDontSave;
        reflectionCamera = go.GetComponent<Camera>();
        //mysky = go.AddComponent<Skybox> ();
        go.transform.SetParent(normalTrans);
        go.transform.localPosition = Vector3.zero;
        reflectionCamera.enabled = false;
        reflectionCamera.targetTexture = m_ReflectionTexture;
        reflectionCamera.cullingMask = ~(1 << 4) & m_ReflectLayers.value;
        reflectionCamera.layerCullSpherical = enableSelfCullingDistance;
        refT = reflectionCamera.transform;
        if (!enableSelfCullingDistance)
        {
            for (int i = 0, length = layerCullingDistances.Length; i < length; ++i)
            {
                layerCullingDistances[i] = 0;
            }
        }
        else
        {
            reflectionCamera.layerCullDistances = layerCullingDistances;
        }
        reflectionCamera.useOcclusionCulling = false;       //Custom Projection Camera should not use occlusionCulling!
        SetTexture(m_ReflectionTexture);
        buffer.name = "Postprocessing Buffer";
        if (addPostProcessingComponent) {
            depthTexture = new RenderTexture(m_ReflectionTexture.width, m_ReflectionTexture.height, 16, RenderTextureFormat.RFloat, RenderTextureReadWrite.Default);
            buffer.GetTemporaryRT(ShaderIDs._TempTex, m_ReflectionTexture.descriptor);
            var edgeBlurAAMaterial = new Material(Shader.Find("Hidden/Mirror/AAEdgeBlur"));
            var fxaaMaterial = new Material(Shader.Find("Hidden/Mirror/FXAA"));
            buffer.BlitSRT(BuiltinRenderTextureType.CameraTarget, ShaderIDs._TempTex, edgeBlurAAMaterial, 0);
            buffer.BlitSRT(ShaderIDs._TempTex, BuiltinRenderTextureType.CameraTarget, fxaaMaterial, 0);

            depthCam = new GameObject("depthCam", typeof(Camera)).GetComponent<Camera>();
            SetDepthCamera();
            depthCam.renderingPath = RenderingPath.Forward;
            depthCam.targetTexture = depthTexture;
            depthCam.transform.SetParent(refT);
            depthCam.transform.localPosition = Vector3.zero;
            depthCam.transform.localRotation = Quaternion.identity;
            depthCam.allowHDR = false;
            depthCam.allowMSAA = true;
            depthCam.enabled = false;
            depthCam.depthTextureMode = DepthTextureMode.None;
            depthCam.SetReplacementShader(Shader.Find("Hidden/DepthBlur"), "RenderType");
            depthCam.backgroundColor = Color.white;
            depthCam.clearFlags = CameraClearFlags.Color;
            depthCam.layerCullSpherical = enableSelfCullingDistance;
            if(enableSelfCullingDistance) depthCam.layerCullDistances = layerCullingDistances;
            buffer.ReleaseTemporaryRT(ShaderIDs._TempTex);
            reflectionCamera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, buffer);
            if(enableDepthOfField)
            dof = reflectionCamera.gameObject.AddComponent<DepthOfField>();
        }
    }

    private void SetDepthCamera() {
        Camera cam = reflectionCamera;
        depthCam.cullingMask = cam.cullingMask;
        depthCam.fieldOfView = cam.fieldOfView;
        depthCam.orthographic = cam.orthographic;
        depthCam.aspect = cam.aspect;
        depthCam.orthographicSize = cam.orthographicSize;
    }
    private static void Clamp(ref float value, float min, ref float max)
    {
        if (value < min) value = min;
        else if (value > max) value = max;
    }

    private static void Clamp(ref float value, float min, float max)
    {
        if (value < min) value = min;
        else if (value > max) value = max;
    }

    private void UpdateDepthCamera() {
        depthCam.worldToCameraMatrix = reflectionCamera.worldToCameraMatrix;
        depthCam.projectionMatrix = reflectionCamera.projectionMatrix;
    }

    public void SetTexture(RenderTexture target)
    {
        for (int i = 0, length = allMats.Count; i < length; ++i)
        {
            Material m = allMats[i];
            m.SetTexture(uniqueTextureID, target);
        }
    }

    void OnDestroy()
    {
        DestroyImmediate(m_ReflectionTexture);
        DestroyImmediate(reflectionCamera.gameObject);
        buffer.Dispose();
    }

    Vector3 pos;
    Vector3 normal;
    Vector4 reflectionPlane;
    Vector4 clipPlane;
    Matrix4x4 reflection = Matrix4x4.zero;
    Matrix4x4 ref_WorldToCam;
    [System.NonSerialized]
    public bool isBaked = false;

    IEnumerator WaitTo()
    {
        yield return null;
        isBaked = false;
    }
    private void OnEnable()
    {
        SetTexture(m_ReflectionTexture);
    }
    public void OnWillRenderObject()
    {
        if (s_InsideRendering || !render.enabled || isBaked)
            return;
        s_InsideRendering = true;
        isBaked = true;
        StartCoroutine(WaitTo());
        if (cam != Camera.current)
        {
            cam = Camera.current;
            camT = cam.transform;
            reflectionCamera.renderingPath = (renderQuality == RenderQuality.VeryLow) ? RenderingPath.VertexLit : cam.renderingPath;
            reflectionCamera.fieldOfView = cam.fieldOfView;
            reflectionCamera.clearFlags = cam.clearFlags;
            reflectionCamera.backgroundColor = cam.backgroundColor;
            reflectionCamera.allowHDR = false;
            reflectionCamera.allowMSAA = true;
            reflectionCamera.orthographic = cam.orthographic;
            reflectionCamera.aspect = cam.aspect;
            reflectionCamera.orthographicSize = cam.orthographicSize;
            reflectionCamera.depthTextureMode = DepthTextureMode.None;
            if (addPostProcessingComponent) SetDepthCamera();
        }
        
        if (useDistanceCull && Vector3.SqrMagnitude(normalTrans.position - camT.position) > m_SqrMaxdistance)
        {
            s_InsideRendering = false;
            return;
        }

        Vector3 localPos = normalTrans.worldToLocalMatrix.MultiplyPoint3x4
(camT.position);
        if (localPos.y < 0)
        {
            s_InsideRendering = false;
            return;
        }

        
        refT.eulerAngles = camT.eulerAngles;
        Vector3 localEuler = refT.localEulerAngles;
        localEuler.x *= -1;
        localEuler.z *= -1;
        refT.localEulerAngles = localEuler;
        localPos.y *= -1;
        refT.localPosition = localPos;
        
        normal = normalTrans.up;
        pos = normalTrans.position;
        float d = -Vector3.Dot(normal, pos) - m_ClipPlaneOffset;
        reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);
        CalculateReflectionMatrix(ref reflection, reflectionPlane);
        ref_WorldToCam = cam.worldToCameraMatrix * reflection;
        reflectionCamera.worldToCameraMatrix = ref_WorldToCam;
        clipPlane = CameraSpacePlane(ref_WorldToCam, pos, normal, 1.0f);
        reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);
        GL.invertCulling = true;
        if (addPostProcessingComponent && enableDepthOfField)
        {
            dof.focus.farFalloff = smoothness;
            UpdateDepthCamera();
            Shader.SetGlobalVector(ShaderIDs._MirrorPos, normalTrans.position);
            Shader.SetGlobalVector(ShaderIDs._MirrorNormal, normalTrans.up);
            Shader.SetGlobalTexture(ShaderIDs._DepthTex, depthTexture);
            depthCam.Render();
        }
#if UNITY_EDITOR
        if (renderQuality == RenderQuality.VeryLow)
        {
            if (reflectionCamera.renderingPath != RenderingPath.VertexLit)
                reflectionCamera.renderingPath = RenderingPath.VertexLit;
        }
        else if (reflectionCamera.renderingPath != cam.renderingPath)
        {
            reflectionCamera.renderingPath = cam.renderingPath;
        }
#endif
        postProcessAction();
        GL.invertCulling = false;
        s_InsideRendering = false;
    }

    private Vector4 CameraSpacePlane(Matrix4x4 worldToCameraMatrix, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * m_ClipPlaneOffset;
        Vector3 cpos = worldToCameraMatrix.MultiplyPoint3x4
(offsetPos);
        Vector3 cnormal = worldToCameraMatrix.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    private static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1F - 2F * plane[0] * plane[0]);
        reflectionMat.m01 = (-2F * plane[0] * plane[1]);
        reflectionMat.m02 = (-2F * plane[0] * plane[2]);
        reflectionMat.m03 = (-2F * plane[3] * plane[0]);

        reflectionMat.m10 = (-2F * plane[1] * plane[0]);
        reflectionMat.m11 = (1F - 2F * plane[1] * plane[1]);
        reflectionMat.m12 = (-2F * plane[1] * plane[2]);
        reflectionMat.m13 = (-2F * plane[3] * plane[1]);

        reflectionMat.m20 = (-2F * plane[2] * plane[0]);
        reflectionMat.m21 = (-2F * plane[2] * plane[1]);
        reflectionMat.m22 = (1F - 2F * plane[2] * plane[2]);
        reflectionMat.m23 = (-2F * plane[3] * plane[2]);

        reflectionMat.m30 = 0F;
        reflectionMat.m31 = 0F;
        reflectionMat.m32 = 0F;
        reflectionMat.m33 = 1F;
    }
}
