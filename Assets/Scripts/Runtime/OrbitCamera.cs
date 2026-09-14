using UnityEngine;

namespace Riverworks
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Camera Camera { get; private set; }
        public CameraCinematics Cinematics { get; private set; }
        public Vector3 AuthoredFocus => target;
        public float AuthoredSize => zoom;
        public Vector3 DisplayedFocus => smoothTarget;
        public float DisplayedSize => Camera!=null?Camera.orthographicSize:zoom;
        GameController controller;
        Vector3 target = Vector3.zero;
        Vector3 smoothTarget;
        float yaw=0, smoothYaw=0, zoom=7.0f;
        Vector3 previousMouse;
        public void Initialize(GameController game)
        {
            controller=game;
            Camera=gameObject.AddComponent<Camera>();
            gameObject.tag="MainCamera";
            Camera.orthographic=true; Camera.orthographicSize=zoom;
            Camera.nearClipPlane=0.1f; Camera.farClipPlane=150;
            Camera.backgroundColor=new Color(0.77f,0.84f,0.85f);
            Camera.clearFlags=CameraClearFlags.SolidColor;
            Camera.allowHDR=true; Camera.allowMSAA=true;
            gameObject.AddComponent<AudioListener>();
            Home(); Snap();
            Cinematics=gameObject.AddComponent<CameraCinematics>();Cinematics.Initialize(this);
        }
        public void Home() { Cinematics?.Reset();target=new Vector3(0,0,0.0f); zoom=7.0f; yaw=0; }
        public void SetZoom(float size) { Cinematics?.Reset();zoom=Mathf.Clamp(size,1.6f,17.5f); }
        public void CommitFocus(Vector3 focus,float size)
        {
            target=focus;ClampTarget();zoom=Mathf.Clamp(size,1.6f,17.5f);
        }
        /// <summary>Moves the camera by a screen-space drag delta. Positive delta follows the finger.</summary>
        public void PanScreen(Vector2 screenDelta)
        {
            Cinematics?.CancelForManualInput();
            Vector3 right=Quaternion.Euler(0,yaw+45,0)*Vector3.right;
            Vector3 forward=Quaternion.Euler(0,yaw+45,0)*Vector3.forward;
            target-=(right*screenDelta.x+forward*screenDelta.y)*zoom*.0024f;
            ClampTarget();
        }
        /// <summary>Applies a multiplicative orthographic zoom. Values below one zoom in.</summary>
        public void Zoom(float multiplier) { Cinematics?.CancelForManualInput();SetZoom(zoom*multiplier); }
        public void FrameRegion(int id) { Cinematics?.CancelForManualInput();target=new Vector3((id%3-1)*5f,0,(id/3-1)*5f); zoom=Mathf.Max(zoom,9f); }
        public void Overview() { Cinematics?.Reset();target=Vector3.zero; zoom=14.4f; yaw=0; Snap(); }
        void Snap() { smoothTarget=target; smoothYaw=yaw; Camera.orthographicSize=zoom; Position(); }
        void LateUpdate()
        {
            if(controller == null) return;
            if(!controller.HelpOpen && !controller.ModalOpen && !controller.ResearchOpen && !controller.SmokeMode && !(controller.Tutorial!=null&&controller.Tutorial.IntroPlaying))
            {
                float dt=Time.unscaledDeltaTime;
                bool overUi=controller.IsPointerOverUI();
                bool cameraKeys=Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.S)||Input.GetKeyDown(KeyCode.Q)||Input.GetKeyDown(KeyCode.E)||Input.GetKeyDown(KeyCode.F);
                if(cameraKeys||(!overUi&&(Mathf.Abs(Input.mouseScrollDelta.y)>.001f||controller.CanPanWithMiddle||controller.CanPanWithRight)))Cinematics?.CancelForManualInput();
                if(Input.GetKeyDown(KeyCode.F)) Home();
                if(Input.GetKeyDown(KeyCode.Q)) yaw-=90;
                if(Input.GetKeyDown(KeyCode.E)) yaw+=90;
                Vector3 right=Quaternion.Euler(0,yaw+45,0)*Vector3.right;
                Vector3 forward=Quaternion.Euler(0,yaw+45,0)*Vector3.forward;
                float x=(Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0);
                float z=(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0);
                target += (right*x+forward*z)*dt*zoom*0.8f;
                if(!overUi)
                {
                    zoom=Mathf.Clamp(zoom-Input.mouseScrollDelta.y*0.75f,1.6f,17.5f);
                    if(controller.CanPanWithMiddle || controller.CanPanWithRight)
                    {
                        if(!Input.GetMouseButtonDown(2)&&!Input.GetMouseButtonDown(1))
                        {
                            var delta=Input.mousePosition-previousMouse;
                            target-=(right*delta.x+forward*delta.y)*zoom*0.0024f;
                        }
                    }
                }
                ClampTarget();
            }
            previousMouse=Input.mousePosition;
            Vector3 desiredFocus=Cinematics!=null&&Cinematics.IsActive?Cinematics.Focus:target;
            float desiredZoom=Cinematics!=null&&Cinematics.IsActive?Cinematics.Size:zoom;
            smoothTarget=Vector3.Lerp(smoothTarget,desiredFocus,1-Mathf.Exp(-10*Time.unscaledDeltaTime));
            smoothYaw=Mathf.Lerp(smoothYaw,yaw,1-Mathf.Exp(-8*Time.unscaledDeltaTime));
            Camera.orthographicSize=Mathf.Lerp(Camera.orthographicSize,desiredZoom,1-Mathf.Exp(-10*Time.unscaledDeltaTime));
            Position();
        }
        void ClampTarget() { target.x=Mathf.Clamp(target.x,-12,12); target.z=Mathf.Clamp(target.z,-12,12); }
        void Position()
        {
            transform.rotation=Quaternion.Euler(49,smoothYaw+45,0);
            // Scale the framing offset at close zoom so the selected resident stays clear of the top HUD.
            float framingOffset=.85f*Mathf.Min(1f,Camera.orthographicSize/5f);
            transform.position=smoothTarget-transform.forward*35-transform.up*framingOffset;
            Transform feedbackOffset=controller!=null&&controller.Feel!=null?controller.Feel.CameraOffset:null;
            if(feedbackOffset!=null&&!FeelDirector.ReducedMotion)
            {
                transform.position+=transform.TransformDirection(feedbackOffset.localPosition);
                transform.rotation*=Quaternion.Euler(feedbackOffset.localEulerAngles);
            }
        }
    }
}
