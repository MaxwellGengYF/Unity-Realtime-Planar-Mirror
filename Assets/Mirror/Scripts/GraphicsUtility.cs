using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class GraphicsUtility
{
    private static Mesh mesh
    {
        get
        {
            if (m_mesh != null)
                return m_mesh;
            m_mesh = new Mesh();
            m_mesh.vertices = new Vector3[]{
                new Vector3(-1,-1,0),
                new Vector3(-1,1,0),
                new Vector3(1,1,0),
                new Vector3(1,-1,0)
            };
            m_mesh.uv = new Vector2[]{
                new Vector2(0,1),
                new Vector2(0,0),
                new Vector2(1,0),
                new Vector2(1,1)
            };

            m_mesh.SetIndices(new int[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);
            return m_mesh;
        }
    }

    private static Mesh m_mesh;
    public static void BlitMRT(this CommandBuffer buffer, RenderTargetIdentifier[] colorIdentifier, RenderTargetIdentifier depthIdentifier, Material mat, int pass)
    {
        buffer.SetRenderTarget(colorIdentifier, depthIdentifier);
        buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
    }

    public static void BlitSRT(this CommandBuffer buffer, RenderTargetIdentifier destination, Material mat, int pass)
    {
        buffer.SetRenderTarget(destination);
        buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
    }

    public static void BlitMRT(this CommandBuffer buffer, Texture source, RenderTargetIdentifier[] colorIdentifier, RenderTargetIdentifier depthIdentifier, Material mat, int pass)
    {
        buffer.SetRenderTarget(colorIdentifier, depthIdentifier);
        buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
    }

    public static void BlitSRT(this CommandBuffer buffer, Texture source, RenderTargetIdentifier destination, Material mat, int pass)
    {
        buffer.SetGlobalTexture(ShaderIDs._MainTex, source);
        buffer.SetRenderTarget(destination);
        buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
    }

    public static void BlitSRT(this CommandBuffer buffer, RenderTargetIdentifier source, RenderTargetIdentifier destination, Material mat, int pass)
    {
        buffer.SetGlobalTexture(ShaderIDs._MainTex, source);
        buffer.SetRenderTarget(destination);
        buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
    }

    public static void BlitStencil(this CommandBuffer buffer, RenderTargetIdentifier colorSrc, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthStencilBuffer, Material mat, int pass) {
        buffer.SetGlobalTexture(ShaderIDs._MainTex, colorSrc);
        buffer.SetRenderTarget(colorBuffer, depthStencilBuffer);
        buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
    }

    public static void BlitStencil(this CommandBuffer buffer, RenderTargetIdentifier colorBuffer, RenderTargetIdentifier depthStencilBuffer, Material mat, int pass)
    {
        buffer.SetRenderTarget(colorBuffer, depthStencilBuffer);
        buffer.DrawMesh(mesh, Matrix4x4.identity, mat, 0, pass);
    }

    public static void GetFarClipPlane(this Camera cam, Vector4[] frust) {
        frust[0] = cam.ViewportToWorldPoint(new Vector3(0, 0, cam.farClipPlane));
        frust[1] = cam.ViewportToWorldPoint(new Vector3(1, 0, cam.farClipPlane));
        frust[2] = cam.ViewportToWorldPoint(new Vector3(0, 1, cam.farClipPlane));
        frust[3] = cam.ViewportToWorldPoint(new Vector3(1, 1, cam.farClipPlane));
    }

    public static void GetNearClipPlane(this Camera cam, Vector4[] frust)
    {
        frust[0] = cam.ViewportToWorldPoint(new Vector3(0, 0, cam.nearClipPlane));
        frust[1] = cam.ViewportToWorldPoint(new Vector3(1, 0, cam.nearClipPlane));
        frust[2] = cam.ViewportToWorldPoint(new Vector3(0, 1, cam.nearClipPlane));
        frust[3] = cam.ViewportToWorldPoint(new Vector3(1, 1, cam.nearClipPlane));
    }
}
