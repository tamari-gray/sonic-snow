using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.XR.XREAL
{
    /// <summary> A frame blender. </summary>
    public class FrameBlender : BlenderBase
    {
        /// <summary> Target camera. </summary>
        protected Camera[] m_TargetCamera;
        protected Camera m_RGBCamera;
        protected Camera m_LeftGrayCamera;
        protected Camera m_RightGrayCamera;
        /// <summary> The encoder. </summary>
        protected IEncoder m_Encoder;
        /// <summary> The blend material. </summary>
        private Material m_BackGroundMat;
        XREALBackGroundRender m_RGBBackGroundRender;
        XREALBackGroundRender m_LeftGrayBackGroundRender;
        XREALBackGroundRender m_RightGrayBackGroundRender;
        private List<XREALCameraInitializer> m_DeviceParamInitializer;

        private CaptureSide m_CaputreSide;
        private CameraType m_CameraType;
        private RenderTexture m_BlendTexture;
        private RenderTexture m_BlendTextureLeft;
        private RenderTexture m_BlendTextureRight;
        /// <summary> Gets or sets the blend texture. </summary>
        /// <value> The blend texture. </value>
        public override RenderTexture BlendTexture
        {
            get
            {
                return m_BlendTexture;
            }
        }

        /// <summary> Initializes this object. </summary>
        /// <param name="cameraArray">  The camera.</param>
        /// <param name="rgbCamera"> The RGB camera.</param>
        /// <param name="encoder"> The encoder.</param>
        /// <param name="param">   The parameter.</param>
        public override void Init(Camera[] cameraArray, Camera rgbCamera, Camera[] grayCameras, IEncoder encoder, CameraParameters param)
        {
            base.Init(cameraArray, rgbCamera, grayCameras, encoder, param);

            Width = param.cameraResolutionWidth;
            Height = param.cameraResolutionHeight;
            m_TargetCamera = cameraArray;
            m_RGBCamera = rgbCamera;
            m_LeftGrayCamera = grayCameras[0];
            m_RightGrayCamera = grayCameras[1];
            m_Encoder = encoder;
            BlendMode = param.blendMode;
            m_CaputreSide = param.captureSide;
            m_CameraType = param.cameraType;

            SetupCamera(m_RGBCamera, ref m_RGBBackGroundRender);
            SetupCamera(m_LeftGrayCamera, ref m_LeftGrayBackGroundRender);
            SetupCamera(m_RightGrayCamera, ref m_RightGrayBackGroundRender);



            m_DeviceParamInitializer = new List<XREALCameraInitializer>();
            for (var i = 0; i < m_TargetCamera.Length; ++i)
            {
                m_DeviceParamInitializer.Add(m_TargetCamera[i].gameObject.GetComponent<XREALCameraInitializer>());

                m_TargetCamera[i].enabled = false;
            }

            if (m_CaputreSide != CaptureSide.Both)
            {
                m_BlendTexture = CreateRenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            }
            else
            {
                m_BlendTextureLeft = CreateRenderTexture((int)(0.5f * Width), (int)(0.5f * Height), 24, RenderTextureFormat.ARGB32);
                m_BlendTextureRight = CreateRenderTexture((int)(0.5f * Width), (int)(0.5f * Height), 24, RenderTextureFormat.ARGB32);
                m_BlendTexture = CreateRenderTexture(Width, (int)(0.5f * Height), 24, RenderTextureFormat.ARGB32);
            }
        }

        /// <summary> Executes the 'frame' action. </summary>
        /// <param name="frame"> The frame.</param>
        public override void OnFrame(UniversalTextureFrame frame)
        {
            base.OnFrame(frame);

            for (var i = 0; i < m_DeviceParamInitializer.Count; ++i)
            {
                if (!m_DeviceParamInitializer[i].IsInitialized)
                {
                    return;
                }
            }

            if (m_BackGroundMat == null)
            {
                m_BackGroundMat = CreatBlendMaterial(frame.textureType);
                m_RGBBackGroundRender.SetMaterial(m_BackGroundMat);
            }

            bool render = ShouldRenderThisFrame();

            bool isyuv = frame.textureType == TextureType.YUV;
            const string MainTextureStr = "_MainTex";
            const string UTextureStr = "_UTex";
            const string VTextureStr = "_VTex";

            switch (BlendMode)
            {
                case BlendMode.VirtualOnly:
                    if (render) CameraRenderToTarget(false);
                    break;
                case BlendMode.CameraOnly:
                case BlendMode.Blend:
                    if (isyuv)
                    {
                        m_BackGroundMat.SetTexture(MainTextureStr, frame.textures[0]);
                        m_BackGroundMat.SetTexture(UTextureStr, frame.textures[1]);
                        m_BackGroundMat.SetTexture(VTextureStr, frame.textures[2]);
                    }
                    else
                    {
                        m_BackGroundMat.SetTexture(MainTextureStr, frame.textures[0]);
                    }
                    if (render) CameraRenderToTarget(true);
                    break;
            }

            // Commit frame                
            m_Encoder.Commit(BlendTexture, frame.timeStamp);
            FrameCount++;
        }
        private void CameraRenderToTarget(bool enableBackGround)
        {
            if (m_CaputreSide != CaptureSide.Both)
            {
                if (m_CameraType == CameraType.RGB)
                {
                    m_RGBBackGroundRender.enabled = enableBackGround;
                    RenderCaptureCamera(m_TargetCamera[0], m_BlendTexture);
                }
                else
                {
                    m_LeftGrayBackGroundRender.enabled = enableBackGround;
                    RenderCaptureCamera(m_TargetCamera[0], m_BlendTexture);
                }
            }
            else
            {
                if (m_CameraType == CameraType.RGB)
                {
                    m_RGBBackGroundRender.enabled = enableBackGround;
                    RenderCaptureCamera(m_TargetCamera[0], m_BlendTextureLeft);

                    RenderCaptureCamera(m_TargetCamera[1], m_BlendTextureRight);
                }
                else
                {
                    m_LeftGrayBackGroundRender.enabled = enableBackGround;
                    m_RightGrayBackGroundRender.enabled = false;
                    RenderCaptureCamera(m_TargetCamera[0], m_BlendTextureLeft);

                    m_LeftGrayBackGroundRender.enabled = false;
                    m_RightGrayBackGroundRender.enabled = enableBackGround;
                    RenderCaptureCamera(m_TargetCamera[1], m_BlendTextureRight);
                }

                MergeRenderTextures(m_BlendTextureLeft, m_BlendTextureRight, m_BlendTexture);
            }
            m_RGBBackGroundRender.enabled = false;
            m_LeftGrayBackGroundRender.enabled = false;
            m_RightGrayBackGroundRender.enabled = false;
        }

        // ---------------------------------------------------------------------------------------
        // LOCAL PATCH (sonic-snow, 2026-08-20). Not XREAL's code — this package is vendored.
        //
        // Upstream drove the capture camera with Camera.Render(): a built-in-render-pipeline
        // immediate-mode call, which is not the supported way to render on demand once a Scriptable
        // Render Pipeline is active. This project runs URP 17.4 with Render Graph on;
        // xreal-hello-world, where these samples are known to work, is on Built-in RP. That
        // difference is the shape of the bug XREAL support read off our logcat — "no dropped frames,
        // but the image only updated about 6 times, with identical frames in between".
        // VideoEncoder.Commit runs once per RGB frame either way, so the mp4 gets a full ~30fps of
        // *samples*; those samples just keep re-reading a blend texture no render refreshed.
        //
        // The supported on-demand render under an SRP is Camera.SubmitRenderRequest, which URP
        // implements in UniversalRenderPipeline.ProcessRenderRequests — and for a Tex2D destination
        // at mip 0 it renders straight into that destination with no intermediate copy, so the
        // native pointer VideoEncoder.Commit cached still points at the pixels just drawn.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// How the capture camera gets driven into the blend texture. Kept as a switch rather than a
        /// straight replacement so both paths can be A/B'd on device from the scene, via
        /// RGBCameraCapture.renderMode — telling them apart is the whole point of the exercise.
        /// </summary>
        public enum CaptureRenderMode
        {
            /// <summary>SubmitRenderRequest when an SRP is active, Camera.Render() otherwise.</summary>
            Auto,
            /// <summary>Always SubmitRenderRequest. Falls back to Camera.Render() if unsupported.</summary>
            RenderRequest,
            /// <summary>Always Camera.Render(). Upstream's behaviour, kept for comparison.</summary>
            LegacyCameraRender,
        }

        public static CaptureRenderMode RenderMode = CaptureRenderMode.Auto;

        // ---------------------------------------------------------------------------------------
        // LOCAL PATCH (sonic-snow, 2026-08-22): capture render-rate cap.
        //
        // The capture camera is a second full render of the scene, and OnFrame is clocked by the RGB
        // camera at ~30fps, so once acquisition was fixed (ARCameraManager contention, c35ccec) the
        // app started paying that cost every frame. Measured on device: ~60fps idle, ~25fps while
        // capture renders at full rate. On optical see-through glasses that halved rate is not a
        // cosmetic problem — world-locked AR judders against the real world, and the compositor's
        // reprojection drags head-locked UI by a whole frame of head motion.
        //
        // Capping the render rate trades footage smoothness for live smoothness. Commits are
        // deliberately NOT capped: the encoder keeps receiving a sample per RGB frame with its real
        // timestamp, so the container stays ~30fps and the skipped frames become H.264 skip frames
        // (a few hundred bytes each) rather than shifting the timeline.
        // ---------------------------------------------------------------------------------------

        /// <summary>Capture-camera renders per second, or 0 for one per RGB frame (~30).</summary>
        public static float MaxRenderFps;

        private float m_LastRenderTime = float.NegativeInfinity;

        private bool ShouldRenderThisFrame()
        {
            if (MaxRenderFps <= 0f) return true;

            float now = Time.realtimeSinceStartup;
            if (now - m_LastRenderTime < 1f / MaxRenderFps) return false;

            m_LastRenderTime = now;
            return true;
        }

        /// <summary>
        /// Capture-camera renders submitted since process start. Read alongside FrameCount (commits)
        /// to tell "the render is never called" apart from "the render is called and produces the
        /// same pixels" — different bugs with different fixes. CaptureFrameDiagnostics samples both.
        /// </summary>
        public static int RenderCount { get; private set; }

        /// <summary>Latched once the request path is rejected, so it is logged once and not 30 times a second.</summary>
        private static bool s_RenderRequestUnavailable;

        // Reused rather than allocated per frame: this runs up to 30 times a second for a whole race.
        private static readonly RenderPipeline.StandardRequest s_RenderRequest = new RenderPipeline.StandardRequest();

        private void RenderCaptureCamera(Camera camera, RenderTexture target)
        {
            RenderCount++;

            bool wantRequest = RenderMode == CaptureRenderMode.RenderRequest
                || (RenderMode == CaptureRenderMode.Auto && GraphicsSettings.currentRenderPipeline != null);

            if (wantRequest && !s_RenderRequestUnavailable)
            {
                s_RenderRequest.destination = target;
                s_RenderRequest.mipLevel = 0;
                s_RenderRequest.face = CubemapFace.Unknown;
                s_RenderRequest.slice = 0;

                if (RenderPipeline.SupportsRenderRequest(camera, s_RenderRequest))
                {
                    try
                    {
                        // URP points camera.targetTexture at the destination and restores it itself.
                        RenderPipeline.SubmitRenderRequest(camera, s_RenderRequest);
                        return;
                    }
                    catch (System.Exception e)
                    {
                        s_RenderRequestUnavailable = true;
                        Debug.LogError("[FrameBlender] SubmitRenderRequest threw; falling back to " +
                                       "Camera.Render() for the rest of this run: " + e);
                    }
                }
                else
                {
                    s_RenderRequestUnavailable = true;
                    Debug.LogWarning("[FrameBlender] Active render pipeline does not support " +
                                     "StandardRequest; falling back to Camera.Render().");
                }
            }

            camera.targetTexture = target;
            camera.Render();
        }
        private void MergeRenderTextures(Texture leftSrc, Texture rightSrc, RenderTexture target)
        {
            //Set the RTT in order to render to it
            Graphics.SetRenderTarget(target);

            //Setup 2D matrix in range 0..1, so nobody needs to care about sized
            GL.LoadPixelMatrix(0, 1, 1, 0);

            //Then clear & draw the texture to fill the entire RTT.
            GL.Clear(true, true, new Color(0, 0, 0, 0));

            Graphics.DrawTexture(new Rect(0, 0, 0.5f, 1.0f), leftSrc);
            Graphics.DrawTexture(new Rect(0.5f, 0, 0.5f, 1.0f), rightSrc);
        }
        private Material CreatBlendMaterial(TextureType texturetype)
        {
            string shader_name = string.Format("Shaders/CaptureBackground{0}", texturetype == TextureType.RGB ? "" : "YUV");
            return new Material(Resources.Load<Shader>(shader_name));
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged
        /// resources. </summary>
        public override void Dispose()
        {
            base.Dispose();

            m_BlendTexture?.Release();
            m_BlendTexture = null;
            m_BlendTextureLeft?.Release();
            m_BlendTextureLeft = null;
            m_BlendTextureRight?.Release();
            m_BlendTextureRight = null;
        }

        private RenderTexture CreateRenderTexture(int width, int height, int depth = 24, RenderTextureFormat format = RenderTextureFormat.ARGB32, bool usequaAnti = true)
        {
            var rt = new RenderTexture(width, height, depth, format, QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Default);
            rt.wrapMode = TextureWrapMode.Clamp;
            if (QualitySettings.antiAliasing > 0 && usequaAnti)
            {
                rt.antiAliasing = QualitySettings.antiAliasing;
            }
            else
            {
                rt.antiAliasing = 1;
            }

            rt.Create();
            return rt;
        }
    }
}
