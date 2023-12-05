using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using BepInEx;

namespace AmazingNewAccessoryLogic
{
    class AnalCameraComponent : MonoBehaviour
    {

        public EventHandler<CameraEventArgs> OnPostRenderEvent;

        void OnPostRender()
        {
            OnPostRenderEvent?.Invoke(this, new CameraEventArgs() { camera = Camera.current});
        }
    }

    public class CameraEventArgs : EventArgs
    {
        public Camera camera { get; set; }
    }
}
